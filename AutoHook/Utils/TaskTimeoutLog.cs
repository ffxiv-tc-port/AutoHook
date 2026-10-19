using System;
using ECommons.Automation.NeoTaskManager;

namespace AutoHook.Utils;

/// <summary>
/// 讓 NeoTaskManager 的「任務逾時」在 dalamud.log 上說得出是<b>哪一步</b>逾時。
/// </summary>
/// <remarks>
/// <para>
/// 由來：ECommons 的 <c>TaskTimeoutException</c> 是一個<b>完全空的類別</b>（連 Message 都沒有），
/// 而逾時時真正印出去的是 <c>TaskManager.Tick</c> 裡的 <c>e.LogWarning()</c>
/// —— 空訊息 ＋ 永遠指向 <c>TaskManager.Tick</c> 的堆疊 ⇒ <b>完全匿名</b>。
/// 唯一帶任務名稱的那一行（<c>→→Task timed out {Name}@{Location}</c>）被 <c>ShowDebug</c> 閘住，
/// 本外掛的 TaskManager 都是 <c>ShowDebug = false</c>。
/// </para>
/// <para>
/// 🔴 等級刻意維持 <c>Warning</c>：ECommons 原本就是 Warning，降級成 Information 只會弱化訊號。
/// 🔴 刻意<b>不</b>用 <c>DuoLog</c>：ECommons 的 <c>DuoLog.*</c> 在每一個等級都會無條件印到
/// 使用者的聊天視窗（沒有等級閘門），逾時是診斷資訊，不該洗聊天。
/// 🔴 逾時那一行的印法刻意<b>不</b>在 ECommons 裡改（那一份是多個外掛共用的），所以在外掛側掛處理器。
/// ⚠️ <c>remainingTimeMS</c> 是 <c>ref</c>：寫它等於偷偷延長逾時，這裡<b>只讀不寫</b>。
/// </para>
/// </remarks>
public static class TaskTimeoutLog
{
    /// <summary>
    /// 把逾時說明掛到 <paramref name="taskManager"/> 上，並回傳同一個實例（方便寫在欄位初始式裡）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 這裡改的是 TaskManager <b>自己那份</b> <c>DefaultConfiguration</c>，不是傳進建構子的那個物件
    /// —— 建構子做的是 <c>new TaskManagerConfiguration{...}.With(傳進來的)</c>，事件被複製進另一個物件，
    /// 事後對傳進去的那個物件指派完全沒有效果。
    /// <para>
    /// <c>TimeoutSilently = true</c> 是為了蓋掉 ECommons 那行沒有任何資訊的匿名 Warning，
    /// 避免同一件事印兩遍。⚠️ 這個蓋法的前提是<b>沒有任務把 <c>ExecuteDefaultConfigurationEvents</c>
    /// 設成 false</b>（設了的話預設事件不會觸發，就會變成「靜默 ＋ 沒有人印」）。
    /// 本外掛目前一處都沒有用到那個旗標；之後若有人加，記得連這裡一起看。
    /// </para>
    /// </remarks>
    public static TaskManager Attach(TaskManager taskManager, string tag)
    {
        taskManager.DefaultConfiguration.TimeoutSilently = true;
        taskManager.DefaultConfiguration.OnTaskTimeout += (TaskManagerTask task, ref long remainingTimeMS) =>
        {
            // 該任務自己帶了處理器就交給它印，不要印兩遍。
            if(task.Configuration?.OnTaskTimeout != null) return;

            var limit = task.Configuration?.TimeLimitMS ?? taskManager.DefaultConfiguration.TimeLimitMS;
            var abort = task.Configuration?.AbortOnTimeout ?? taskManager.DefaultConfiguration.AbortOnTimeout ?? true;
            Service.PluginLog.Warning(
                $"[{tag}] 任務逾時：{Describe(task)}，上限 {(limit.HasValue ? limit.Value.ToString() : "?")} ms" +
                (abort ? "，整條任務佇列會被中止。" : "，只丟棄這一步，其餘任務繼續。"));
        };
        return taskManager;
    }

    /// <summary>
    /// 盡量把一個任務描述成人看得懂的樣子。
    /// </summary>
    /// <remarks>
    /// 子模組那份 ECommons 已經把未具名任務的 <c>Name</c> 改寫成可讀的形狀（lambda＝<c>類別.外層方法#b__N</c>、
    /// 區域函式＝<c>類別.外層方法.區域函式</c>，方法群組維持方法名）；<c>Location</c> 仍是原始的宣告型別名，
    /// lambda 是編譯器產生的 <c>&lt;&gt;c</c>／<c>&lt;&gt;c__DisplayClassN_M</c>，一律不印。
    /// ⚠️ 查得到的是哪個方法／哪個檔，不是第幾行。
    /// </remarks>
    public static string Describe(TaskManagerTask task)
    {
        var name = task.Name ?? "";
        var location = task.Location ?? "";
        if(TryGetEnclosingMethod(name, out var enclosing))
        {
            // lambda 的 Location 是編譯器產生的 <>c / <>c__DisplayClassN_M，印出來只是噪音。
            return location.StartsWith("<>", StringComparison.Ordinal)
                ? $"{enclosing}() 內的匿名步驟 [{name}]"
                : $"{enclosing}() 內的匿名步驟 [{name}@{location}]";
        }
        return location.StartsWith("<>", StringComparison.Ordinal) ? $"[{name}]" : $"[{name}@{location}]";
    }

    /// <summary>從 <c>類別.外層方法#b__N</c>（ECommons 給 lambda 的名字）或編譯器原名 <c>&lt;外層方法&gt;b__N</c> 取出外層方法。</summary>
    /// <remarks>要連 <c>b__</c> 一起認：具名任務的名字也可能含 '#'。</remarks>
    private static bool TryGetEnclosingMethod(string name, out string enclosing)
    {
        enclosing = "";
        if(name.Length >= 3 && name[0] == '<')
        {
            var end = name.IndexOf('>');
            if(end <= 1) return false;
            enclosing = name.Substring(1, end - 1);
            return true;
        }
        var hash = name.IndexOf("#b__", StringComparison.Ordinal);
        if(hash <= 0) return false;
        enclosing = name[..hash];
        return true;
    }
}
