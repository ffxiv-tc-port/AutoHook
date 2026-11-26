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
        Service.TaskManager.Enqueue(() =>
        {
            var lures = hookCfg.GetHookset().CastLures;
            lures.TryCasting(LureStopRequested(lures.LureTarget),
                _fishingTimer.IsRunning ? _fishingTimer.Elapsed.TotalSeconds : null);
        });
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

    private void CastLineMoochOrRelease(AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg, bool allowSwimbait = true)
    {
        var blockMooch = lastFishCatchCfg is { Enabled: true, NeverMooch: true };

        if (allowSwimbait)
        {
            var attempt = TryUseSwimbait(acCfg, lastFishCatchCfg, blockMooch, out var slot, out var slots);
            if (attempt == SwimbaitAttempt.AlreadySelected)
            {
                acCfg.TryCastAction(acCfg.CastLine, true);
                return;
            }

            if (attempt == SwimbaitAttempt.Sent)
            {
                var seq = Service.BaitManager.SwimbaitSendSeq;
                Service.TaskManager.Insert(() => WaitSwimbaitThenCast(acCfg, lastFishCatchCfg, seq, slot, slots),
                    @"游動餌：等選中再拋竿");
                return;
            }
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

    private enum SwimbaitAttempt
    {
        NotUsed,
        Sent,
        AlreadySelected,
    }

    private readonly SwimbaitFailureGate _swimbaitFailures = new();

    /// <summary>
    /// 上游 v6+ 的「游動餌」：欄位裡有魚、而且這條魚的以小釣大設定勾了 UseSwimbait、條件成立 ⇒ 改用牠當這一竿的餌。
    /// </summary>
    /// <remarks>
    /// 第一行先看 preset 有沒有任何 UseSwimbait：沒有就不碰原生狀態（既有 preset 全部走這條）。
    /// 上游順位：剛釣起的魚本身就在欄位裡、而且以小釣大這一竿放得出去時，讓給以小釣大。
    /// </remarks>
    private SwimbaitAttempt TryUseSwimbait(AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg, bool blockMooch,
        out int sentSlot, out uint[] slots)
    {
        sentSlot = -1;
        slots = [];
        var preset = Presets.SelectedPreset;
        if (preset == null || !preset.ListOfMooch.Any(h => h.Enabled && (h.SwimbaitNormal.UseSwimbait || h.SwimbaitIntuition.UseSwimbait)))
            return SwimbaitAttempt.NotUsed;

        slots = Service.BaitManager.SwimBaitIds;
        if (slots.All(id => id == 0))
            return SwimbaitAttempt.NotUsed;

        if (_swimbaitFailures.Refresh(slots))
            Service.PrintInfo(@"[游動餌] 游動餌清單變了，恢復嘗試先前停用的那一格");

        if (!blockMooch && _lastCatch is { Id: > 0 } last && slots.Contains((uint)last.Id))
        {
            var moochWouldCast = (lastFishCatchCfg is { Enabled: true } && lastFishCatchCfg.Mooch.IsAvailableToCast())
                                 || (acCfg.EnableAll && acCfg.CastMooch.Enabled && acCfg.CastMooch.IsAvailableToCast());
            if (moochWouldCast)
                return SwimbaitAttempt.NotUsed;
        }

        for (var i = 0; i < slots.Length; i++)
        {
            var fishId = slots[i];
            if (fishId == 0 || _swimbaitFailures.IsSuppressed(i))
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

            var result = Service.BaitManager.ChangeSwimbait((uint)i,
                @$"自動（條件：{ConditionEvaluator.Describe(swimbait.ConditionSet, ctx)}）");

            switch (result)
            {
                case SeFunctions.BaitManager.ChangeBaitReturn.Success:
                    sentSlot = i;
                    return SwimbaitAttempt.Sent;
                case SeFunctions.BaitManager.ChangeBaitReturn.AlreadyEquipped:
                    return SwimbaitAttempt.AlreadySelected;
                case SeFunctions.BaitManager.ChangeBaitReturn.BlockedByStatus:
                    return SwimbaitAttempt.NotUsed;
            }
        }

        return SwimbaitAttempt.NotUsed;
    }

    /// <summary>
    /// 拋竿前等游動餌確認選中（上限 <see cref="SwimbaitRules.ConfirmTimeoutMs"/>）；確認不到就不用游動餌、照常拋竿。
    /// </summary>
    private bool? WaitSwimbaitThenCast(AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg, int seq, int slot, uint[] slots)
    {
        var mine = Service.BaitManager.SwimbaitSendSeq == seq;
        var result = mine ? Service.BaitManager.PollSwimbaitConfirm() : SwimbaitRules.ConfirmResult.None;
        if (result == SwimbaitRules.ConfirmResult.Pending)
            return false;

        if (_lastStep.HasFlag(FishingSteps.None) || _lastStep.HasFlag(FishingSteps.BeganFishing)
                                                 || _lastStep.HasFlag(FishingSteps.Quitting)
                                                 || !Service.Configuration.EffectivePluginEnabled)
            return true;

        if (result == SwimbaitRules.ConfirmResult.Confirmed)
        {
            _swimbaitFailures.RecordSuccess();
            acCfg.TryCastAction(acCfg.CastLine, true);
            return true;
        }

        var suppressed = mine && _swimbaitFailures.RecordFailure(slot, slots);
        Service.PrintInfo(@$"[游動餌] 沒確認到選中欄位 {slot + 1}，這一竿照常拋竿（不用游動餌）"
                          + (suppressed ? @$"｜連續 {SwimbaitFailureGate.Threshold} 次失敗，清單改變前不再自動切換這一格" : @"")
                          + (mine ? @"" : @"｜等待中有別的切換送出"));
        CastLineMoochOrRelease(acCfg, lastFishCatchCfg, allowSwimbait: false);
        return true;
    }
}
