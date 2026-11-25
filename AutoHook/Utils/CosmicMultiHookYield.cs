using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoHook.Data;
using AutoHook.Enums;

namespace AutoHook.Utils;

/// <summary>一竿的觀測：咬鉤當下的情境（力道、拋竿後秒數、引誘層數、撒餌）＋釣起哪條魚、用哪種提鉤、起獲幾條。</summary>
/// <param name="Seconds">拋竿到咬鉤的秒數；<see cref="double.NaN"/>＝只提供起獲數、不參與時間比對。</param>
public readonly record struct FishSample(
    uint FishId, BiteType Bite, double Seconds, int LureStacks, bool Chum, HookType Hook, uint Amount);

/// <summary>這一咬值不值得放三重／雙重提鉤。<paramref name="Matched"/>＝0 表示沒有可用樣本、照 preset。</summary>
/// <param name="PreferDouble">每 GP 多起獲的條數雙重提鉤比較多：preset 這一咬放得出雙重提鉤時就不放三重。</param>
/// <param name="FromPool">時間窗內沒有樣本，改用這個任務同咬鉤力道的全部樣本估。</param>
public readonly record struct MultiHookGate(
    bool TripleWorth, bool DoubleWorth, bool PreferDouble, int Matched, double ExtraDouble, double ExtraTriple,
    string Detail, bool FromPool = false)
{
    public static readonly MultiHookGate NoData = new(true, true, false, 0, double.NaN, double.NaN, string.Empty);
}

/// <summary>
/// 宇宙探索「每條魚都算分」的任務：用實測的「這個咬鉤時間窗裡上鉤的是哪些魚」＋「那些魚雙重／三重提鉤實際起幾條」
/// 決定這一咬值不值得放雙重／三重提鉤；另判斷只算條數的任務要不要放引誘。
/// 樣本依任務分開存、存檔在插件設定目錄（下次開遊戲接著用）；起獲數是魚的屬性，跨情境共用。
/// </summary>
public static class CosmicMultiHookYield
{
    /// <summary>咬鉤秒數差在這個範圍內的樣本才算「同一個時間窗」。</summary>
    public const double WindowSeconds = 1.0;

    /// <summary>平均每次至少要多起獲這麼多條（雙重；三重是兩倍）才值得花 GP。</summary>
    public const double MinExtraFishPerHook = 0.5;

    public const int DoubleHookGp = 400;
    public const int TripleHookGp = 700;

    private const int MaxSamplesPerMission = 300;
    private const long SaveIntervalMs = 30_000;
    private const string FileName = "cosmic_fish_samples.json";

    /// <summary>
    /// 實機量到的起獲數（只有起獲數、不含時間）：讓第一場就認得「多重提鉤只起 1 條」的魚。
    /// 新觀測會跟它一起平均，觀測到起獲 2 條以上就不再算「起 1 條」。
    /// </summary>
    private static readonly FishSample[] Seeds =
    [
        new(45789, BiteType.Weak, double.NaN, 0, false, HookType.Triple, 1),
        new(45789, BiteType.Weak, double.NaN, 0, false, HookType.Triple, 1),
        new(45789, BiteType.Weak, double.NaN, 0, false, HookType.Triple, 1),
        new(45789, BiteType.Weak, double.NaN, 0, false, HookType.Double, 1),
        new(45792, BiteType.Weak, double.NaN, 0, false, HookType.Double, 2),
        new(45792, BiteType.Weak, double.NaN, 0, false, HookType.Double, 2),
        new(45792, BiteType.Weak, double.NaN, 0, false, HookType.Triple, 3),
    ];

    // ── 純判斷（不讀遊戲、不讀設定），離線探針也走這裡 ──────────────────────

    /// <summary>
    /// 這一咬放不放三重／雙重提鉤。先看同情境、時間窗內的樣本；沒有就退到同咬鉤力道的全部樣本
    /// （沒看過的時間不賭：只起 1 條的魚很常見時照樣不放）；連這個也沒有＝<see cref="MultiHookGate.NoData"/>（照 preset）。
    /// </summary>
    public static MultiHookGate Judge(IReadOnlyList<FishSample> samples, BiteType bite, double seconds,
        int lureStacks, bool chum)
    {
        var inWindow = new List<uint>();
        var pool = new List<uint>();
        foreach (var s in samples)
        {
            if (s.Bite != bite || double.IsNaN(s.Seconds))
                continue;
            pool.Add(s.FishId);
            if (s.LureStacks == lureStacks && s.Chum == chum && Math.Abs(s.Seconds - seconds) <= WindowSeconds)
                inWindow.Add(s.FishId);
        }

        var fromPool = inWindow.Count == 0;
        if (fromPool)
            inWindow = pool;
        if (inWindow.Count == 0)
            return MultiHookGate.NoData;

        var table = YieldTable(samples);
        double extraDouble = 0, extraTriple = 0;
        foreach (var fish in inWindow)
        {
            var (d, t) = ExpectedYield(table, fish);
            extraDouble += d - 1;
            extraTriple += t - 1;
        }

        extraDouble /= inWindow.Count;
        extraTriple /= inWindow.Count;

        var doubleWorth = extraDouble >= MinExtraFishPerHook;
        var tripleWorth = extraTriple >= 2 * MinExtraFishPerHook;
        var preferDouble = doubleWorth && extraTriple / TripleHookGp < extraDouble / DoubleHookGp;

        var detail = string.Join(@"、", inWindow.GroupBy(f => f).OrderByDescending(g => g.Count())
            .Select(g => @$"#{g.Key}×{g.Count()}"));
        return new MultiHookGate(tripleWorth, doubleWorth, preferDouble, inWindow.Count, extraDouble, extraTriple,
            detail, fromPool);
    }

    /// <summary>每條魚的（雙重平均起獲, 三重平均起獲）；沒觀測過的那一格是 NaN。</summary>
    internal static Dictionary<uint, (double Double, double Triple)> YieldTable(IReadOnlyList<FishSample> samples)
    {
        var sums = new Dictionary<uint, (double ds, int dn, double ts, int tn)>();
        foreach (var s in samples)
        {
            if (s.Amount == 0 || s.Hook is not (HookType.Double or HookType.Triple))
                continue;
            sums.TryGetValue(s.FishId, out var v);
            sums[s.FishId] = s.Hook == HookType.Double
                ? (v.ds + s.Amount, v.dn + 1, v.ts, v.tn)
                : (v.ds, v.dn, v.ts + s.Amount, v.tn + 1);
        }

        return sums.ToDictionary(kv => kv.Key, kv => (
            kv.Value.dn > 0 ? kv.Value.ds / kv.Value.dn : double.NaN,
            kv.Value.tn > 0 ? kv.Value.ts / kv.Value.tn : double.NaN));
    }

    /// <summary>
    /// 沒觀測過的那一格用推估：另一種多重提鉤只起 1 條＝這種也只起 1 條（這條魚不吃多重提鉤）；
    /// 否則雙重至少 2、三重至少「雙重＋1」且至少 3。
    /// </summary>
    internal static (double Double, double Triple) ExpectedYield(
        Dictionary<uint, (double Double, double Triple)> table, uint fish)
    {
        if (!table.TryGetValue(fish, out var y))
            return (2, 3);

        var d = y.Double;
        var t = y.Triple;
        if (double.IsNaN(d))
            d = !double.IsNaN(t) && t <= 1 ? 1 : 2;
        if (double.IsNaN(t))
            t = d <= 1 ? 1 : Math.Max(3, d + 1);
        return (d, t);
    }

    // ── 遊戲端 ───────────────────────────────────────────────────────────

    private static readonly object Gate = new();
    private static Dictionary<ushort, List<FishSample>>? _byMission;
    private static long _lastSaveTick = Environment.TickCount64;
    private static int _saving;
    private static bool _saveFailureLogged;
    private static ushort _lureSkipLoggedMission;

    /// <summary>咬鉤當下的情境（引誘層數、撒餌）。只在框架執行緒、咬鉤那一刻呼叫。</summary>
    public static (int LureStacks, bool Chum) CurrentContext()
        => (PlayerRes.GetStatusStacks(IDs.Status.ModestLure) + PlayerRes.GetStatusStacks(IDs.Status.AmbitiousLure),
            PlayerRes.HasStatus(IDs.Status.Chum));

    /// <summary>目前任務、這一咬的判斷；設定關閉、不是「每條魚都算分」、沒有任務＝NoData（照 preset）。</summary>
    public static MultiHookGate GateForBite(BiteType bite, double seconds)
    {
        if (!Service.Configuration.MultiHookByObservedYield)
            return MultiHookGate.NoData;

        var profile = CosmicMissionInfo.GetCurrentProfile();
        if (!profile.EveryFishCounts)
            return MultiHookGate.NoData;

        var mission = CosmicMissionInfo.GetCurrentMissionId();
        if (mission == 0)
            return MultiHookGate.NoData;

        var (lure, chum) = CurrentContext();
        return Judge(Snapshot(mission), bite, seconds, lure, chum);
    }

    /// <summary>
    /// 只算條數的任務（WKSMissionText 115）不放「任何魚都可以」的引誘：每次引誘後 5 秒內魚不會咬、
    /// 三層共 60 GP，而任何一條魚都算數。鎖定特殊魚影（<see cref="LureTarget.Special"/>）的不受影響。
    /// </summary>
    public static bool ShouldSkipLures(LureTarget target)
    {
        if (target != LureTarget.Any || !Service.Configuration.SkipLuresWhenAnyFishCounts)
            return false;

        if (!CosmicMissionInfo.IsInCosmicZone() || !CosmicMissionInfo.GetCurrentProfile().AnyFishCountOnly)
            return false;

        var mission = CosmicMissionInfo.GetCurrentMissionId();
        if (mission != _lureSkipLoggedMission)
        {
            _lureSkipLoggedMission = mission;
            Service.PrintInfo(@$"[宇宙探索] 任務#{mission} 只算釣到幾條：preset 的引誘（任何魚）這個任務不放" +
                              @"（引誘後 5 秒內不會咬鉤、三層共 60 GP）。設定頁可關。");
        }

        return true;
    }

    /// <summary>記一竿。只在宇宙探索、有任務、咬鉤力道已知時記；失敗只寫 log，不影響釣魚。</summary>
    public static void Record(BiteType bite, double seconds, int lureStacks, bool chum, HookType? hook,
        uint fishId, uint amount)
    {
        try
        {
            if (bite == BiteType.Unknown || double.IsNaN(seconds) || !CosmicMissionInfo.IsInCosmicZone())
                return;

            var mission = CosmicMissionInfo.GetCurrentMissionId();
            if (mission == 0)
                return;

            var sample = new FishSample(fishId, bite, seconds, lureStacks, chum, hook ?? HookType.None, amount);
            Dictionary<ushort, List<FishSample>>? toSave = null;
            lock (Gate)
            {
                var all = EnsureLoadedLocked();
                if (!all.TryGetValue(mission, out var list))
                    all[mission] = list = new List<FishSample>();
                list.Add(sample);
                if (list.Count > MaxSamplesPerMission)
                    list.RemoveRange(0, list.Count - MaxSamplesPerMission);

                if (Environment.TickCount64 - _lastSaveTick >= SaveIntervalMs && _saving == 0)
                {
                    _lastSaveTick = Environment.TickCount64;
                    toSave = all.ToDictionary(kv => kv.Key, kv => new List<FishSample>(kv.Value));
                }
            }

            if (toSave != null && Interlocked.CompareExchange(ref _saving, 1, 0) == 0)
                Task.Run(() => Save(toSave));
        }
        catch (Exception e)
        {
            Service.PluginLog.Error($"[宇宙多重提鉤] 記錄失敗：{e.Message}");
        }
    }

    /// <summary>這個任務的樣本（含起獲數種子）的複本。</summary>
    public static List<FishSample> Snapshot(ushort mission)
    {
        lock (Gate)
        {
            var all = EnsureLoadedLocked();
            var copy = new List<FishSample>(Seeds);
            if (all.TryGetValue(mission, out var list))
                copy.AddRange(list);
            return copy;
        }
    }

    public static string Describe(MultiHookGate gate) =>
        @$"{(gate.FromPool ? @"時間窗沒樣本、同力道全部" : @"時間窗內")} {gate.Matched} 筆（{gate.Detail}）｜平均多起獲：雙重 " +
        gate.ExtraDouble.ToString(@"0.00", CultureInfo.InvariantCulture) + @" 條、三重 " +
        gate.ExtraTriple.ToString(@"0.00", CultureInfo.InvariantCulture) + @" 條";

    private static string FilePath() => Path.Combine(Service.PluginInterface.GetPluginConfigDirectory(), FileName);

    /// <summary>第一次用到才讀檔（檔很小）；讀不到就從空的開始，不擋釣魚。呼叫端必須持有 <see cref="Gate"/>。</summary>
    private static Dictionary<ushort, List<FishSample>> EnsureLoadedLocked()
    {
        if (_byMission != null)
            return _byMission;

        _byMission = new Dictionary<ushort, List<FishSample>>();
        try
        {
            var path = FilePath();
            if (!File.Exists(path))
                return _byMission;

            var loaded = JsonSerializer.Deserialize<Dictionary<ushort, List<FishSample>>>(File.ReadAllText(path));
            if (loaded != null)
                _byMission = loaded;
        }
        catch (Exception e)
        {
            Service.PluginLog.Warning($"[宇宙多重提鉤] 讀不到樣本檔，從空的開始：{e.Message}");
        }

        return _byMission;
    }

    // 刻意不在卸載時補存：最多少掉最後 30 秒的樣本，換來卸載路徑不碰檔案。
    private static void Save(Dictionary<ushort, List<FishSample>> data)
    {
        try
        {
            var path = FilePath();
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e)
        {
            if (!_saveFailureLogged)
            {
                _saveFailureLogged = true;
                Service.PluginLog.Warning($"[宇宙多重提鉤] 樣本存檔失敗（之後不再重複提示）：{e.Message}");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _saving, 0);
        }
    }
}
