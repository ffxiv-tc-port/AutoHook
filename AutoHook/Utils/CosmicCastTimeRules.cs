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
    /// 解析任務面板計時列：「圖示＋剩餘」或「圖示＋剩餘/時限」（任務進行中多是後者，例「 29:59/30:00」），
    /// 只取斜線左邊的「分:秒」。全形數字／冒號／斜線先當半形；圖示、空白與其他不可見字元略過。
    /// 左邊不是剛好一個冒號、或秒數不是兩位數且小於 60，就回 false（呼叫端當成不知道）。
    /// </summary>
    public static bool TryParseRemainingSeconds(string? timerText, out int seconds)
    {
        seconds = 0;
        if (timerText is null)
            return false;

        int minutes = 0, secs = 0, minuteDigits = 0, secondDigits = 0, colons = 0;
        foreach (var raw in timerText)
        {
            var c = ToHalfWidth(raw);
            if (c == '/')
                break;

            if (c == ':')
            {
                if (++colons > 1)
                    return false;
                continue;
            }

            if (c is < '0' or > '9')
                continue;

            if (colons == 0)
            {
                if (++minuteDigits > 3)
                    return false;
                minutes = minutes * 10 + (c - '0');
            }
            else
            {
                if (++secondDigits > 2)
                    return false;
                secs = secs * 10 + (c - '0');
            }
        }

        if (colons != 1 || minuteDigits == 0 || secondDigits != 2 || secs >= 60)
            return false;

        seconds = minutes * 60 + secs;
        return true;
    }

    private static char ToHalfWidth(char c) => c switch
    {
        >= '０' and <= '９' => (char)('0' + (c - '０')),
        '：' or '∶' => ':',
        '／' or '∕' => '/',
        _ => c,
    };

    /// <summary>讀不到（null）一律回 false：不知道剩餘時間就照常拋竿，不可因此卡住。</summary>
    public static bool ShouldHoldCast(int? remainingSeconds)
        => remainingSeconds is { } s && s < MinSecondsForOneCast;
}
