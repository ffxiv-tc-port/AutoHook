using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AutoHook.Classes;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;

namespace AutoHook.Utils;

public static class GameRes
{
    public const uint FishingTackleRow = 30;
    public const int AllBaitsId = -99;
    public const int AllMoochesId = -98;

    public static List<BaitFishClass> Baits { get; private set; } = new();
    public static List<BaitFishClass> Fishes { get; private set; } = new();
    public static List<BaitFishClass> LureFishes => Fishes.Where(f => f.LureMessage != "").ToList();

    public static List<ImportedFish> ImportedFishes { get; private set; } = new();

    public static List<BiteTimers> BiteTimers { get; private set; } = new();

    public static void Initialize()
    {
        
        Baits =
            (Service.DataManager.GetExcelSheet<Item>()?
                 .Where(i => i.ItemSearchCategory.RowId == FishingTackleRow)
             ?? [])
            .Concat(
                Service.DataManager.GetExcelSheet<WKSItemInfo>()?
                    .Where(i => i.WKSItemSubCategory.RowId == 5)
                    .Select(i => Service.DataManager.GetExcelSheet<Item>()?.GetRow(i.Item.RowId))
                    .Where(item => item != null)
                    .Cast<Item>()
                ?? []
            )
            .Select(b => new BaitFishClass(b))
            .ToList();

        Fishes = Service.DataManager.GetExcelSheet<FishParameter>()?
                     .Where(f => f.Item.RowId != 0 && f.Item.RowId < 1000000)
                     .Select(f => new BaitFishClass(f))
                     .GroupBy(f => f.Id)
                     .Select(group => group.First())
                     .ToList()
                 ?? new List<BaitFishClass>();

        try
        {
            var fishList = Path.Combine(Service.PluginInterface.AssemblyLocation.DirectoryName!,
                $"Data\\FishData\\fish_list.json");

            // 直接從檔案串流解析：先讀成整份字串會多配置約 4 MB（字串＋StreamReader 的 StringBuilder／char[]）。
            // Stream 多載與 ReadAllText 一樣會跳過 UTF-8 BOM，解析結果相同。
            if (File.Exists(fishList))
            {
                using var stream = File.OpenRead(fishList);

                ImportedFishes = JsonSerializer.Deserialize<List<ImportedFish>>(stream)!;
            }

            var biteTimers = Path.Combine(Service.PluginInterface.AssemblyLocation.DirectoryName!,
                $"Data\\FishData\\bitetimers.json");

            if (File.Exists(biteTimers))
            {
                using var stream = File.OpenRead(biteTimers);

                BiteTimers = JsonSerializer.Deserialize<List<BiteTimers>>(stream)!;
            }
        }
        catch (Exception e)
        {
            ImGui.SetClipboardText(e.Message);
            Service.PluginLog.Error($"{e.Message}");
        }
    }
}