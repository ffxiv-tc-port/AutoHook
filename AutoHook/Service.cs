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

    // Attach()：逾時時多印一行「是哪一步逾時」的 Warning，並蓋掉 ECommons 那行完全匿名的。
    // 理由與限制寫在 TaskTimeoutLog 的註解裡。
    public static readonly TaskManager TaskManager = TaskTimeoutLog.Attach(new TaskManager()
    {
        DefaultConfiguration = { TimeLimitMS = 5000 }
    }, "AutoHook");

    
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
    /// 🔴🔴 <b>這張表被多條執行緒碰</b>：Framework 執行緒、遊戲自己的執行緒（<c>UseAction</c> 與
    /// <c>UpdateCatch</c> 的 hook detour）、繪製執行緒（<c>PluginUI.Debug()</c>），IPC 端點還可能從<b>呼叫端的執行緒</b>
    /// 進來。裸的 <c>Queue&lt;T&gt;</c> 零同步，
    /// 失敗形式<b>不是「少一行 log」而是佇列本身壞掉</b>。
    /// <br/>🔴 外面一律透過 <see cref="SnapshotLogMessages"/> 讀；這個欄位刻意不公開。
    /// </remarks>
    private static readonly Queue<string> LogMessages = new();

    public static bool OpenConsole;

    /// <summary>
    /// 把一則訊息推進環形緩衝區。<b>鎖內只碰佇列</b>，寫 <c>PluginLog</c> 是呼叫端在鎖外做的。
    /// </summary>
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
    /// ⚠️ 這一級留給「值錯了也不會報錯、只會表現成行為怪怪的」那種地方，不要拿來當一般 log。
    /// </remarks>
    public static void PrintInfo(string msg)
    {
        PushLog(msg);
        PluginLog.Information(msg);
    }

    /// <summary>
    /// ⚠️ Verbose 在使用者的 log 裡收不到 ——
    /// 這一級寫出去的東西只有外掛內建的除錯主控台看得到。
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
    /// 直接呼叫 <c>IChatGui.Print</c> 的話，較舊的 Dalamud 待印佇列是裸的
    /// <c>Queue&lt;T&gt;</c>，從非 Framework 執行緒進去會把<b>全域</b>的那一個弄壞（外掛側不假設 Dalamud 的版本）。
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
