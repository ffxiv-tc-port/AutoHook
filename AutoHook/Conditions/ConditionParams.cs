// 條件參數的讀取工具。
//
// 語意逐條對照上游 PunishXIV/AutoHook 的 AutoHook/Conditions/IConditionDefinition.cs
// （BSD 3-Clause License, Copyright (c) 2024, Puni.sh）的靜態輔助函式：
//   GetInt / GetBool / GetOp / CompareInt / GetIds / GetRanges / GetIntCompareParams / GetRangeParams。
// 差別只在來源型別：上游是 Dictionary<string, object>（配一個自訂 converter），我們是 JObject。
//
// 🔴 預設值必須跟上游一模一樣，否則同一段匯入字串在兩邊會得到不同結果，而且**不會報錯**：
//    * 比較運算子預設 ">="（`CompareInt` 的 default 分支也是 >=）
//    * `inv` 缺鍵＝false
//    * 範圍的 max <= 0 代表「沒有上限」，不是「上限 0」

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AutoHook.Conditions;

public static class ConditionParams
{
    /// <summary>
    /// 取一個純量參數。找不到、是 null、或鍵名以 <c>$</c> 開頭（Newtonsoft 的中繼鍵）都回 null。
    /// </summary>
    private static JToken? Raw(JObject? p, string key)
    {
        if (p == null || key.Length == 0 || key[0] == '$')
            return null;

        var token = p[key];
        return token is null or { Type: JTokenType.Null } ? null : token;
    }

    public static int GetInt(JObject? p, string key, int def)
    {
        var token = Raw(p, key);
        if (token == null)
            return def;

        try
        {
            return Convert.ToInt32(((JValue)token).Value);
        }
        catch
        {
            // 型別對不上（例如上游某版把整數寫成字串）時不要擲例外，維持預設值。
            return def;
        }
    }

    public static double GetDouble(JObject? p, string key, double def)
    {
        var token = Raw(p, key);
        if (token == null)
            return def;

        try
        {
            return Convert.ToDouble(((JValue)token).Value);
        }
        catch
        {
            return def;
        }
    }

    public static bool GetBool(JObject? p, string key, bool def)
    {
        var token = Raw(p, key);
        if (token is not JValue value)
            return def;

        return value.Value switch
        {
            bool b => b,
            long l => l != 0,
            int i => i != 0,
            double d => d != 0,
            _ => def,
        };
    }

    /// <summary>比較運算子。上游允許缺鍵，缺鍵時用呼叫端給的預設。</summary>
    public static string GetOp(JObject? p, string key, string def)
    {
        var token = Raw(p, key);
        if (token is not JValue { Value: not null } value)
            return def;

        var s = value.Value.ToString();
        return string.IsNullOrEmpty(s) ? def : s!;
    }

    /// <summary>
    /// <c>ids</c> 參數：可能是陣列，也可能是單一整數（上游兩種都收）。
    /// </summary>
    public static List<uint> GetIds(JObject? p)
    {
        var result = new List<uint>();
        var token = Raw(p, @"ids");
        if (token == null)
            return result;

        try
        {
            switch (token)
            {
                case JArray array:
                    foreach (var item in array)
                    {
                        if (item is JValue { Value: not null } v)
                            result.Add(Convert.ToUInt32(v.Value));
                    }

                    break;
                case JValue { Value: not null } single:
                    result.Add(Convert.ToUInt32(single.Value));
                    break;
            }
        }
        catch
        {
            // 壞資料就當成「沒有指定 id」，讓呼叫端走它的 ids.Count == 0 分支。
            result.Clear();
        }

        return result;
    }

    /// <summary>
    /// <c>r</c> 參數：扁平的 [min0, max0, min1, max1, ...]。
    /// ⚠️ 落單的最後一個值會被丟掉（上游 `for (i + 1 &lt; list.Count; i += 2)` 就是這樣）。
    /// </summary>
    public static List<(double Min, double Max)> GetRanges(JObject? p)
    {
        var result = new List<(double, double)>();
        if (Raw(p, @"r") is not JArray array)
            return result;

        try
        {
            for (var i = 0; i + 1 < array.Count; i += 2)
                result.Add((Convert.ToDouble(((JValue)array[i]).Value), Convert.ToDouble(((JValue)array[i + 1]).Value)));
        }
        catch
        {
            result.Clear();
        }

        return result;
    }

    /// <summary>整數比較的三件組：門檻值、運算子、是否反轉。</summary>
    public readonly record struct IntCompare(int Value, string Op, bool Invert)
    {
        public bool Apply(bool result) => Invert ? !result : result;
    }

    public static IntCompare GetIntCompare(JObject? p, string valueKey = "val", int defaultValue = 0,
        string defaultOp = ">=")
        => new(GetInt(p, valueKey, defaultValue), GetOp(p, @"op", defaultOp), GetBool(p, @"inv", false));

    /// <summary>範圍比較：一組區間 ＋ 是否反轉。</summary>
    public readonly record struct RangeCompare(List<(double Min, double Max)> Ranges, bool Invert)
    {
        public bool Apply(bool result) => Invert ? !result : result;

        /// <summary>時間落在任一區間內。<c>Max &lt;= 0</c> ＝ 沒有上限。</summary>
        public bool Contains(double t)
        {
            foreach (var (min, max) in Ranges)
            {
                if (t >= min && (max <= 0 || t <= max))
                    return true;
            }

            return false;
        }
    }

    public static RangeCompare GetRangeCompare(JObject? p)
        => new(GetRanges(p), GetBool(p, @"inv", false));

    /// <summary>逐字對應上游 <c>IConditionDefinition.CompareInt</c>，含「不認得的運算子當成 &gt;=」。</summary>
    public static bool CompareInt(int lhs, int rhs, string op) => op switch
    {
        ">" => lhs > rhs,
        ">=" => lhs >= rhs,
        "<" => lhs < rhs,
        "<=" => lhs <= rhs,
        "=" => lhs == rhs,
        _ => lhs >= rhs,
    };
}
