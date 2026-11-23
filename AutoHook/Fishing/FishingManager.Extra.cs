using System.Collections.Generic;
using System.Linq;
using AutoHook.Conditions;
using AutoHook.Configurations;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.SeFunctions;
using AutoHook.Utils;

namespace AutoHook.Fishing;

public partial class FishingManager
{
    public ExtraConfig GetExtraCfg()
    {
        return Presets.SelectedPreset?.ExtraCfg.Enabled ?? false
            ? Presets.SelectedPreset.ExtraCfg
            : Presets.DefaultPreset.ExtraCfg;
    }

    private void CheckExtraActions(ExtraConfig extraCfg)
    {
        CheckIntuition(extraCfg);
        CheckSpectral(extraCfg);
        CheckAnglersArt(extraCfg);
        CheckTriggers();
    }

    /// <summary>規則觸發的換 preset 在同一輪內最多連鎖幾次（上游同值）。</summary>
    private const int TriggerSwapCap = 8;

    /// <summary>同一句規則診斷只寫一次（找不到 preset、連鎖過長）。</summary>
    private readonly HashSet<string> _reportedTriggerIssues = new();

    /// <summary>「仍需換餌」會每輪重觸發；同一句不重寫。</summary>
    private string _lastTriggerLine = @"";

    /// <summary>
    /// 上游 v6+ 的 ExtraCfg.Triggers。語意照上游：條件由假轉真時觸發；仍成立而且換 preset／換餌還沒達成時再觸發。
    /// 換了 preset 就在同一輪改看新 preset 的規則（資料夾狀態機一輪內走完）。
    /// </summary>
    private void CheckTriggers()
    {
        for (var iteration = 0; iteration < TriggerSwapCap; iteration++)
        {
            var extraCfg = GetExtraCfg();
            if (extraCfg.Triggers.Count == 0)
                return;

            var before = Presets.SelectedPreset;
            RunTriggersOnce(extraCfg);

            if (ReferenceEquals(before, Presets.SelectedPreset))
                return;
        }

        ReportTriggerIssueOnce(@$"[規則] 換 preset 連鎖超過 {TriggerSwapCap} 次（規則互相觸發），這一輪停在「{Presets.SelectedPreset?.PresetName ?? @"（未選取）"}」。");
    }

    private void RunTriggersOnce(ExtraConfig extraCfg)
    {
        var states = extraCfg.LastTriggerStates;
        var ctx = ConditionContext.AfterCatch();

        for (var i = 0; i < extraCfg.Triggers.Count; i++)
        {
            var trig = extraCfg.Triggers[i];
            if (trig is not { Enabled: true, ConditionSet: not null })
                continue;

            var current = ConditionEvaluator.Passes(trig.ConditionSet, ctx, unconfiguredResult: false,
                unsupportedResult: false, where: @"規則");
            var last = i < states.Count && states[i];

            while (states.Count <= i)
                states.Add(false);
            states[i] = current;

            if (!current || (last && !TriggerSwapStillNeeded(trig)))
                continue;

            var line = @$"[規則] 「{Presets.SelectedPreset?.PresetName ?? @"（未選取）"}」第 {i + 1} 條成立："
                       + ConditionEvaluator.Describe(trig.ConditionSet, ctx);
            if (line != _lastTriggerLine)
            {
                _lastTriggerLine = line;
                Service.PrintInfo(line);
            }

            ApplyTrigger(trig);
        }
    }

    private bool TriggerSwapStillNeeded(ExtraTrigger trig)
    {
        if (trig.SwapPreset && !string.IsNullOrEmpty(trig.PresetToSwap) && trig.PresetToSwap != @"-"
            && Presets.SelectedPreset?.PresetName != trig.PresetToSwap)
            return true;

        return trig.SwapBait && trig.BaitToSwap.Id > 0 && !Service.BaitManager.IsBaitEquipped((uint)trig.BaitToSwap.Id)
               && PlayerRes.HasItem((uint)trig.BaitToSwap.Id) > 0;
    }

    private void ApplyTrigger(ExtraTrigger trig)
    {
        if (trig.StopAction != ExtraStopAction.None)
        {
            // 對應既有的「漁師之識結束時停止／收竿」：StopOnly＝不再拋竿，其餘＝收竿。
            _lastStep = trig.StopAction == ExtraStopAction.StopOnly ? FishingSteps.None : FishingSteps.Quitting;
            Service.PrintChat(trig.StopAction == ExtraStopAction.StopOnly ? @"[Extra] 規則：停止拋竿" : @"[Extra] 規則：收竿");
        }

        if (trig.ResetFishCaughtCounter)
        {
            var owner = Presets.SelectedPreset?.ExtraCfg.Enabled == true ? Presets.SelectedPreset : Presets.DefaultPreset;
            owner.ResetCounter();
        }

        if (trig.SwapPreset && !string.IsNullOrEmpty(trig.PresetToSwap) && trig.PresetToSwap != @"-"
            && Presets.SelectedPreset?.PresetName != trig.PresetToSwap)
        {
            var preset = Presets.CustomPresets.FirstOrDefault(p => p.PresetName == trig.PresetToSwap);
            if (preset == null)
            {
                ReportTriggerIssueOnce(@$"[規則] 找不到名為「{trig.PresetToSwap}」的 preset（目前在「{Presets.SelectedPreset?.PresetName ?? @"（未選取）"}」）。");
            }
            else
            {
                preset.ExtraCfg.LastTriggerStates.Clear();
                Presets.SelectedPreset = preset;
                Service.PrintChat(@$"[Extra] 規則：換成 preset「{trig.PresetToSwap}」");
                Service.Save();
            }
        }

        if (trig.SwapBait && trig.BaitToSwap.Id > 0 && !Service.BaitManager.IsBaitEquipped((uint)trig.BaitToSwap.Id))
        {
            var result = Service.BaitManager.ChangeBait(trig.BaitToSwap);
            var line = @$"[規則] 換餌 → {trig.BaitToSwap.Name} (#{trig.BaitToSwap.Id})：{result}";
            if (result == BaitManager.ChangeBaitReturn.Success)
                Service.PrintInfo(line);
            else
                ReportTriggerIssueOnce(line);
        }
    }

    private void ReportTriggerIssueOnce(string line)
    {
        if (_reportedTriggerIssues.Add(line))
            Service.PrintInfo(line);
    }

    private void CheckSpectral(ExtraConfig extraCfg)
    {
        if (_spectralCurrentStatus == SpectralCurrentStatus.NotActive)
        {
            if (!PlayerRes.IsInActiveSpectralCurrent())
                return;

            _spectralCurrentStatus = SpectralCurrentStatus.Active;

            if (!extraCfg.Enabled)
                return;

            // Check if the preset was already swapped
            if (extraCfg.SwapPresetSpectralCurrentGain && !_lastStep.HasFlag(FishingSteps.PresetSwapped))
            {
                var preset =
                    Presets.CustomPresets.FirstOrDefault(preset =>
                        preset.PresetName == extraCfg.PresetToSwapSpectralCurrentGain);

                _lastStep |= FishingSteps.PresetSwapped; // one try
                if (preset != null)
                {
                    Service.Save();
                    Presets.SelectedPreset = preset;
                    Service.PrintChat(
                        @$"[Extra] Spectral Current Active: Swapping preset to {extraCfg.PresetToSwapSpectralCurrentGain}");
                    Service.Save();
                }
                else
                    Service.PrintChat(@$"Preset {extraCfg.PresetToSwapSpectralCurrentGain} not found.");
            }

            // Check if the bait was already swapped
            if (extraCfg.SwapBaitSpectralCurrentGain && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
            {
                var result = Service.BaitManager.ChangeBait(extraCfg.BaitToSwapSpectralCurrentGain);

                _lastStep |= FishingSteps.BaitSwapped; // one try
                if (result == BaitManager.ChangeBaitReturn.Success)
                {
                    Service.PrintChat(
                        @$"[Extra] Spectral Current Active: Swapping bait to {extraCfg.BaitToSwapSpectralCurrentGain.Name}");
                    Service.Save();
                }
            }
        }

        if (_spectralCurrentStatus == SpectralCurrentStatus.Active)
        {
            if (PlayerRes.IsInActiveSpectralCurrent())
                return;

            _spectralCurrentStatus = SpectralCurrentStatus.NotActive;

            // Check if the preset was already swapped
            if (!extraCfg.Enabled)
                return;

            if (extraCfg.SwapPresetSpectralCurrentLost && !_lastStep.HasFlag(FishingSteps.PresetSwapped))
            {
                var preset =
                    Presets.CustomPresets.FirstOrDefault(preset =>
                        preset.PresetName == extraCfg.PresetToSwapSpectralCurrentLost);

                _lastStep |= FishingSteps.PresetSwapped; // one try

                if (preset != null)
                {
                    Service.Save();
                    Presets.SelectedPreset = preset;
                    Service.PrintChat(
                        @$"[Extra] Spectral Current Ended: Swapping preset to {extraCfg.PresetToSwapIntuitionLost}");
                    Service.Save();
                }
                else
                    Service.PrintChat(@$"Preset {extraCfg.SwapPresetSpectralCurrentLost} not found.");
            }

            // Check if the bait was already swapped
            if (extraCfg.SwapBaitSpectralCurrentLost && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
            {
                var result = Service.BaitManager.ChangeBait(extraCfg.BaitToSwapSpectralCurrentLost);

                _lastStep |= FishingSteps.BaitSwapped; // one try

                if (result == BaitManager.ChangeBaitReturn.Success)
                {
                    Service.PrintChat(
                        @$"[Extra] Spectral Current Ended: Swapping bait to {extraCfg.BaitToSwapSpectralCurrentLost.Name}");
                    Service.Save();
                }
            }
        }
    }

    private void CheckIntuition(ExtraConfig extraCfg)
    {
        if (IntuitionStatus == IntuitionStatus.NotActive)
        {
            if (!PlayerRes.HasStatus(IDs.Status.FishersIntuition))
                return;

            IntuitionStatus = IntuitionStatus.Active; // only one try

            if (!extraCfg.Enabled)
                return;
            ExtraCfgGainedIntuition(extraCfg);
        }

        if (IntuitionStatus == IntuitionStatus.Active)
        {
            if (PlayerRes.HasStatus(IDs.Status.FishersIntuition))
                return;

            IntuitionStatus = IntuitionStatus.NotActive; // only one try

            if (!extraCfg.Enabled)
                return;

            ExtraCfgLostIntuition(extraCfg);
        }
    }

    private void ExtraCfgGainedIntuition(ExtraConfig extraCfg)
    {
        // Check if the preset was already swapped
        if (extraCfg.SwapPresetIntuitionGain && !_lastStep.HasFlag(FishingSteps.PresetSwapped))
        {
            var preset = Presets.CustomPresets.FirstOrDefault(preset =>
                preset.PresetName == extraCfg.PresetToSwapIntuitionGain);

            _lastStep |= FishingSteps.PresetSwapped;
            if (preset != null)
            {
                Service.Save();
                Presets.SelectedPreset = preset;
                Service.PrintChat(
                    @$"[Extra] Intuition Active - Swapping preset to {extraCfg.PresetToSwapIntuitionGain}");
                Service.Save();
            }
            else
                Service.PrintChat(
                    @$"[Extra] Intuition Active - Preset {extraCfg.PresetToSwapIntuitionGain} not found.");
        }

        // Check if the bait was already swapped
        if (extraCfg.SwapBaitIntuitionGain && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
        {
            var result = Service.BaitManager.ChangeBait(extraCfg.BaitToSwapIntuitionGain);

            _lastStep |= FishingSteps.BaitSwapped; // one try per catch

            if (result == BaitManager.ChangeBaitReturn.Success)
            {
                Service.PrintChat(
                    @$"[Extra] Intuition Active - Swapping bait to {extraCfg.BaitToSwapIntuitionGain.Name}");
                Service.Save();
            }
        }
    }

    private void ExtraCfgLostIntuition(ExtraConfig extraCfg)
    {
        // Check if the preset was already swapped
        if (extraCfg.SwapPresetIntuitionLost && !_lastStep.HasFlag(FishingSteps.PresetSwapped))
        {
            var preset =
                Presets.CustomPresets.FirstOrDefault(preset =>
                    preset.PresetName == extraCfg.PresetToSwapIntuitionLost);

            _lastStep |= FishingSteps.PresetSwapped;

            if (preset != null)
            {
                Service.Save();
                // one try per catch
                Presets.SelectedPreset = preset;
                Service.PrintChat(@$"[Extra] Intuition Lost - Swapping preset to {extraCfg.PresetToSwapIntuitionLost}");
                Service.Save();
            }
            else
                Service.PrintChat(@$"[Extra] Intuition Lost - Preset {extraCfg.PresetToSwapIntuitionLost} not found.");
        }

        // Check if the bait was already swapped
        if (extraCfg.SwapBaitIntuitionLost && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
        {
            var result = Service.BaitManager.ChangeBait(extraCfg.BaitToSwapIntuitionLost);

            // one try per catch
            _lastStep |= FishingSteps.BaitSwapped;
            if (result == BaitManager.ChangeBaitReturn.Success)
            {
                Service.PrintChat(
                    @$"[Extra] Intuition Lost - Swapping bait to {extraCfg.BaitToSwapIntuitionLost.Name}");
                Service.Save();
            }
        }

        if (extraCfg.QuitOnIntuitionLost)
        {
            _lastStep = FishingSteps.Quitting;
        }

        if (extraCfg.StopOnIntuitionLost)
        {
            _lastStep = FishingSteps.None;
        }
    }

    private void CheckAnglersArt(ExtraConfig extraCfg)
    {
        if (!PlayerRes.HasAnglersArtStacks(extraCfg.AnglerStackQtd))
            return;

        if (extraCfg.SwapPresetAnglersArt && !_lastStep.HasFlag(FishingSteps.PresetSwapped))
        {
            var preset =
                Presets.CustomPresets.FirstOrDefault(preset =>
                    preset.PresetName == extraCfg.PresetToSwapAnglersArt);

            _lastStep |= FishingSteps.PresetSwapped;

            if (preset != null)
            {
                Service.Save();
                Presets.SelectedPreset = preset;
                Service.PrintChat(
                    @$"[Extra] Angler's Stack - Swapping preset to {extraCfg.PresetToSwapAnglersArt}");
                Service.Save();
            }
            else
                Service.PrintChat(@$"[Extra] Anglers Stack - Preset {extraCfg.PresetToSwapAnglersArt} not found.");
        }

        if (extraCfg.SwapBaitAnglersArt && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
        {
            var result = Service.BaitManager.ChangeBait(extraCfg.BaitToSwapAnglersArt);
            _lastStep |= FishingSteps.BaitSwapped;
            if (result == BaitManager.ChangeBaitReturn.Success)
            {
                Service.PrintChat(
                    @$"[Extra] Angler's Stack - Swapping bait to {extraCfg.BaitToSwapAnglersArt.Name}");
                Service.Save();
            }
        }
    }
}