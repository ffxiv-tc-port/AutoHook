// ---------------------------------------------------------------------------------------------
// 出處：GatherBuddyReborn — GatherBuddy.GameData/Time/RepeatingInterval.cs
//       https://github.com/ffxiv-tc-port/GatherBuddyReborn （fork 自 GatherBuddy / GatherBuddyReborn）
// 授權：Apache License 2.0 —— 見該 repo 根目錄的 LICENSE。
//       Apache 2.0 與 AutoHook 的 BSD 3-Clause 相容（寬鬆對寬鬆，無 copyleft）。
//
// 變更聲明（Apache License 2.0 §4(b) 要求）：
//   - 命名空間由 GatherBuddy.Time 改為 AutoHook.Time。
//   - 移除未使用的 using System.Diagnostics。
//   - 其餘內容未修改（PrintHours 保留：它輸出的是 "HH:MM - HH:MM ET"，只有數字沒有英文詞彙）。
//
// 📌 這個結構就是 fish_list.json 裡 Interval 那三個欄位的語意：
//    OnTime/OffTime/ShiftTime 都是**真實時間的毫秒**，週期固定是一個艾奧傑亞日（4200000 ms）。
//    RepeatingInterval.FromEorzeanMinutes(起始 ET 分, 結束 ET 分) 就是它們的產生式。
// ---------------------------------------------------------------------------------------------

using System;

namespace AutoHook.Time;

public readonly struct RepeatingInterval : IEquatable<RepeatingInterval>
{
    public int OnTime    { get; init; }
    public int OffTime   { get; init; }
    public int ShiftTime { get; init; }

    public long Period
        => OnTime + OffTime;

    public bool AlwaysUp()
        => OffTime == 0;

    public bool NeverUp()
        => OnTime == 0;

    public bool IsUp(TimeStamp time)
    {
        if (this == Invalid || NeverUp())
            return false;
        if (AlwaysUp())
            return true;

        var shift  = SyncToShift(time);
        var period = shift % Period;
        return period < OnTime;
    }

    private TimeStamp SyncToShift(TimeStamp ts)
        => new((ts - ShiftTime) / Period * Period + ShiftTime);

    public TimeInterval FirstOverlap(TimeInterval interval)
    {
        if (interval == TimeInterval.Invalid)
            return TimeInterval.Invalid;

        if (OnTime == 0)
            return OffTime == 0 ? TimeInterval.Invalid : TimeInterval.Never;
        if (OffTime == 0)
            return interval;

        if (interval == TimeInterval.Always)
            return new TimeInterval
            {
                Start = TimeStamp.Epoch + ShiftTime,
                End   = TimeStamp.Epoch + ShiftTime + OnTime,
            };

        var start = SyncToShift(interval.Start);
        var end   = start + OnTime;
        if (end < interval.Start)
        {
            start += Period;
            end   += Period;
        }

        var newStart = interval.Start.Max(start);
        var newEnd   = interval.End.Min(end);
        return newEnd <= newStart
            ? TimeInterval.Never
            : new TimeInterval(newStart, newEnd);
    }

    public TimeInterval NextRealUptime(TimeStamp now)
    {
        if (AlwaysUp())
            return TimeInterval.Always;
        if (OnTime == 0)
            return TimeInterval.Never;

        var syncedNow = SyncToShift(now);
        var end       = syncedNow + OnTime;
        return end > now
            ? new TimeInterval(syncedNow,          end)
            : new TimeInterval(syncedNow + Period, end + Period);
    }

    public static readonly RepeatingInterval Always = new()
    {
        OnTime    = 1,
        OffTime   = 0,
        ShiftTime = 0,
    };

    public static readonly RepeatingInterval Never = new()
    {
        OnTime    = 0,
        OffTime   = 1,
        ShiftTime = 0,
    };

    public static readonly RepeatingInterval Invalid = new()
    {
        OnTime    = 0,
        OffTime   = 0,
        ShiftTime = 0,
    };

    public static bool operator ==(RepeatingInterval left, RepeatingInterval right)
        => left.Equals(right);

    public static bool operator !=(RepeatingInterval left, RepeatingInterval right)
        => !(left == right);

    public bool Equals(RepeatingInterval other)
        => OnTime == other.OnTime
         && OffTime == other.OffTime
         && ShiftTime == other.ShiftTime;

    public override bool Equals(object? obj)
        => obj is RepeatingInterval other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(OnTime, OffTime, ShiftTime);

    public static RepeatingInterval FromEorzeanMinutes(int startMinute, int endMinute)
    {
        if (startMinute == endMinute)
            return Never;

        startMinute %= RealTime.MinutesPerDay;
        endMinute   %= RealTime.MinutesPerDay;
        if (startMinute == endMinute)
            return Always;

        var duration = TimeStamp.Epoch.AddEorzeaMinutes(endMinute < startMinute
            ? endMinute + RealTime.MinutesPerDay - startMinute
            : endMinute - startMinute);
        var offset = TimeStamp.Epoch.AddEorzeaMinutes(startMinute);
        return new RepeatingInterval()
        {
            ShiftTime = (int)(startMinute < endMinute ? offset : offset.AddEorzeaDays(1)),
            OnTime    = (int)duration,
            OffTime   = (int)(TimeStamp.Epoch.AddEorzeaDays(1) - duration),
        };
    }

    // Print eorzean hours in human readable format.
    public string PrintHours(bool simple = false)
    {
        var start = new TimeStamp(ShiftTime).CurrentEorzeaMinuteOfDay();
        if (start < 0)
            start += RealTime.MinutesPerDay;

        var end = (int)(start + new TimeStamp(OnTime).TotalEorzeaMinutes());
        if (end > RealTime.MinutesPerDay)
            end -= RealTime.MinutesPerDay;

        var hStart = start / RealTime.MinutesPerHour;
        var hEnd   = end / RealTime.MinutesPerHour;
        var mStart = start - hStart * RealTime.MinutesPerHour;
        var mEnd   = end - hEnd * RealTime.MinutesPerHour;
        var sStart = $"{hStart:D2}:{mStart:D2}";
        var sEnd   = $"{hEnd:D2}:{mEnd:D2}";

        return simple ? $"{sStart}-{sEnd}" : $"{sStart} - {sEnd} ET";
    }

    public bool Contains(RepeatingInterval other)
        => ShiftTime <= other.ShiftTime && OnTime >= other.OnTime;
}
