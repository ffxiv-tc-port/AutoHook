using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoHook.Configurations;

/// <summary>
/// 「請 AutoHook 在我這段序列期間先別動」的<b>租約（lease）登記處</b>：
/// 多個外掛各自持有一把帶到期時間的租約，讀取端一律是
/// <c>使用者的值 &amp;&amp; 沒有任何一把租約在壓制</c>。放約或逾時就自動還原，<b>不需要任何人記得還</b>。
/// </summary>
/// <remarks>
/// 🔴🔴 <b>存在的理由＝舊的開關沒有主人。</b>
/// <see cref="Configuration.PluginEnabled"/> 是執行期的<b>全域</b>欄位，舊端點
/// <c>AutoHook.SetPluginState</c> 對它是單向寫入——誰寫進去就一直停在那裡。
/// 目前三個借用端各自「拍快照 → 設值 → 還原」，其中 <b>ICE 根本沒有還原路徑</b>
/// （全 repo 只寫 <see langword="true"/>，找不到對應的 <see langword="false"/>）。
/// <see cref="IpcConfigOverrides"/> 已經把<b>設定檔</b>那半邊保護住了（磁碟上永遠是使用者的值），
/// 但<b>執行期</b>仍然是最後寫入者獲勝、而且沒有逾時：持有者當掉在 <see langword="false"/> 上時，
/// 使用者看到的是「AutoHook 突然不會自動上鉤了」，log 一個字都沒有，唯一自癒是重載外掛。
/// <para>
/// 🔑🔑 <b>形狀與 YesAlready／AutoRetainer 逐字相同</b>（只差 EzIPC 的 prefix），
/// 而且<b>拿到租約本身就開始壓制</b>（refcount &gt; 0）。這一點刻意不跟 vnavmesh 的
/// <c>MovementLeases</c> 一樣（那邊拿到租約是惰性的，因為它同時管兩個不同的受控值）——
/// AutoHook 只有一個受控值，若跟著 vnavmesh 做，照標準三步驟寫的消費端會
/// <b>完全靜默地什麼都沒發生</b>。
/// </para>
/// <para>
/// 🔑 <b>租約解掉的是「誰的意思」與「什麼時候還」這兩個資訊</b>：每一把記名字、記到期時間；
/// 逾時自動掃除並寫 <c>Information</c>，使用者的 log 因此看得到是誰壓著。
/// </para>
/// <para>
/// 🔴 <b>租約只能往「壓制」壓，不能反過來替使用者開啟。</b>
/// <see cref="ResolveEnabled"/> 是「任何一把租約說停手 ⇒ 停手」，其餘照使用者的值。
/// 反過來寫（租約設 <see langword="true"/> 就強制啟用）會蓋掉使用者自己在設定頁取消勾選的
/// 「Enable AutoHook」——那是他的明示選擇。<see cref="SetPluginEnabled"/> 傳
/// <see langword="true"/> 的語意是「我這把不再壓制」，<b>不是</b>「我要求啟用」。
/// </para>
/// <para>
/// 🔴 <b>逾時上限是硬性的</b>：租用者當掉／被卸載／忘了放開，都不能讓 AutoHook 永久停手。
/// 每一把都有 <see cref="MaxLeaseMilliseconds"/> 的天花板（5 分鐘），長工作要自己
/// <see cref="Renew"/> 續約（心跳，建議間隔 <see cref="RenewIntervalHintMs"/>，＝租期的十分之一）。
/// ⚠️ <b>續約間隔不能接近租期</b>：<see cref="Renew"/> 的第一件事是掃除，掃除條件是
/// <c>now &gt;= ExpiresAt</c> ⇒ 間隔只要接近租期，第一次心跳送到時那把已經被掃掉、
/// 續約<b>必定</b>回 <see langword="false"/>（不是競態，是每次都會發生）。
/// </para>
/// <para>
/// 📌 <b>逾時方向是安全的</b>：租約失效＝AutoHook 恢復使用者自己的設定，
/// 不會讓外掛卡在停手狀態。這也是「只能壓制、不能開啟」的另一面——反向的租約逾時會是
/// 「使用者關掉的東西被別人打開又忘了關」，那個方向沒有安全的失效。
/// </para>
/// <para>
/// ⚠️ <b>執行緒</b>：IPC 端點跑在<b>呼叫端的執行緒</b>上（沒有任何「一定在 Framework 執行緒」
/// 的保證），而 <see cref="ResolveEnabled"/> 每幀從 Framework 執行緒讀、<b>而且也從
/// <c>UseAction</c> 的 hook detour（遊戲自己的執行緒）讀</b>，<see cref="Snapshot"/> 每幀從
/// 繪製執行緒讀 ⇒ <b>全程上鎖</b>。
/// 🔴 <b>絕不使用 ECommons 的 EzThrottler</b> —— 它是整個外掛共用的靜態 <c>Dictionary</c> 且零同步，
/// 從 IPC 端點碰它的失敗形式不是「拿到舊值」而是<b>字典本身壞掉</b>，還會連帶弄壞
/// 同一外掛內所有模組的節流。
/// 🔴 <b>鎖內絕不呼叫 ImGui、絕不做檔案 I/O，也不寫 log</b>：逾時訊息在鎖內先收進一個 list，
/// 出了鎖才 <see cref="Flush"/>。UI 走「鎖內拍快照、鎖外畫」。
/// 📌 <b>寫 log 走 <c>Service.PluginLog</c></b>。（歷史理由寫的是「<c>Service.PrintInfo</c>
/// 底下的 <c>Service.LogMessages</c> 是沒有同步的 <c>Queue</c>」——<b>那個前提已經不成立</b>：
/// 該佇列自「除錯主控台的訊息佇列加鎖」那一顆起已經上鎖，兩者現在都可以從任意執行緒呼叫。
/// 這裡維持 <c>PluginLog</c> 是因為租約的診斷不需要出現在外掛內建的除錯主控台。）
/// 🔴 <b>要送到遊戲聊天視窗的訊息一律走 <c>ChatQueue</c></b>——直接呼叫 <c>IChatGui.Print</c>
/// 會從非 Framework 執行緒碰到 Dalamud <b>全域</b>的待印佇列。
/// </para>
/// <para>
/// 📌 <b>所有端點的回傳型別都是不可為 null 的值型別</b>（<see cref="Guid"/> / <see cref="bool"/>），
/// 失敗一律回 <see cref="Guid.Empty"/> 或 <see langword="false"/>，<b>永不回 null</b>。
/// Dalamud 的 <c>CallGateChannel.ConvertObject</c> 對 null 輸入立刻回 null，而
/// <c>return (TRet)result;</c> 對值型別擲的是 <c>NullReferenceException</c>——
/// 「有值時靜默成功、只有回 null 那一次炸一個看起來與 IPC 無關的 NRE」是最難歸因的那一類。
/// </para>
/// </remarks>
internal static class PluginEnabledLeases
{
    /// <summary>沒指定時長時的預設租期（5 分鐘）。與 vnavmesh／YesAlready 的租約政策一致。</summary>
    public const int DefaultLeaseMilliseconds = 300_000;

    /// <summary>單一把租約的<b>硬性</b>上限（5 分鐘）。要求更長會被夾到這個值。</summary>
    public const int MaxLeaseMilliseconds = 300_000;

    /// <summary>建議的續約間隔（30 秒＝租期的十分之一）。</summary>
    public const int RenewIntervalHintMs = 30_000;

    /// <summary>同時存在的租約把數上限。超過就拒絕新的請求（回 <see cref="Guid.Empty"/>）。</summary>
    /// <remarks>
    /// 🔴 防的是「每次迴圈都 Acquire、從來不 Release」的呼叫端：那種形狀不會有任何錯誤，
    /// 只會讓這張表無限長大，而且因為租約會續命，AutoHook 會永遠停手。
    /// 撞到上限時寫 <c>Warning</c> 並附上目前的持有者名單——名單本身就指出了兇手。
    /// </remarks>
    public const int LeaseCap = 32;

    private sealed class Lease(Guid id, string owner, long expiresAt)
    {
        public Guid Id { get; } = id;
        public string Owner { get; } = owner;

        /// <summary><see cref="Environment.TickCount64"/> 座標系的到期時刻。</summary>
        public long ExpiresAt { get; set; } = expiresAt;

        /// <summary>續約時沿用的時長（<see cref="Renew"/> 不帶參數時用）。</summary>
        public int DurationMs { get; set; }

        /// <summary>
        /// <see langword="false"/>＝這把租約要求停手（<b>新租約的預設</b>）；
        /// <see langword="true"/>＝這把暫時不壓制；<see langword="null"/> 視同 <see langword="true"/>。
        /// </summary>
        public bool? Enabled { get; set; }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, Lease> Leases = [];

    /// <summary>「現在一把租約都沒有」的不上鎖快路。</summary>
    /// <remarks>
    /// 🔴 只用來<b>提早否定</b>：<see langword="false"/> 一定代表沒有租約（清空一定在設它之前），
    /// <see langword="true"/> 只代表「可能有」，還是要進鎖裡掃過期。
    /// 反過來寫（樂觀地相信 true）會讓已經到期的租約繼續壓住。
    /// </remarks>
    private static volatile bool _anyLeases;

    /// <summary>「現在沒有任何一把在壓制」的不上鎖快路。語意與 <see cref="_anyLeases"/> 相同。</summary>
    private static volatile bool _anySuppressing;

    /// <summary>目前是否有<b>任何一把</b>沒到期的租約（診斷用）。</summary>
    public static bool AnyActive
    {
        get
        {
            if (!_anyLeases)
                return false;

            List<string>? logs = null;
            bool any;
            lock (Gate)
            {
                SweepLocked(ref logs);
                any = Leases.Count != 0;
            }

            Flush(logs);
            return any;
        }
    }

    /// <summary>目前是否有<b>任何一把租約正在要求 AutoHook 停手</b>（UI 標記用）。</summary>
    public static bool AnySuppressing
    {
        get
        {
            if (!_anySuppressing)
                return false;

            List<string>? logs = null;
            bool any;
            lock (Gate)
            {
                SweepLocked(ref logs);
                any = Leases.Values.Any(x => x.Enabled == false);
            }

            Flush(logs);
            return any;
        }
    }

    /// <summary>把使用者的啟用開關與目前的租約疊起來，回傳<b>實際生效</b>的值。</summary>
    /// <remarks>🔴 只能往「停手」的方向壓；沒有任何一把在壓制時，照使用者的值。</remarks>
    public static bool ResolveEnabled(bool userValue)
    {
        if (!_anyLeases)
            return userValue;

        List<string>? logs = null;
        var result = userValue;
        lock (Gate)
        {
            SweepLocked(ref logs);
            foreach (var lease in Leases.Values)
            {
                if (lease.Enabled == false)
                {
                    result = false;
                    break;
                }
            }
        }

        Flush(logs);
        return result;
    }

    /// <summary>目前每一把有效租約的診斷快照：租用者名字、距離逾時還有多久（毫秒）、有沒有在壓制。</summary>
    /// <remarks>⚠️ 只給 UI／tooltip 用（會配置陣列），呼叫前先判 <see cref="AnySuppressing"/>。</remarks>
    public static (string Owner, long RemainingMs, bool Suppressing)[] Snapshot()
    {
        if (!_anyLeases)
            return [];

        List<string>? logs = null;
        (string, long, bool)[] snapshot;
        lock (Gate)
        {
            SweepLocked(ref logs);
            var now = Environment.TickCount64;
            snapshot = Leases.Values
                .Select(x => (x.Owner, Math.Max(0, x.ExpiresAt - now), x.Enabled == false))
                .ToArray();
        }

        Flush(logs);
        return snapshot;
    }

    /// <summary>
    /// 取得一把新的租約。回傳的 <see cref="Guid"/> 就是憑證；<see cref="Guid.Empty"/>＝<b>沒拿到</b>
    /// （沒帶名字，或已達 <see cref="LeaseCap"/>），呼叫端必須自己判斷，不要當成拿到了。
    /// </summary>
    /// <param name="owner">租用者名字（慣例是自己的 InternalName）。空白會被拒絕。</param>
    /// <param name="milliseconds">租期毫秒；夾在 <c>1</c> 與 <see cref="MaxLeaseMilliseconds"/> 之間。</param>
    /// <remarks>
    /// 📌 <b>每次呼叫都是一把新的</b>（不是「同名就共用」）：同一個外掛內部有兩段序列並行時
    /// 各自持一把，先結束的那段放開自己那把不會影響另一段。
    /// <br/>🔴 <b>拿到租約就已經開始壓制了</b>，不需要再呼叫別的端點——
    /// 與 YesAlready／AutoRetainer 的租約形狀一致（消費端可以直接把那邊的
    /// 「Acquire → 每 30 秒 Renew → Release」幫手指向這裡）。
    /// <see cref="SetPluginEnabled"/> 是<b>選配</b>：用來在不交回租約的前提下暫時不壓制。
    /// </remarks>
    public static Guid Acquire(string? owner, int milliseconds)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            Service.PluginLog.Warning(
                "[PluginEnabledLease] 收到沒有帶名字的暫停租用請求，已拒絕。租用者必須帶一個識別字串" +
                "（慣例是自己的 InternalName），否則使用者無從得知是誰讓 AutoHook 停手。");
            return Guid.Empty;
        }

        var name = owner!.Trim();
        List<string>? logs = null;
        var duration = ClampDuration(milliseconds, name, ref logs);
        var id = Guid.NewGuid();
        string owners;
        bool overCap;

        lock (Gate)
        {
            SweepLocked(ref logs);
            overCap = Leases.Count >= LeaseCap;
            if (!overCap)
            {
                // 🔴 Enabled = false ⇒ 拿到租約本身就開始壓制。
                //    這是全艦隊的標準形狀（YesAlready／AutoRetainer 的 refcount）；
                //    若改成 vnavmesh 那種「拿到了還要再呼叫一次才生效」，
                //    照標準三步驟（Acquire → Renew → Release）寫的消費端會
                //    什麼都沒發生而且完全靜默。
                Leases[id] = new Lease(id, name, Environment.TickCount64 + duration)
                {
                    DurationMs = duration,
                    Enabled = false,
                };
                _anyLeases = true;
                _anySuppressing = true;
            }

            owners = DistinctOwnersLocked();
        }

        Flush(logs);

        if (overCap)
        {
            Service.PluginLog.Warning(
                $"[PluginEnabledLease] 暫停租約已達上限 {LeaseCap} 把，拒絕「{name}」的請求。" +
                $"目前持有者：{owners}。⇒ 這幾乎一定是某個呼叫端只 Acquire 不 Release。");
            return Guid.Empty;
        }

        Service.PluginLog.Information(
            $"[PluginEnabledLease] 「{name}」取得暫停租約 {id}（{duration} 毫秒）。目前持有者：{owners}。");
        return id;
    }

    /// <summary>交回一把租約。回 <see langword="false"/>＝這把不存在（已經放開過或已經逾時）。</summary>
    /// <remarks>🔑 放開的那一刻它押著的值就不再參與疊加 ⇒ 使用者的值自動變回權威。</remarks>
    public static bool Release(Guid id)
    {
        string? owner = null;
        int remaining;
        List<string>? logs = null;

        lock (Gate)
        {
            if (Leases.Remove(id, out var lease))
                owner = lease.Owner;

            SweepLocked(ref logs);
            remaining = Leases.Count;
        }

        Flush(logs);

        if (owner == null)
            return false;

        Service.PluginLog.Information(
            $"[PluginEnabledLease] 「{owner}」放開暫停租約 {id}，剩餘 {remaining} 把。");
        return true;
    }

    /// <summary>
    /// 續約（心跳）。回 <see langword="false"/>＝這把已經不在了，呼叫端必須重新
    /// <see cref="Acquire"/>，<b>不要當成續約成功</b>。
    /// </summary>
    /// <param name="id">租約憑證。</param>
    /// <param name="milliseconds"><see langword="null"/>＝沿用取得時的時長。</param>
    public static bool Renew(Guid id, int? milliseconds = null)
    {
        List<string>? logs = null;
        bool ok;

        lock (Gate)
        {
            SweepLocked(ref logs);
            if (Leases.TryGetValue(id, out var lease))
            {
                var duration = milliseconds is { } ms ? ClampDuration(ms, lease.Owner, ref logs) : lease.DurationMs;
                lease.DurationMs = duration;

                // 🔴 取 max：續約永遠只會往後延，不會把已經談好的到期時間往前搬。
                var until = Environment.TickCount64 + duration;
                if (until > lease.ExpiresAt)
                    lease.ExpiresAt = until;
                ok = true;
            }
            else
            {
                ok = false;
            }
        }

        Flush(logs);
        return ok;
    }

    /// <summary>
    /// 用這把租約押住啟用開關。<paramref name="enabled"/> 傳 <see langword="false"/>＝
    /// 「我這把要求 AutoHook 停手」；傳 <see langword="true"/>＝「我這把不再要求停手」
    /// （<b>不是</b>「我要求啟用」）。回 <see langword="false"/>＝這把租約已經不在了。
    /// </summary>
    public static bool SetPluginEnabled(Guid id, bool enabled)
    {
        string? owner = null;
        var changed = false;
        List<string>? logs = null;

        lock (Gate)
        {
            SweepLocked(ref logs);
            if (Leases.TryGetValue(id, out var lease))
            {
                owner = lease.Owner;
                changed = lease.Enabled != enabled;
                lease.Enabled = enabled;
                RecomputeFlagsLocked();
            }
        }

        Flush(logs);

        if (owner == null)
            return false;

        if (changed)
            Service.PluginLog.Information(
                $"[PluginEnabledLease] 「{owner}」的租約 {id} 把 AutoHook 押成 " +
                (enabled ? "「不再要求停手」。" : "「停手」。⇒ 在這把租約放開或逾時之前，AutoHook 不會自動上鉤。"));

        return true;
    }

    /// <summary>把所有租約丟掉（外掛卸載）。</summary>
    public static void ReleaseAll(string reason)
    {
        string owners;

        lock (Gate)
        {
            if (Leases.Count == 0)
            {
                _anyLeases = false;
                _anySuppressing = false;
                return;
            }

            owners = DistinctOwnersLocked();
            Leases.Clear();
            _anyLeases = false;
            _anySuppressing = false;
        }

        Service.PluginLog.Information($"[PluginEnabledLease] 丟掉全部暫停租約（{reason}）：{owners}。");
    }

    /// <summary>已經回報過的訊息鍵。<b>同一個鍵只寫一次</b>，永不清空。</summary>
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    /// <summary>
    /// 只保護 <see cref="Reported"/>。<b>刻意不共用 <see cref="Gate"/></b>：
    /// <see cref="ClampDuration"/> 會在已經持有 <see cref="Gate"/> 的狀態下被 <see cref="Renew"/> 呼叫進來。
    /// </summary>
    private static readonly object ReportGate = new();


    /// <summary>
    /// 把要求的租期夾進 <c>1</c>～<see cref="MaxLeaseMilliseconds"/>，並在<b>真的夾到</b>時
    /// 對同一個租用者寫一次 <c>Information</c>。
    /// </summary>
    /// <remarks>🔴 夾值如果是靜默的，呼叫端會以為自己拿到了要求的時長，然後在半路被逾時掃掉。</remarks>
    private static int ClampDuration(int milliseconds, string owner, ref List<string>? logs)
    {
        if (milliseconds >= 1 && milliseconds <= MaxLeaseMilliseconds)
            return milliseconds;

        var clamped = milliseconds < 1 ? 1 : MaxLeaseMilliseconds;

        // 🔴 Renew 是在鎖內呼叫這支的 ⇒ 這裡不能直接寫 log
        //    （Serilog 有自己的鎖，在 Gate 裡面做 I/O 會把死鎖面積擴到別人的元件上），
        //    只能收進 logs，由呼叫端出了鎖再 Flush。
        //    ⚠️ ReportGate 是另一把鎖，而且沒有任何路徑是 ReportGate → Gate，不會死鎖。
        bool first;
        lock (ReportGate)
            first = Reported.Add("duration:" + owner);

        if (!first)
            return clamped;

        (logs ??= []).Add(
            $"[PluginEnabledLease] 「{owner}」要求的租期 {milliseconds} 毫秒超出範圍，已夾成 {clamped} 毫秒" +
            $"（上限 {MaxLeaseMilliseconds} 毫秒）。要壓住更久必須自己每 {RenewIntervalHintMs} 毫秒續約一次，" +
            "不要假設拿到了要求的時長。這行訊息對同一個租用者只會出現一次。");
        return clamped;
    }

    /// <summary>清掉已經到期的租約。<b>呼叫端必須先持有 <see cref="Gate"/>。</b></summary>
    /// <remarks>
    /// 🔴 逾時訊息<b>不在這裡寫出去</b>，只收進 <paramref name="logs"/>：這支一定在鎖內被呼叫，
    /// 而鎖內做 I/O（Serilog 有自己的鎖）會把死鎖面積擴大到別人的元件上。
    /// 呼叫端出了鎖再 <see cref="Flush"/>。
    /// </remarks>
    private static void SweepLocked(ref List<string>? logs)
    {
        if (Leases.Count == 0)
        {
            _anyLeases = false;
            _anySuppressing = false;
            return;
        }

        var now = Environment.TickCount64;
        List<Guid>? expired = null;

        foreach (var (id, lease) in Leases)
        {
            if (now >= lease.ExpiresAt)
                (expired ??= []).Add(id);
        }

        if (expired != null)
        {
            foreach (var id in expired)
            {
                var lease = Leases[id];
                Leases.Remove(id);

                // 🔴 寫 Information：使用者的記錄等級收得到。租約逾時＝「有人壓著 AutoHook 卻沒放開」，
                //    這一行是使用者回報「AutoHook 突然不動了／突然又會動了」時唯一的線索。
                (logs ??= []).Add(
                    $"[PluginEnabledLease] 「{lease.Owner}」的暫停租約 {id} 已逾時，自動放開" +
                    $"（押著的值：{FormatBool(lease.Enabled)}）。" +
                    "租用者沒有續約，可能已經當掉或被卸載 —— AutoHook 恢復使用者自己的設定。");
            }
        }

        RecomputeFlagsLocked();
    }

    /// <summary>重算兩個不上鎖快路旗標。<b>呼叫端必須先持有 <see cref="Gate"/>。</b></summary>
    private static void RecomputeFlagsLocked()
    {
        _anyLeases = Leases.Count != 0;
        _anySuppressing = Leases.Values.Any(x => x.Enabled == false);
    }

    /// <summary>把收在鎖內的訊息寫出去。<b>一定要在鎖外呼叫。</b></summary>
    private static void Flush(List<string>? logs)
    {
        if (logs == null)
            return;

        foreach (var line in logs)
            Service.PluginLog.Information(line);
    }

    private static string FormatBool(bool? v) => v is null || v.Value ? "不壓制" : "停手";

    /// <summary>目前持有者名單（去重）。<b>呼叫端必須先持有 <see cref="Gate"/>。</b></summary>
    private static string DistinctOwnersLocked()
    {
        if (Leases.Count == 0)
            return "(無)";

        return string.Join("、", Leases.Values.Select(x => x.Owner).Distinct(StringComparer.Ordinal));
    }
}
