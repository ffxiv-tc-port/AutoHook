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

    // Stellar Hook（華麗提鉤，動作 ID 41278）
    //
    // 遊戲說明：「以華麗無比的操竿技巧來提鉤，可以不受『提鉤成功率降低』狀態影響。
    //           此種方式釣起的魚完好無損，能夠提高評價。**該技能僅限在探索任務的垂釣中使用**」
    // 60 秒復唱、不耗 GP。
    //
    // 🔑 預設開著是安全的：這個動作在宇宙探索的探索任務以外一律不可用，
    //    而底下的判定完全交給遊戲自己的 GetActionStatus / 復唱時間
    //    （不是用地區 ID 或計時去猜），所以在別的地方這個選項不會改變任何行為。
    //
    // ⚠️ 為什麼要獨立成一層，而不是沿用原本「在 Patience 欄位選華麗提鉤」的做法：
    //    原本那條路在華麗提鉤進 CD 的那 55 秒會退回**普通提鉤**（HookType.Normal），
    //    連精準／強力提鉤都用不到（見 HookConfig.GetHook 的 Patience 分支）。
    //    獨立成一層之後，華麗提鉤用不了就直接往下走原本設定好的提鉤，不會被降級。
    //
    // ⚠️ [DefaultValue(true)] 是必要的，不是裝飾。
    //    匯出 preset 用 DefaultValueHandling.Ignore、匯入用 ObjectCreationHandling.Replace
    //    （缺鍵就吃欄位初始值），所以「初始值」與「Ignore 的比較基準」必須一致。
    //    沒有這個屬性時基準是 default(bool)=false：使用者把它**關掉**後匯出，
    //    false 會被當成預設值省略，對方匯入時又吃到初始值 true —— 關掉的狀態靜默消失。
    //    掛上之後：true 省略（匯入端初始值本來就是 true，等價），false 才寫出去。
    [DefaultValue(true)] public bool UseStellarHook = true;

    /// <summary>
    /// 華麗提鉤要不要排在雙重／三重提鉤之前。
    ///
    /// 預設 false（排在後面）。理由：雙重／三重提鉤一次能起 2/3 條魚，
    /// 而華麗提鉤只起 1 條，只是品質較好。在「要數量」的任務上把三重提鉤換成華麗提鉤
    /// 是淨損失，所以預設不搶它們的順位 —— 只在它們沒開／GP 不夠／條件不符時才補位。
    /// 要衝評價（分數型任務）的人再自己打開。
    /// </summary>
    public bool StellarBeforeMultiHook;

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
        if (ImGui.TreeNodeEx(UIStrings.StellarHookset,
                ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap))
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, UIStrings.StellarHookCosmicOnly);

            DrawUtil.Checkbox(UIStrings.UseStellarHook, ref UseStellarHook, UIStrings.UseStellarHookHelpText);
            DrawUtil.Checkbox(UIStrings.StellarHookBeforeMultiHook, ref StellarBeforeMultiHook,
                UIStrings.StellarHookBeforeMultiHookHelpText);
            ImGui.Separator();
            StellarWeak.DrawOptions(UIStrings.HookWeakExclamation);
            StellarStrong.DrawOptions(UIStrings.HookStrongExclamation);
            StellarLegendary.DrawOptions(UIStrings.HookLegendaryExclamation);
            ImGui.TreePop();
        }
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