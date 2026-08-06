using System.Linq;
using AutoHook.Classes;
using AutoHook.Conditions;
using AutoHook.Configurations;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.SeFunctions;
using AutoHook.Utils;

namespace AutoHook.Fishing;

public partial class FishingManager
{
    private FishConfig? GetLastCatchConfig()
    {
        if (_lastCatch == null)
            return null;

        return Presets.SelectedPreset?.GetFishById(_lastCatch.Id) ?? Presets.DefaultPreset.GetFishById(_lastCatch.Id);
    }
    
    private bool UseFishCaughtActions(FishConfig? lastFishCatchCfg)
    {
        BaseActionCast? cast = null;

        if (lastFishCatchCfg == null || !lastFishCatchCfg.Enabled || _lastStep.HasFlag(FishingSteps.PresetSwapped))
            return false;

        if (PlayerRes.HasStatus(IDs.Status.FishersIntuition) && lastFishCatchCfg.IgnoreOnIntuition)
            return false;

        var caughtCount = FishingHelper.GetFishCount(lastFishCatchCfg.UniqueId);

        if (lastFishCatchCfg.IdenticalCast.IsAvailableToCast(caughtCount))
            cast = lastFishCatchCfg.IdenticalCast;

        if (lastFishCatchCfg.SurfaceSlap.IsAvailableToCast())
            cast = lastFishCatchCfg.SurfaceSlap;

        if (cast != null)
        {
            PlayerRes.CastActionDelayed(cast.Id, cast.ActionType, cast.Name);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 上一次寫出去的「為什麼還沒換 preset」診斷。內容沒變就不重複寫 ——
    /// <see cref="CheckPluginActions"/> 是 500ms 節流的迴圈，不去重會把 log 洗爆。
    /// </summary>
    private string _lastSwapDiagnostic = @"";

    private void CheckFishCaughtSwap(FishConfig? lastCatchCfg)
    {
        if (lastCatchCfg == null || !lastCatchCfg.Enabled)
            return;

        var guid = lastCatchCfg.UniqueId;
        var caughtCount = FishingHelper.GetFishCount(guid);

        // ── 換 preset ────────────────────────────────────────────────────────
        // 兩種寫法並存：
        //   舊（AH4_／既有使用者）＝ SwapPresets 旗標 ＋ SwapPresetCount 計數
        //   新（AH6_／AH7_／上游 config v6+）＝ SwapPresetConditionSet 條件
        // 🔴 新的那份沒有條件時（null 或只有空群組）**完全走舊路徑**，行為與修改前逐位元相同。
        var swapConditions = lastCatchCfg.SwapPresetConditionSet;
        var useConditions = ConditionEvaluator.HasAnyCondition(swapConditions);
        var swapArmed = useConditions || lastCatchCfg.SwapPresets;

        if (swapArmed && !FishingHelper.SwappedPreset(guid) &&
            !_lastStep.HasFlag(FishingSteps.PresetSwapped))
        {
            var ctx = ConditionContext.AfterCatch();

            // 不支援的條件一律回 false ＝「不換」。這條路徑上「亂換」比「不換」危險得多。
            var thresholdMet = useConditions
                ? ConditionEvaluator.Passes(swapConditions, ctx, unconfiguredResult: false,
                    unsupportedResult: false, where: @"換 preset")
                : caughtCount >= lastCatchCfg.SwapPresetCount;

            var alreadySelected = lastCatchCfg.PresetToSwap == Presets.SelectedPreset?.PresetName;

            if (thresholdMet && !alreadySelected)
            {
                var preset =
                    Presets.CustomPresets.FirstOrDefault(preset => preset.PresetName == lastCatchCfg.PresetToSwap);

                FishingHelper.AddPresetSwap(guid); // one try per catch
                _lastStep |= FishingSteps.PresetSwapped;

                if (preset == null)
                {
                    Service.PrintChat(@$"Preset {lastCatchCfg.PresetToSwap} not found.");

                    // 🔴 PrintChat 會被 ShowChatLogs 關掉，而這是整條狀態機斷掉的地方 ——
                    //    多階段 preset 只要有一份被改名／沒匯入，就會停在這一階且沒有任何徵兆。
                    Service.PrintInfo(
                        @$"[換 preset] 找不到名為「{lastCatchCfg.PresetToSwap}」的 preset（由 {lastCatchCfg.Fish.Name} "
                        + @$"觸發，目前在「{Presets.SelectedPreset?.PresetName ?? @"（未選取）"}」）。"
                        + @"多階段 preset 會停在這一階 —— 請確認同資料夾的每一份都匯入了、而且名字沒有被改過。");
                }
                else
                {
                    Service.Save();
                    Presets.SelectedPreset = preset;
                    Service.PrintChat(@$"[Fish Caught] Swapping current preset to {lastCatchCfg.PresetToSwap}");
                    Service.PrintInfo(
                        @$"[換 preset] {lastCatchCfg.Fish.Name} 觸發換階：→「{lastCatchCfg.PresetToSwap}」"
                        + @$"（判斷依據：{(useConditions ? ConditionEvaluator.Describe(swapConditions, ctx) : @$"計數 {caughtCount} >= {lastCatchCfg.SwapPresetCount}")}）");
                    _lastSwapDiagnostic = @"";
                    Service.Save();
                }
            }
            else if (useConditions)
            {
                // 「為什麼還沒換」：目前在哪一階、條件各自的目前值與門檻。
                var reason = alreadySelected
                    ? @"已經在目標 preset 上了"
                    : ConditionEvaluator.Describe(swapConditions, ctx);

                var line = @$"[換 preset] 尚未換階：目前「{Presets.SelectedPreset?.PresetName ?? @"（未選取）"}」"
                           + @$"→ 目標「{lastCatchCfg.PresetToSwap}」，由 {lastCatchCfg.Fish.Name} 判斷｜{reason}";

                if (line != _lastSwapDiagnostic)
                {
                    _lastSwapDiagnostic = line;
                    Service.PrintInfo(line);
                }
            }
        }

        if (lastCatchCfg.SwapBait && !FishingHelper.SwappedBait(guid) && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
        {
            if (caughtCount >= lastCatchCfg.SwapBaitCount &&
                lastCatchCfg.BaitToSwap.Id != Service.BaitManager.Current)
            {
                var result = Service.BaitManager.ChangeBait(lastCatchCfg.BaitToSwap);

                FishingHelper.AddBaitSwap(guid); // one try per catch
                _lastStep |= FishingSteps.BaitSwapped;
                if (result == BaitManager.ChangeBaitReturn.Success)
                {
                    Service.PrintChat(@$"[Fish Caught] Swapping bait to {lastCatchCfg.BaitToSwap.Name}");
                    Service.Save();
                }
            }
        }
    }
}