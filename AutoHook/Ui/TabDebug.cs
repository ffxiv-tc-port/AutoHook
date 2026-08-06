using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoHook.Utils;
using ECommons.Automation.NeoTaskManager;
using ECommons.Throttlers;
using Dalamud.Bindings.ImGui;
using HtmlAgilityPack;
using System.Linq;
using AutoHook.Enums;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;

namespace AutoHook.Ui;

public class TabDebug : BaseTab
{
    public override OpenWindow Type => OpenWindow.Debug;

    private delegate byte ExecuteCommandDelegate(int id, int unk1, uint baitId, int unk2, int unk3);

    private Hook<ExecuteCommandDelegate>? _executeCommandHook;
    public TabDebug()
    {
        //_taskManager.DefaultConfiguration.OnTaskTimeout += RepairFailed;
        //CreateDalamudHooks();
        //taskManager.DefaultConfiguration.OnTaskCompletion
    }

    private unsafe void CreateDalamudHooks()
    {
        _executeCommandHook = Service.GameInteropProvider.HookFromSignature<ExecuteCommandDelegate>(
            @"E8 ?? ?? ?? ?? 41 C6 04 24",
            ExecuteCommandDetour);
        _executeCommandHook?.Enable();
    }

    private unsafe byte ExecuteCommandDetour(int id, int unk1, uint baitId, int unk2, int unk3)
    {
        
        Service.PluginLog.Debug($"ExecuteCommandDetour: {id} {unk1} {baitId} {unk2} {unk3}");
        return _executeCommandHook!.Original(id, unk1, baitId, unk2, unk3);
    }
    
    private TaskManager _taskManager = new TaskManager()
    {
        DefaultConfiguration = { TimeLimitMS = 10000 }
    };
    
    public override string TabName => "Debug";
    public override bool Enabled => true;

    private static RepairStatus repairStauts = RepairStatus.Idle;

    public override void DrawHeader()
    {
        DrawUtil.TextV($"Theres no debug here its just random stuff i add to see what happens");

        DrawUtil.TextV($"AutoRepair Status: {repairStauts}");
    }

    enum RepairStatus
    {
        Idle,
        Repairing,
        Success,
        Failed
    }

    private unsafe uint fishCaught => PlayerState.Instance()->NumFishCaught;

    public override void Draw()
    {
        try
        {
            if (ImGui.Selectable($"Revert Plugin Version: {Service.Configuration.Version}"))
                Service.Configuration.Version = 4;

            if (Player.Available)
            {
                ImGui.Text($"Fish Caught: {fishCaught}");
            }

            if (ImGui.Selectable($" {Service.Configuration.HookPresets.Folders.Count} Folders"))
            {
                Service.Configuration.HookPresets.Folders.Clear();
                Service.Configuration.Save();
            }

            if (ImGui.CollapsingHeader("Testing buttons (scary)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (ImGui.Button("Try repair"))
                {
                    repairStauts = RepairStatus.Repairing;
                    _taskManager.Enqueue(ProcessRepair, "Repair");
                }

                ImGui.SetNextItemWidth(140 * ImGuiHelpers.GlobalScale);
                ImGui.InputInt("##wksScanValue", ref _wksScanValue);
                ImGui.SameLine();
                if (ImGui.Button("Scan WKS Offsets"))
                {
                    // 原本寫死找 45949（宇宙大蚊）。掃描目標本來就該由使用者指定，
                    // 不然想找別的欄位還要改碼重編。
                    Checkoffsets(_wksScanValue < 0 ? 0u : (uint)_wksScanValue);
                }

                if (ImGui.Button("Export fish ids"))
                {
                    var fishList = GameRes.Fishes;

                    string allKeys = $"[{string.Join(", ", fishList.Select(f => f.Id))}]";
                    ImGui.SetClipboardText(allKeys);
                }

                if (ImGui.Button("Fix Global Preset"))
                {
                    Service.Configuration.HookPresets.DefaultPreset.PresetName = Service.GlobalPresetName;
                }
            }

            ImGui.InputInt("Swimbait Id", ref _swimbaitId);

            if (ImGui.Button("Swap Swimbait"))
            {
                Service.BaitManager.ChangeBait((uint)_swimbaitId);
            }


            if (ImGui.CollapsingHeader("Get Wiki presets", ImGuiTreeNodeFlags.DefaultOpen))
            {
                using (ImRaii.Group())
                {
                    if (ImGui.Button($"Get Wiki info (cd: {EzThrottler.GetRemainingTime("WikiUpdate")})"))
                    {
                        WikiPresets.ListWikiPages();
                    }

                    //ImGui.InputTextWithHint("", "regex", ref regex, 500);

                    foreach (var preset in WikiPresets.Presets)
                    {
                        ImGui.TextWrapped($"Preset: {preset.Key}, Qtd: {preset.Value.Count}");
                        foreach (var item in preset.Value)
                            ImGui.TextWrapped($"-> {item.PresetName}");
                        DrawUtil.SpacingSeparator();
                    }
                }
            }
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e.Message);
        }
    }

    private static string regexold = @"```\s*AH\s*([\s\S]*?)\s*```";
    private static string regex = @"```\s*(AH\s*[\s\S]*?)\s*```";
    

    private static bool ProcessRepair()
    {/*
        var s = RepairManager.ProcessRepair();

        if (s)
            repairStauts = RepairStatus.Success;*/

        return false;
    }

    private void RepairFailed(TaskManagerTask task, ref long ms)
    {
        repairStauts = RepairStatus.Failed;
    }

    private static readonly HttpClient client = new HttpClient();

    private static Dictionary<string, List<string>> Presets = new();
    private int _swimbaitId = 45949;

    /// <summary>WKSManager 偏移掃描要找的值。預設 45949 是「宇宙大蚊」的 item ID，沿用原本寫死的那個。</summary>
    private int _wksScanValue = 45949;

    public static async Task UpdateWiki()
    {
        if (!EzThrottler.Throttle("WikiUpdate", 10000))
        {
        }

        // Example usage:
        string wikiPageUrl =
            "https://raw.githubusercontent.com/wiki/PunishXIV/AutoHook/Scrip-Farming-%5BUpdated-to-DT%5D.md"; // Replace with the actual URL
        //presets = await ExtractBase64FromWikiPage(wikiPageUrl);

        // Print the extracted base64 codes
    }

    private const string BaseUrl = "https://github.com/PunishXIV/AutoHook/wiki";
    private const string RawWiki = "https://raw.githubusercontent.com/wiki/PunishXIV/AutoHook";
    private static readonly HttpClient httpClient = new(); // Reuse HttpClient

    public static async Task ListWikiPages()
    {
        var mdUrls = await GetWikiPageUrls(BaseUrl);
        Service.PrintDebug($"Size1: {mdUrls.Count}");

        foreach (var mdUrl in mdUrls)
        {
            var preset = await ExtractBase64FromWikiPage($"{RawWiki}/{mdUrl}.md");
            Presets.Add(mdUrl.Replace(@"-", @" "), preset);
        }
    }

    static async Task<List<string>> GetWikiPageUrls(string url)
    {
        var pageUrls = new List<string>();
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(await httpClient.GetStringAsync(url));

        var pageLinks = htmlDoc.DocumentNode
            ?.SelectSingleNode("//nav[contains(@class, 'wiki-pages-box')]")
            ?.SelectNodes(".//a[@href]")
            ?.Skip(1) // Skip the first link (usually the Home link)
            ?.Select(link => $"{link.Attributes["href"]?.Value?.Replace(@"/PunishXIV/AutoHook/wiki/", "")}");

        if (pageLinks != null)
            pageUrls.AddRange(pageLinks);


        return pageUrls;
    }

    static async Task<List<string>> ExtractBase64FromWikiPage(string url)
    {
        string wikiPageContent = await httpClient.GetStringAsync(url);
        return Regex.Matches(wikiPageContent, TabDebug.regex)
            .Select(match => match.Groups[1].Value)
            .ToList();
    }

    public override void Dispose()
    {
        _executeCommandHook?.Dispose();
        _taskManager.Dispose();
    }

    /// <summary>
    /// 在 <see cref="WKSManager"/> 裡找出「哪個偏移放著某個餌的 item ID」。
    ///
    /// 🔴 原本這裡是 <c>for (offset = 1; offset &lt;= 10000; offset++)</c>。
    ///    WKSManager 在目前釘住的 FFXIVClientStructs 裡宣告 <c>Size = 0xF60</c>（3936 bytes），
    ///    所以原本的迴圈會往結構尾端外面讀最多約 6 KB。讀到未對映的分頁就是
    ///    AccessViolationException —— 那在 .NET Core 是 corrupted-state exception，
    ///    <c>try/catch</c> 攔不到，直接整個遊戲崩。按一次按鈕就可能中。
    ///
    /// 現在：上界收到 <c>sizeof(WKSManager) - sizeof(uint)</c>，且以 4 bytes 對齊掃描
    ///    （欄位本來就對齊，逐 byte 掃只是多產生 3 倍的假命中）。
    ///
    /// 另外把輸出改成 Information：使用者的記錄等級是 2，原本寫 Debug 等於按了按鈕
    /// 什麼都不會出現，看起來像「掃不到」。開頭先印一行 CS 已知的 FishingBait 欄位當
    /// 校準基準 —— 沒有已知會命中的對照，掃出 0 筆是分不出「真的沒有」還是「掃錯了」的。
    /// </summary>
    public unsafe void Checkoffsets(uint searchValue)
    {
        var cosmicManager = WKSManager.Instance();
        if (cosmicManager == null)
        {
            Service.PrintInfo("[Debug] WKSManager 是空指標（沒進宇宙探索時本來就會是這樣）。");
            return;
        }

        var size = sizeof(WKSManager);
        Service.PrintInfo($"[Debug] 開始掃 WKSManager 找值 {searchValue}。" +
                          $"結構大小 0x{size:X}，校準用：CS 已知的 FishingBait(+0xC4C) 目前是 {cosmicManager->FishingBait}。");

        var hits = 0;
        for (var offset = 0; offset <= size - sizeof(uint); offset += sizeof(uint))
        {
            var value = *(uint*)((byte*)cosmicManager + offset);
            if (value != searchValue)
                continue;

            hits++;
            Service.PrintInfo($"[Debug] 命中：偏移 0x{offset:X} = {value}");
        }

        Service.PrintInfo($"[Debug] 掃描結束，共 {hits} 筆命中。" +
                          (hits == 0 ? "（0 筆的時候先確認上面那行的 FishingBait 是不是你要找的值，別直接當成『沒有』。）" : ""));
    }
}