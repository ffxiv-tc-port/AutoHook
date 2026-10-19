// ---------------------------------------------------------------------------------------------
// 出處：GatherBuddyReborn — GatherBuddy.GameData/Time/RealTime.cs
//       https://github.com/ffxiv-tc-port/GatherBuddyReborn （fork 自 GatherBuddy / GatherBuddyReborn）
// 授權：Apache License 2.0 —— 見該 repo 根目錄的 LICENSE。
//       Apache 2.0 與 AutoHook 的 BSD 3-Clause 相容（寬鬆對寬鬆，無 copyleft）。
//
// 變更聲明（Apache License 2.0 §4(b) 要求）：
//   - 命名空間由 GatherBuddy.Time 改為 AutoHook.Time。
//   - 其餘內容未修改。
// ---------------------------------------------------------------------------------------------

namespace AutoHook.Time;

public static class RealTime
{
    public const int MillisecondsPerSecond = 1000;
    public const int SecondsPerMinute      = 60;
    public const int MinutesPerHour        = 60;
    public const int HoursPerDay           = 24;

    public const int MillisecondsPerMinute = MillisecondsPerSecond * SecondsPerMinute;
    public const int MillisecondsPerHour   = MillisecondsPerMinute * MinutesPerHour;
    public const int MillisecondsPerDay    = MillisecondsPerHour * HoursPerDay;

    public const int SecondsPerHour = SecondsPerMinute * MinutesPerHour;
    public const int SecondsPerDay  = SecondsPerHour * HoursPerDay;
    public const int MinutesPerDay  = MinutesPerHour * HoursPerDay;
}
