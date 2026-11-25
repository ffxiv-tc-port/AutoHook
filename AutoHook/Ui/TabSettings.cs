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

        DrawCosmicPatience();

        DrawUtil.Checkbox(@"宇宙探索：每條魚都算分的任務，preset 沒開的咬鉤也提",
            ref Service.Configuration.HookUnconfiguredBitesWhenEveryFishCounts,
            @"只作用在任務說明寫「任意水產品」或「每條魚均給予評價」的宇宙探索釣魚任務。" + "\n" +
            @"preset 把某種咬鉤（例如弱咬）的提鉤全部關掉時，這類任務改用一般提鉤、不歇竿：" +
            @"任何一條魚都算數，歇竿等於白等一竿。" + "\n" +
            @"身上有大魚獵手或大魚知識時照 preset 放掉（魚一上岸效果就用掉，留給 preset 要的咬鉤）。" + "\n" +
            @"其他原因的跳過照舊（提鉤條件不符、preset 設定 GP 不夠時放掉魚）。" + "\n" +
            @"關掉＝完全照 preset。");

        DrawUtil.Checkbox(@"宇宙探索：要湊幾種不同魚的任務，不用雙重／三重提鉤",
            ref Service.Configuration.NoMultiHookWhenDistinctSpecies,
            @"只作用在任務說明寫「釣到幾種不同的水產品」的宇宙探索釣魚任務。" + "\n" +
            @"這類任務只算種類，同一種魚一次起獲好幾條沒有幫助，雙重提鉤（400 GP）、三重提鉤（700 GP）等於白花。" + "\n" +
            @"preset 在這種咬鉤要放雙重／三重提鉤時，改用一般提鉤；preset 本來就不提的咬鉤照樣歇竿。" + "\n" +
            @"關掉＝完全照 preset。");

        DrawUtil.Checkbox(@"宇宙探索：每條魚都算分的任務，雙重／三重提鉤看實測起獲",
            ref Service.Configuration.MultiHookByObservedYield,
            @"只作用在任務說明寫「任意水產品」或「每條魚均給予評價」的宇宙探索釣魚任務。" + "\n" +
            @"咬鉤前只知道咬鉤力道與拋竿後幾秒，所以記下每一竿「這個時間上鉤的是哪條魚、多重提鉤起了幾條」：" +
            @"同樣力道、同樣引誘層數與撒餌、咬鉤秒數差 1 秒內的那些魚，雙重提鉤平均多起獲不到半條就不放、三重提鉤不到一條就不放；" +
            @"雙重提鉤每 GP 多起獲得比較多時改放雙重。" + "\n" +
            @"例：緊急籌備水產食材的月岩藻多重提鉤只起 1 條，月亮姐妹雙重 2 條、三重 3 條。" + "\n" +
            @"還沒有樣本的時間窗照 preset。樣本存在插件設定資料夾，下次開遊戲接著用。" + "\n" +
            @"關掉＝完全照 preset。");

        DrawUtil.Checkbox(@"宇宙探索：只算釣到幾條的任務，不放引誘",
            ref Service.Configuration.SkipLuresWhenAnyFishCounts,
            @"只作用在任務說明寫「盡快釣到指定數量的任意水產品」的宇宙探索釣魚任務。" + "\n" +
            @"每次放謙遜之餌／雄心之餌後 5 秒內魚不會咬鉤（三層等於至少等 14 秒才咬），三層共花 60 GP；" +
            @"這類任務任何一條魚都算數，省下的 GP 留給雙重／三重提鉤。" + "\n" +
            @"preset 的引誘設成鎖定特殊魚影時照放。關掉＝完全照 preset。");

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
        }, @"需要另外安裝 TataruPraise（塔塔露誇獎）插件；沒裝的話這裡勾了也不會有任何事發生，也不會影響釣魚。");
    }

    /// <summary>宇宙探索尺寸計分任務的耐心策略。字面繁中的理由同 <see cref="DrawTataruPraise"/>。</summary>
    private static void DrawCosmicPatience()
    {
        if (!ImGui.TreeNodeEx(@"宇宙探索：尺寸計分任務的耐心###CosmicPatience", ImGuiTreeNodeFlags.FramePadding))
            return;

        ImGui.TextWrapped(@"只作用在任務說明寫「根據大小／最大尺寸給予評價」的宇宙探索釣魚任務；其他釣魚完全不受影響。");

        DrawCosmicPatienceRadio(@"自動（建議）", CosmicPatienceMode.Auto,
            @"preset 已經用耐心、大魚獵手或雙重／三重提鉤時照 preset；" + "\n" +
            @"preset 沒在尺寸上花 GP 時，GP 夠就放耐心II。" + "\n" +
            @"耐心期間：雙重／三重提鉤暫停，弱咬用精準提鉤、強咬／傳說咬用強力提鉤，華麗提鉤冷卻好時優先。");
        DrawCosmicPatienceRadio(@"尺寸總和型任務優先耐心II", CosmicPatienceMode.PreferForSizeSum,
            @"「根據大小給予評價」（每條魚尺寸加總）的任務，即使 preset 用大魚獵手或雙重／三重提鉤也先放耐心II。" + "\n" +
            @"⚠️ 耐心期間大魚獵手不會施放、雙重／三重提鉤會暫停；雙重提鉤每竿起 2 條、分數直接翻倍，這個模式不一定比較高分。" + "\n" +
            @"「最大尺寸」型任務仍照「自動」。");
        DrawCosmicPatienceRadio(@"關閉（完全照 preset）", CosmicPatienceMode.Off,
            @"不自動放耐心，耐心期間也不換提鉤。");

        if (Service.Configuration.CosmicPatienceMode != CosmicPatienceMode.Off)
        {
            var reserve = Service.Configuration.CosmicPatienceHookGpReserve;
            if (DrawUtil.EditNumberField(@"放耐心II前另外保留給提鉤的 GP", 60f, ref reserve,
                    @"耐心II 本身 560 GP；耐心期間每次精準／強力提鉤再 50 GP，不夠就只能用一般提鉤（成功率降低）。" + "\n" +
                    @"預設 150＝大約三竿。"))
            {
                Service.Configuration.CosmicPatienceHookGpReserve = Math.Clamp(reserve, 0, 1000);
                Service.Save();
            }
        }

        ImGui.TreePop();
    }

    private static void DrawCosmicPatienceRadio(string label, CosmicPatienceMode mode, string help)
    {
        if (ImGui.RadioButton(label, Service.Configuration.CosmicPatienceMode == mode))
        {
            Service.Configuration.CosmicPatienceMode = mode;
            Service.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(help);
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