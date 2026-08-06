// 條件式設定（ConditionSet）的資料模型。
//
// 來源：上游 PunishXIV/AutoHook 的 AutoHook/Conditions/{ConditionSet,ConditionGroup,Condition,
// ConditionCombineMode}.cs（BSD 3-Clause License, Copyright (c) 2024, Puni.sh）。
// 本檔是**重寫**而非整段搬運：上游那份跑在 Dalamud SDK 15 / API15 上、而且求值時要一個完整的
// WorldState 物件；我們釘 API13，沒有那套 WorldState。所以這裡只保留**序列化格式**與**求值語意**，
// 求值實作改接我們自己的狀態來源（見 ConditionEvaluator / ConditionContext）。
//
// 🔴 序列化的鍵名（m/g/e、m/c/a、t/p/e）**必須逐字元與上游相同** ——
//    它們是外部匯入字串（AH6_／AH7_／AHFOLDER_）的一部分，改一個字就靜默對不上，
//    表現成「條件消失」而不是「匯入失敗」。

using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoHook.Conditions;

/// <summary>群組／條件之間的結合方式。<c>All</c>＝AND、<c>Any</c>＝OR。序列化成整數。</summary>
public enum ConditionCombineMode
{
    All = 0,
    Any = 1,
}

/// <summary>
/// 單一條件：型別代號 ＋ 型別自己的參數。
/// </summary>
/// <remarks>
/// <para><c>Params</c> 刻意用 <see cref="JObject"/> 而不是 <c>Dictionary&lt;string, object&gt;</c>：</para>
/// <para>
/// Dalamud 存設定檔時用的是 <c>TypeNameHandling.Objects</c>（實測使用者的 AutoHook.json 裡有 10332 個
/// <c>$type</c>），<c>object</c> 值會被寫進型別名稱；而 <see cref="JToken"/> 走 Newtonsoft 的
/// <c>JsonLinqContract</c>，會原封不動寫成 JSON。用 JObject 就不必去賭那條路徑的行為。
/// </para>
/// </remarks>
public class Condition
{
    /// <summary>註冊表代號，例如 <c>FishCaughtCountCD</c>、<c>BiteTimerCD</c>。</summary>
    [JsonProperty("t")]
    public string TypeId { get; set; } = "";

    /// <summary>型別自己的參數。上游只寫非預設值，所以缺鍵是正常的。</summary>
    [JsonProperty("p")]
    public JObject? Params { get; set; }

    /// <summary>false ＝ 求值時略過（不刪除也能停用）。</summary>
    [JsonProperty("e")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;
}

/// <summary>一組條件，彼此用 <see cref="CombineMode"/> 結合。</summary>
public class ConditionGroup
{
    [JsonProperty("m")]
    public ConditionCombineMode CombineMode { get; set; } = ConditionCombineMode.All;

    [JsonProperty("c")]
    public List<Condition> Conditions { get; set; } = new();

    /// <summary>false ＝ 求值時略過整組。</summary>
    [JsonProperty("a")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;
}

/// <summary>群組的集合。沒有任何群組時代表「沒有設定條件」。</summary>
public class ConditionSet
{
    [JsonProperty("m")]
    public ConditionCombineMode CombineMode { get; set; } = ConditionCombineMode.All;

    [JsonProperty("g")]
    public List<ConditionGroup> Groups { get; set; } = new();

    /// <summary>
    /// 上游的進階運算式（例如 <c>A &amp;&amp; (B || C)</c>），會蓋過 <see cref="CombineMode"/>。
    /// 🔴 **我們沒有實作運算式解析器。** 這個欄位有值時整份條件會被判為「不支援」，
    ///    呼叫端會退回它自己的保底行為（見 <see cref="ConditionEvaluator"/>），而不是猜一個答案。
    ///    離線掃過 ICE 內建的 108 段釣魚 preset，這個欄位**全部是 null**。
    /// </summary>
    [JsonProperty("e")]
    public string? Expression { get; set; }
}
