using System.ComponentModel;
using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;

namespace AutoHook.Classes;

public class BaseBiteConfig
{
    [DefaultValue(true)]
    public bool HooksetEnabled = true;

    public bool EnableHooksetSwap;

    /// <summary>
    /// 「這一格提鉤可不可以出手」的條件（上游 config v6／v7 的格式）。
    ///
    /// 📌 上游把舊的 <see cref="HookTimerEnabled"/>／<see cref="MinHookTimer"/>／<see cref="MaxHookTimer"/>
    ///    整組換成了一條 <c>BiteTimerCD</c> 條件（上游 <c>HookConfig.SetBiteTimerInConditionSet</c> 就是
    ///    那個遷移函式）。所以匯入的新版 preset 裡**時間窗只存在於這個屬性**，
    ///    舊欄位一律缺鍵 → 落在 false／0 → 我們的 <c>CheckTimer</c> 等於完全沒有限制。
    ///
    /// 🔴 <c>null</c> 時完全不參與判斷（＝既有使用者與所有 AH4_ preset 行為逐位元相同）。
    ///    含有我們不認得的條件時**也不擋** —— 見 <see cref="Conditions.ConditionEvaluator"/> 的說明：
    ///    在提鉤這條路徑上「不認得就不提鉤」會讓外掛看起來整個壞掉，比忽略條件更糟。
    /// </summary>
    public Conditions.ConditionSet? ConditionSet { get; set; }

    public bool HookTimerEnabled;
    public double MinHookTimer;
    public double MaxHookTimer;

    public bool ChumTimerEnabled;
    public double ChumMinHookTimer;
    public double ChumMaxHookTimer;

    public bool OnlyWhenActiveSlap;
    public bool OnlyWhenNotActiveSlap;

    public bool OnlyWhenActiveIdentical;
    public bool OnlyWhenNotActiveIdentical;

    public bool PrizeCatchReq;
    public bool PrizeCatchNotReq;
    
    public HookType HooksetType;

    public BaseBiteConfig(HookType type)
    {
        HooksetType = type;
    }

    public void DrawOptions(string biteName, bool enableSwap = false)
    {
        EnableHooksetSwap = enableSwap;
        ImGui.PushID(@$"{biteName}");

        DrawUtil.DrawCheckboxTree(biteName, ref HooksetEnabled,
            () =>
            {
                DrawUtil.DrawTreeNodeEx(UIStrings.Conditions, () =>
                {
                    ImGui.Indent();
                    DrawUtil.DrawTreeNodeEx(UIStrings.Surface_Slap_Options, DrawSurfaceSwap);
               
                    DrawUtil.DrawTreeNodeEx(UIStrings.Identical_Cast_Options, DrawIdenticalCast);
                    
                    DrawUtil.DrawTreeNodeEx(UIStrings.Prize_Catch_Options, DrawPrizeCatch);
                    
                    ImGui.Unindent();
                    
                }, UIStrings.Conditions_HelpText);
                
                if (EnableHooksetSwap)
                    DrawUtil.DrawTreeNodeEx(UIStrings.HookType, DrawBite, UIStrings.HookWillBeUsedIfPatienceIsNotUp);
                
                DrawUtil.DrawTreeNodeEx(UIStrings.HookingTimer,DrawTimers, UIStrings.HookingTimerHelpText);
                
            });

        ImGui.PopID();
    }

    private void DrawBite()
    {
        ImGui.Indent();
        
        if (ImGui.RadioButton(UIStrings.Normal_Hook, HooksetType == HookType.Normal))
        {
            HooksetType = HookType.Normal;
            Service.Save();
        }

        if (ImGui.RadioButton(UIStrings.PrecisionHookset, HooksetType == HookType.Precision))
        {
            HooksetType = HookType.Precision;
            Service.Save();
        }

        if (ImGui.RadioButton(UIStrings.PowerfulHookset, HooksetType == HookType.Powerful))
        {
            HooksetType = HookType.Powerful;
            Service.Save();
        }
        
        if (ImGui.RadioButton(UIStrings.StellarHookset, HooksetType == HookType.Stellar))
        {
            HooksetType = HookType.Stellar;
            Service.Save();
        }
        
        ImGui.Unindent();
    }

    private void DrawSurfaceSwap()
    {
        ImGui.Indent();
        
        if (DrawUtil.Checkbox(UIStrings.OnlyHookWhenActiveSurfaceSlap, ref OnlyWhenActiveSlap))
        {
            OnlyWhenNotActiveSlap = false;
            Service.Save();
        }

        if (DrawUtil.Checkbox(UIStrings.OnlyHookWhenNOTActiveSurfaceSlap, ref OnlyWhenNotActiveSlap))
        {
            OnlyWhenActiveSlap = false;
            Service.Save();
        }
        
        ImGui.Unindent();
    }

    private void DrawIdenticalCast()
    {
        ImGui.Indent();
        
        if (DrawUtil.Checkbox(UIStrings.OnlyHookWhenActiveIdentical, ref OnlyWhenActiveIdentical))
        {
            OnlyWhenNotActiveIdentical = false;
            Service.Save();
        }

        if (DrawUtil.Checkbox(UIStrings.OnlyHookWhenNOTActiveIdentical, ref OnlyWhenNotActiveIdentical))
        {
            OnlyWhenActiveIdentical = false;
            Service.Save();
        }
        
        ImGui.Unindent();
    }
    
    private void DrawPrizeCatch()
    {
        ImGui.Indent();

        DrawUtil.Checkbox(UIStrings.Prize_Catch_Required, ref PrizeCatchReq);
        
        DrawUtil.Checkbox(UIStrings.PrizeCatchNotActive, ref PrizeCatchNotReq);
        
        ImGui.Unindent();
    }

    private void DrawTimers()
    {
        ImGui.Indent();
        ImGui.PushID(@"HookingTimer");
        ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.SetZeroToIgnore);
        DrawUtil.Checkbox(UIStrings.EnableHookingTimer, ref HookTimerEnabled);
        SetupTimer(ref MinHookTimer, ref MaxHookTimer);
        ImGui.PopID();

        DrawUtil.SpacingSeparator();

        //ImGui.TextWrapped(UIStrings.ChumTimer);
        ImGui.PushID(@"MoochTimer");
        DrawUtil.Checkbox(UIStrings.EnableChumTimer, ref ChumTimerEnabled);
        SetupTimer(ref ChumMinHookTimer, ref ChumMaxHookTimer);
        ImGui.PopID();
        ImGui.Unindent();
    }
    
    private void SetupTimer(ref double minTimeDelay, ref double maxTimeDelay)
    {
        
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputDouble(UIStrings.MinWait, ref minTimeDelay, .1, 1, @"%.1f%"))
        {
            switch (minTimeDelay)
            {
                case <= 0:
                    minTimeDelay = 0;
                    break;
                case > 99:
                    minTimeDelay = 99;
                    break;
            }

            Service.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker($"{UIStrings.HelpMarkerMinWaitTimer}\n\n{UIStrings.DoesntHaveAffectUnderChum}");

        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputDouble(UIStrings.MaxWait, ref maxTimeDelay, .1, 1, @"%.1f%"))
        {
            switch (maxTimeDelay)
            {
                case 0.1:
                    maxTimeDelay = 2;
                    break;
                case <= 0:
                case <= 1.9: //This makes the option turn off if delay = 2 seconds when clicking the minus.
                    maxTimeDelay = 0;
                    break;
                case > 99:
                    maxTimeDelay = 99;
                    break;
            }

            Service.Save();
        }
        
        ImGuiComponents.HelpMarker(UIStrings.HelpMarkerMaxWaitTimer);
    }
}