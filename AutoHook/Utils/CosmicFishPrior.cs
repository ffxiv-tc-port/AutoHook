using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AutoHook.Enums;

namespace AutoHook.Utils;

/// <summary>
/// 一條宇宙探索任務魚的先驗：咬鉤力道、雙重提鉤起獲條數、不放引誘不撒餌時的咬鉤秒數範圍。
/// 沒有資料的欄位是 <see cref="double.NaN"/>。
/// </summary>
public readonly record struct PriorFish(uint FishId, BiteType Bite, double DoubleYield, double TimeMin, double TimeMax);

/// <summary>
/// 內建資料檔 <c>Data/FishData/cosmic_fish_momola.json</c>（魚糕 v2 整理、已對台服資料表核對）：任務 id → 魚池。
/// 只讀本機檔案；刻意不做線上更新（使用者規則：不直連外部資料站）。
/// </summary>
public static class CosmicFishPrior
{
    public const string FileName = "cosmic_fish_momola.json";

    private static readonly object Gate = new();
    private static Dictionary<ushort, PriorFish[]>? _byMission;

    /// <summary>這個任務的魚池先驗；任務不在資料檔或檔案讀不到＝空陣列（判斷退回只看實測）。</summary>
    public static IReadOnlyList<PriorFish> ForMission(ushort mission)
    {
        lock (Gate)
        {
            if (_byMission == null)
                _byMission = LoadFromPluginFolder();
            return _byMission.TryGetValue(mission, out var list) ? list : [];
        }
    }

    /// <summary>解析資料檔內容。雙重起獲是範圍時取最小值（保守）；空＝NaN。咬鉤字串不認得的魚略過。</summary>
    public static Dictionary<ushort, PriorFish[]> Parse(string json)
    {
        var result = new Dictionary<ushort, PriorFish[]>();
        using var doc = JsonDocument.Parse(json);
        foreach (var mission in doc.RootElement.GetProperty(@"missions").EnumerateObject())
        {
            if (!ushort.TryParse(mission.Name, out var id))
                continue;

            var fish = new List<PriorFish>();
            foreach (var f in mission.Value.EnumerateArray())
            {
                var bite = f.GetProperty(@"bite").GetString() switch
                {
                    @"weak" => BiteType.Weak,
                    @"strong" => BiteType.Strong,
                    @"legendary" => BiteType.Legendary,
                    _ => BiteType.Unknown,
                };
                if (bite == BiteType.Unknown)
                    continue;

                var dbl = double.NaN;
                foreach (var v in f.GetProperty(@"double").EnumerateArray())
                    dbl = double.IsNaN(dbl) ? v.GetDouble() : Math.Min(dbl, v.GetDouble());

                double tMin = double.NaN, tMax = double.NaN;
                if (f.TryGetProperty(@"biteTime", out var bt) && bt.GetArrayLength() == 2)
                {
                    tMin = bt[0].GetDouble();
                    tMax = bt[1].GetDouble();
                }

                fish.Add(new PriorFish(f.GetProperty(@"fish").GetUInt32(), bite, dbl, tMin, tMax));
            }

            result[id] = fish.ToArray();
        }

        return result;
    }

    private static Dictionary<ushort, PriorFish[]> LoadFromPluginFolder()
    {
        try
        {
            var path = Path.Combine(Service.PluginInterface.AssemblyLocation.DirectoryName!, @"Data", @"FishData",
                FileName);
            if (File.Exists(path))
                return Parse(File.ReadAllText(path));

            Service.PluginLog.Warning($"[宇宙多重提鉤] 找不到內建魚池資料 {FileName}，只用實測樣本判斷");
        }
        catch (Exception e)
        {
            Service.PluginLog.Warning($"[宇宙多重提鉤] 內建魚池資料讀取失敗，只用實測樣本判斷：{e.Message}");
        }

        return new Dictionary<ushort, PriorFish[]>();
    }
}
