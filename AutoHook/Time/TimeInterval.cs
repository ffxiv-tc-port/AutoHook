// ---------------------------------------------------------------------------------------------
// 出處：GatherBuddyReborn — GatherBuddy.GameData/Time/TimeInterval.cs
//       https://github.com/ffxiv-tc-port/GatherBuddyReborn （fork 自 GatherBuddy / GatherBuddyReborn）
// 授權：Apache License 2.0 —— 見該 repo 根目錄的 LICENSE。
//       Apache 2.0 與 AutoHook 的 BSD 3-Clause 相容（寬鬆對寬鬆，無 copyleft）。
//
// 變更聲明（Apache License 2.0 §4(b) 要求）：
//   - 命名空間由 GatherBuddy.Time 改為 AutoHook.Time。
//   - 移除 ToTimeString(...)：它會輸出英文的 "Always"/"Never"/"Unknown"，
//     AutoHook 這邊的顯示字串自己排版（見 Ui/TabWeather）。DurationString 保留，
//     因為它只輸出數字與單位，且 Overlap/Merge 等核心運算不依賴被移除的那個方法。
//   - 其餘內容未修改。
//
// ⚠️ Always / Never / Invalid 是三個不同的東西，不要混用：
//    Always  = 永遠可釣（我們的 fish_list.json 用 (OnTime,OffTime,ShiftTime)=(1,0,0) 表示）
//    Never   = 這輩子不會開
//    Invalid = 不知道 —— (0,0,0) 會落在這裡，所以資料如果被弄丟成全 0，
//              表現出來是「永遠查不到窗口」而不是報錯。
// ---------------------------------------------------------------------------------------------

using System;
using System.Globalization;

namespace AutoHook.Time;

public readonly struct TimeInterval : IEquatable<TimeInterval>
{
    public TimeStamp Start { get; init; }
    public TimeStamp End   { get; init; }

    public TimeInterval(TimeStamp start, TimeStamp end)
    {
        Start = start;
        End   = end;
    }

    public long Duration
        => this == Always ? long.MaxValue : this == Invalid ? 0 : End - Start;

    public long SecondDuration
        => this == Always ? long.MaxValue : this == Invalid ? 0 : (End - Start) / RealTime.MillisecondsPerSecond;

    public string DurationString(bool shortString = false)
        => DurationString(Start, End, shortString);

    public TimeInterval Overlap(TimeInterval rhs)
    {
        if (rhs == Invalid || this == Invalid)
            return Invalid;

        var newStart = Start.Max(rhs.Start);
        var newEnd   = End.Min(rhs.End);
        return newEnd <= newStart ? Never : new TimeInterval(newStart, newEnd);
    }

    public TimeInterval FirstOverlap(RepeatingInterval rhs)
        => rhs.FirstOverlap(this);

    public TimeInterval Merge(TimeInterval rhs)
    {
        if (rhs.Start > End || Start > rhs.End || this == Invalid || rhs == Invalid)
            return Invalid;

        var newStart = Start.Min(rhs.Start);
        var newEnd   = End.Max(rhs.End);
        return new TimeInterval(newStart, newEnd);
    }

    public TimeInterval Extend(long duration)
    {
        if (duration == 0)
            return this;
        if (this == Always)
            return Always;
        if (this == Invalid)
            return Invalid;
        if (this == Never)
            return Never;

        return duration > 0
            ? new TimeInterval(Start,            End + duration)
            : new TimeInterval(Start + duration, End);
    }

    public bool this[TimeStamp timeStamp]
        => InRange(timeStamp);

    public bool InRange(TimeStamp timeStamp)
        => timeStamp >= Start && timeStamp < End;

    public static readonly TimeInterval Always = new(TimeStamp.MinValue, TimeStamp.MaxValue);

    public static readonly TimeInterval Never = new(TimeStamp.Epoch, TimeStamp.Epoch);

    public static readonly TimeInterval Invalid = new(TimeStamp.MaxValue, TimeStamp.MinValue);

    public bool Equals(TimeInterval other)
        => Start == other.Start
         && End == other.End;

    public override bool Equals(object? obj)
        => obj is TimeInterval other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Start, End);

    public static bool operator ==(TimeInterval left, TimeInterval right)
        => left.Equals(right);

    public static bool operator !=(TimeInterval left, TimeInterval right)
        => !(left == right);

    public int Compare(TimeInterval rhs)
    {
        if (this == Invalid)
            return Never.Compare(rhs);

        if (rhs == Invalid)
            rhs = Never;

        var diff = End - rhs.End;
        if (Math.Abs(diff) > 0)
            return diff.CompareTo(0);

        var diff2 = Start - rhs.Start;
        return diff2.CompareTo(0);
    }

    public static string DurationString(TimeStamp a, TimeStamp b, bool shortString)
    {
        (a, b) = a < b ? (a, b) : (b, a);
        var tmp = new TimeStamp(b - a).RoundToSecond();
        return tmp.Time switch
        {
            > RealTime.MillisecondsPerDay => shortString
                ? $">{tmp.TotalDays}d"
                : $"{((float)tmp.Time / RealTime.MillisecondsPerDay).ToString("F2", CultureInfo.InvariantCulture)} Days",
            > RealTime.MillisecondsPerHour => shortString
                ? $">{tmp.TotalHours}h"
                : $"{tmp.TotalHours:D2}:{tmp.CurrentMinute:D2} Hours",
            _ => shortString
                ? $"{tmp.TotalMinutes}:{tmp.CurrentSecond:D2}m"
                : $"{tmp.TotalMinutes:D2}:{tmp.CurrentSecond:D2} Minutes",
        };
    }
}
