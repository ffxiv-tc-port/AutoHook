using System.Linq;

namespace AutoHook.Utils;

/// <summary>宇宙探索任務「剩餘時間夠不夠一竿」的純判斷：只吃面板文字，不碰遊戲狀態。</summary>
public static class CosmicCastTimeRules
{
    /// <summary>
    /// 剩餘少於這個秒數就不拋竿。取「這一竿一定來不及」的下限，不是平均一竿的時間：
    /// 實測宇宙釣魚兩次起獲最短相隔約 13 秒（含收竿後到再拋竿），拋竿到起獲不會少於 10 秒。
    /// </summary>
    public const int MinSecondsForOneCast = 10;

    /// <summary>
    /// 解析任務面板計時列（圖示＋「分:秒」）。與 ICE 的 <c>MedalRules.TryParseRemaining</c> 同規則：
    /// 只留數字與冒號、必須剛好兩段，否則回 false（呼叫端當成不知道）。
    /// </summary>
    public static bool TryParseRemainingSeconds(string? timerText, out int seconds)
    {
        seconds = 0;
        if (timerText is null)
            return false;

        var cleaned = new string(timerText.Trim().Where(c => char.IsDigit(c) || c == ':').ToArray());
        var parts = cleaned.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var minutes) || !int.TryParse(parts[1], out var secs))
            return false;

        seconds = minutes * 60 + secs;
        return true;
    }

    /// <summary>讀不到（null）一律回 false：不知道剩餘時間就照常拋竿，不可因此卡住。</summary>
    public static bool ShouldHoldCast(int? remainingSeconds)
        => remainingSeconds is { } s && s < MinSecondsForOneCast;
}
