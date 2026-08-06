using System.Collections.Generic;
using AutoHook.Classes;
using AutoHook.Time;

namespace AutoHook.Utils;

/// <summary>魚的窗口狀態。<b>「不知道」是一個獨立的狀態，不可以四捨五入成「關著」。</b></summary>
public enum FishWindowState
{
    /// <summary>沒有任何條件，隨時都能釣。</summary>
    Always,

    /// <summary>條件目前成立。</summary>
    Open,

    /// <summary>條件目前不成立，但已經算出下一次成立是什麼時候。</summary>
    Closed,

    /// <summary>
    /// 算不出來。原因可能是：天氣查詢不可用、前置天氣條件缺跨幀記錄、
    /// 或搜尋範圍內找不到窗口。<b>畫在畫面上時要用「?」不能用 0 或「關著」。</b>
    /// </summary>
    Unknown,
}

/// <summary>單條魚的窗口判定結果。</summary>
public sealed class FishWindowResult
{
    public FishWindowState State { get; init; } = FishWindowState.Unknown;

    /// <summary>目前這個（<see cref="FishWindowState.Open"/>）或下一個（<see cref="FishWindowState.Closed"/>）窗口。</summary>
    public TimeInterval Window { get; init; } = TimeInterval.Invalid;

    /// <summary>判定成 <see cref="FishWindowState.Unknown"/> 的原因，直接拿去顯示。</summary>
    public string UnknownReason { get; init; } = string.Empty;

    /// <summary>有沒有 ET 時間條件。</summary>
    public bool HasTimeCondition { get; init; }

    /// <summary>有沒有天氣條件。</summary>
    public bool HasWeatherCondition { get; init; }
}

/// <summary>
/// 把 fish_list.json 的 <c>Interval</c>（ET 窗口）與 <c>Weathers</c>／<c>WeathersFrom</c>（天氣條件）
/// 合起來算出「現在開著嗎／下一個窗口什麼時候」。
///
/// <para>
/// 🔴 <b>這裡只算、只顯示，不做任何決策。</b> 自動預備、自動換餌、窗口自動開關釣魚
/// 都不在這一層 —— 那需要另外裁決，而且魚→天氣是社群資料，拿沒驗證過的資料去
/// 自動停手比不做還糟。
/// </para>
///
/// <para>
/// 🔴 <b>「不知道」與「不符合」必須分開。</b> 前置天氣（<c>WeathersFrom</c>）在
/// 「目前這個時段」是問不到遊戲的（負 offset 會靜默算錯），只有外掛從那個時段起
/// 就在跑才記得到。記不到時回 <see cref="FishWindowState.Unknown"/> 並附原因，
/// 不可以當成條件不成立。
/// </para>
/// </summary>
public static class FishWindow
{
    /// <summary>預設往後找幾個天氣時段。24 個時段 ≈ 9 小時 20 分真實時間。</summary>
    public const int DefaultSearchPeriods = 24;

    /// <summary>把 json 的三個欄位轉成可運算的重複區間。</summary>
    public static RepeatingInterval Uptime(ImportedFish fish)
    {
        var iv = fish.Interval;
        return new RepeatingInterval
        {
            OnTime    = iv.OnTime,
            OffTime   = iv.OffTime,
            ShiftTime = iv.ShiftTime,
        };
    }

    /// <summary>這條魚有沒有 ET 時間限制。(1,0,0) 是「隨時」，(0,0,0) 是「不知道」。</summary>
    public static bool HasTimeCondition(ImportedFish fish)
    {
        var uptime = Uptime(fish);
        return uptime != RepeatingInterval.Invalid && !uptime.AlwaysUp();
    }

    /// <summary>這條魚有沒有天氣限制。</summary>
    public static bool HasWeatherCondition(ImportedFish fish)
        => fish.Weathers.Count > 0 || fish.WeathersFrom.Count > 0;

    /// <summary>有沒有任何窗口條件（拿來過濾清單用）。</summary>
    public static bool HasAnyCondition(ImportedFish fish)
        => HasTimeCondition(fish) || HasWeatherCondition(fish);

    /// <summary>
    /// 判定窗口。
    /// </summary>
    /// <param name="fish">要判定的魚。</param>
    /// <param name="territoryTypeId">要用哪個地區的天氣。通常是玩家目前所在地區。</param>
    /// <param name="now">基準時刻（真實時間）。</param>
    /// <param name="searchPeriods">往後找幾個天氣時段。</param>
    public static FishWindowResult Evaluate(ImportedFish fish, ushort territoryTypeId, TimeStamp now,
        int searchPeriods = DefaultSearchPeriods)
    {
        var hasTime = HasTimeCondition(fish);
        var hasWeather = HasWeatherCondition(fish);
        var uptime = Uptime(fish);

        if (uptime == RepeatingInterval.Invalid)
            return new FishWindowResult
            {
                State = FishWindowState.Unknown,
                UnknownReason = @"這條魚的 ET 資料是 (0,0,0)，那代表「未知」而不是「隨時」",
                HasTimeCondition = false,
                HasWeatherCondition = hasWeather,
            };

        if (!hasTime && !hasWeather)
            return new FishWindowResult
            {
                State = FishWindowState.Always,
                Window = TimeInterval.Always,
                HasTimeCondition = false,
                HasWeatherCondition = false,
            };

        IReadOnlyList<byte>? forecast = null;
        if (hasWeather)
        {
            forecast = WeatherForecast.GetPeriodForecast(territoryTypeId, searchPeriods);
            if (forecast == null)
                return new FishWindowResult
                {
                    State = FishWindowState.Unknown,
                    UnknownReason = @"天氣預報現在查不到（還沒進遊戲，或特徵碼失效）",
                    HasTimeCondition = hasTime,
                    HasWeatherCondition = true,
                };
        }

        var missingPrevWeather = false;

        for (var k = 0; k < searchPeriods; k++)
        {
            var period = WeatherTimeline.PeriodAt(now, k);

            if (hasWeather)
            {
                var current = forecast![k];

                if (fish.Weathers.Count > 0 && !fish.Weathers.Contains(current))
                    continue;

                if (fish.WeathersFrom.Count > 0)
                {
                    byte? previous;
                    if (k >= 1)
                    {
                        previous = forecast[k - 1];
                    }
                    else
                    {
                        // 🔴 目前這個時段的「前一個時段」問不到遊戲，只能查自己跨幀記的。
                        previous = WeatherTimeline.GetObserved(territoryTypeId,
                            WeatherTimeline.PeriodIndex(now) - 1);
                        if (previous == null)
                            missingPrevWeather = true;
                    }

                    if (previous == null || !fish.WeathersFrom.Contains(previous.Value))
                        continue;
                }
            }

            var overlap = uptime.FirstOverlap(period);
            if (overlap == TimeInterval.Never || overlap == TimeInterval.Invalid)
                continue;

            if (overlap.End <= now)
                continue;

            return new FishWindowResult
            {
                State = overlap.InRange(now) ? FishWindowState.Open : FishWindowState.Closed,
                Window = overlap,
                HasTimeCondition = hasTime,
                HasWeatherCondition = hasWeather,
            };
        }

        // 沒找到。要把「因為缺前置天氣記錄所以第一格判不了」跟「真的就是很久以後才開」分開講。
        var reason = missingPrevWeather
            ? $@"目前時段的前一個天氣沒有記錄（外掛剛啟動時本來就會這樣），" +
              $@"且往後 {searchPeriods} 個天氣時段內沒有符合的窗口"
            : $@"往後 {searchPeriods} 個天氣時段（約 {searchPeriods * WeatherTimeline.PeriodMs / 3_600_000.0:F1} 小時）內沒有符合的窗口";

        return new FishWindowResult
        {
            State = FishWindowState.Unknown,
            UnknownReason = reason,
            HasTimeCondition = hasTime,
            HasWeatherCondition = hasWeather,
        };
    }

    /// <summary>把一段長度（毫秒）排成台服慣用的倒數字串。</summary>
    public static string FormatCountdown(long milliseconds)
    {
        if (milliseconds < 0)
            milliseconds = 0;

        var totalSeconds = milliseconds / 1000;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;

        if (hours > 0)
            return $@"{hours} 小時 {minutes} 分";

        return minutes > 0 ? $@"{minutes} 分 {seconds} 秒" : $@"{seconds} 秒";
    }
}
