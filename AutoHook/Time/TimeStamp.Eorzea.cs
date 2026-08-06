// ---------------------------------------------------------------------------------------------
// 出處：GatherBuddyReborn — GatherBuddy.GameData/Time/TimeStamp.Eorzea.cs
//       https://github.com/ffxiv-tc-port/GatherBuddyReborn （fork 自 GatherBuddy / GatherBuddyReborn）
// 授權：Apache License 2.0 —— 見該 repo 根目錄的 LICENSE。
//       Apache 2.0 與 AutoHook 的 BSD 3-Clause 相容（寬鬆對寬鬆，無 copyleft）。
//
// 變更聲明（Apache License 2.0 §4(b) 要求）：
//   - 命名空間由 GatherBuddy.Time 改為 AutoHook.Time。
//   - 其餘內容未修改。
//
// 📌 這裡的換算常數就是艾奧傑亞時間的定義：1 個 ET 小時 = 175 真實秒，
//    天氣每 8 個 ET 小時換一次（= 1400 真實秒 = 23 分 20 秒）。
// ---------------------------------------------------------------------------------------------

namespace AutoHook.Time;

public static class EorzeaTimeStampExtensions
{
    public const int MillisecondsPerEorzeaHour    = 175000;
    public const int SecondsPerEorzeaHour         = MillisecondsPerEorzeaHour / RealTime.MillisecondsPerSecond;
    public const int MillisecondsPerEorzeaWeather = 8 * MillisecondsPerEorzeaHour;
    public const int SecondsPerEorzeaWeather      = MillisecondsPerEorzeaWeather / RealTime.MillisecondsPerSecond;
    public const int MillisecondsPerEorzeaDay     = RealTime.HoursPerDay * MillisecondsPerEorzeaHour;
    public const int SecondsPerEorzeaDay          = MillisecondsPerEorzeaDay / RealTime.MillisecondsPerSecond;

    public static TimeStamp ConvertToEorzea(this TimeStamp t)
        => new(t.Time * 144 / 7);

    public static TimeStamp AddEorzeaSeconds(this TimeStamp t, long value)
        => new(t.Time + value * 875 / 18);

    public static TimeStamp AddEorzeaMinutes(this TimeStamp t, long value)
        => new(t.Time + value * 8750 / 3);

    public static TimeStamp AddEorzeaHours(this TimeStamp t, long value)
        => new(t.Time + value * 175000);

    public static TimeStamp AddEorzeaDays(this TimeStamp t, long value)
        => new(t.Time + value * 4200000);

    public static long TotalEorzeaMinutes(this TimeStamp t)
        => t.Time * 144 / 7 / RealTime.MillisecondsPerMinute;

    public static long TotalEorzeaHours(this TimeStamp t)
        => t.Time * 144 / 7 / RealTime.MillisecondsPerHour;

    public static long TotalEorzeaDays(this TimeStamp t)
        => t.Time * 144 / 7 / RealTime.MillisecondsPerDay;

    public static int CurrentEorzeaMinute(this TimeStamp t)
        => (int)(t.TotalEorzeaMinutes() % RealTime.MinutesPerHour);

    public static int CurrentEorzeaMinuteOfDay(this TimeStamp t)
        => (int)(t.TotalEorzeaMinutes() % RealTime.MinutesPerDay);

    public static int CurrentEorzeaHour(this TimeStamp t)
        => (int)(t.TotalEorzeaHours() % RealTime.HoursPerDay);

    public static TimeStamp SyncToEorzeaHour(this TimeStamp now)
        => new(now.Time - now.Time % MillisecondsPerEorzeaHour);

    public static TimeStamp SyncToEorzeaDay(this TimeStamp now)
        => new(now.Time - now.Time % MillisecondsPerEorzeaDay);

    public static TimeStamp SyncToEorzeaWeather(this TimeStamp now)
        => new(now.Time - now.Time % MillisecondsPerEorzeaWeather);

    public static (int Hours, int Minutes) CurrentEorzeaTimeOfDay(this TimeStamp now, int eorzeaMinuteOffset = 0)
    {
        var minutes = now.TotalEorzeaMinutes() + eorzeaMinuteOffset;
        return ((int)(minutes / RealTime.MinutesPerHour) % RealTime.HoursPerDay, (int)minutes % RealTime.MinutesPerHour);
    }

    public static (long minutes, int seconds) MinutesToReal(long eorzeaMinutes)
    {
        var seconds = (double)eorzeaMinutes * SecondsPerEorzeaHour / RealTime.MinutesPerHour;
        var m       = (long)seconds;
        var s       = (int)((seconds - m) * RealTime.SecondsPerMinute);
        return (m, s);
    }
}
