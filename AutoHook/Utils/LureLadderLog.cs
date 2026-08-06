using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AutoHook.Data;
using AutoHook.Enums;

namespace AutoHook.Utils;

/// <summary>
/// 引誘階梯訊息（雄心之餌 5566-5568 ／ 謙遜之餌 5570-5572）的**純蒐證**記錄器：
/// 只寫 log，**不參與任何決策**、**不改變任何行為**。
///
/// 為什麼存在：<see cref="Classes.AutoCasts.AutoLures"/> 判斷「現在幾層」是靠
/// <see cref="PlayerRes.GetStatusStacks"/> 讀狀態列推出來的，而狀態列有延遲；伺服器其實
/// 每一次引誘成功都會直接下發一則階梯訊息，那個才是**精確的層數轉換時點**。
///
/// 這個檔要回答的問題只有一個：<c>AutoLures</c> 那個 <b>2500ms</b> 節流跟第三方文件寫的
/// 「固定 3.5 秒咬餌延後期」到底哪個對。
/// 🔴 **在拿到實機分布之前不要動那個數字** —— 現在改是用猜的換掉一個至少已知能動的值，
///    而且改壞了完全沒有徵兆（只是效率變差，不會報錯）。所以這一版只量不改。
///
/// 量兩件事：
///   1. **狀態列落後多久** —— 收到階梯訊息的當下讀一次狀態列，然後每幀輪詢到它追上為止。
///      這個差值就是「靠狀態列推層數」相對於「看伺服器訊息」的代價。
///   2. **兩則階梯訊息的間隔** —— 也就是實際上兩次引誘生效之間隔了多久，
///      直接拿來跟 2500ms 對照。
///
/// 輸出兩種行（形式沿用 <see cref="CosmicCatchLog"/>，理由一樣：長時間掛機不能把 log 洗掉）：
/// 明細行有上限 <see cref="MaxDetailLines"/>，之後改看每 <see cref="SummaryIntervalMs"/> 毫秒的彙總行。
/// </summary>
public static class LureLadderLog
{
    /// <summary>彙總行的間隔。太短會洗版，太長則短時間釣魚結束前印不出來。</summary>
    private const int SummaryIntervalMs = 60_000;

    /// <summary>明細行總量上限。超過之後仍然**照常統計**，只是不再印明細行。</summary>
    private const int MaxDetailLines = 40;

    /// <summary>
    /// 等狀態列追上的放棄門檻。超過就記成「沒追上」——
    /// 「不知道」要看得見，不能靜靜當成「落後 0ms」混進平均值裡。
    /// </summary>
    private const long CatchUpTimeoutMs = 8_000;

    private sealed class LadderStats
    {
        public int Count;

        // 狀態列落後（ms）
        public int LagSamples;
        public long LagSum;
        public long LagMin = long.MaxValue;
        public long LagMax;
        public int LagTimeouts;

        // 距上一則階梯訊息（ms）。每竿的第一則沒有「上一則」，不計入樣本。
        public int GapSamples;
        public long GapSum;
        public long GapMin = long.MaxValue;
        public long GapMax;

        public bool Dirty;
    }

    /// <summary>
    /// 兩個進入點（聊天訊息 handler 與 Framework.Update）都在遊戲主執行緒上，理論上不會併發；
    /// 這個 lock 純粹是保險，代價遠低於 <see cref="Dictionary{TKey,TValue}"/> 併發寫壞掉。
    /// </summary>
    private static readonly object Gate = new();

    private static readonly Dictionary<(string Lure, int Stacks), LadderStats> Buckets = new();

    private static int _detailLines;
    private static long _lastSummaryTick = Environment.TickCount64;

    /// <summary>這一竿的拋竿時刻。0＝還沒拋竿。</summary>
    private static long _castStartTick;

    /// <summary>這一竿上一則階梯訊息的時刻。0＝這一竿還沒有過階梯訊息。</summary>
    private static long _lastLadderTick;

    // ── 等狀態列追上伺服器層數的暫存（同一時間只會有一筆）──────────────────
    private static bool _pending;
    private static uint _pendingStatusId;
    private static string _pendingLure = "";
    private static int _pendingStacks;
    private static long _pendingTick;

    /// <summary>
    /// 有沒有正在等狀態列追上。呼叫端拿它當每幀輪詢的閘門 ——
    /// 沒有 pending 的時候 <see cref="Poll"/> 一次狀態列都不會讀。
    /// </summary>
    public static bool HasPending => _pending;

    /// <summary>拋竿時呼叫：重設「距拋竿」與「距上一則」的基準。</summary>
    public static void OnCastStarted()
    {
        try
        {
            lock (Gate)
            {
                FlushPendingLocked(@"換竿前狀態列還沒追上");
                _castStartTick = Environment.TickCount64;
                _lastLadderTick = 0;
            }
        }
        catch (Exception e)
        {
            Service.PluginLog.Error($"[引誘階梯] 重設失敗：{e.Message}");
        }
    }

    /// <summary>離開釣魚時呼叫：把還沒收尾的暫存倒出來，別留到下一次釣魚才記錯時間。</summary>
    public static void OnFishingStopped()
    {
        try
        {
            lock (Gate)
            {
                FlushPendingLocked(@"離開釣魚前狀態列還沒追上");
                _castStartTick = 0;
                _lastLadderTick = 0;
            }
        }
        catch (Exception e)
        {
            Service.PluginLog.Error($"[引誘階梯] 收尾失敗：{e.Message}");
        }
    }

    /// <summary>
    /// 收到一則釣魚訊息時呼叫。不是階梯訊息就什麼都不做。
    /// </summary>
    /// <param name="logId">該訊息對應的 <c>LogMessage</c> row id（比對不到時為 null）。</param>
    /// <returns>這則訊息是不是階梯訊息。純資訊用，呼叫端目前不依賴它做決策。</returns>
    public static bool Record(uint? logId)
    {
        try
        {
            if (logId is not { } id || !TryResolve(id, out var lure, out var statusId, out var stacks))
                return false;

            var now = Environment.TickCount64;

            lock (Gate)
            {
                FlushPendingLocked(@"被下一則階梯訊息取代，狀態列沒追上");

                var bucket = GetBucketLocked(lure, stacks);
                bucket.Count++;
                bucket.Dirty = true;

                // 每竿第一則沒有「上一則」——不計入樣本，log 上印 ? 而不是 0。
                long? gap = _lastLadderTick == 0 ? null : now - _lastLadderTick;
                if (gap is { } g)
                {
                    bucket.GapSamples++;
                    bucket.GapSum += g;
                    if (g < bucket.GapMin) bucket.GapMin = g;
                    if (g > bucket.GapMax) bucket.GapMax = g;
                }

                _lastLadderTick = now;

                // 收到訊息的當下狀態列說幾層。
                // ⚠️ GetStatusStacks 找不到狀態時回 0 —— 層數的有效值是 1~3，
                //    所以 0 一律當成「狀態列上還沒有這個狀態」，不要當成「0 層」。
                var listed = PlayerRes.GetStatusStacks(statusId);

                if (listed >= stacks)
                {
                    // 狀態列已經同步，沒有落後可量。
                    bucket.LagSamples++;
                    if (0 < bucket.LagMin) bucket.LagMin = 0;
                }
                else
                {
                    _pending = true;
                    _pendingStatusId = statusId;
                    _pendingLure = lure;
                    _pendingStacks = stacks;
                    _pendingTick = now;
                }

                if (_detailLines < MaxDetailLines)
                {
                    _detailLines++;
                    var sinceCast = _castStartTick == 0 ? @"?" : $@"{now - _castStartTick}ms";
                    var sinceLast = gap is { } gv ? $@"{gv}ms" : @"?（本竿第一則）";
                    var listedText = listed == 0 ? @"狀態列上還沒有這個狀態" : $@"{listed}層";

                    Service.PrintInfo(
                        $@"[引誘階梯] {lure} 第{stacks}層（伺服器訊息#{id}）" +
                        $@"｜距拋竿 {sinceCast}｜距上一則階梯 {sinceLast}" +
                        $@"｜此刻狀態列＝{listedText}{(listed >= stacks ? @"（已同步）" : @"（落後中）")}");
                }

                if (Environment.TickCount64 - _lastSummaryTick >= SummaryIntervalMs)
                    FlushSummaryLocked();
            }

            return true;
        }
        catch (Exception e)
        {
            // 蒐證失敗絕對不能影響釣魚本身。
            Service.PluginLog.Error($"[引誘階梯] 記錄失敗：{e.Message}");
            return false;
        }
    }

    /// <summary>
    /// 每幀輪詢狀態列有沒有追上。呼叫端必須先看 <see cref="HasPending"/>，
    /// 沒有待處理的時候連狀態列都不要去讀。
    /// </summary>
    public static void Poll()
    {
        try
        {
            if (!_pending)
                return;

            lock (Gate)
            {
                if (!_pending)
                    return;

                var elapsed = Environment.TickCount64 - _pendingTick;

                if (PlayerRes.GetStatusStacks(_pendingStatusId) >= _pendingStacks)
                {
                    var bucket = GetBucketLocked(_pendingLure, _pendingStacks);
                    bucket.LagSamples++;
                    bucket.LagSum += elapsed;
                    if (elapsed < bucket.LagMin) bucket.LagMin = elapsed;
                    if (elapsed > bucket.LagMax) bucket.LagMax = elapsed;
                    bucket.Dirty = true;

                    if (_detailLines < MaxDetailLines)
                    {
                        _detailLines++;
                        Service.PrintInfo(
                            $@"[引誘階梯] 狀態列追上：{_pendingLure} 第{_pendingStacks}層｜落後 {elapsed}ms");
                    }

                    _pending = false;
                    return;
                }

                if (elapsed >= CatchUpTimeoutMs)
                    FlushPendingLocked($@"{CatchUpTimeoutMs}ms 內狀態列沒追上");
            }
        }
        catch (Exception e)
        {
            Service.PluginLog.Error($"[引誘階梯] 輪詢失敗：{e.Message}");
            _pending = false;
        }
    }

    /// <summary>把「沒追上」的暫存收掉。呼叫端必須已經持有 <see cref="Gate"/>。</summary>
    private static void FlushPendingLocked(string reason)
    {
        if (!_pending)
            return;

        var elapsed = Environment.TickCount64 - _pendingTick;
        var bucket = GetBucketLocked(_pendingLure, _pendingStacks);
        bucket.LagTimeouts++;
        bucket.Dirty = true;

        if (_detailLines < MaxDetailLines)
        {
            _detailLines++;
            Service.PrintInfo(
                $@"[引誘階梯] 狀態列沒追上：{_pendingLure} 第{_pendingStacks}層｜等了 {elapsed}ms｜原因＝{reason}");
        }

        _pending = false;
    }

    private static LadderStats GetBucketLocked(string lure, int stacks)
    {
        var key = (lure, stacks);
        if (Buckets.TryGetValue(key, out var stats))
            return stats;

        stats = new LadderStats();
        Buckets[key] = stats;
        return stats;
    }

    /// <summary>每個「引誘種類 × 層數」印一行彙總。呼叫端必須已經持有 <see cref="Gate"/>。</summary>
    private static void FlushSummaryLocked()
    {
        _lastSummaryTick = Environment.TickCount64;

        foreach (var (key, stats) in Buckets)
        {
            if (!stats.Dirty)
                continue;

            stats.Dirty = false;

            var sb = new StringBuilder();
            sb.Append($@"[引誘階梯][彙總] {key.Lure} 第{key.Stacks}層 n={stats.Count}");
            sb.Append($@"｜狀態列落後 {Range(stats.LagSamples, stats.LagMin, stats.LagMax, stats.LagSum)}");
            sb.Append($@"（沒追上 {stats.LagTimeouts}）");
            sb.Append($@"｜距上一則階梯 {Range(stats.GapSamples, stats.GapMin, stats.GapMax, stats.GapSum)}");

            Service.PrintInfo(sb.ToString());
        }
    }

    /// <summary>樣本數 0 時印 <c>?</c>，不要印成 0 —— 那會被讀成「量到了，值是 0」。</summary>
    private static string Range(int samples, long min, long max, long sum)
    {
        if (samples == 0)
            return @"?（無樣本）";

        var avg = (sum / (double)samples).ToString("0", CultureInfo.InvariantCulture);
        return $@"{min}~{max}ms 均{avg}（樣本 {samples}）";
    }

    /// <summary>把 LogMessage row id 對應到「哪一種引誘、第幾層、對應的狀態 id」。</summary>
    private static bool TryResolve(uint logId, out string lure, out uint statusId, out int stacks)
    {
        switch (logId)
        {
            case XivChatLog.AmbLureStack1:
            case XivChatLog.AmbLureStack2:
            case XivChatLog.AmbLureStack3:
                lure = @"雄心之餌";
                statusId = IDs.Status.AmbitiousLure;
                stacks = (int)(logId - XivChatLog.AmbLureStack1) + 1;
                return true;

            case XivChatLog.ModLureStack1:
            case XivChatLog.ModLureStack2:
            case XivChatLog.ModLureStack3:
                lure = @"謙遜之餌";
                statusId = IDs.Status.ModestLure;
                stacks = (int)(logId - XivChatLog.ModLureStack1) + 1;
                return true;

            default:
                lure = "";
                statusId = 0;
                stacks = 0;
                return false;
        }
    }
}
