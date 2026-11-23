using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace AutoHook.Fishing.Ocean;

/// <summary>出海垂釣的資料表查詢（IKDRoute／IKDSpot／TerritoryType），全部只讀、查一次就快取。</summary>
internal static class OceanData
{
    /// <summary>TerritoryIntendedUse 46＝出海垂釣的船（台服 7.30：900、1163）。</summary>
    private const uint OceanFishingIntendedUse = 46;

    private static HashSet<uint>? _boatTerritories;
    private static List<(uint Id, string Name)>? _spots;
    private static readonly Dictionary<uint, (uint Spot, uint Time)[]> RouteStops = new();

    public static bool IsBoatTerritory(uint territoryId)
    {
        _boatTerritories ??= Service.DataManager.GetExcelSheet<TerritoryType>()
            .Where(t => t.TerritoryIntendedUse.RowId == OceanFishingIntendedUse)
            .Select(t => t.RowId)
            .ToHashSet();
        return _boatTerritories.Contains(territoryId);
    }

    /// <summary>這條航線第 <paramref name="zone"/> 站的（IKDSpot 列號, IKDTimeDefine 列號）；查不到回 (0, 0)。</summary>
    public static (uint Spot, uint Time) Stop(uint route, uint zone)
    {
        if (!RouteStops.TryGetValue(route, out var stops))
        {
            stops = [];
            if (Service.DataManager.GetExcelSheet<IKDRoute>().TryGetRow(route, out var row))
            {
                var count = System.Math.Min(row.Spot.Count, row.Time.Count);
                stops = new (uint, uint)[count];
                for (var k = 0; k < count; k++)
                    stops[k] = (row.Spot[k].RowId, row.Time[k].RowId);
            }

            RouteStops[route] = stops;
        }

        return zone < stops.Length ? stops[zone] : (0, 0);
    }

    /// <summary>所有出海垂釣海域（IKDSpot 1 起），名稱取 PlaceName。</summary>
    public static IReadOnlyList<(uint Id, string Name)> Spots
        => _spots ??= Service.DataManager.GetExcelSheet<IKDSpot>()
            .Where(s => s.RowId != 0)
            .Select(s => (s.RowId, SpotNameOf(s)))
            .ToList();

    public static string SpotName(uint spotId)
    {
        foreach (var (id, name) in Spots)
        {
            if (id == spotId)
                return name;
        }

        return spotId == 0 ? @"?" : @$"海域 #{spotId}";
    }

    /// <summary>IKDTimeDefine 1／2／3＝白天／黃昏／夜晚；0＝規則用的「任何時段」。</summary>
    public static string TimeName(uint timeId) => timeId switch
    {
        0 => @"任何時段",
        1 => @"白天",
        2 => @"黃昏",
        3 => @"夜晚",
        _ => @$"時段 #{timeId}",
    };

    private static string SpotNameOf(IKDSpot spot)
    {
        var name = spot.PlaceName.ValueNullable?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? @$"海域 #{spot.RowId}" : name;
    }
}
