using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AutoHook.Classes;
using AutoHook.Configurations;
using AutoHook.Ui;
using ECommons.Throttlers;
using HtmlAgilityPack;

namespace AutoHook.Utils;

public class WikiPresets
{
    private const string BaseUrl = "https://github.com/PunishXIV/AutoHook/wiki";
    private const string RawWiki = "https://raw.githubusercontent.com/wiki/PunishXIV/AutoHook";
    private static readonly HttpClient httpClient = new(); // Reuse HttpClient

    private static string regex = @"```\s*(AH\s*[\s\S]*?)\s*```";
    private static string regexSf = @"```\s*(AHSF\s*[\s\S]*?)\s*```";

    // 契約：發布出去的字典不再改動，每次更新都換一份新的；繪製執行緒每幀列舉的是讀到的那份快照。
    // 寫入端都在框架執行緒；「讀舊表 → 建新表 → 發布」仍用鎖串起來，不依賴這一點（鎖內不碰 ImGui、不做 I/O）。
    private static readonly object PublishGate = new();
    private static Dictionary<string, List<CustomPresetConfig>> _presets = new();
    private static Dictionary<string, List<AutoGigConfig>> _presetsSf = new();

    public static IReadOnlyDictionary<string, List<CustomPresetConfig>> Presets => Volatile.Read(ref _presets);
    public static IReadOnlyDictionary<string, List<AutoGigConfig>> PresetsSf => Volatile.Read(ref _presetsSf);

    private static void ResetPublished()
    {
        lock (PublishGate)
        {
            Volatile.Write(ref _presets, new());
            Volatile.Write(ref _presetsSf, new());
        }
    }

    /// <exception cref="ArgumentException">同名頁已發布過；兩份都不改（與原本 Dictionary.Add 的結果相同）。</exception>
    private static void Publish(string key, List<CustomPresetConfig> list, List<AutoGigConfig> listSf)
    {
        lock (PublishGate)
        {
            var presets = new Dictionary<string, List<CustomPresetConfig>>(_presets);
            presets.Add(key, list);
            var presetsSf = new Dictionary<string, List<AutoGigConfig>>(_presetsSf);
            presetsSf.Add(key, listSf);
            Volatile.Write(ref _presets, presets);
            Volatile.Write(ref _presetsSf, presetsSf);
        }
    }

    // IFramework.Run 排進去的工作不隨插件卸載取消 ⇒ 卸載時取消這個，還沒跑的匯入自己放棄。
    private static readonly CancellationTokenSource Unloading = new();

    public static void Shutdown() => Unloading.Cancel();

    public static async Task ListWikiPages()
    {
        if (!EzThrottler.Throttle("WikiUpdate", 20000))
            return;

        ResetPublished();
        var token = Unloading.Token;
        var mdUrls = await GetWikiPageUrls(BaseUrl);
        foreach (var mdUrl in mdUrls)
        {
            if (token.IsCancellationRequested)
                return;

            try
            {
                var base64 = await ExtractBase64FromWikiPage($"{RawWiki}/{mdUrl}.md");

                // 下載留在背景；匯入排到框架執行緒：反序列化會經 BaseActionCast 建構子呼叫原生 GetActionCost。
                // 一個 preset 一件工作（整頁一次做完會在框架執行緒上卡數十毫秒）；整頁匯入完才發布、才抓下一頁。
                var list = new List<CustomPresetConfig>();
                var listsf = new List<AutoGigConfig>();
                foreach (var code in base64.presets)
                    await Service.Framework.Run(() =>
                    {
                        if (!token.IsCancellationRequested && Configuration.ImportPreset(code) is CustomPresetConfig preset)
                            list.Add(preset);
                    }, token);
                foreach (var code in base64.presetsSf)
                    await Service.Framework.Run(() =>
                    {
                        if (!token.IsCancellationRequested && Configuration.ImportPreset(code) is AutoGigConfig gig)
                            listsf.Add(gig);
                    }, token);

                await Service.Framework.Run(() =>
                {
                    if (!token.IsCancellationRequested)
                        Publish(mdUrl.Replace(@"-", @" "), list, listsf);
                }, token);
            }
            catch (Exception e)
            {
                Service.PluginLog.Debug($"Can probably ignore: {e.Message}");
            }
           
        }
    }

    static async Task<List<string>> GetWikiPageUrls(string url)
    {
        var pageUrls = new List<string>();
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(await httpClient.GetStringAsync(url));

        var pageLinks = htmlDoc.DocumentNode
            ?.SelectSingleNode("//nav[contains(@class, 'wiki-pages-box')]")
            ?.SelectNodes(".//a[@href]") // Skip the first link (usually the Home link)
            ?.Select(link => $"{link.Attributes["href"]?.Value?.Replace(@"/PunishXIV/AutoHook/wiki/", "")}");

        if (pageLinks != null)
            pageUrls.AddRange(pageLinks);

        
        return pageUrls;
    }

    static async Task<(List<string> presets, List<string> presetsSf)> ExtractBase64FromWikiPage(string url)
    {
        string wikiPageContent = await httpClient.GetStringAsync(url);
        var presets = Regex.Matches(wikiPageContent, regex) 
            .Select(match => match.Groups[1].Value)
            .ToList();
        
        var presetsSf = Regex.Matches(wikiPageContent, regexSf) 
            .Select(match => match.Groups[1].Value)
            .ToList();

        return (presets, presetsSf);
    }
}