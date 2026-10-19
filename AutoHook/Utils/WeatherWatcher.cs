using System;
using AutoHook.Time;
using Dalamud.Plugin.Services;

namespace AutoHook.Utils;

/// <summary>
/// 每隔一小段時間把「目前地區、目前時段的天氣」記進 <see cref="WeatherTimeline"/>。
///
/// <para>
/// 為什麼需要它：「前一個時段下的是什麼天氣」<b>問不到遊戲</b>
/// （<c>GetWeatherForHour</c> 餵負數會靜默算錯，API13 也沒有 <c>GetPreviousWeather()</c>）。
/// 唯一的辦法是趁它還是「目前時段」的時候把它抄下來。
/// </para>
///
/// <para>
/// 🔴 <b>這不是 detour，是 Framework.Update</b> —— CS 的函式未解析時是擲例外，
/// 從原生 hook 的堆疊往外擲會殺掉行程；掛在 Framework 上就只是一個普通的 .NET 例外。
/// </para>
///
/// <para>
/// 效能：每幀先比一次 <see cref="Environment.TickCount64"/> 就返回，
/// 真正的工作最多每 <see cref="SampleIntervalMs"/> 毫秒做一次。
/// 天氣時段長 23 分 20 秒，5 秒取樣一次已經遠比需要的密。
/// </para>
/// </summary>
public sealed class WeatherWatcher : IDisposable
{
    private const int SampleIntervalMs = 5_000;

    private long _nextSampleTick;
    private bool _disposed;

    public WeatherWatcher()
    {
        Service.Framework.Update += OnUpdate;
    }

    private void OnUpdate(IFramework framework)
    {
        var tick = Environment.TickCount64;
        if (tick < _nextSampleTick)
            return;

        _nextSampleTick = tick + SampleIntervalMs;

        try
        {
            if (!Service.ClientState.IsLoggedIn)
                return;

            var territory = Service.ClientState.TerritoryType;
            if (territory == 0)
                return;

            // 用預報的第 0 格而不是 GetCurrentWeather()：
            // 前置天氣條件比對的是「預報序列上的前一格」，兩邊要用同一個來源才對得起來。
            // GetCurrentWeather() 會把特殊天氣覆寫算進去，拿它當歷史紀錄會造成序列不連續。
            var weather = WeatherForecast.GetWeatherIdForPeriod(territory, 0);
            if (weather == null)
                return;

            WeatherTimeline.Record(territory, TimeStamp.UtcNow, weather.Value);
        }
        catch (Exception e)
        {
            // 記錄天氣失敗只會讓「前置天氣」顯示成不知道，絕不該讓釣魚本身受影響。
            Service.PluginLog.Error($@"WeatherWatcher: {e.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Service.Framework.Update -= OnUpdate;
    }
}
