// 條件求值的唯一入口。
//
// 結合語意（群組 AND/OR、停用的群組怎麼算、空群組＝true）逐條對照上游 PunishXIV/AutoHook 的
// AutoHook/Conditions/{ConditionSet,ConditionGroup,Condition}.cs
// （BSD 3-Clause License, Copyright (c) 2024, Puni.sh）。
//
// 🔴🔴 這一檔最重要的設計是「**不認得的條件怎麼辦**」，而且答案**依呼叫端而異**：
//
//    上游的做法是「註冊表查不到 → 這一條回 false」。對「要不要多做一件事」（換 preset、換餌）
//    那是安全的：不認得就不動。但同一條規則套到「可不可以提鉤」上就會變成
//    **整個外掛看起來壞掉**（每一咬都放生），那比現況（條件被忽略、照舊提鉤）更糟。
//
//    所以這裡把它拆成兩件事：
//      * <see cref="IsSupported"/>：整份條件裡有沒有我們不認得的型別／進階運算式。
//      * <see cref="Passes"/>：呼叫端自己指定「不支援時要回什麼」（unsupportedResult）。
//    ⇒ 換 preset 傳 false（不切換），提鉤閘門傳 true（不擋）。兩邊的失敗形式都是「維持現況」。
//
// 另外：不認得的型別會寫一行 Information（使用者跑 LogLevel 1，Debug 收得到但單檔數十萬行會淹沒），
// 而且每個型別只寫一次，避免每一咬洗版。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AutoHook.Conditions;

public static class ConditionEvaluator
{
    /// <summary>已經回報過「不支援」的型別代號，避免洗版。整個 session 記一次就好。</summary>
    private static readonly HashSet<string> ReportedUnsupported = new(StringComparer.Ordinal);

    /// <summary>有沒有設定任何群組。沒有＝這份條件等於不存在。</summary>
    public static bool HasGroups(ConditionSet? set) => set is { Groups.Count: > 0 };

    /// <summary>
    /// 有沒有「至少一條真的條件」。
    /// ⚠️ 上游的滑桿式 UI 就算沒有任何條件也一定會留一個空群組，所以
    /// <see cref="HasGroups"/> 為 true 不代表真的設了東西 —— 要判斷「使用者有沒有設定條件」
    /// 一律用這個，不要用 HasGroups（實測 ICE 的 495 preset 裡 IgnoreConditionSet 就是空群組）。
    /// </summary>
    public static bool HasAnyCondition(ConditionSet? set)
        => set is { Groups.Count: > 0 } && set.Groups.Any(g => g.Conditions.Count > 0);

    /// <summary>整份條件是不是我們求得出來的（型別全認得，而且沒有用進階運算式）。</summary>
    public static bool IsSupported(ConditionSet? set)
    {
        if (set == null)
            return true;

        if (!string.IsNullOrWhiteSpace(set.Expression))
            return false;

        foreach (var group in set.Groups)
        {
            foreach (var condition in group.Conditions)
            {
                if (!condition.Enabled)
                    continue;

                if (!ConditionRegistry.IsKnown(condition.TypeId))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 求值。
    /// </summary>
    /// <param name="set">條件。null／沒有群組時回 <paramref name="unconfiguredResult"/>。</param>
    /// <param name="ctx">求值 context。</param>
    /// <param name="unconfiguredResult">沒有設定條件時的答案。</param>
    /// <param name="unsupportedResult">含有不支援的條件時的答案（＝呼叫端的保底行為）。</param>
    /// <param name="where">寫進 log 用的呼叫端名稱。</param>
    public static bool Passes(ConditionSet? set, ConditionContext ctx, bool unconfiguredResult,
        bool unsupportedResult, string where)
    {
        if (!HasGroups(set))
            return unconfiguredResult;

        if (!IsSupported(set))
        {
            ReportUnsupported(set!, where, unsupportedResult);
            return unsupportedResult;
        }

        return EvaluateSet(set!, ctx);
    }

    private static void ReportUnsupported(ConditionSet set, string where, bool unsupportedResult)
    {
        var unknown = new List<string>();

        if (!string.IsNullOrWhiteSpace(set.Expression))
            unknown.Add(@$"進階運算式「{set.Expression}」");

        foreach (var group in set.Groups)
        {
            foreach (var condition in group.Conditions)
            {
                if (condition.Enabled && !ConditionRegistry.IsKnown(condition.TypeId))
                    unknown.Add(condition.TypeId);
            }
        }

        // 每個「型別組合＋呼叫端」只講一次。
        var key = where + @"|" + string.Join(@",", unknown);
        lock (ReportedUnsupported)
        {
            if (!ReportedUnsupported.Add(key))
                return;
        }

        Service.PrintInfo(
            @$"[條件] {where}：這份 preset 用到我們還沒實作的條件（{string.Join(@"、", unknown)}），"
            + @$"整份條件視為{(unsupportedResult ? @"通過" : @"不成立")}（維持既有行為，不猜）。");
    }

    private static bool EvaluateSet(ConditionSet set, ConditionContext ctx)
    {
        // 上游：沒有群組＝true。（呼叫端已經先擋掉，這裡是防禦性重複。）
        if (set.Groups.Count == 0)
            return true;

        var all = set.CombineMode != ConditionCombineMode.Any;

        foreach (var group in set.Groups)
        {
            // 上游：停用的群組在 AND 下算 true、在 OR 下算 false（＝不影響結果）。
            var value = !group.Enabled ? all : EvaluateGroup(group, ctx);

            if (all)
            {
                if (!value)
                    return false;
            }
            else if (value)
            {
                return true;
            }
        }

        return all;
    }

    private static bool EvaluateGroup(ConditionGroup group, ConditionContext ctx)
    {
        var active = group.Conditions.Where(c => c.Enabled).ToList();

        // 上游：整組沒有啟用中的條件＝true。
        if (active.Count == 0)
            return true;

        var all = group.CombineMode != ConditionCombineMode.Any;

        foreach (var condition in active)
        {
            var value = ConditionRegistry.Evaluate(condition.TypeId, condition.Params, ctx);

            if (all)
            {
                if (!value)
                    return false;
            }
            else if (value)
            {
                return true;
            }
        }

        return all;
    }

    /// <summary>
    /// 只描述條件**長什麼樣子**（不讀任何遊戲狀態）。設定視窗等「不保證在遊戲世界裡」的地方用這個。
    /// 為什麼不能在那些地方用 <see cref="Describe"/>，見 <see cref="ConditionRegistry.DescribeShape"/>。
    /// </summary>
    public static string DescribeShape(ConditionSet? set)
    {
        if (!HasAnyCondition(set))
            return @"（未設定條件）";

        var sb = new StringBuilder();
        var groupJoin = set!.CombineMode == ConditionCombineMode.Any ? @" 或 " : @" 且 ";
        var first = true;

        foreach (var group in set.Groups)
        {
            if (!group.Enabled)
                continue;

            var active = group.Conditions.Where(c => c.Enabled).ToList();
            if (active.Count == 0)
                continue;

            if (!first)
                sb.Append(groupJoin);
            first = false;

            var condJoin = group.CombineMode == ConditionCombineMode.Any ? @" 或 " : @" 且 ";
            sb.Append(string.Join(condJoin,
                active.Select(c => ConditionRegistry.DescribeShape(c.TypeId, c.Params))));
        }

        if (!string.IsNullOrWhiteSpace(set.Expression))
            sb.Append(@$"（另有進階運算式「{set.Expression}」—— 我們沒有實作，整份條件會被視為不成立）");

        return sb.Length == 0 ? @"（未設定條件）" : sb.ToString();
    }

    /// <summary>
    /// 診斷用：把整份條件的「目前值 vs 門檻 ＝ 成不成立」寫成一行。
    /// 給「為什麼還沒切換」那種問題用的，所以每一條都要看得到目前值。
    /// 🔴 會現查遊戲狀態 —— 只能在確定人在遊戲世界裡的路徑上呼叫（釣魚流程）。UI 用
    /// <see cref="DescribeShape"/>。
    /// </summary>
    public static string Describe(ConditionSet? set, ConditionContext ctx)
    {
        if (!HasGroups(set))
            return @"（未設定條件）";

        if (!IsSupported(set))
            return @"（含有不支援的條件，未求值）";

        var sb = new StringBuilder();
        var groupJoin = set!.CombineMode == ConditionCombineMode.Any ? @" 或 " : @" 且 ";
        var first = true;

        foreach (var group in set.Groups)
        {
            if (!group.Enabled)
                continue;

            var active = group.Conditions.Where(c => c.Enabled).ToList();
            if (active.Count == 0)
                continue;

            if (!first)
                sb.Append(groupJoin);
            first = false;

            var condJoin = group.CombineMode == ConditionCombineMode.Any ? @" 或 " : @" 且 ";
            sb.Append('[');
            for (var i = 0; i < active.Count; i++)
            {
                if (i > 0)
                    sb.Append(condJoin);

                var condition = active[i];
                var result = ConditionRegistry.Evaluate(condition.TypeId, condition.Params, ctx);
                sb.Append(ConditionRegistry.Describe(condition.TypeId, condition.Params, ctx));
                sb.Append(result ? @" ✔" : @" ✘");
            }

            sb.Append(']');
        }

        if (first)
            return @"（只有空群組，等同未設定）";

        sb.Append(@" ⇒ ");
        sb.Append(EvaluateSet(set, ctx) ? @"成立" : @"不成立");
        return sb.ToString();
    }
}
