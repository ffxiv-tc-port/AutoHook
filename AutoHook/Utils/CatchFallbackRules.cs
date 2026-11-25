using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Lumina.Text.ReadOnly;

namespace AutoHook.Utils;

/// <summary>
/// 釣獲通知備援的純判斷（不碰 Service、不碰遊戲記憶體），給 <c>FishingManager.CatchFallback</c> 用，也給離線探針直接呼叫。
/// </summary>
public static class CatchFallbackRules
{
    /// <summary>拋竿時間不明時，聊天訊息與 detour 視為同一竿的時間窗（竿與竿至少隔數秒）。</summary>
    public const long MatchWindowMs = 1500;

    /// <summary>等收竿的上限；超過＝已經再拋竿，這一竿不補記。</summary>
    public const long MaxAgeMs = 10000;

    /// <summary>LogMessage 3512＝釣起一條、3559＝一竿多條（「×N」）。換餌等其他帶魚名的訊息不算。</summary>
    public static readonly uint[] CatchLogMessageIds = [3512, 3559];

    private static readonly Regex AmountSuffix = new(@"×\s*(\d+)\s*[。.]?\s*$", RegexOptions.Compiled);

    /// <summary>
    /// 這一竿的 detour 有沒有進來（有＝通知正常，不補記）。一竿只會釣起一次：有這一竿的拋竿時間就以它為界，
    /// 先後順序都不影響；拋竿時間不明（晚於訊息或沒記到）才退回訊息前後的時間窗。
    /// </summary>
    public static bool CatchNotificationArrived(long chatTick, long lastDetourTick, long castTick)
        => lastDetourTick >= (castTick < chatTick ? castTick : chatTick - MatchWindowMs);

    /// <summary>重新掛 hook 的條件：從沒掛過，或上一次重掛之後 detour 確實進來過（＝那次有效，之後又被擠下去）。</summary>
    public static bool ShouldRehook(long lastRehookTick, long lastDetourTick, bool everRehooked)
        => !everRehooked || lastDetourTick > lastRehookTick;

    /// <summary>訊息結尾的「×N」＝一竿起獲 N 條；沒有就是 1。</summary>
    public static uint ParseCatchAmount(string text)
    {
        var m = AmountSuffix.Match(text);
        return m.Success && uint.TryParse(m.Groups[1].Value, out var n) && n is > 0 and < 100 ? n : 1;
    }

    /// <summary>範本在巨集處切開的字面片段（含空字串）。</summary>
    public static string[] Literals(ReadOnlySeString text)
    {
        var literals = new List<string>();
        var current = new StringBuilder();
        foreach (var payload in text)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                current.Append(Encoding.UTF8.GetString(payload.Body.Span));
                continue;
            }

            literals.Add(current.ToString());
            current.Clear();
        }

        literals.Add(current.ToString());
        return literals.ToArray();
    }

    /// <summary>訊息依序含有某一則範本的全部非空字面片段。範本全空＝判不出來，不算。</summary>
    public static bool MatchesCatchTemplate(string text, IEnumerable<string[]> templates)
    {
        foreach (var literals in templates)
        {
            var parts = literals.Where(l => l.Length > 0).ToArray();
            if (parts.Length == 0)
                continue;

            var at = 0;
            var ok = true;
            foreach (var part in parts)
            {
                var idx = text.IndexOf(part, at, StringComparison.Ordinal);
                if (idx < 0)
                {
                    ok = false;
                    break;
                }

                at = idx + part.Length;
            }

            if (ok)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 訊息沒有道具連結時用魚名比對：取最長的命中名稱；同名多個 id（宇宙探索常見）只在剛好一個被目前 preset 引用時採用。
    /// 判不出來回 0（寧可不補記，也不記錯魚）。
    /// </summary>
    public static int PickFishIdByName(string text, IEnumerable<(int Id, string Name)> fishes, Func<int, bool> referencedByPreset)
    {
        var hits = fishes.Where(f => f.Id > 0 && f.Name.Length > 0 && text.Contains(f.Name, StringComparison.Ordinal)).ToList();
        if (hits.Count == 0)
            return 0;

        var longest = hits.Max(f => f.Name.Length);
        var top = hits.Where(f => f.Name.Length == longest).Select(f => f.Id).Distinct().ToList();
        if (top.Count == 1)
            return top[0];

        var referenced = top.Where(referencedByPreset).ToList();
        return referenced.Count == 1 ? referenced[0] : 0;
    }
}
