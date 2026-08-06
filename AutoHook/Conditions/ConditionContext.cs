// 條件求值時需要、但**不能從全域狀態現查**的東西。
//
// 上游把所有狀態集中在一個 WorldState 物件裡（含錄影／重播基礎建設）。那整套跟我們的 API13 基底
// 差太多，搬過來等於重構整個外掛，所以這裡只留「每次求值都不一樣、必須由呼叫端傳進來」的兩個值；
// 其餘（GP、狀態列、目前餌、已釣數量…）一律在求值當下向既有的 PlayerRes／BaitManager／FishingHelper
// 現查，跟外掛其他地方讀同一份資料。
//
// 🔴 這裡刻意**不存任何原生指標**。所有原生資料都在 Evaluate 當下重查一次。

namespace AutoHook.Conditions;

public sealed class ConditionContext
{
    /// <summary>
    /// 這一咬已經過了幾秒。null ＝ 現在不是在判斷某一次咬鉤（例如「釣起之後要不要換 preset」）。
    /// </summary>
    /// <remarks>
    /// null 時 <c>BiteTimerCD</c>／<c>ChumTimerCD</c> 會被判為「不適用」→ 條件回 false。
    /// 那是刻意的：拿一個不存在的咬鉤秒數去比時間窗，答案一定是錯的，而且錯得沒有徵兆。
    /// </remarks>
    public double? BiteSeconds { get; init; }

    /// <summary>
    /// 這一竿要不要忽略「提鉤時間窗」。來自 <c>BaseHookset.ShouldIgnoreHookTimers()</c>
    /// （任務說明講明「每條魚都算分」時為 true）。
    /// </summary>
    /// <remarks>
    /// 🔴 一定要傳進來。上游把時間窗從 <c>MinHookTimer/MaxHookTimer</c> 改成了 <c>BiteTimerCD</c> 條件，
    ///    如果條件求值不看這個旗標，2026-08-06 那個「115／121 任務忽略時間窗」的修法會在
    ///    帶條件的 preset 上**靜默失效** —— 症狀是「換了新 preset 之後又開始一直不提鉤」。
    /// </remarks>
    public bool IgnoreBiteTimers { get; init; }

    /// <summary>釣起之後的判斷（換 preset／換餌）用：沒有咬鉤秒數，也不涉及時間窗。</summary>
    public static ConditionContext AfterCatch() => new();

    /// <summary>咬鉤當下的判斷（要不要提鉤）用。</summary>
    public static ConditionContext OnBite(double biteSeconds, bool ignoreBiteTimers)
        => new() { BiteSeconds = biteSeconds, IgnoreBiteTimers = ignoreBiteTimers };
}
