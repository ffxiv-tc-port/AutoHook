using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AutoHook.Configurations;
using AutoHook.Enums;
using AutoHook.Fishing;

namespace AutoHook.Utils;

/// <summary>
/// 宇宙探索「湊幾種不同的魚」任務（WKSMissionText 113、116）：咬鉤可能是還沒釣到的魚種時照提，
/// 還有不需要魚影的魚種沒釣到時不因為沒出魚影就收竿。
/// 本檔假設與契約：
/// ①目標魚種＝內建魚池先驗的整個釣場魚池（台服 14 個種類型任務的釣場魚池與先驗逐列相同）；任務不在先驗＝不覆寫。
/// ②已釣到＝本次釣魚期間的釣獲計數（<see cref="FishingManager.FishingHelper.SessionCatch"/>，收竿清空，與 preset 換階同一份）。
/// ③只會把「歇竿」改成單鉤、把「收竿」改成繼續等；preset 要提的咬鉤一律照舊。
/// </summary>
public static class CosmicDistinctSpecies
{
    /// <summary>某種餌在這個任務至少有這麼多筆釣獲紀錄、都沒有某條魚（而那條魚用別的餌釣到過），才認定這種餌釣不到牠。</summary>
    public const int MinBaitSamples = 8;

    /// <summary>同一條魚在同樣引誘層數＋撒餌下至少有這麼多筆實測，就改用實測秒數範圍（±<see cref="CosmicMultiHookYield.WindowSeconds"/>）。</summary>
    public const int MinTimeSamples = 5;

    // ── 純判斷（不讀遊戲、不讀設定），離線探針也走這裡 ──────────────────────

    /// <summary>
    /// 這一咬可能是哪些還沒釣到的目標魚：同咬鉤力道、秒數對得上（<see cref="TimeFits"/>）、
    /// 不在 <paramref name="baitExcluded"/>；魚影魚只在牠的魚影正出現時算。
    /// </summary>
    internal static List<PriorFish> PossibleNewSpecies(IReadOnlyList<PriorFish> prior,
        IReadOnlyDictionary<uint, int> caught, BiteType bite, double seconds, int lureStacks, bool chum,
        IReadOnlyCollection<uint> shadowFish, uint activeShadow, IReadOnlyCollection<uint> baitExcluded,
        IReadOnlyList<FishSample> samples)
        => prior.Where(p => p.Bite == bite
                            && caught.GetValueOrDefault(p.FishId) == 0
                            && !baitExcluded.Contains(p.FishId)
                            && (!shadowFish.Contains(p.FishId) || p.FishId == activeShadow)
                            && TimeFits(p, seconds, lureStacks, chum, samples))
            .ToList();

    /// <summary>
    /// 秒數對不對得上：這條魚在同樣引誘層數＋撒餌下有 <see cref="MinTimeSamples"/> 筆以上實測時看實測範圍，
    /// 否則看先驗（<see cref="CosmicMultiHookYield.TimeCompatible"/>：引誘只看下限、撒餌只看上限）。
    /// 撒餌會把咬鉤壓到先驗下限之前，只看先驗時分不出同力道的魚。
    /// </summary>
    internal static bool TimeFits(PriorFish p, double seconds, int lureStacks, bool chum,
        IReadOnlyList<FishSample> samples)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        var n = 0;
        foreach (var s in samples)
        {
            if (s.FishId != p.FishId || s.LureStacks != lureStacks || s.Chum != chum || double.IsNaN(s.Seconds))
                continue;
            n++;
            lo = System.Math.Min(lo, s.Seconds);
            hi = System.Math.Max(hi, s.Seconds);
        }

        if (n < MinTimeSamples || double.IsNaN(seconds))
            return CosmicMultiHookYield.TimeCompatible(p, seconds, lureStacks, chum);
        return seconds >= lo - CosmicMultiHookYield.WindowSeconds && seconds <= hi + CosmicMultiHookYield.WindowSeconds;
    }

    /// <summary>還沒釣到、不需要魚影、這種餌釣得到的目標魚（有任何一條＝沒出魚影也值得等這一竿）。</summary>
    internal static List<PriorFish> UncaughtWithoutShadow(IReadOnlyList<PriorFish> prior,
        IReadOnlyDictionary<uint, int> caught, IReadOnlyCollection<uint> shadowFish,
        IReadOnlyCollection<uint> baitExcluded)
        => prior.Where(p => caught.GetValueOrDefault(p.FishId) == 0
                            && !shadowFish.Contains(p.FishId)
                            && !baitExcluded.Contains(p.FishId))
            .ToList();

    /// <summary>
    /// 從實測樣本判「這種餌釣不到」的魚：這種餌已有 <see cref="MinBaitSamples"/> 筆以上紀錄都沒有牠，
    /// 而牠用別的餌釣到過。餌不明（0）或樣本不夠＝空集合（不排除任何魚）。
    /// </summary>
    internal static HashSet<uint> BaitExcluded(IReadOnlyList<FishSample> samples, uint bait)
    {
        var result = new HashSet<uint>();
        if (bait == 0)
            return result;

        var withBait = new HashSet<uint>();
        var otherBait = new HashSet<uint>();
        var count = 0;
        foreach (var s in samples)
        {
            if (s.Bait == 0)
                continue;
            if (s.Bait == bait)
            {
                count++;
                withBait.Add(s.FishId);
            }
            else
                otherBait.Add(s.FishId);
        }

        if (count < MinBaitSamples)
            return result;

        foreach (var f in otherBait)
            if (!withBait.Contains(f))
                result.Add(f);
        return result;
    }

    // ── 遊戲端（只在框架執行緒呼叫）───────────────────────────────────────

    private static readonly Dictionary<ushort, HashSet<uint>> ShadowFishByMission = new();
    private static ushort _intuitionMission;
    private static int _holdCast = -1;
    private static bool _holdResult;
    private static bool _failureLogged;

    private readonly record struct Context(ushort Mission, IReadOnlyList<PriorFish> Prior,
        Dictionary<uint, int> Caught, HashSet<uint> ShadowFish, HashSet<uint> BaitExcluded, List<FishSample> Samples);

    /// <summary>
    /// 能不能覆寫；不能＝null。preset 用到漁師之識專用設定的任務整場不覆寫（那類 preset 的換階順序綁在誰先上鉤）。
    /// </summary>
    private static Context? TryContext(HookConfig cfg, bool mooching, uint bait)
    {
        if (!Service.Configuration.HookNewSpeciesWhenDistinctSpecies || mooching)
            return null;
        if (!CosmicMissionInfo.IsInCosmicZone())
            return null;

        var profile = CosmicMissionInfo.GetCurrentProfile();
        if (!profile.DistinctSpecies || profile.Size != CosmicSizeScoring.None)
            return null;

        var mission = CosmicMissionInfo.GetCurrentMissionId();
        if (mission == 0)
            return null;

        if (cfg.IntuitionHook.UseCustomStatusHook)
            _intuitionMission = mission;
        if (_intuitionMission == mission || FishingManager.IntuitionStatus == IntuitionStatus.Active)
            return null;

        var prior = CosmicFishPrior.ForMission(mission);
        if (prior.Count == 0)
            return null;

        var caught = new Dictionary<uint, int>();
        foreach (var (id, n) in FishingManager.FishingHelper.SessionCatch)
            if (id > 0)
                caught[(uint)id] = n;

        var samples = CosmicMultiHookYield.Snapshot(mission);
        return new Context(mission, prior, caught, ShadowFishFor(mission, prior), BaitExcluded(samples, bait), samples);
    }

    private static HashSet<uint> ShadowFishFor(ushort mission, IReadOnlyList<PriorFish> prior)
    {
        if (ShadowFishByMission.TryGetValue(mission, out var set))
            return set;

        set = new HashSet<uint>(prior.Select(p => p.FishId)
            .Where(id => GameRes.Fishes.Any(f => f.Id == id && f.LureMessage != "")));
        if (GameRes.Fishes.Count > 0)
            ShadowFishByMission[mission] = set;
        return set;
    }

    /// <summary>
    /// preset 這一咬要歇竿時呼叫：可能是還沒釣到的目標魚＝回傳 true 與要用的單鉤，並寫一行 Information。
    /// 任何例外都當成「不覆寫」（照 preset 歇竿），不讓提鉤流程中斷。
    /// </summary>
    public static bool TryHookInstead(HookConfig cfg, BiteType bite, double seconds, int lureStacks, bool chum,
        bool mooching, uint activeShadow, uint bait, string? skipReason, out HookType hook)
    {
        hook = HookType.None;
        try
        {
            return TryHookInsteadCore(cfg, bite, seconds, lureStacks, chum, mooching, activeShadow, bait, skipReason,
                out hook);
        }
        catch (Exception e)
        {
            LogFailure(e);
            hook = HookType.None;
            return false;
        }
    }

    private static bool TryHookInsteadCore(HookConfig cfg, BiteType bite, double seconds, int lureStacks, bool chum,
        bool mooching, uint activeShadow, uint bait, string? skipReason, out HookType hook)
    {
        hook = HookType.None;
        if (bite is BiteType.Unknown or BiteType.None)
            return false;

        if (TryContext(cfg, mooching, bait) is not { } ctx)
            return false;

        var maybe = PossibleNewSpecies(ctx.Prior, ctx.Caught, bite, seconds, lureStacks, chum, ctx.ShadowFish,
            activeShadow, ctx.BaitExcluded, ctx.Samples);
        if (maybe.Count == 0)
            return false;

        hook = cfg.SingleHookFor(bite);
        Service.PrintInfo(
            @$"[HookManager] 湊種類型（任務#{ctx.Mission}）：{CosmicCatchLog.BiteLabel(bite)}" +
            @$" 拋竿後 {seconds.ToString(@"0.00", CultureInfo.InvariantCulture)} 秒，" +
            @$"可能是還沒釣到的 {Names(maybe)}，照提（{HookLabel(hook)}）" +
            @$"｜preset 原本歇竿：{skipReason ?? @"?"}" +
            (ctx.BaitExcluded.Count > 0 ? @$"｜這種餌釣不到（實測）：{Names(ctx.BaitExcluded)}" : string.Empty));
        return true;
    }

    /// <summary>
    /// 引誘放滿、沒出魚影、preset 設了「沒魚影就歇竿」時呼叫：還有不需要魚影的目標魚沒釣到＝這一竿不收。
    /// 同一竿只判一次、只寫一行；例外＝照 preset 收竿。
    /// </summary>
    public static bool ShouldHoldLureCancel(HookConfig cfg, bool mooching, uint bait, int castSerial)
    {
        if (castSerial == _holdCast)
            return _holdResult;

        _holdCast = castSerial;
        _holdResult = false;
        try
        {
            _holdResult = HoldCore(cfg, mooching, bait);
        }
        catch (Exception e)
        {
            LogFailure(e);
        }

        return _holdResult;
    }

    private static bool HoldCore(HookConfig cfg, bool mooching, uint bait)
    {
        if (TryContext(cfg, mooching, bait) is not { } ctx)
            return false;

        var waiting = UncaughtWithoutShadow(ctx.Prior, ctx.Caught, ctx.ShadowFish, ctx.BaitExcluded);
        if (waiting.Count == 0)
            return false;

        Service.PrintInfo(
            @$"[魚影] 湊種類型（任務#{ctx.Mission}）：引誘放滿沒出魚影，但還沒釣到的 {Names(waiting)} 不需要魚影，" +
            @"這一竿不收竿、等咬鉤");
        return true;
    }

    private static void LogFailure(Exception e)
    {
        if (_failureLogged)
            return;
        _failureLogged = true;
        Service.PluginLog.Warning($"[湊種類型] 判斷失敗，照 preset（之後不再重複提示）：{e}");
    }

    private static string Names(IEnumerable<PriorFish> fish) => Names(fish.Select(p => p.FishId));

    private static string Names(IEnumerable<uint> ids)
        => string.Join(@"／", ids.Select(id => @$"{MultiString.GetItemName(id)}(#{id})"));

    private static string HookLabel(HookType hook) => hook switch
    {
        HookType.Precision => @"精準提鉤",
        HookType.Powerful => @"強力提鉤",
        _ => @"一般提鉤",
    };
}
