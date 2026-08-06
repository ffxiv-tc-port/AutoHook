// 我們認得的條件型別。
//
// 每一條的參數語意都逐字對照上游 PunishXIV/AutoHook 的 AutoHook/Conditions/Definitions/*.cs
// （BSD 3-Clause License, Copyright (c) 2024, Puni.sh），求值的資料來源改接我們自己的既有 API。
//
// 🔴🔴 兩個名字非常容易搞混，而且搞混了完全不會報錯：
//    * 上游 <c>SessionCaughtCountCD</c> 的 Id 是 **"FishCaughtCountCD"**（本次釣魚期間，某魚 id 釣到幾條）
//    * 上游 <c>FishCaughtCounterCD</c> 的 Id 是 **"FishCountCD"**（把所有 preset 裡該魚的計數器加總）
//    上游原始碼裡兩處都留著 "either migrate this or don't change it" 的註解。
//    ICE 內建 preset 的「換 preset」條件用的是前者（FishCaughtCountCD）。
//
// 📌 離線統計（ICE FishingPresets.cs 的 108 段字串、114 份 preset、解碼零失敗）裡出現過的型別
//    只有下列 10 種，全部在這裡實作了：
//      FishCountCD 76、BiteTimerCD 38、StatusActiveCD 30、SwimbaitCountCD 23、ChumTimerCD 18、
//      FishCaughtCountCD 10、CurrentBaitCD 6、StatusStacksCD 5、BaitCountCD 2、GpCD 1。
//    沒出現過的型別（海釣、天氣、艾奧傑亞時間…）刻意不實作 —— 猜錯的代價是靜默給錯行為，
//    而「不認得」有明確的保底路徑（見 ConditionEvaluator）。

using System;
using System.Collections.Generic;
using System.Linq;
using AutoHook.Data;
using AutoHook.Fishing;
using AutoHook.Utils;
using Newtonsoft.Json.Linq;

namespace AutoHook.Conditions;

public static class ConditionRegistry
{
    /// <summary>求值函式：拿參數與 context，回傳這一條成不成立。</summary>
    public delegate bool Evaluator(JObject? p, ConditionContext ctx);

    private static readonly Dictionary<string, Evaluator> Definitions = new(StringComparer.Ordinal)
    {
        // ── 計數 ────────────────────────────────────────────────────────────
        { @"FishCaughtCountCD", EvalSessionCaughtCount },
        { @"FishCountCD", EvalFishCounter },
        { @"SwimbaitCountCD", EvalSwimbaitCount },
        { @"BaitCountCD", EvalBaitCount },

        // ── 時間窗 ──────────────────────────────────────────────────────────
        { @"BiteTimerCD", EvalBiteTimer },
        { @"ChumTimerCD", EvalChumTimer },

        // ── 玩家狀態 ────────────────────────────────────────────────────────
        { @"StatusActiveCD", EvalStatusActive },
        { @"StatusStacksCD", EvalStatusStacks },
        { @"GpCD", EvalGp },
        { @"CurrentBaitCD", EvalCurrentBait },
    };

    public static bool IsKnown(string typeId) => Definitions.ContainsKey(typeId);

    public static bool Evaluate(string typeId, JObject? p, ConditionContext ctx)
        => Definitions.TryGetValue(typeId, out var eval) && eval(p, ctx);

    /// <summary>診斷用：把這一條的「目前值 vs 門檻」寫成人看得懂的一句話。</summary>
    public static string Describe(string typeId, JObject? p, ConditionContext ctx)
    {
        var compare = ConditionParams.GetIntCompare(p);
        var range = ConditionParams.GetRangeCompare(p);
        var id = ConditionParams.GetInt(p, @"id", 0);

        return typeId switch
        {
            @"FishCaughtCountCD" =>
                @$"本次釣魚已釣到 {FishName(id)}×{SessionCount(id)}（需 {compare.Op}{compare.Value}）",
            @"FishCountCD" =>
                @$"{FishName(id)} 計數器＝{FishCounter(id)}（需 {compare.Op}{compare.Value}）",
            @"SwimbaitCountCD" =>
                @$"泳餌欄位裡的 {(id > 0 ? FishName(id) : @"魚")}＝{SwimbaitCount(id)}（需 {compare.Op}{compare.Value}）",
            @"BaitCountCD" =>
                @$"背包裡的餌 #{id}＝{(id > 0 ? PlayerRes.HasItem((uint)id) : 0)}（需 {compare.Op}{compare.Value}）",
            @"BiteTimerCD" =>
                @$"咬鉤秒數＝{(ctx.BiteSeconds?.ToString(@"0.0") ?? @"?")}（需落在 {DescribeRanges(range)}"
                + (ctx.IgnoreBiteTimers ? @"，但本任務忽略時間窗" : @"") + @"）",
            @"ChumTimerCD" =>
                @$"打窩中＝{PlayerRes.HasStatus(IDs.Status.Chum)}、咬鉤秒數＝{(ctx.BiteSeconds?.ToString(@"0.0") ?? @"?")}"
                + @$"（需落在 {DescribeRanges(range)}）",
            @"StatusActiveCD" =>
                @$"狀態 {string.Join(@"/", ConditionParams.GetIds(p))} 是否存在",
            @"StatusStacksCD" =>
                @$"狀態 {string.Join(@"/", ConditionParams.GetIds(p))} 層數（需 "
                + @$"{ConditionParams.GetIntCompare(p, @"minStacks", 1).Op}{ConditionParams.GetIntCompare(p, @"minStacks", 1).Value}）",
            @"GpCD" => @$"GP＝{PlayerRes.GetCurrentGp()}（需 {compare.Op}{compare.Value}）",
            @"CurrentBaitCD" => @$"目前餌＝{Service.BaitManager.Current}（需為 {string.Join(@"/", ConditionParams.GetIds(p))}）",
            _ => @"（不支援的條件型別）",
        };
    }

    /// <summary>
    /// 只描述條件**長什麼樣子**，不去讀任何遊戲狀態。
    /// </summary>
    /// <remarks>
    /// 🔴 UI 一律用這個、不要用 <see cref="Describe"/>。
    ///    <see cref="Describe"/> 會現查 GP／狀態列／目前餌，那些都要解原生指標；
    ///    設定視窗在不在遊戲世界裡是**使用者說了算**（可以在標題畫面開），
    ///    在那裡解參考就是 AccessViolation，而 AVE 是 try/catch 攔不到的。
    ///    <see cref="Describe"/> 的呼叫點全部在釣魚流程上，那裡指標是已知有效的。
    /// </remarks>
    public static string DescribeShape(string typeId, JObject? p)
    {
        var compare = ConditionParams.GetIntCompare(p);
        var range = ConditionParams.GetRangeCompare(p);
        var id = ConditionParams.GetInt(p, @"id", 0);

        return typeId switch
        {
            @"FishCaughtCountCD" => @$"本次釣魚釣到 {FishName(id)} {compare.Op}{ConditionParams.GetIntCompare(p, defaultValue: 1).Value} 條",
            @"FishCountCD" => @$"{FishName(id)} 的計數器 {compare.Op}{compare.Value}",
            @"SwimbaitCountCD" => @$"泳餌欄位裡的 {(id > 0 ? FishName(id) : @"魚")} {compare.Op}{compare.Value}",
            @"BaitCountCD" => @$"背包裡的餌 #{id} {compare.Op}{compare.Value}",
            @"BiteTimerCD" => @$"咬鉤時間落在 {DescribeRanges(range)}",
            @"ChumTimerCD" => @$"打窩中且咬鉤時間落在 {DescribeRanges(range)}",
            @"StatusActiveCD" => @$"有狀態 {string.Join(@"/", ConditionParams.GetIds(p))}",
            @"StatusStacksCD" =>
                @$"狀態 {string.Join(@"/", ConditionParams.GetIds(p))} 的層數 "
                + @$"{ConditionParams.GetIntCompare(p, @"minStacks", 1).Op}{ConditionParams.GetIntCompare(p, @"minStacks", 1).Value}",
            @"GpCD" => @$"GP {compare.Op}{compare.Value}",
            @"CurrentBaitCD" => @$"目前的餌是 {string.Join(@"/", ConditionParams.GetIds(p))}",
            _ => @$"（不支援的條件：{typeId}）",
        };
    }

    private static string DescribeRanges(ConditionParams.RangeCompare range)
        => range.Ranges.Count == 0
            ? @"任意時間"
            : string.Join(@" 或 ", range.Ranges.Select(r => r.Max <= 0 ? @$"{r.Min:0.0}秒以後" : @$"{r.Min:0.0}~{r.Max:0.0}秒"));

    private static string FishName(int fishId)
    {
        if (fishId <= 0)
            return @"（未指定魚）";

        var fish = GameRes.Fishes.FirstOrDefault(f => f.Id == fishId);
        return fish == null ? @$"#{fishId}" : @$"{fish.Name} (#{fishId})";
    }

    // ── 求值實作 ────────────────────────────────────────────────────────────

    private static int SessionCount(int fishId)
        => fishId <= 0 ? 0 : FishingManager.FishingHelper.GetSessionCatch(fishId);

    /// <summary>
    /// 上游 <c>SessionCaughtCountCD</c>（Id "FishCaughtCountCD"）：本次釣魚期間某魚 id 釣到的數量。
    /// ⚠️ 上游的 <c>defaultValue</c> 是 **1** 不是 0 —— 缺 <c>val</c> 鍵時門檻是 1。
    /// </summary>
    private static bool EvalSessionCaughtCount(JObject? p, ConditionContext ctx)
    {
        var fishId = ConditionParams.GetInt(p, @"id", 0);
        var args = ConditionParams.GetIntCompare(p, defaultValue: 1);

        // 上游：id 沒指定時直接回 Invert（＝「這條沒設定完，當它不成立」）。
        if (fishId <= 0)
            return args.Invert;

        return args.Apply(ConditionParams.CompareInt(SessionCount(fishId), args.Value, args.Op));
    }

    private static int FishCounter(int fishId)
    {
        if (fishId <= 0)
            return 0;

        var presets = Service.Configuration.HookPresets;
        var total = 0;

        foreach (var preset in presets.CustomPresets.Append(presets.DefaultPreset))
        {
            foreach (var fish in preset.ListOfFish)
            {
                if (fish.Fish.Id == fishId)
                    total += FishingManager.FishingHelper.GetFishCount(fish.UniqueId);
            }
        }

        return total;
    }

    /// <summary>上游 <c>FishCaughtCounterCD</c>（Id "FishCountCD"）：所有 preset 裡該魚計數器的總和。</summary>
    private static bool EvalFishCounter(JObject? p, ConditionContext ctx)
    {
        var fishId = ConditionParams.GetInt(p, @"id", 0);
        var args = ConditionParams.GetIntCompare(p);

        if (fishId <= 0)
            return args.Invert;

        return args.Apply(ConditionParams.CompareInt(FishCounter(fishId), args.Value, args.Op));
    }

    private static int SwimbaitCount(int fishId)
    {
        var slots = Service.BaitManager.SwimBaitIds;

        if (fishId > 0)
            return slots.Count(id => id == (uint)fishId);

        return slots.Count(id => id != 0);
    }

    /// <summary>
    /// 上游 <c>SwimbaitCountCD</c>：三個泳餌欄位裡符合條件的數量。
    /// ⚠️ 上游額外支援舊的 <c>above</c> 布林參數（true→"&gt;="、false→"&lt;="），這裡照做。
    /// </summary>
    private static bool EvalSwimbaitCount(JObject? p, ConditionContext ctx)
    {
        var args = ConditionParams.GetIntCompare(p);
        var op = args.Op;

        // 上游 ResolveOp：沒有 op 鍵時才看 above，兩個都沒有就 ">="。
        if (p?[@"op"] == null && p?[@"above"] != null)
            op = ConditionParams.GetBool(p, @"above", true) ? ">=" : "<=";

        var fishId = ConditionParams.GetInt(p, @"id", 0);
        return args.Apply(ConditionParams.CompareInt(SwimbaitCount(fishId), args.Value, op));
    }

    private static bool EvalBaitCount(JObject? p, ConditionContext ctx)
    {
        var baitId = ConditionParams.GetInt(p, @"id", 0);
        var args = ConditionParams.GetIntCompare(p);

        if (baitId <= 0)
            return args.Invert;

        return args.Apply(ConditionParams.CompareInt(PlayerRes.HasItem((uint)baitId), args.Value, args.Op));
    }

    /// <summary>
    /// 上游 <c>BiteTimerCD</c>：咬鉤秒數落在任一區間內。
    /// </summary>
    /// <remarks>
    /// 兩處與上游不同、而且都是刻意的：
    /// <list type="bullet">
    /// <item>本任務忽略時間窗時直接回 true（見 <see cref="ConditionContext.IgnoreBiteTimers"/>）。</item>
    /// <item>沒有咬鉤秒數（不是在判斷某一咬）時回 false —— 不拿 0 當秒數去比。</item>
    /// </list>
    /// </remarks>
    private static bool EvalBiteTimer(JObject? p, ConditionContext ctx)
    {
        var args = ConditionParams.GetRangeCompare(p);

        // 沒設任何區間＝永遠成立（上游：ranges.Count == 0 → return true，注意這裡**不套用** inv）。
        if (args.Ranges.Count == 0)
            return true;

        if (ctx.IgnoreBiteTimers)
            return true;

        if (ctx.BiteSeconds is not { } t)
            return false;

        return args.Apply(args.Contains(t));
    }

    /// <summary>上游 <c>ChumTimerCD</c>：打窩生效中，且咬鉤秒數落在區間內。</summary>
    private static bool EvalChumTimer(JObject? p, ConditionContext ctx)
    {
        var args = ConditionParams.GetRangeCompare(p);

        // 上游：沒在打窩就直接回 Invert。
        if (!PlayerRes.HasStatus(IDs.Status.Chum))
            return args.Invert;

        if (args.Ranges.Count == 0)
            return true;

        if (ctx.IgnoreBiteTimers)
            return true;

        if (ctx.BiteSeconds is not { } t)
            return false;

        return args.Apply(args.Contains(t));
    }

    private static bool EvalStatusActive(JObject? p, ConditionContext ctx)
    {
        var ids = ConditionParams.GetIds(p);
        var invert = ConditionParams.GetBool(p, @"inv", false);

        if (ids.Count == 0)
            return invert;

        var result = ids.Any(PlayerRes.HasStatus);
        return invert ? !result : result;
    }

    private static bool EvalStatusStacks(JObject? p, ConditionContext ctx)
    {
        var ids = ConditionParams.GetIds(p);
        var args = ConditionParams.GetIntCompare(p, @"minStacks", 1);

        // 上游這裡回 false（不是 Invert），照抄。
        if (ids.Count == 0)
            return false;

        var result = ids.Any(id => ConditionParams.CompareInt(PlayerRes.GetStatusStacks(id), args.Value, args.Op));
        return args.Apply(result);
    }

    private static bool EvalGp(JObject? p, ConditionContext ctx)
    {
        var args = ConditionParams.GetIntCompare(p);
        return args.Apply(ConditionParams.CompareInt((int)PlayerRes.GetCurrentGp(), args.Value, args.Op));
    }

    private static bool EvalCurrentBait(JObject? p, ConditionContext ctx)
    {
        var ids = ConditionParams.GetIds(p);
        var invert = ConditionParams.GetBool(p, @"inv", false);

        if (ids.Count == 0)
            return invert;

        var result = ids.Contains(Service.BaitManager.Current);
        return invert ? !result : result;
    }
}
