using System.Collections.Generic;
using AutoHook.Classes;
using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using AutoHook.SeFunctions;
using Dalamud.Plugin.Services;
using Dalamud;
using AutoHook.Configurations;
using AutoHook.Utils;
using Dalamud.Game.ClientState.Objects;
using ECommons.Automation.NeoTaskManager;

namespace AutoHook;

public class Service
{
    public static void Initialize(IDalamudPluginInterface pluginInterface)
        => pluginInterface.Create<Service>();

    public const string PluginName = "AutoHook";
    
    public const string GlobalPresetName = "Global Preset";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IObjectTable Objects { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static ICommandManager Commands { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public static IPluginLog  PluginLog { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get;private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;
    
    public static BaitManager BaitManager { get; set; } = null!;
    public static Configuration Configuration { get; set; } = null!;
    public static WindowSystem WindowSystem { get; } = new(PluginName);
    public static SeTugType TugType { get; set; } = null!;
    public static ClientLanguage Language { get; set; }

    public static string _status = @"";
    
    public static BaitFishClass LastCatch { get; set; } = new(@"-", -1);

    public static string Status
    {
        get => _status;
        set => _status = value;
    }

    public static readonly TaskManager TaskManager = new TaskManager()
    {
        DefaultConfiguration = { TimeLimitMS = 5000 }
    };

    
    public static void Save()
    {
        Configuration.Save();
    }

    private const int MaxLogSize = 50;

    /// <summary>
    /// 只保護 <see cref="LogMessages"/>。<b>鎖內只碰佇列</b>：不呼叫 ImGui、不做檔案 I/O、
    /// 也不寫 <c>PluginLog</c>（那是 I/O）。
    /// </summary>
    private static readonly object LogMessagesGate = new();

    /// <summary>
    /// 外掛內建除錯主控台的環形緩衝區（最多 <see cref="MaxLogSize"/> 則）。
    /// </summary>
    /// <remarks>
    /// 🔴🔴 <b>這張表被四種執行緒碰</b>：
    /// <list type="bullet">
    /// <item>Framework 執行緒（<c>FishingManager.OnFrameworkUpdate</c> 往下的絕大多數呼叫點）；</item>
    /// <item>遊戲自己的執行緒（<c>UseAction</c> 與 <c>UpdateCatch</c> 的 hook detour）；</item>
    /// <item><b>呼叫端的執行緒</b>——IPC 端點跑在呼叫者的執行緒上，沒有任何「一定在 Framework
    ///       執行緒」的保證。<c>CreateAndSelectAnonymousPreset</c> 與 <c>ImportAndSelectPreset</c>
    ///       會走到 <c>Configuration.ImportPreset</c> → <c>ConvertOldPresetV3</c> →
    ///       <see cref="PrintDebug"/>（匯入字串帶 <c>AH3_</c> 前綴時），沿路<b>沒有執行緒閘門</b>；</item>
    /// <item>繪製執行緒（<c>PluginUI.Debug()</c> 把內容畫出來）。</item>
    /// </list>
    /// 裸的 <c>Queue&lt;T&gt;</c> 零同步，失敗形式<b>不是「少一行 log」而是佇列本身壞掉</b>——
    /// <c>Enqueue</c> 與 <c>Dequeue</c>／<c>ToArray</c> 並行時會擲 <c>InvalidOperationException</c>
    /// 或讀到撕裂的內容，而且會連帶弄壞除錯主控台。跟 <c>ECommons.Throttlers.EzThrottler</c>
    /// 那條紅線完全同形狀。
    /// <br/>🔴 外面一律透過 <see cref="SnapshotLogMessages"/> 讀；這個欄位刻意不再公開。
    /// </remarks>
    private static readonly Queue<string> LogMessages = new();

    public static bool OpenConsole;

    /// <summary>
    /// 把一則訊息推進環形緩衝區。<b>鎖內只碰佇列</b>，寫 <c>PluginLog</c> 是呼叫端在鎖外做的。
    /// </summary>
    /// <remarks>
    /// 📌 原本這段是「<c>if (Count &gt;= Max) Dequeue();</c> 再 <c>Enqueue</c>」，逐字散在三支
    /// <c>Print*</c> 裡。收進鎖之後 <c>Count</c> 不可能超過上限，所以 <c>while</c> 與原本的
    /// <c>if</c> 在任何到得了的狀態下結果相同（改成 <c>while</c> 只是不依賴那個不變式）。
    /// </remarks>
    private static void PushLog(string msg)
    {
        lock (LogMessagesGate)
        {
            while (LogMessages.Count >= MaxLogSize)
                LogMessages.Dequeue();

            LogMessages.Enqueue(msg);
        }
    }

    /// <summary>
    /// 拍一份緩衝區快照給 UI 畫。<b>鎖內只複製，畫圖一律在鎖外</b>。
    /// </summary>
    public static string[] SnapshotLogMessages()
    {
        lock (LogMessagesGate)
            return LogMessages.ToArray();
    }

    public static void PrintDebug(string msg)
    {
        PushLog(msg);
        PluginLog.Debug(msg);
    }

    /// <summary>
    /// 給「要使用者回報時看得到」的診斷用。
    /// </summary>
    /// <remarks>
    /// 📌 使用者的 Dalamud <c>LogLevel</c> 是 <b>1</b>（Serilog 的 <c>Debug</c>），門檻放行 Debug、
    /// <b>只濾掉 Verbose</b> —— 所以 <see cref="PrintDebug"/> 寫出去的東西他們的 log <b>收得到</b>。
    /// （舊註解寫「等級是 2、Debug 一行都不會有」是錯的；真正的盲區只有 <see cref="PrintVerbose"/>。）
    /// <br/>⚠️ 但實機單檔的 <c>[DBG]</c> 有十幾萬到數十萬行，寫 Debug 的診斷會被淹沒 ⇒
    /// 這一級留給「值錯了也不會報錯、只會表現成行為怪怪的」那種地方，不要拿來當一般 log。
    /// </remarks>
    public static void PrintInfo(string msg)
    {
        PushLog(msg);
        PluginLog.Information(msg);
    }

    /// <summary>
    /// ⚠️ 使用者的 <c>LogLevel</c> 是 1，<b>Verbose 是唯一被濾掉的等級</b> ——
    /// 這一級寫出去的東西在使用者的 log 裡一行都不會有，只有外掛內建的除錯主控台看得到。
    /// </summary>
    public static void PrintVerbose(string msg)
    {
        PushLog(msg);
        PluginLog.Verbose(msg);
    }
    
    /// <summary>
    /// 寫狀態列文字，並（在使用者沒關掉聊天輸出時）把同一段文字送到遊戲的聊天視窗。
    /// </summary>
    /// <remarks>
    /// 🔴 聊天那一半走 <see cref="ChatQueue"/>：<b>可以從任何執行緒呼叫</b>。
    /// 直接呼叫 <c>IChatGui.Print</c> 的話，使用者實機那顆 Dalamud 的待印佇列是裸的
    /// <c>Queue&lt;T&gt;</c>，從非 Framework 執行緒進去會把<b>全域</b>的那一個弄壞。
    /// <br/>📌 <see cref="Status"/> 刻意<b>不</b>走佇列——它是 UI 每幀直接讀的欄位，
    /// 當場寫入才不會讓狀態列慢一幀。
    /// </remarks>
    public static void PrintChat(string msg)
    {
        Status = msg;

        if (Configuration.ShowChatLogs)
            ChatQueue.Print(msg);
    }
}
