using System;
using System.ComponentModel;
using AutoHook.Classes.AutoCasts;
using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;

// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable MemberCanBePrivate.Global

namespace AutoHook.Classes;

public class BaseHookset
{
    // for future use, maybe we need a hooking condition under a different status?
    public uint RequiredStatus;

    private Guid _uniqueId;

    // Patience > Normal, Precision and Powerful
    public BaseBiteConfig PatienceWeak = new(HookType.Precision);
    public BaseBiteConfig PatienceStrong = new(HookType.Powerful);
    public BaseBiteConfig PatienceLegendary = new(HookType.Powerful);

    // Double Hook
    public bool UseDoubleHook;
    public bool LetFishEscapeDoubleHook;
    public BaseBiteConfig DoubleWeak = new(HookType.Double);
    public BaseBiteConfig DoubleStrong = new(HookType.Double);
    public BaseBiteConfig DoubleLegendary = new(HookType.Double);

    // Triple Hook
    public bool UseTripleHook;
    public bool LetFishEscapeTripleHook;
    public BaseBiteConfig TripleWeak = new(HookType.Triple);
    public BaseBiteConfig TripleStrong = new(HookType.Triple);
    public BaseBiteConfig TripleLegendary = new(HookType.Triple);

    // Stellar Hook（華麗提鉤，動作 ID 41278）：只能在宇宙探索的探索任務垂釣中使用，60 秒復唱、不耗 GP。
    // 🔑 預設開著是安全的：能不能用完全交給遊戲自己的 GetActionStatus／復唱時間判定，在別的地方不會改變任何行為。
    // ⚠️ 獨立成一層而不是「在 Patience 欄位選華麗提鉤」：那條路在華麗提鉤進 CD 時會退回普通提鉤（HookType.Normal）。
    // ⚠️ [DefaultValue(true)] 是必要的：匯出 preset 用 DefaultValueHandling.Ignore、匯入缺鍵吃欄位初始值，
    //    兩者的基準必須一致，否則使用者把它**關掉**後匯出，false 會被省略、對方匯入又變回 true。
    [DefaultValue(true)] public bool UseStellarHook = true;

    /// <summary>華麗提鉤的順位怎麼決定。預設 <see cref="StellarPriorityMode.Auto"/>。</summary>
    public StellarPriorityMode StellarPriority = StellarPriorityMode.Auto;

    /// <summary>
    /// 華麗提鉤要不要排在雙重／三重提鉤之前。
    /// 在 <see cref="StellarPriorityMode.Auto"/> 底下，這一格是**判不出任務型別時的退路**
    /// （不在宇宙探索、沒有進行中的任務、或該任務的計分方式不在對照表裡）。
    /// 預設 false（排在後面）。理由：雙重／三重提鉤一次能起 2/3 條魚，而華麗提鉤只起 1 條、
    /// 只是品質較好。不知道任務要什麼的時候，維持原本的吞吐量比賭品質安全。
    /// </summary>
    public bool StellarBeforeMultiHook;

    /// <summary>
    /// 任務說明自己講明「任意／每條魚都算分」時，忽略提鉤的時間窗設定。
    /// 🔴 理由：宇宙 preset 常拿時間窗來**挑特定魚種**（窗外的咬鉤一律放生），但計分方式是
    ///    「每條魚均給予評價」的任務裡任何一條魚都算分，放掉咬鉤是純粹的損失。
    /// ⚠️ 只在任務說明**自己講明**「任意／每一條」時才套用（WKSMissionText 115、121）。
    ///    113/114/141 那種「幾種不同／特定／目標水產品」的任務**不套用**（時間窗正是拿來選魚的）；
    ///    判不出任務型別時也不套用（不知道就不要放寬限制）。
    /// </summary>
    [DefaultValue(true)] public bool IgnoreTimersWhenEveryFishCounts = true;

    /// <summary>
    /// 這一竿要不要忽略提鉤時間窗。看 <see cref="IgnoreTimersWhenEveryFishCounts"/> 有沒有開，
    /// 以及目前任務是不是「任意／每條魚都算分」那一型。
    /// 不在宇宙探索、沒有進行中的任務、型別判不出來 → 一律 false（維持 preset 原本的行為）。
    /// </summary>
    public bool ShouldIgnoreHookTimers()
        => IgnoreTimersWhenEveryFishCounts && CosmicMissionInfo.GetCurrentProfile().EveryFishCounts;

    /// <summary>
    /// 依目前任務的計分方式，決定華麗提鉤要不要排在雙重／三重提鉤之前。
    ///
    /// 🔑 偵測不到就退回 <see cref="StellarBeforeMultiHook"/>，**不猜**。
    ///    「不知道」的正確行為是維持現狀，不是隨便選一邊 —— 選錯在遊戲裡完全看不出來。
    /// </summary>
    public bool ResolveStellarFirst() => StellarPriority switch
    {
        StellarPriorityMode.Evaluation => true,
        StellarPriorityMode.Quantity => false,
        _ => CosmicMissionInfo.GetCurrentFocus() switch
        {
            CosmicScoreFocus.Evaluation => true,
            CosmicScoreFocus.Quantity => false,
            _ => StellarBeforeMultiHook,
        },
    };

    public BaseBiteConfig StellarWeak = new(HookType.Stellar);
    public BaseBiteConfig StellarStrong = new(HookType.Stellar);
    public BaseBiteConfig StellarLegendary = new(HookType.Stellar);

    // Timeout
    //public double TimeoutMin = 0;
    public double TimeoutMax = 0;
    public double ChumTimeoutMax = 0;

    // Stop condition
    public bool StopAfterCaught;
    public bool StopAfterResetCount;
    public int StopAfterCaughtLimit = 1;

    public FishingSteps StopFishingStep = FishingSteps.None;

    public bool UseCustomStatusHook;

    public AutoLures CastLures = new();

    public Guid GetUniqueId()
    {
        if (_uniqueId == Guid.Empty)
            _uniqueId = Guid.NewGuid();

        return _uniqueId;
    }

    public BaseHookset(uint requiredStatus)
    {
        this.RequiredStatus = requiredStatus;
    }


    public void DrawOptions()
    {
        ImGui.PushID(@"BaseHookset");
        if (RequiredStatus != 0)
        {
            ImGui.Spacing();
            var statusName = MultiString.GetStatusName(RequiredStatus);
            DrawUtil.Checkbox(string.Format(UIStrings.UseConfigRequiredStatus, statusName), ref UseCustomStatusHook,
                UIStrings.RequiredStatusSettingHelpText);
        }

        DrawPatience();
        ImGui.Spacing();

        DrawDoubleHook();
        ImGui.Spacing();

        DrawTripleHook();
        ImGui.Spacing();

        DrawStellarHook();
        ImGui.Spacing();

        DrawTimeout();
        ImGui.Spacing();

        DrawLures();
        ImGui.Spacing();

        DrawStopCondition();

        ImGui.PopID();
    }

    private void DrawPatience()
    {
        if (ImGui.TreeNodeEx(UIStrings.NormalPatienceHookset,
                ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap))
        {
            PatienceWeak.DrawOptions(UIStrings.HookWeakExclamation, true);
            PatienceStrong.DrawOptions(UIStrings.HookStrongExclamation, true);
            PatienceLegendary.DrawOptions(UIStrings.HookLegendaryExclamation, true);
            ImGui.TreePop();
        }
    }

    private void DrawDoubleHook()
    {
        if (ImGui.TreeNodeEx(UIStrings.Double_Hook,
                ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap))
        {
            DrawUtil.Checkbox(UIStrings.UseDoubleHook, ref UseDoubleHook);
            DrawUtil.Checkbox(UIStrings.LetTheFishEscape, ref LetFishEscapeDoubleHook, UIStrings.LetFishEscapeHelpText);
            ImGui.Separator();
            DoubleWeak.DrawOptions(UIStrings.HookWeakExclamation);
            DoubleStrong.DrawOptions(UIStrings.HookStrongExclamation);
            DoubleLegendary.DrawOptions(UIStrings.HookLegendaryExclamation);
            ImGui.TreePop();
        }
    }

    private void DrawTripleHook()
    {
        if (ImGui.TreeNodeEx(UIStrings.Triple_Hook,
                ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap))
        {
            DrawUtil.Checkbox(UIStrings.UseTripleHook, ref UseTripleHook);
            DrawUtil.Checkbox(UIStrings.LetTheFishEscape, ref LetFishEscapeTripleHook, UIStrings.LetFishEscapeHelpText);
            ImGui.Separator();
            TripleWeak.DrawOptions(UIStrings.HookWeakExclamation);
            TripleStrong.DrawOptions(UIStrings.HookStrongExclamation);
            TripleLegendary.DrawOptions(UIStrings.HookLegendaryExclamation);
            ImGui.TreePop();
        }
    }

    private void DrawStellarHook()
    {
        // 🔴 每一個交給 ImGui 的在地化字串都要過 Loc.Safe：空字串在原生邊界會變成空指標，
        //    igFindRenderedTextEnd 從位址 0 開始掃 → 遊戲當場結束。
        // ⚠️ TreeNodeEx 的 id 用固定英文再接 ###：id 跟著語系跑會讓展開狀態在換語言時丟失，
        //    而且那個語系的值是空的時連 id 都會是空字串。可見文字放 ### 後面。
        var title = Loc.Safe(UIStrings.StellarHookset, nameof(UIStrings.StellarHookset), @"Stellar Hookset");
        if (ImGui.TreeNodeEx($"{title}###StellarHookSection",
                ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap))
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey,
                Loc.Safe(UIStrings.StellarHookCosmicOnly, nameof(UIStrings.StellarHookCosmicOnly),
                    @"Cosmic Exploration only. 60s cooldown, no GP cost."));

            DrawUtil.Checkbox(
                Loc.Safe(UIStrings.UseStellarHook, nameof(UIStrings.UseStellarHook),
                    @"Use Stellar Hookset when available"),
                ref UseStellarHook,
                Loc.Safe(UIStrings.UseStellarHookHelpText, nameof(UIStrings.UseStellarHookHelpText),
                    @"Uses Stellar Hookset whenever the game says it is usable."));

            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.DalamudYellow,
                Loc.Safe(UIStrings.StellarPriorityLabel, nameof(UIStrings.StellarPriorityLabel),
                    @"Stellar Hookset priority"));
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(
                Loc.Safe(UIStrings.StellarPriorityHelpText, nameof(UIStrings.StellarPriorityHelpText),
                    @"Auto follows the current mission's scoring method."));

            ImGui.Indent();
            DrawPriorityRadio(
                Loc.Safe(UIStrings.StellarPriorityAuto, nameof(UIStrings.StellarPriorityAuto), @"Auto"),
                StellarPriorityMode.Auto);
            DrawPriorityRadio(
                Loc.Safe(UIStrings.StellarPriorityEvaluation, nameof(UIStrings.StellarPriorityEvaluation),
                    @"Evaluation first"), StellarPriorityMode.Evaluation);
            DrawPriorityRadio(
                Loc.Safe(UIStrings.StellarPriorityQuantity, nameof(UIStrings.StellarPriorityQuantity),
                    @"Quantity first"), StellarPriorityMode.Quantity);

            // 這一格只有「自動」才有意義 —— 它是判不出型別時的退路，不是獨立開關。
            if (StellarPriority == StellarPriorityMode.Auto)
            {
                DrawUtil.Checkbox(
                    Loc.Safe(UIStrings.StellarHookBeforeMultiHook, nameof(UIStrings.StellarHookBeforeMultiHook),
                        @"When the mission type cannot be detected, favour evaluation"),
                    ref StellarBeforeMultiHook,
                    Loc.Safe(UIStrings.StellarHookBeforeMultiHookHelpText,
                        nameof(UIStrings.StellarHookBeforeMultiHookHelpText),
                        @"Which way Auto goes when the mission type is unknown."));
            }

            ImGui.Unindent();

            ImGui.Spacing();
            DrawUtil.Checkbox(
                Loc.Safe(UIStrings.IgnoreTimersEveryFishCounts, nameof(UIStrings.IgnoreTimersEveryFishCounts),
                    @"Ignore hook timers when every fish counts"),
                ref IgnoreTimersWhenEveryFishCounts,
                Loc.Safe(UIStrings.IgnoreTimersEveryFishCountsHelpText,
                    nameof(UIStrings.IgnoreTimersEveryFishCountsHelpText),
                    @"Only applies to missions whose own text says any/every fish is rated."));

            ImGui.Separator();
            StellarWeak.DrawOptions(
                Loc.Safe(UIStrings.HookWeakExclamation, nameof(UIStrings.HookWeakExclamation), @"Weak Bite (!)"));
            StellarStrong.DrawOptions(
                Loc.Safe(UIStrings.HookStrongExclamation, nameof(UIStrings.HookStrongExclamation),
                    @"Strong Bite (!!)"));
            StellarLegendary.DrawOptions(
                Loc.Safe(UIStrings.HookLegendaryExclamation, nameof(UIStrings.HookLegendaryExclamation),
                    @"Legendary Bite (!!!)"));
            ImGui.TreePop();
        }
    }

    private void DrawPriorityRadio(string label, StellarPriorityMode mode)
    {
        if (!ImGui.RadioButton(label, StellarPriority == mode))
            return;

        StellarPriority = mode;
        Service.Save();
    }

    private void DrawTimeout()
    {
        if (ImGui.TreeNodeEx(UIStrings.Timeout,
                ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap))
        {
            ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.TimeoutOption);
            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputDouble(UIStrings.TimeLimit, ref TimeoutMax, .1, 1, @"%.1f%"))
            {
                switch (TimeoutMax)
                {
                    case 0.1:
                        TimeoutMax = 2;
                        break;
                    case <= 0:
                    case <= 1.9: //This makes the option turn off if delay = 2 seconds when clicking the minus.
                        TimeoutMax = 0;
                        break;
                    case > 99:
                        TimeoutMax = 99;
                        break;
                }

                Service.Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker($"{UIStrings.TimeoutHelpText}\n\n{UIStrings.DoesntHaveAffectUnderChum}");

            ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
            if (ImGui.InputDouble(UIStrings.ChumTimeLimit, ref ChumTimeoutMax, .1, 1, @"%.1f%"))
            {
                switch (ChumTimeoutMax)
                {
                    case 0.1:
                        ChumTimeoutMax = 2;
                        break;
                    case <= 0:
                    case <= 1.9: //This makes the option turn off if delay = 2 seconds when clicking the minus.
                        ChumTimeoutMax = 0;
                        break;
                    case > 99:
                        ChumTimeoutMax = 99;
                        break;
                }

                Service.Save();
            }

            ImGui.SameLine();
            ImGuiComponents.HelpMarker(UIStrings.TimeoutHelpText);
            ImGui.TreePop();
        }
    }


    private void DrawLures()
    {
        ImGui.PushID($"Lures");
        
        CastLures.DrawConfig();

        ImGui.PopID();
    }

    private void DrawStopCondition()
    {
        DrawUtil.DrawCheckboxTree(UIStrings.StopAfterHooking, ref StopAfterCaught,
            () =>
            {
                ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt(UIStrings.TimeS, ref StopAfterCaughtLimit))
                {
                    if (StopAfterCaughtLimit < 1)
                        StopAfterCaughtLimit = 1;
                    Service.Save();
                }

                ImGui.Spacing();
                if (ImGui.RadioButton(UIStrings.Stop_Casting, StopFishingStep == FishingSteps.None))
                {
                    StopFishingStep = FishingSteps.None;
                    Service.Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(UIStrings.Auto_Cast_Stopped);

                if (ImGui.RadioButton(UIStrings.Quit_Fishing, StopFishingStep == FishingSteps.Quitting))
                {
                    StopFishingStep = FishingSteps.Quitting;
                    Service.Save();
                }

                DrawUtil.Checkbox(UIStrings.Reset_the_counter, ref StopAfterResetCount);
            });
    }
}