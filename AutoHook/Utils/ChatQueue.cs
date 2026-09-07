using System;
using System.Collections.Concurrent;
using System.Threading;
using Dalamud.Plugin.Services;

namespace AutoHook.Utils;

/// <summary>
/// 送到<b>遊戲聊天視窗</b>的訊息佇列。所有對 <c>IChatGui.Print</c>／<c>PrintError</c> 的呼叫
/// 一律先排進這裡，再由 <c>Framework.Update</c> 在 Framework 執行緒上一次排乾。
/// </summary>
/// <remarks>
/// 🔴🔴 <b>為什麼需要它</b>：使用者實機跑的那顆 Dalamud（<c>DalamudLive</c>）裡，
/// <c>ChatGui</c> 的待印佇列是<b>裸的 <c>Queue&lt;T&gt;</c></b>（零同步）。
/// 它自己在 Framework 執行緒上 <c>TryDequeue</c>，而外掛從別的執行緒 <c>Enqueue</c> ——
/// 失敗形式<b>不是「少印一行」而是佇列本身壞掉</b>（擲 <c>InvalidOperationException</c>
/// 或讀到撕裂的內容），而且弄壞的是 <b>Dalamud 全域</b>的那一個，會連累所有外掛的聊天輸出。
/// 跟 <c>ECommons.Throttlers.EzThrottler</c>／<c>Service.LogMessages</c> 那條紅線完全同形狀。
/// <br/>📌 Dalamud 本體已經改成 <c>ConcurrentQueue</c>，但<b>那顆還沒部署到使用者機器上</b>，
/// 而且外掛側自己 marshal 本來就是正解（不能假設使用者跑的是哪一版 Dalamud）。
/// <para>
/// 🔑 <b>為什麼是「一個佇列＋每幀排乾」而不是逐則 <c>RunOnFrameworkThread</c></b>：
/// Dalamud 的 <c>ThreadBoundTaskScheduler</c> <b>不保證順序</b>，逐則轉送會讓連續三行訊息
/// 亂序落地（換 preset 的那三行讀起來就會前後顛倒）。單一 <c>ConcurrentQueue</c> 是 FIFO，
/// 而且<b>所有生產者共用同一條佇列</b>，所以跨模組的相對順序也一起保住。
/// </para>
/// <para>
/// ⚠️ <b>代價是最多晚一幀</b>：訊息在下一次 <c>Framework.Update</c> 才會出現在聊天視窗。
/// <c>Service.Status</c>（狀態列文字）刻意<b>沒有</b>走佇列，仍然是當場寫入，
/// 所以 UI 上的即時性沒有變。
/// </para>
/// </remarks>
internal static class ChatQueue
{
    private enum Kind
    {
        Normal,
        Error,
    }

    private readonly record struct Entry(Kind Kind, string Message);

    /// <summary>
    /// 待印上限。正常情況下每幀就排乾，堆到這個數字只可能是「排乾那一端死了」
    /// （外掛還沒註冊完、或已經卸載）—— 這時候<b>無上限成長才是真正的問題</b>。
    /// 超過就丟最舊的，語意與 <c>Service.LogMessages</c> 的環形緩衝區一致。
    /// </summary>
    private const int MaxPending = 200;

    private static readonly ConcurrentQueue<Entry> Pending = new();

    /// <summary>已經訂閱 <c>Framework.Update</c> 了沒。</summary>
    private static bool _registered;

    /// <summary>溢位訊息只寫一次，不洗版。</summary>
    private static int _overflowReported;

    /// <summary>開始每幀排乾。外掛建構子呼叫一次。</summary>
    /// <remarks>
    /// ⚠️ 外掛建構子跑在<b>專用的 LongRunning 執行緒</b>上，不是 Framework 執行緒。
    /// <c>Framework.Update</c> 是 field-like event，<c>+=</c> 由編譯器產生的
    /// <c>Interlocked.CompareExchange</c> 迴圈保護，從這裡訂閱是安全的。
    /// </remarks>
    public static void Initialize()
    {
        if (_registered)
            return;

        Service.Framework.Update += Drain;
        _registered = true;
    }

    /// <summary>停止排乾，並把還沒送出去的訊息處理掉。外掛 <c>Dispose()</c> 呼叫一次。</summary>
    /// <remarks>
    /// 🔴 只有<b>確定在 Framework 執行緒上</b>才把剩下的送出去；不在的話直接清掉。
    /// 卸載時少印幾行訊息是可以接受的，把全域的聊天佇列弄壞不是。
    /// </remarks>
    public static void Shutdown()
    {
        if (_registered)
        {
            Service.Framework.Update -= Drain;
            _registered = false;
        }

        if (Service.Framework.IsInFrameworkUpdateThread)
            DrainOnce();
        else
            Pending.Clear();
    }

    /// <summary>排一則一般訊息。<b>可以從任何執行緒呼叫。</b></summary>
    public static void Print(string message) => Enqueue(new Entry(Kind.Normal, message));

    /// <summary>排一則錯誤訊息（紅字）。<b>可以從任何執行緒呼叫。</b></summary>
    public static void PrintError(string message) => Enqueue(new Entry(Kind.Error, message));

    private static void Enqueue(Entry entry)
    {
        Pending.Enqueue(entry);

        var dropped = 0;
        while (Pending.Count > MaxPending && Pending.TryDequeue(out _))
            dropped++;

        // 🔴 寫 PluginLog 而不是 Service.PrintInfo：後者會再排一次佇列（那是另一條路徑），
        //    而這裡本來就可能在任意執行緒上。Serilog 自己是執行緒安全的。
        if (dropped > 0 && Interlocked.Exchange(ref _overflowReported, 1) == 0)
            Service.PluginLog.Information(
                $"[聊天佇列] 待送出的訊息超過 {MaxPending} 則，已丟掉最舊的 {dropped} 則。" +
                @"正常情況下每一幀就會排乾，堆到這個數字通常代表 Framework 更新沒有在跑" +
                @"（外掛正在載入／卸載，或遊戲主執行緒被擋住）。這行訊息只會出現一次。");
    }

    private static void Drain(IFramework _) => DrainOnce();

    /// <summary>把佇列排乾。<b>只在 Framework 執行緒上呼叫。</b></summary>
    private static void DrainOnce()
    {
        while (Pending.TryDequeue(out var entry))
        {
            try
            {
                if (entry.Kind == Kind.Error)
                    Service.Chat.PrintError(entry.Message);
                else
                    Service.Chat.Print(entry.Message);
            }
            catch (Exception e)
            {
                // 🔴 一則送不出去不可以讓後面的一起卡住，也不可以讓例外往上竄進
                //    Framework 的更新迴圈（那會讓 Dalamud 把這個處理器整個拆掉）。
                Service.PluginLog.Warning($"[聊天佇列] 送出訊息時失敗（已跳過這一則）：{e.Message}");
            }
        }
    }
}
