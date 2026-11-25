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

/// <summary>
/// 這一咬值不值得放三重／雙重提鉤。<paramref name="Matched"/>＝時間窗內的實測筆數；
/// 它是 0 而且不是 <paramref name="FromPrior"/>＝沒有可用資料、照 preset。
/// </summary>
/// <param name="PreferDouble">每 GP 多起獲的條數雙重提鉤比較多：preset 這一咬放得出雙重提鉤時就不放三重。</param>
/// <param name="FromPool">時間窗內沒有樣本，改用這個任務同咬鉤力道的全部樣本估。</param>
/// <param name="FromPrior">候選魚來自內建魚池先驗（再以時間窗內實測加權）。</param>
public readonly record struct MultiHookGate(
    bool TripleWorth, bool DoubleWorth, bool PreferDouble, int Matched, double ExtraDouble, double ExtraTriple,
    string Detail, bool FromPool = false, bool FromPrior = false)
{
    public static readonly MultiHookGate NoData = new(true, true, false, 0, double.NaN, double.NaN, string.Empty);
}

/// <summary>
/// 宇宙探索「每條魚都算分」的任務：用實測的「這個咬鉤時間窗裡上鉤的是哪些魚」＋「那些魚雙重／三重提鉤實際起幾條」
/// 決定這一咬值不值得放雙重／三重提鉤；另判斷只算條數的任務要不要放引誘。
/// 樣本依任務分開存、存檔在插件設定目錄（下次開遊戲接著用）；起獲數是魚的屬性，跨情境共用。
/// 實測不足時用內建魚池先驗（<see cref="CosmicFishPrior"/>）補：每個任務的魚、咬鉤力道、雙重提鉤起獲、咬鉤秒數。
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
        => JudgeWithPrior(samples, [], bite, seconds, lureStacks, chum);

    /// <summary>
    /// 同 <see cref="Judge"/>，再加內建魚池先驗（<see cref="CosmicFishPrior"/>）：這個咬鉤力道在先驗裡有魚時，
    /// 候選＝咬鉤秒數對得上的先驗魚，每條權重＝1＋時間窗內實測次數（實測越多越以實測為準）；
    /// 秒數全對不上就取同力道全部先驗魚。先驗裡沒有這個力道＝與 <see cref="Judge"/> 相同。
    /// </summary>
    public static MultiHookGate JudgeWithPrior(IReadOnlyList<FishSample> samples, IReadOnlyList<PriorFish> prior,
        BiteType bite, double seconds, int lureStacks, bool chum)
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

        var priorYield = new Dictionary<uint, double>();
        var sameBite = new List<PriorFish>();
        foreach (var p in prior)
        {
            priorYield[p.FishId] = p.DoubleYield;
            if (p.Bite == bite)
                sameBite.Add(p);
        }

        var table = YieldTable(samples);
        if (sameBite.Count > 0)
        {
            var candidates = sameBite.Where(p => TimeCompatible(p, seconds, lureStacks, chum)).ToList();
            if (candidates.Count == 0)
                candidates = sameBite;

            var weights = new Dictionary<uint, double>();
            foreach (var p in candidates)
                weights[p.FishId] = 1;
            foreach (var f in inWindow)
                weights[f] = weights.GetValueOrDefault(f) + 1;
            return Score(weights.Select(kv => (kv.Key, kv.Value)).ToList(), table, priorYield, inWindow.Count,
                false, true);
        }

        var fromPool = inWindow.Count == 0;
        if (fromPool)
            inWindow = pool;
        if (inWindow.Count == 0)
            return MultiHookGate.NoData;

        return Score(inWindow.GroupBy(f => f).Select(g => (g.Key, (double)g.Count())).ToList(), table, priorYield,
            inWindow.Count, fromPool, false);
    }

    public const double PriorEarlySeconds = 0.75;
    public const double PriorLateSeconds = 2.5;

    /// <summary>
    /// 先驗秒數是不放引誘、不撒餌時量的：引誘只會讓魚晚咬（只看下限）、撒餌只會讓魚早咬（只看上限），兩個都有就不看。
    /// 容許量：本機實測（不放引誘）的咬鉤秒數比先驗早最多約 0.4 秒、晚最多約 2 秒。
    /// </summary>
    internal static bool TimeCompatible(PriorFish p, double seconds, int lureStacks, bool chum)
    {
        if (double.IsNaN(p.TimeMin) || double.IsNaN(p.TimeMax) || double.IsNaN(seconds))
            return true;
        var aboveMin = seconds >= p.TimeMin - PriorEarlySeconds;
        var belowMax = seconds <= p.TimeMax + PriorLateSeconds;
        return (lureStacks > 0, chum) switch
        {
            (false, false) => aboveMin && belowMax,
            (true, false) => aboveMin,
            (false, true) => belowMax,
            _ => true,
        };
    }

    private static MultiHookGate Score(List<(uint Fish, double Weight)> weighted,
        Dictionary<uint, (double Double, double Triple)> table, Dictionary<uint, double> priorYield,
        int matched, bool fromPool, bool fromPrior)
    {
        double extraDouble = 0, extraTriple = 0, total = 0;
        foreach (var (fish, w) in weighted)
        {
            var (d, t) = ExpectedYield(table, fish, priorYield.GetValueOrDefault(fish, double.NaN));
            extraDouble += w * (d - 1);
            extraTriple += w * (t - 1);
            total += w;
        }

        extraDouble /= total;
        extraTriple /= total;

        var doubleWorth = extraDouble >= MinExtraFishPerHook;
        var tripleWorth = extraTriple >= 2 * MinExtraFishPerHook;
        var preferDouble = doubleWorth && extraTriple / TripleHookGp < extraDouble / DoubleHookGp;

        var detail = string.Join(@"、", weighted.OrderByDescending(x => x.Weight)
            .Select(x => @$"#{x.Fish}×{x.Weight.ToString(@"0.#", CultureInfo.InvariantCulture)}"));
        return new MultiHookGate(tripleWorth, doubleWorth, preferDouble, matched, extraDouble, extraTriple,
            detail, fromPool, fromPrior);
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
    /// 沒觀測過的那一格用推估。雙重：三重實測只起 1 條＝1，否則內建先驗 <paramref name="priorDouble"/>，再否則 2。
    /// 三重＝2×雙重－1（實測：雙重 1／2／4 條的魚三重起 1／3／7 條）。
    /// </summary>
    internal static (double Double, double Triple) ExpectedYield(
        Dictionary<uint, (double Double, double Triple)> table, uint fish, double priorDouble = double.NaN)
    {
        if (!table.TryGetValue(fish, out var y))
            y = (double.NaN, double.NaN);

        var d = y.Double;
        var t = y.Triple;
        if (double.IsNaN(d))
            d = !double.IsNaN(t) && t <= 1 ? 1 : !double.IsNaN(priorDouble) ? priorDouble : 2;
        if (double.IsNaN(t))
            t = d <= 1 ? 1 : 2 * d - 1;
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
        return JudgeWithPrior(Snapshot(mission), CosmicFishPrior.ForMission(mission), bite, seconds, lure, chum);
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
        (gate.FromPrior
            ? @$"內建魚池資料（魚糕 v2）秒數對得上的魚＋時間窗內實測 {gate.Matched} 筆（權重 {gate.Detail}）"
            : @$"{(gate.FromPool ? @"時間窗沒樣本、同力道全部" : @"時間窗內")} {gate.Matched} 筆（{gate.Detail}）") +
        @"｜平均多起獲：雙重 " +
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
