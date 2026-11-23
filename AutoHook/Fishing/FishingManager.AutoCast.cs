using System.Linq;
using AutoHook.Conditions;
using AutoHook.Configurations;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using ECommons.Throttlers;

namespace AutoHook.Fishing;

public partial class FishingManager
{
    public AutoCastsConfig GetAutoCastCfg()
    {
        return Presets.SelectedPreset?.AutoCastsCfg.EnableAll ?? false
            ? Presets.SelectedPreset.AutoCastsCfg
            : Presets.DefaultPreset.AutoCastsCfg;
    }

    private void CheckWhileFishingActions()
    {
        if (!EzThrottler.Throttle("CheckWhileFishingActions", 500))
            return;

        if (Service.TaskManager.IsBusy)
            return;

        var hookCfg = GetHookCfg();

        if (!hookCfg.Enabled)
            return;

        // 傳的是「鎖定 或 魚影」的合併判斷（LureStopRequested），不能只看每竿重設的 _lureSuccess ——
        // 否則魚影還在的時候只要釣起別的魚，下一竿就會重新引誘而毀掉已觸發的魚影。
        Service.TaskManager.Enqueue(() => hookCfg.GetHookset().CastLures.TryCasting(LureStopRequested,
            _fishingTimer.IsRunning ? _fishingTimer.Elapsed.TotalSeconds : null));
    }
    
    private void CastCollect()
    {
        var cfg = GetAutoCastCfg();

        if (PlayerRes.HasStatus(IDs.Status.CollectorsGlove) && cfg.RecastAnimationCancel && cfg.TurnCollectOff &&
            !cfg.CastCollect.Enabled)
        {
            PlayerRes.CastAction(IDs.Actions.Collect);
        }
        else
        {
            cfg.TryCastAction(cfg.CastCollect);
            return;
        }
    }

    private void UseAutoCasts()
    {
        // if _lastStep is FishBit but currentState is FishingState.PoleReady, it means that the fish was hooked, but it escaped.
        if (_lastStep.HasFlag(FishingSteps.None) || _lastStep.HasFlag(FishingSteps.BeganFishing)
                                                 || _lastStep.HasFlag(FishingSteps.Quitting))
        {
            return;
        }

        if (!PlayerRes.IsCastAvailable() || Service.TaskManager.IsBusy)
            return;

        Service.TaskManager.Enqueue(() =>
        {
            var lastFishCatchCfg = GetLastCatchConfig();

            var acCfg = GetAutoCastCfg();

            var ignoreMooch = lastFishCatchCfg?.NeverMooch ?? false;
            var hookset = GetHookCfg().GetHookset();

            if (CosmicPatience.TryAutoCast(acCfg, hookset, ignoreMooch, beforePreset: true))
                return;

            var autoCast = acCfg.GetNextAutoCast(ignoreMooch);

            if (acCfg.TryCastAction(autoCast, false, ignoreMooch))
                return;

            if (CosmicPatience.TryAutoCast(acCfg, hookset, ignoreMooch, beforePreset: false))
                return;

            CastLineMoochOrRelease(acCfg, lastFishCatchCfg);
        }, "AutoCasting");
    }

    private void CastLineMoochOrRelease(AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg)
    {
        var blockMooch = lastFishCatchCfg is { Enabled: true, NeverMooch: true };

        if (TryUseSwimbait(acCfg, lastFishCatchCfg, blockMooch))
        {
            acCfg.TryCastAction(acCfg.CastLine, true);
            return;
        }

        if (!blockMooch)
        {
            if (lastFishCatchCfg is { Enabled: true } && lastFishCatchCfg.Mooch.IsAvailableToCast())
            {
                PlayerRes.CastActionNoDelay(lastFishCatchCfg.Mooch.Id, lastFishCatchCfg.Mooch.ActionType,
                    UIStrings.Mooch);
                return;
            }

            if (acCfg.TryCastAction(acCfg.CastMooch, true))
                return;
        }

        if (acCfg.TryCastAction(acCfg.CastLine, true))
            return;
    }

    /// <summary>
    /// 上游 v6+ 的「游動餌」：欄位裡有魚、而且這條魚的以小釣大設定勾了 UseSwimbait、條件成立 ⇒ 改用牠當這一竿的餌。
    /// </summary>
    /// <remarks>
    /// 第一行先看 preset 有沒有任何 UseSwimbait：沒有就不碰原生狀態（既有 preset 全部走這條）。
    /// 上游順位：剛釣起的魚本身就在欄位裡、而且以小釣大這一竿放得出去時，讓給以小釣大。
    /// </remarks>
    private static readonly bool SwimbaitCommandVerified = false;
    private bool _swimbaitGateLogged;

    private bool TryUseSwimbait(AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg, bool blockMooch)
    {
        var preset = Presets.SelectedPreset;
        if (preset == null || !preset.ListOfMooch.Any(h => h.Enabled && (h.SwimbaitNormal.UseSwimbait || h.SwimbaitIntuition.UseSwimbait)))
            return false;

        // 刻意先不送：游動餌切換指令（ExecuteCommand 701／25）還沒對台服執行檔驗證，驗過再改成 true。
        if (!SwimbaitCommandVerified)
        {
            if (!_swimbaitGateLogged)
            {
                _swimbaitGateLogged = true;
                Service.PluginLog.Information(@"[游動餌] 這個 preset 想用游動餌，但切換指令尚未對台服驗證，暫不自動切換（其餘照常）");
            }
            return false;
        }

        var slots = Service.BaitManager.SwimBaitIds;
        if (slots.All(id => id == 0))
            return false;

        if (!blockMooch && _lastCatch is { Id: > 0 } last && slots.Contains((uint)last.Id))
        {
            var moochWouldCast = (lastFishCatchCfg is { Enabled: true } && lastFishCatchCfg.Mooch.IsAvailableToCast())
                                 || (acCfg.EnableAll && acCfg.CastMooch.Enabled && acCfg.CastMooch.IsAvailableToCast());
            if (moochWouldCast)
                return false;
        }

        for (var i = 0; i < slots.Length; i++)
        {
            var fishId = slots[i];
            if (fishId == 0)
                continue;

            var cfg = preset.GetCfgById((int)fishId, true);
            if (cfg is not { Enabled: true })
                continue;

            var swimbait = cfg.GetSwimbaitConfig();
            if (!swimbait.UseSwimbait)
                continue;

            var ctx = ConditionContext.ForAction(swimbaitFishId: fishId);
            if (!ConditionEvaluator.Passes(swimbait.ConditionSet, ctx, unconfiguredResult: true,
                    unsupportedResult: false, where: @"游動餌"))
                continue;

            var result = Service.BaitManager.ChangeSwimbait((uint)i);

            // 要使用者回報的診斷：游動餌的切換指令沒有在台服實機驗過，成敗都要看得到。
            Service.PrintInfo(@$"[游動餌] 欄位 {i + 1}（{MultiString.GetItemName(fishId)} #{fishId}）→ {result}"
                              + @$"｜條件：{ConditionEvaluator.Describe(swimbait.ConditionSet, ctx)}");

            if (result == SeFunctions.BaitManager.ChangeBaitReturn.Success)
                return true;
        }

        return false;
    }
}