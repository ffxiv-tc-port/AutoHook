using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AutoHook.Classes;
using AutoHook.Conditions;
using AutoHook.Configurations;
using AutoHook.Enums;
using AutoHook.Fishing;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

namespace AutoHook.Ui;

public class SubTabFish
{
    private static CustomPresetConfig _preset = null!;

    public static void DrawFishTab(CustomPresetConfig presetCfg)
    {
        _preset = presetCfg;
        var listOfFish = presetCfg.ListOfFish;

        DrawDescription(listOfFish);

        using (var item = ImRaii.Child("###FishItems", new Vector2(0, 0), true))
        {
            for (var idx = 0; idx < listOfFish.Count; idx++)
            {
                var fish = listOfFish[idx];
                ImGui.PushID($"fishTab###{idx}");

                var count = FishingManager.FishingHelper.GetFishCount(fish.UniqueId);
                var fishCount = count > 0 ? $"({UIStrings.Caught_Counter} {count})" : "";

                if (DrawUtil.Checkbox($"###checkbox{idx}", ref fish.Enabled))
                    Service.Save();

                ImGui.SameLine(0, 6);
                var x = ImGui.GetCursorPosX();
                if (ImGui.CollapsingHeader($"{fish.Fish.Name} {fishCount}###a{idx}"))
                {
                    ImGui.SetCursorPosX(x);
                    ImGui.BeginGroup();
                    ImGui.Spacing();
                    DrawFishSearchBar(fish);
                    DrawDeleteButton(fish);
                    DrawUtil.SpacingSeparator();

                    DrawSurfaceSlapIdenticalCast(fish);
                    ImGui.Spacing();

                    DrawMooch(fish);
                    ImGui.Spacing();

                    DrawSwapBait(fish);
                    ImGui.Spacing();

                    DrawSwapPreset(fish);
                    ImGui.Spacing();

                    DrawStopAfter(fish);
                    ImGui.Spacing();

                    if (DrawUtil.Checkbox(UIStrings.Ignore_When_Intuition, ref fish.IgnoreOnIntuition))
                        Service.Save();

                    ImGui.EndGroup();
                }

                ImGui.Spacing();
                ImGui.PopID();
            }
        }
    }

    private static void DrawDescription(List<FishConfig> list)
    {
        if (ImGui.Button(UIStrings.Add))
        {
            if (list.All(x => x.Fish.Id != -1))
            {
                list.Add(new FishConfig(new BaitFishClass()));
            }

            Service.Save();
        }

        ImGui.SameLine();
        ImGui.Text($"{UIStrings.Add_new_fish} ({list.Count})");
        ImGui.SameLine();

        ImGui.SameLine();

        if (ImGui.Button($"{UIStrings.AddLastCatch} {Service.LastCatch.Name ?? "-"}"))
        {
            if (Service.LastCatch.Id == 0 || Service.LastCatch.Id == -1)
                return;
            if (list.Any(x => x.Fish.Id == Service.LastCatch.Id))
                return;

            list.Add(new FishConfig(Service.LastCatch));
            Service.Save();
        }
    }

    private static void DrawDeleteButton(FishConfig fishConfig)
    {
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash) && ImGui.GetIO().KeyShift)
        {
            _preset.RemoveItem(fishConfig.UniqueId);
            Service.Save();
        }

        ImGui.PopFont();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);
    }

    private static void DrawFishSearchBar(FishConfig fishConfig)
    {
        ImGui.PushID("DrawFishSearchBar");
        DrawUtil.DrawComboSelector<BaitFishClass>(
            GameRes.Fishes,
            (BaitFishClass fish) => fish.Name,
            fishConfig.Fish.Name,
            (BaitFishClass fish) => fishConfig.Fish = fish);

        ImGui.PopID();
    }

    private static void DrawSurfaceSlapIdenticalCast(FishConfig fishConfig)
    {
        ImGui.PushID($"{UIStrings.SurfaceSlapIdenticalCast}");

        if (ImGui.TreeNodeEx(UIStrings.SurfaceSlapIdenticalCast, ImGuiTreeNodeFlags.FramePadding))
        {
            fishConfig.SurfaceSlap.DrawConfig();

            fishConfig.IdenticalCast.DrawConfig();

            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private static void DrawMooch(FishConfig fishConfig)
    {
        ImGui.PushID(@"DrawMooch");
        if (ImGui.TreeNodeEx(UIStrings.Mooch_Setting, ImGuiTreeNodeFlags.FramePadding))
        {
            fishConfig.Mooch.DrawConfig();

            if (DrawUtil.Checkbox(UIStrings.Never_Mooch, ref fishConfig.NeverMooch, UIStrings.NeverMoochHelpText))
            {
                fishConfig.Mooch.Enabled = false;
                Service.Save();
            }

            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private static void DrawSwapBait(FishConfig fishConfig)
    {
        ImGui.PushID("DrawSwapBait");

        var alreadySwapped = "";
        if (FishingManager.FishingHelper.SwappedBait(fishConfig.UniqueId))
            alreadySwapped = UIStrings.AlreadySwapped;

        DrawUtil.DrawCheckboxTree($"{UIStrings.Swap_Bait} {alreadySwapped}", ref fishConfig.SwapBait,
            () =>
            {
                DrawUtil.DrawComboSelector(
                    GameRes.Baits,
                    bait => bait.Name,
                    fishConfig.BaitToSwap.Name,
                    bait => fishConfig.BaitToSwap = bait);

                ImGui.Spacing();

                DrawUtil.DrawWordWrappedString(UIStrings.AfterBeingCaught);

                ImGui.SameLine();
                ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt(UIStrings.TimeS, ref fishConfig.SwapBaitCount))
                {
                    if (fishConfig.SwapBaitCount < 1)
                        fishConfig.SwapBaitCount = 1;

                    Service.Save();
                }
            }
        );

        ImGui.PopID();
    }

    /// <summary>
    /// 匯入的 preset 用的是「條件式換 preset」（上游 config v6／v7 的格式），而畫面上那個
    /// <c>Swap Preset</c> 勾選框綁的是舊的 <c>SwapPresets</c> 旗標 —— 條件式的 preset 那個旗標是
    /// false，勾選框會顯示成「沒有開啟」。
    ///
    /// 🔴 那是**騙人的**：它其實會換。所以在勾選框上面直接畫一列，把「有條件、換去哪、條件是什麼」
    ///    講明白。詳細條件放 tooltip（長字串不佔列上版面），但「有這回事」必須在列上看得見。
    ///
    /// ⚠️ 這裡只用 <see cref="ConditionEvaluator.DescribeShape"/>（不讀遊戲狀態）——
    ///    設定視窗可以在標題畫面開著，在那裡去解原生指標就是 AccessViolation。
    /// </summary>
    private static void DrawSwapPresetConditionNotice(FishConfig fishConfig)
    {
        if (!ConditionEvaluator.HasAnyCondition(fishConfig.SwapPresetConditionSet))
            return;

        var supported = ConditionEvaluator.IsSupported(fishConfig.SwapPresetConditionSet);

        ImGui.TextColored(supported ? ImGuiColors.HealerGreen : ImGuiColors.DalamudOrange,
            supported ? @"● 由匯入的條件控制換 preset" : @"● 匯入的換階條件含有本外掛還不支援的項目");
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.ParsedGold, @$"→ {fishConfig.PresetToSwap}");

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(@$"條件：{ConditionEvaluator.DescribeShape(fishConfig.SwapPresetConditionSet)}");
            ImGui.TextUnformatted(supported
                ? @"下面那個勾選框是舊版的計數式設定，條件存在時以條件為準。"
                : @"含有不支援的條件時一律「不換」，並在記錄裡寫一行 Information 說明是哪個條件。");
            ImGui.EndTooltip();
        }
    }

    private static void DrawSwapPreset(FishConfig fishConfig)
    {
        ImGui.PushID("DrawSwapPreset");

        DrawSwapPresetConditionNotice(fishConfig);

        var alreadySwapped = "";
        if (FishingManager.FishingHelper.SwappedPreset(fishConfig.UniqueId))
            alreadySwapped = UIStrings.AlreadySwapped;
        DrawUtil.DrawCheckboxTree($"{UIStrings.Swap_Preset} {alreadySwapped}", ref fishConfig.SwapPresets,
            () =>
            {
                DrawUtil.DrawComboSelector(
                    Service.Configuration.HookPresets.CustomPresets,
                    preset => preset.PresetName,
                    fishConfig.PresetToSwap,
                    preset => fishConfig.PresetToSwap = preset.PresetName);

                ImGui.Spacing();

                DrawUtil.DrawWordWrappedString(UIStrings.AfterBeingCaught);

                ImGui.SameLine();
                ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt(UIStrings.TimeS, ref fishConfig.SwapPresetCount))
                {
                    if (fishConfig.SwapPresetCount < 1)
                        fishConfig.SwapPresetCount = 1;

                    Service.Save();
                }
            }
        );

        ImGui.PopID();
    }

    private static void DrawStopAfter(FishConfig fishConfig)
    {
        ImGui.PushID("DrawStopAfter");

        DrawUtil.DrawCheckboxTree(UIStrings.Stop_After_Caught, ref fishConfig.StopAfterCaught,
            () =>
            {
                ImGui.SetNextItemWidth(90 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt(UIStrings.TimeS, ref fishConfig.StopAfterCaughtLimit))
                {
                    if (fishConfig.StopAfterCaughtLimit < 1)
                        fishConfig.StopAfterCaughtLimit = 1;

                    Service.Save();
                }

                if (ImGui.RadioButton(UIStrings.Stop_Casting, fishConfig.StopFishingStep == FishingSteps.None))
                {
                    fishConfig.StopFishingStep = FishingSteps.None;
                    Service.Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(UIStrings.Auto_Cast_Stopped);

                if (ImGui.RadioButton(UIStrings.Quit_Fishing, fishConfig.StopFishingStep == FishingSteps.Quitting))
                {
                    fishConfig.StopFishingStep = FishingSteps.Quitting;
                    Service.Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(UIStrings.Quit_Action_HelpText);

                DrawUtil.Checkbox(UIStrings.Reset_the_counter, ref fishConfig.StopAfterResetCount);
            });
        ImGui.PopID();
    }
}