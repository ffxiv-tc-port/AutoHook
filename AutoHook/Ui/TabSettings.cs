using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using AutoHook.Configurations;
using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Common.Math;
using Dalamud.Bindings.ImGui;

namespace AutoHook.Ui;

public class TabSettings : BaseTab
{
    public override string TabName => UIStrings.SettingsTab;
    public override bool Enabled { get; } = true;

    public override OpenWindow Type => OpenWindow.Settings;

    public override void DrawHeader()
    {
        DrawLanguageSelector();

        ImGui.Spacing();
        
        if (ImGui.Button(UIStrings.TabGeneral_DrawHeader_Localization_Help))
        {
            Process.Start(new ProcessStartInfo
                { FileName = "https://crowdin.com/project/autohook", UseShellExecute = true });
        }

        ImGui.Spacing();

        if (ImGui.Button(UIStrings.TabAutoCasts_DrawHeader_Guide_Collectables))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/PunishXIV/AutoHook/blob/main/AcceptCollectable.md",
                UseShellExecute = true
            });
        }

        ImGui.Spacing();
    }

    public override void Draw()
    {
        using (var item = ImRaii.Child("SettingItems", new Vector2(0, 0), true))
        {
            DrawConfigs();
        }
    }

    private void DrawConfigs()
    {
        DrawUtil.Checkbox(UIStrings.Plugin_Enabled, ref Service.Configuration.PluginEnabled, UIStrings.PluginEnabledHelp,
            ipcOverrideKey: IpcConfigOverrides.PluginEnabledKey);
        DrawUtil.DrawSuppressionLeaseMarker();
        
        if (ImGui.TreeNodeEx(UIStrings.DelaySettings, ImGuiTreeNodeFlags.FramePadding))
        {
            DrawDelayHook();
            DrawDelayCasts();
            DrawDelayCancel();
            ImGui.TreePop();
        }

        ImGui.Separator();
        
        DrawUtil.Checkbox(UIStrings.AntiAfkOption, ref Service.Configuration.ResetAfkTimer);

        DrawUtil.Checkbox(UIStrings.DontHideExtraAutoCast, ref Service.Configuration.DontHideOptionsDisabled);

        DrawUtil.Checkbox(UIStrings.Hide_Tab_Description, ref Service.Configuration.HideTabDescription);

        DrawUtil.Checkbox(UIStrings.Show_Current_Status_Header, ref Service.Configuration.ShowStatus);

        DrawUtil.Checkbox(UIStrings.Show_Chat_Logs, ref Service.Configuration.ShowChatLogs, UIStrings.Show_Chat_Logs_HelpText);

        DrawUtil.Checkbox(@"沒在釣魚時自動拋竿", ref Service.Configuration.AutoStartFishing,
            @"人站在釣點、竿子收著的時候自動幫你拋第一竿。" + "\n" +
            @"⚠️ 還需要「自動施放」那一頁的總開關也開著，而且那裡的「自動拋竿」要是啟用的；" +
            @"職業不對或不在水邊時不會有動作。" + "\n" +
            @"一直開著 AutoHook 的人建議關掉這個 —— 收竿之後它會自己再拋一次。",
            ipcOverrideKey: IpcConfigOverrides.AutoStartFishingKey);

        DrawTataruPraise();

        //DrawUtil.Checkbox(UIStrings.Show_Debug_Console, ref Service.Configuration.ShowDebugConsole);

        //DrawUtil.Checkbox(UIStrings.Show_Presets_As_Sidebar, ref Service.Configuration.ShowPresetsAsSidebar);
        
        DrawUtil.DrawCheckboxTree(UIStrings.SwapTreeNodeButtons, ref Service.Configuration.SwapToButtons, () =>
        {
            if (ImGui.RadioButton(UIStrings.Type_1, Service.Configuration.SwapType == 0))
            {
                Service.Configuration.SwapType = 0;
                Service.Save();
            }

            if (ImGui.RadioButton(UIStrings.Type_2, Service.Configuration.SwapType == 1))
            {
                Service.Configuration.SwapType = 1;
                Service.Save();
            }

            ImGui.Text("Hello, you're cute!");
        });
    }

    /// <summary>
    /// 「釣到稀有魚時請塔塔露念一句」的設定。
    /// <para>
    /// ⚠️ 字串刻意寫成字面繁中而不是走 <c>UIStrings</c>：這是台服 fork 專屬的功能，
    /// 而 resx 那條路要同時動 11 份語言檔＋Designer，且「鍵存在但值為空」不會退回英文
    /// （會直接顯示空字串）。這裡的取捨是「少一層可以靜默壞掉的東西」。
    /// </para>
    /// </summary>
    private static void DrawTataruPraise()
    {
        DrawUtil.DrawCheckboxTree(@"釣到稀有魚時請塔塔露誇獎", ref Service.Configuration.TataruPraiseEnabled, () =>
        {
            DrawUtil.Checkbox(@"大魚（需要漁人的直覺／需要天氣轉換／魚影魚）",
                ref Service.Configuration.TataruPraiseBigFish,
                @"用 AutoHook 自己的魚資料判斷：這條魚要先靠捕食魚累出「漁人的直覺」、" +
                @"或要等天氣轉換、或它是有魚影的大魚。" + "\n" +
                @"非魚叉魚 1869 條裡只有 135 條符合（7.2%），掛機一整天也不會誤觸幾次。");

            DrawUtil.Checkbox(@"遊戲的「大物」旗標",
                ref Service.Configuration.TataruPraiseLargeFlag,
                @"遊戲自己標記為大物的那一竿。語意精確，但觸發頻率沒有實機數據 —— " +
                @"如果覺得太常出聲就關掉。");

            DrawUtil.Checkbox(@"傳說咬「!!!」",
                ref Service.Configuration.TataruPraiseLegendaryBite,
                @"最強的咬鉤力道。某些餌／釣場的「!!!」是常態，開之前先想一下。");

            DrawUtil.Checkbox(@"收藏品",
                ref Service.Configuration.TataruPraiseCollectible,
                @"⚠️ 收藏品釣魚時每一條都是收藏品 —— 開這個等於每一竿都出聲。");

            var interval = Service.Configuration.TataruPraiseMinIntervalSeconds;
            if (DrawUtil.EditNumberField(@"兩次出聲的最短間隔（秒）", 60f, ref interval,
                    @"保險絲：不管上面勾了什麼，出聲都不會比這個間隔更頻繁。0＝不限制。"))
            {
                Service.Configuration.TataruPraiseMinIntervalSeconds = Math.Clamp(interval, 0, 3600);
                Service.Save();
            }
        }, @"需要另外安裝 TataruPraise（塔塔露誇獎）外掛；沒裝的話這裡勾了也不會有任何事發生，也不會影響釣魚。");
    }

    private static void DrawDelayHook()
    {
        ImGui.PushID("DrawDelayHook");

        ImGui.TextWrapped(UIStrings.Delay_when_hooking);
        
        ref var min = ref Service.Configuration.DelayBetweenHookMin;
        ref var max = ref Service.Configuration.DelayBetweenHookMax;

        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref min,0))
        {
            min = Math.Clamp(min, 0, max);
            Service.Save();
        }

        ImGui.SameLine();

        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref max, 0))
        {
            max = Math.Clamp(max, min, 9999);
            Service.Save();
        }

        ImGui.PopID();
    }

    private static void DrawDelayCasts()
    {
        ImGui.PushID("DrawDelayCasts");

        ImGui.TextWrapped(UIStrings.Delay_Between_Casts);
        
        ref var min = ref Service.Configuration.DelayBetweenCastsMin;
        ref var max = ref Service.Configuration.DelayBetweenCastsMax;
        
        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref min, 0))
        {
            min = Math.Clamp(min, 0, max);
            Service.Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref max, 0))
        {
            max = Math.Clamp(max, min, 9999);
            Service.Save();
        }

        ImGui.PopID();
    }
    
    private static void DrawDelayCancel()
    {
        ImGui.PushID("DrawDelayCancel");

        DrawUtil.TextV(UIStrings.DelayBeforeCancel);
        ImGui.SameLine();
        DrawUtil.Info(UIStrings.DelayBeforeCancelInfo);
        
        ref var min = ref Service.Configuration.DelayBeforeCancelMin;
        ref var max = ref Service.Configuration.DelayBeforeCancelMax;
        
        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Min_, ref min, 0))
        {
            min = Math.Clamp(min, 0, max);
            Service.Save();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(45 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputInt(UIStrings.DrawConfigs_Max_, ref max, 0))
        {
            max = Math.Clamp(max, min, 9999);
            Service.Save();
        }

        ImGui.PopID();
    }

    private void DrawLanguageSelector()
    {
        ImGui.SetNextItemWidth(55);
        var languages = new List<string>
        {
            @"en",
            @"es",
            @"fr",
            @"de",
            @"ja",
            @"ko",
            @"ru",
            @"zh",
            @"zh-Hant"
        };
        var currentLanguage = languages.IndexOf(Service.Configuration.CurrentLanguage);

        if (!ImGui.Combo($"{UIStrings.PluginUi_Language}###currentLanguage", ref currentLanguage, languages.ToArray(),
                languages.Count))
            return;

        Service.Configuration.CurrentLanguage = languages[currentLanguage];
        UIStrings.Culture = new CultureInfo(Service.Configuration.CurrentLanguage);
        Service.Save();
        //Service.Chat.Print("Saved");
    }
}