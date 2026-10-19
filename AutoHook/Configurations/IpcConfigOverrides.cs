using System;
using System.Collections.Generic;

namespace AutoHook.Configurations;

/// <summary>
/// 跨外掛 IPC 對「使用者設定」的覆寫層。
/// </summary>
/// <remarks>
/// 🔑 IPC 改的仍然是欄位本身，<b>所有讀取端一行都不必動</b>；同時把「使用者自己設定的值」記進這張表，
/// 序列化時換回使用者的值 ⇒ <b>設定檔內容永遠是使用者的選擇</b>（否則借用端各自拍快照、各自還原，
/// 留在磁碟上的是最後一個還原者的快照）。使用者自己在 UI 或指令改同一個設定時，覆寫被清掉。
/// ⚠️ 光是「IPC 不呼叫 <c>Save()</c>」<b>不夠</b>：存檔是整份序列化，<b>必須在序列化那一刻換回來</b>
/// （見 <see cref="Configuration.PluginEnabledPersisted"/>）。
/// <para>🔴🔴 <b>這張表必須有鎖</b>：<see cref="Set"/> 走 IPC 端點＝<b>呼叫端的執行緒</b>，UI 每幀從繪製執行緒讀，
/// 存檔時走訪整張表。<b>鎖內絕不呼叫 ImGui、絕不做檔案 I/O</b>——取值／拍快照就出來。</para>
/// </remarks>
internal static class IpcConfigOverrides
{
    /// <summary>鍵＝<see cref="Configuration.PluginEnabled"/>。</summary>
    public const string PluginEnabledKey = @"PluginEnabled";

    /// <summary>鍵＝<c>SpearFishingPresets.AutoGigEnabled</c>。</summary>
    public const string AutoGigEnabledKey = @"AutoGigEnabled";

    /// <summary>鍵＝<see cref="Configuration.AutoStartFishing"/>。</summary>
    public const string AutoStartFishingKey = @"AutoStartFishing";

    private static readonly Dictionary<string, bool> UserValues = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    /// <summary>
    /// 由 IPC 改寫一個布林設定：只改執行期的值，並在<b>第一次</b>覆寫時把使用者的值記下來。
    /// </summary>
    /// <remarks>
    /// 🔴 <c>ContainsKey</c> ＋ 索引指派是兩步，中間被別的執行緒插入就會把 IPC 的值
    /// 當成「使用者的值」記下去 ⇒ 整段必須在鎖內。
    /// </remarks>
    public static void Set(ref bool field, bool value, string key)
    {
        lock (Gate)
        {
            if (!UserValues.ContainsKey(key))
                UserValues[key] = field;
        }

        field = value;

        // 🔴 刻意不呼叫 Service.Save()：呼叫它就等於把 IPC 的值寫進使用者的設定檔。
    }

    /// <summary>序列化時要寫出去的值：有覆寫就用使用者自己的值，沒有就用目前的值。</summary>
    public static bool ValueForSave(string key, bool current)
    {
        lock (Gate)
            return UserValues.TryGetValue(key, out var user) ? user : current;
    }

    /// <summary>目前有沒有覆寫在生效（有的話一併回傳使用者自己的值），供 UI 標記用。</summary>
    public static bool TryGetUserValue(string key, out bool userValue)
    {
        lock (Gate)
            return UserValues.TryGetValue(key, out userValue);
    }

    /// <summary>使用者自己動了這個設定 ⇒ 他的選擇重新成為權威，丟掉 IPC 覆寫。</summary>
    public static void Clear(string key)
    {
        lock (Gate)
            UserValues.Remove(key);
    }

    /// <summary>載入設定檔＝重新確立「使用者的值」，任何殘留的覆寫都作廢。</summary>
    public static void ClearAll()
    {
        lock (Gate)
            UserValues.Clear();
    }
}
