using System.Collections.Generic;
using AutoHook.Time;

namespace AutoHook.Utils;

/// <summary>
/// 把「天氣時段」放回時間軸上，並跨幀記錄已經觀察到的天氣。
///
/// <para>
/// 天氣每 8 個艾奧傑亞小時換一次，也就是每 1400 真實秒（23 分 20 秒）。
/// 時段邊界對齊 Unix epoch，所以第 n 個時段的起點就是
/// <c>now.SyncToEorzeaWeather()</c> 再加 n 個時段長度 —— 不需要問遊戲。
/// </para>
///
/// <para>
/// 🔴 <b>「前一個時段的天氣」是這裡存在的唯一理由。</b>
/// 遊戲的 <c>GetWeatherForHour</c> 不能餵負數 offset（會靜默算出完全錯誤的答案），
/// Dalamud API13 也沒有 <c>GetPreviousWeather()</c>。所以要知道「上一個時段下的是什麼」，
/// 只能在它還是「目前時段」的時候把它記下來。
/// 沒記到就是<b>不知道</b> —— 回 null，<b>不要回 0</b>（0 是 <c>Weather</c> 表的合法列號）。
/// </para>
/// </summary>
public static class WeatherTimeline
{
    /// <summary>一個天氣時段的真實毫秒數（8 個 ET 小時）。</summary>
    public const long PeriodMs = EorzeaTimeStampExtensions.MillisecondsPerEorzeaWeather;

    /// <summary>
    /// 記錄上限。每個時段 23 分 20 秒，256 筆 ≈ 4 天份，
    /// 遠超過任何窗口計算需要回看的範圍。有上限是為了不讓長時間掛機把記憶體吃掉。
    /// </summary>
    private const int MaxObservations = 256;

    private static readonly Dictionary<(ushort Territory, long Period), byte> Observed = new();
    private static readonly Queue<(ushort Territory, long Period)> Order = new();

    /// <summary>把某個時刻換算成天氣時段序號（對齊 Unix epoch，全世界一致）。</summary>
    public static long PeriodIndex(TimeStamp time)
        => time.Time / PeriodMs;

    /// <summary>第 <paramref name="periodIndex"/> 個天氣時段的起點。</summary>
    public static TimeStamp PeriodStart(long periodIndex)
        => new(periodIndex * PeriodMs);

    /// <summary>以 <paramref name="now"/> 為基準，往後第 <paramref name="offset"/> 個天氣時段的完整區間。</summary>
    public static TimeInterval PeriodAt(TimeStamp now, int offset)
    {
        var start = PeriodStart(PeriodIndex(now) + offset);
        return new TimeInterval(start, start + PeriodMs);
    }

    /// <summary>
    /// 記下「這個地區在這個時段下的是什麼天氣」。每幀呼叫是安全的（同一格只會存一次）。
    /// </summary>
    public static void Record(ushort territoryTypeId, TimeStamp now, byte weatherId)
    {
        var key = (territoryTypeId, PeriodIndex(now));
        if (Observed.ContainsKey(key))
            return;

        Observed[key] = weatherId;
        Order.Enqueue(key);

        while (Order.Count > MaxObservations)
            Observed.Remove(Order.Dequeue());
    }

    /// <summary>
    /// 查已記錄的天氣。<b>沒記到回 null，那就是「不知道」</b>，呼叫端必須把它跟
    /// 「知道，而且不符合」分開處理。
    /// </summary>
    public static byte? GetObserved(ushort territoryTypeId, long periodIndex)
        => Observed.TryGetValue((territoryTypeId, periodIndex), out var id) ? id : null;

    /// <summary>目前有多少筆跨幀記錄（診斷用；0 代表「前置天氣條件一律顯示成不知道」）。</summary>
    public static int ObservationCount
        => Observed.Count;

    /// <summary>換地圖或重載時清掉，避免拿舊地區的記錄去判斷新地區。</summary>
    public static void Clear()
    {
        Observed.Clear();
        Order.Clear();
    }
}
