using System;
using AutoHook.Classes;
using AutoHook.Configurations;
using System.Collections.Generic;
using System.Linq;
using AutoHook.SeFunctions;
using AutoHook.Utils;
using System.Threading;
using System.Threading.Tasks;
using ECommons.EzIpcManager;

namespace AutoHook.IPC;

public class AutoHookIPC
{
    private Configuration _cfg = Service.Configuration;

    /// <summary>
    /// 匿名 preset／資料夾的名稱前綴。<see cref="DeleteAllAnonymousPresets"/> 靠它辨識該清掉誰，
    /// 所以**凡是由 IPC 建出來的東西都必須帶上它**，否則會永遠留在使用者的設定檔裡。
    /// </summary>
    private const string AnonPrefix = "anon_";

    /// <summary>
    /// <see cref="CreateAndSelectAnonymousFolder"/> 的合約版本，給呼叫端做能力探測用
    /// （形狀比照 AutoRetainer 的 <c>GetRetainerItemRetrieveApiVersion</c>）。
    /// <br/>🔴 <b>改變回傳語意或參數時一定要加號</b>——呼叫端是拿它決定「這個功能能不能用」的。
    /// </summary>
    private const int FolderImportApiVersion = 1;

    public AutoHookIPC()
    {
        EzIPC.Init(this, "AutoHook");
    }

    /// <summary>
    /// 資料夾匯入 IPC 的合約版本。**沒有這個 IPC 的舊版 AutoHook**：呼叫端帶
    /// <c>SafeWrapper.AnyException</c> 時例外會被吞掉並回傳 <c>default(int)</c> ＝ <b>0</b>，
    /// 所以 <b>0 一律代表「這版沒有這個功能」</b>，版本號從 1 起跳刻意不使用 0。
    /// </summary>
    [EzIPC]
    public int GetFolderImportApiVersion() => FolderImportApiVersion;

    /// <summary>
    /// 讀取目前的啟用狀態。與 <see cref="SetPluginState"/> 讀寫同一個欄位（<c>Configuration.PluginEnabled</c>）。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>回傳型別必須維持 <c>bool</c></b>——消費端（Questionable 的 <c>External/AutoHookIpc.cs</c>）
    /// 宣告的是 <c>Func&lt;bool&gt;</c>。端點型別對不上時 Dalamud 擲的是 <c>IpcTypeMismatchError</c>，
    /// 而消費端常帶的 <c>SafeWrapper.IPCException</c> <b>只攔 <c>IpcNotReadyError</c></b>，攔不住型別不合，
    /// 會變成每次呼叫都擲例外。要改形狀請<b>開新端點名</b>，不要同名改型別。
    /// <br/>⚠️ 這個端點在本檔是<b>後補</b>的：呼叫端早就宣告了訂閱，缺它時對方的
    /// <c>IsAvailable()</c> 探測會被 SafeWrapper 吞成「可用」，於是任務鏈卡在等釣魚。
    /// </remarks>
    [EzIPC]
    public bool GetPluginState() => _cfg.PluginEnabled;

    /// <summary>
    /// 借走使用者的「啟用 AutoHook」開關：<b>只改執行期的值，不寫進使用者的設定檔</b>。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>改動前這裡是「改欄位 ＋ 立刻 <c>Service.Save()</c>」</b>，而三個借用端
    /// （Questionable／GBR／ICE）各自拍快照、各自還原，互相覆蓋之後留在磁碟上的是
    /// <b>最後一個還原者的快照</b>，不是使用者本來的選擇——全程零訊息。
    /// 完整序列與修法見 <see cref="IpcConfigOverrides"/>。
    /// <br/>⚠️ <b>讀取端與呼叫端一行都不必改</b>：改的仍然是同一個欄位，只是存檔時會換回
    /// 使用者自己的值。行為差異只有一個——<b>借來的值不再留到下次開遊戲</b>。
    /// <br/>📌 真的需要「永久寫進設定檔」的舊語意，請改用
    /// <see cref="SetPluginStatePersistent"/>（<b>新名字</b>，不是同名改語意：
    /// 端點不存在時呼叫端攔得住 <c>IpcNotReadyError</c>，會乾淨落回 fail-safe）。
    /// </remarks>
    [EzIPC]
    public void SetPluginState(bool state)
    {
        
        IpcConfigOverrides.Set(ref _cfg.PluginEnabled, state, IpcConfigOverrides.PluginEnabledKey);
    }

    /// <summary>
    /// 寫入使用者的「啟用 AutoHook」開關並<b>存進設定檔</b>（<see cref="SetPluginState"/> 的舊語意）。
    /// </summary>
    /// <remarks>
    /// 📌 存在的理由：SomethingNeedDoing 的 Lua 巨集可以呼叫
    /// <c>IPC.AutoHook.SetPluginState(true)</c>，而使用者寫巨集時可能就是要它留到下次開遊戲。
    /// <see cref="SetPluginState"/> 改成不持久之後，那個語意需要一個逃生口。
    /// <br/>🔴 <b>刻意用新名字，不是同名改語意</b>：舊名字改語意的話呼叫端完全看不出差別；
    /// 新名字在舊版 AutoHook 上不存在，呼叫端會收到 <c>IpcNotReadyError</c> 並落回 fail-safe。
    /// <br/>⚠️ <b>不要拿它做「借用」</b>——借用端請一律用 <see cref="SetPluginState"/>，
    /// 否則又會回到「互相覆蓋、把別人的快照寫死在使用者設定檔裡」的老問題。
    /// <br/>⚠️ 目前這支<b>還沒有任何呼叫端</b>：SND 的 Lua 橋接只暴露它
    /// <c>External/AutoHook.cs</c> 裡宣告過的成員，要讓巨集用得到需要在 SND 那邊補一行宣告。
    /// </remarks>
    [EzIPC]
    public void SetPluginStatePersistent(bool state)
        => ForwardToFramework(nameof(SetPluginStatePersistent), () => SetPluginStatePersistentCore(state));

    /// <summary>
    /// <see cref="SetPluginStatePersistent"/> 的實體。<b>只在 Framework 執行緒上呼叫</b>
    /// （<c>Service.Save()</c> 會把整份設定序列化一次，等於從頭到尾走訪每一個 <c>List</c>）。
    /// </summary>
    private void SetPluginStatePersistentCore(bool state)
    {
        // 明確要求持久化 ⇒ 這就是新的權威值，丟掉任何借用中的覆寫。
        IpcConfigOverrides.Clear(IpcConfigOverrides.PluginEnabledKey);
        _cfg.PluginEnabled = state;
        Service.Save();
    }

    /// <summary>
    /// 借走「啟用自動魚叉」開關：<b>只改執行期的值，不寫進使用者的設定檔</b>。
    /// </summary>
    /// <remarks>與 <see cref="SetPluginState"/> 完全同一個形狀與理由。</remarks>
    [EzIPC]
    public void SetAutoGigState(bool state)
    {
        IpcConfigOverrides.Set(ref _cfg.AutoGigConfig.AutoGigEnabled, state,
            IpcConfigOverrides.AutoGigEnabledKey);
    }

    /// <summary>
    /// 讀取「沒在釣魚時自動拋竿」的開關（<see cref="Configuration.AutoStartFishing"/>）。
    /// </summary>
    /// <remarks>
    /// ⚠️ 這是<b>後補</b>的端點：消費端（Questionable 的 <c>External/AutoHookIpc.cs</c>）
    /// 早就宣告了 <c>Func&lt;bool&gt;</c> 訂閱，而本 fork 分岔自上游 2025-05-08，
    /// 這個設定與它的自動拋竿路徑當時還不存在 ⇒ 呼叫下去只會被吞掉、每次回 <c>false</c>。
    /// <br/>🔴 回傳型別必須維持 <c>bool</c>，理由同 <see cref="GetPluginState"/>。
    /// <br/>📌 這裡回的是<b>使用者自己的值</b>（欄位本身），與 <see cref="GetPluginState"/> 一致：
    /// 呼叫端拿它當「事後要還原成什麼」的快照，回別人覆寫過的值會讓它把別人的值還給使用者。
    /// </remarks>
    [EzIPC]
    public bool GetAutoStartFishing() => _cfg.AutoStartFishing;

    /// <summary>
    /// 借走「沒在釣魚時自動拋竿」的開關：<b>只改執行期的值，不寫進使用者的設定檔</b>。
    /// </summary>
    /// <remarks>與 <see cref="SetPluginState"/> 完全同一個形狀與理由（見 <see cref="IpcConfigOverrides"/>）。</remarks>
    [EzIPC]
    public void SetAutoStartFishing(bool state)
    {
        IpcConfigOverrides.Set(ref _cfg.AutoStartFishing, state, IpcConfigOverrides.AutoStartFishingKey);
    }

    [EzIPC]
    public void SetPreset(string preset)
        => ForwardToFramework(nameof(SetPreset), () => SetPresetCore(preset));

    /// <summary><see cref="SetPreset"/> 的實體。<b>只在 Framework 執行緒上呼叫。</b></summary>
    private void SetPresetCore(string preset)
    {
        Service.Save();
        _cfg.HookPresets.SelectedPreset =
            _cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == preset);
        Service.Save();
    }

    // 這裡原本漏了 [EzIPC]：夾在 SetPreset 與 CreateAndSelectAnonymousPreset 兩個有屬性的方法中間，
    // 自己卻沒有掛，所以 AutoHook.SetPresetAutogig 從來沒被註冊過。
    // 消費端（ICE 的 IPC/AutoHookIPC.cs）早就宣告了訂閱，帶的又是 SafeWrapper.AnyException，
    // 呼叫下去只會被吞掉、回傳 default —— 完全靜默。
    // 它只做「在既有的魚叉 preset 清單裡挑一個」，沒有任何危險行為，補上屬性即可。
    [EzIPC]
    public void SetPresetAutogig(string preset)
        => ForwardToFramework(nameof(SetPresetAutogig), () => SetPresetAutogigCore(preset));

    /// <summary><see cref="SetPresetAutogig"/> 的實體。<b>只在 Framework 執行緒上呼叫。</b></summary>
    private void SetPresetAutogigCore(string preset)
    {
        Service.Save();
        _cfg.AutoGigConfig.SelectedPreset =
            _cfg.AutoGigConfig.Presets.FirstOrDefault(x => x.PresetName == preset);
        Service.Save();
    }

    [EzIPC]
    public void CreateAndSelectAnonymousPreset(string preset)
        => ForwardToFramework(nameof(CreateAndSelectAnonymousPreset),
            () => CreateAndSelectAnonymousPresetCore(preset));

    /// <summary><see cref="CreateAndSelectAnonymousPreset"/> 的實體。<b>只在 Framework 執行緒上呼叫。</b></summary>
    private void CreateAndSelectAnonymousPresetCore(string preset)
    {
        var _import = Configuration.ImportPreset(preset);
        if (_import == null) return;
        var name = $"anon_{_import.PresetName}";
        _import.RenamePreset(name);
        Service.Save();
        _cfg.HookPresets.AddNewPreset(_import);
        _cfg.HookPresets.SelectedPreset =
            _cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == name);
        Service.Save();
    }

    /// <summary>
    /// 匯入一整包 <c>AHFOLDER_</c>／<c>AHFOLDER2_</c> 資料夾匯出，全部掛成匿名 preset，
    /// 並選取<b>第一筆</b>當進入點。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>這不是「把資料夾裡的 preset 逐筆丟給 <see cref="CreateAndSelectAnonymousPreset"/>」。</b>
    /// 那個函式每一筆都會重設 <c>SelectedPreset</c>，迴圈送 N 筆的結果是<b>只有最後一筆生效</b>。
    /// 資料夾裡的多筆 preset 是一台<b>狀態機</b>：彼此用 <c>PresetToSwap</c>（<b>比對名稱字串</b>，
    /// 見 <c>FishingManager.FishCaught.CheckFishCaughtSwap</c>）互指，所以正確語意是
    /// 「全部裝進去、只選進入點，之後由 AutoHook 自己換」。
    /// <br/><br/>
    /// 🔴 <b>改名與轉指必須成對做。</b>匿名 preset 一律加 <see cref="AnonPrefix"/> 前綴，
    /// 否則 <see cref="DeleteAllAnonymousPresets"/> 清不掉、每接一次任務就多留一份；
    /// 但 <c>PresetToSwap</c> 存的是<b>改名前</b>的字串，只改名不轉指的話每次換 preset 都會
    /// 變成「Preset X not found」。所以這裡先轉指、再改名。
    /// <br/><br/>
    /// ⚠️ 指向資料夾<b>以外</b>的名稱一律不動——那可能是使用者自己既有的 preset。
    /// <br/><br/>
    /// ⚠️ preset 是用 <c>CustomPresets.Add</c> 直接掛上去的，<b>不走
    /// <see cref="Fishing.FishingPresets.AddNewPreset(BasePresetConfig)"/></b>：後者會做一次
    /// JSON 深拷貝並<b>重新配發 <c>UniqueId</c></b>，那會讓 <c>ImportFolder</c> 收好的
    /// <c>PresetFolder.PresetIds</c> 全部對不上。UI 的匯入路徑（<c>TabFishingPresets</c>）
    /// 也是直接 Add，作法一致。
    /// </remarks>
    /// <returns>
    /// 實際掛上去的 preset 名稱（改名後），順序同資料夾；<b>第一筆就是被選取的那筆</b>。
    /// 匯入失敗（字串不是資料夾格式／解不開／空資料夾）回傳<b>空清單</b>。
    /// <br/>📌 呼叫端若收到 <c>null</c>，那不是這個函式回的——代表這版 AutoHook 根本沒有這個
    /// IPC，例外被呼叫端的 SafeWrapper 吞掉了。兩者要分開處理。
    /// </returns>
    [EzIPC]
    public List<string> CreateAndSelectAnonymousFolder(string folderExport)
        => ForwardToFramework(
            nameof(CreateAndSelectAnonymousFolder),
            () => CreateAndSelectAnonymousFolderCore(folderExport),
            new List<string>());

    /// <summary>
    /// <see cref="CreateAndSelectAnonymousFolder"/> 的實體。<b>只在 Framework 執行緒上呼叫。</b>
    /// </summary>
    /// <remarks>
    /// 📌 轉送逾時的回傳值刻意用<b>空清單</b>，與「匯入失敗」同一個值——呼叫端本來就要處理它，
    /// 不必為了逾時再多認一種回傳。
    /// </remarks>
    private List<string> CreateAndSelectAnonymousFolderCore(string folderExport)
    {
        var empty = new List<string>();

        var imported = Configuration.ImportFolder(folderExport);
        if (imported == null)
        {
            // 使用者回報用，寫 Information（使用者跑 LogLevel 1，Debug 收得到但單檔數十萬行會淹沒）。
            Service.PluginLog.Information(
                "[IPC] CreateAndSelectAnonymousFolder：傳進來的字串不是可用的資料夾匯出" +
                "（需要 AHFOLDER_ 或 AHFOLDER2_ 前綴），沒有匯入任何 preset。");
            return empty;
        }

        var folder = imported.Value.Folder;
        var presets = imported.Value.Presets;

        if (presets.Count == 0)
        {
            Service.PluginLog.Information(
                $"[IPC] CreateAndSelectAnonymousFolder：資料夾「{folder.FolderName}」解得開，但裡面沒有任何 preset。");
            return empty;
        }

        // ① 先建立「原名 → 匿名」對照表（改名前）。
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var preset in presets)
        {
            if (preset.PresetName != null)
                renames[preset.PresetName] = AnonPrefix + preset.PresetName;
        }

        // ② 先轉指、再改名。順序反過來的話對照表就查不到原名了。
        foreach (var preset in presets)
        {
            RemapSwapTargets(preset, renames);

            if (preset.PresetName != null && renames.TryGetValue(preset.PresetName, out var anonName))
                preset.PresetName = anonName;
        }

        // ③ 掛上去。folder.PresetIds 是 ImportFolder 依這些 UniqueId 收好的，這裡不能重配發。
        foreach (var preset in presets)
            _cfg.HookPresets.CustomPresets.Add(preset);

        folder.FolderName = AnonPrefix + folder.FolderName;
        _cfg.HookPresets.Folders.Add(folder);

        // ④ 選取進入點。必須在 Add 之後——SelectedPreset 的 getter 是去 PresetList 裡查 GUID 的。
        _cfg.HookPresets.SelectedPreset = presets[0];
        Service.Save();

        var names = presets.Select(p => p.PresetName).ToList();

        // 進入點判定的旁證：正常的資料夾狀態機裡，進入點是「沒有任何人指向它」的那一筆。
        // 這裡不拿它去改選擇（順序才是上游的意圖），只在對不上時把事實寫進 log，
        // 免得日後換了一份 preset 順序不同的資料夾時，症狀變成「就是不動」而查不出所以然。
        var pointedAt = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in presets)
            foreach (var target in EnumerateSwapTargets(preset))
                if (target != null)
                    pointedAt.Add(target);

        if (pointedAt.Contains(names[0]))
        {
            Service.PluginLog.Information(
                $"[IPC] 資料夾「{folder.FolderName}」選取的進入點「{names[0]}」被其他 preset 指向，" +
                "代表它可能不是這台狀態機的起點（本函式一律選第一筆）。若釣魚行為不如預期，這是第一個該看的地方。");
        }

        Service.PluginLog.Information(
            $"[IPC] 資料夾「{folder.FolderName}」已匯入 {names.Count} 個匿名 preset，選取「{names[0]}」為進入點。" +
            $"（其餘 {names.Count - 1} 筆等 PresetToSwap 條件成立時才會換過去。）");

        return names;
    }

    /// <summary>
    /// 把 preset 裡所有指向<b>同一個資料夾內</b>其他 preset 的名稱換成改名後的新名。
    /// 查不到的目標（指向資料夾外、或就是預設值 <c>"-"</c>）原樣保留。
    /// </summary>
    private static void RemapSwapTargets(CustomPresetConfig preset, Dictionary<string, string> renames)
    {
        if (preset.ListOfFish != null)
        {
            foreach (var fish in preset.ListOfFish)
            {
                if (fish != null)
                    fish.PresetToSwap = Remap(fish.PresetToSwap, renames);
            }
        }

        var extra = preset.ExtraCfg;
        if (extra != null)
        {
            extra.PresetToSwapIntuitionGain = Remap(extra.PresetToSwapIntuitionGain, renames);
            extra.PresetToSwapIntuitionLost = Remap(extra.PresetToSwapIntuitionLost, renames);
            extra.PresetToSwapSpectralCurrentGain = Remap(extra.PresetToSwapSpectralCurrentGain, renames);
            extra.PresetToSwapSpectralCurrentLost = Remap(extra.PresetToSwapSpectralCurrentLost, renames);
            extra.PresetToSwapAnglersArt = Remap(extra.PresetToSwapAnglersArt, renames);
        }
    }

    /// <summary>列出 preset 裡所有的換 preset 目標名稱（僅供診斷用，不改任何狀態）。</summary>
    private static IEnumerable<string?> EnumerateSwapTargets(CustomPresetConfig preset)
    {
        if (preset.ListOfFish != null)
        {
            foreach (var fish in preset.ListOfFish)
            {
                if (fish != null)
                    yield return fish.PresetToSwap;
            }
        }

        var extra = preset.ExtraCfg;
        if (extra == null)
            yield break;

        yield return extra.PresetToSwapIntuitionGain;
        yield return extra.PresetToSwapIntuitionLost;
        yield return extra.PresetToSwapSpectralCurrentGain;
        yield return extra.PresetToSwapSpectralCurrentLost;
        yield return extra.PresetToSwapAnglersArt;
    }

    private static string Remap(string? target, Dictionary<string, string> renames)
    {
        if (target == null)
            return "-";

        return renames.TryGetValue(target, out var renamed) ? renamed : target;
    }

    [EzIPC]
    public void ImportAndSelectPreset(string preset)
        => ForwardToFramework(nameof(ImportAndSelectPreset), () => ImportAndSelectPresetCore(preset));

    /// <summary><see cref="ImportAndSelectPreset"/> 的實體。<b>只在 Framework 執行緒上呼叫。</b></summary>
    private void ImportAndSelectPresetCore(string preset)
    {
        var _import = Configuration.ImportPreset(preset);
        if (_import == null) return;
        var name = $"{_import.PresetName}";
        _import.RenamePreset(name);
        
        if (_import is CustomPresetConfig customPreset)
            _cfg.HookPresets.AddNewPreset(customPreset);
        else if (_import is AutoGigConfig gigPreset)
            _cfg.AutoGigConfig.AddNewPreset(gigPreset);

        Service.Save();
    }

    [EzIPC]
    public void DeleteSelectedPreset()
        => ForwardToFramework(nameof(DeleteSelectedPreset), DeleteSelectedPresetCore);

    /// <summary><see cref="DeleteSelectedPreset"/> 的實體。<b>只在 Framework 執行緒上呼叫。</b></summary>
    private void DeleteSelectedPresetCore()
    {
        var selected = _cfg.HookPresets.SelectedPreset;
        if (selected == null) return;
        _cfg.HookPresets.RemovePreset(selected.UniqueId);
        _cfg.HookPresets.SelectedPreset = null;
        Service.Save();
    }

    [EzIPC]
    public void DeleteAllAnonymousPresets()
        => ForwardToFramework(nameof(DeleteAllAnonymousPresets), DeleteAllAnonymousPresetsCore);

    /// <summary><see cref="DeleteAllAnonymousPresets"/> 的實體。<b>只在 Framework 執行緒上呼叫。</b></summary>
    private void DeleteAllAnonymousPresetsCore()
    {
        _cfg.HookPresets.CustomPresets.RemoveAll(p => p.PresetName.StartsWith(AnonPrefix));

        // 🔴 資料夾也要收。<see cref="CreateAndSelectAnonymousFolder"/> 會建一個 anon_ 前綴的資料夾，
        //    只刪 preset 的話那個（已經空掉的）資料夾會永遠留在使用者的設定檔裡，
        //    而 ICE 是**每接一次釣魚任務就呼叫一次**匯入 —— 那是會累積的。
        //    ⚠️ 只刪 anon_ 前綴的：使用者自己建的資料夾一個都不能碰。
        _cfg.HookPresets.Folders.RemoveAll(f => f.FolderName != null && f.FolderName.StartsWith(AnonPrefix));

        Service.Save();
    }

    /// <summary>
    /// 換餌（依道具 ID）。<b>已經掛著同一個餌時也算成功</b>——呼叫端要的是
    /// 「餌對了沒」，不是「這一次有沒有真的送出換餌指令」。
    /// </summary>
    /// <remarks>
    /// 🔴🔴 <b>只在 Framework 執行緒上做事，不在的話直接回 <see langword="false"/>。</b>
    /// IPC 端點跑在<b>呼叫端的執行緒</b>上（沒有任何「一定在 Framework 執行緒」的保證），
    /// 而 <c>BaitManager.ChangeBait</c> 會呼叫遊戲的原生函式，且沿路裸解參考
    /// <c>EventFramework</c>／<c>PlayerState</c>／<c>WKSManager</c> 的實例指標。
    /// 那些指標在遊戲自己的執行緒上隨時會變，跨執行緒踩下去是 AccessViolationException
    /// ——它是 corrupted-state exception，<c>try/catch</c> 完全攔不到，直接把遊戲帶走。
    /// <br/>🔑 刻意<b>不</b>用 <c>RunOnFrameworkThread(...).GetAwaiter().GetResult()</c> 阻塞等下一幀：
    /// 那在「Framework 執行緒正好在等這個呼叫端」時是死鎖。回 <see langword="false"/> 是安全方向
    /// （呼叫端的合約本來就允許失敗），而且會留下一行 Information 說明為什麼。
    /// </remarks>
    /// <returns>餌已經是（或已成功要求換成）<paramref name="baitId"/>。</returns>
    [EzIPC]
    public bool SwapBaitById(uint baitId)
    {
        if (!EnsureFrameworkThread(nameof(SwapBaitById)))
            return false;

        return Service.BaitManager.ChangeBait(baitId)
            is BaitManager.ChangeBaitReturn.Success or BaitManager.ChangeBaitReturn.AlreadyEquipped;
    }

    /// <summary>換餌（依名稱或道具 ID 字串）。</summary>
    /// <remarks>
    /// ⚠️ 名稱比對用 <c>OrdinalIgnoreCase</c>，而 <c>BaitFishClass.Name</c> 是從遊戲資料表
    /// 拿的<b>當前語言</b>名稱 ⇒ 台服上要傳繁中名字才會命中。呼叫端手上有 ID 的話，
    /// <b>傳 ID 字串或直接用 <see cref="SwapBaitById"/> 才是可靠的</b>。
    /// <br/>📌 與上游一致：先試著把字串當成 <c>uint</c> ID 解，解得開就走 ID 路徑。
    /// </remarks>
    [EzIPC]
    public bool SwapBait(string baitNameOrId)
    {
        if (string.IsNullOrWhiteSpace(baitNameOrId))
            return false;

        if (uint.TryParse(baitNameOrId, out var parsedId))
            return SwapBaitById(parsedId);

        if (!EnsureFrameworkThread(nameof(SwapBait)))
            return false;

        var bait = GameRes.Baits.FirstOrDefault(
            b => string.Equals(b.Name, baitNameOrId, StringComparison.OrdinalIgnoreCase));

        if (bait == null || bait.Id <= 0)
            return false;

        return Service.BaitManager.ChangeBait((uint)bait.Id)
            is BaitManager.ChangeBaitReturn.Success or BaitManager.ChangeBaitReturn.AlreadyEquipped;
    }

    /// <summary>換泳餌槽位（索引 0、1、2）。</summary>
    /// <remarks>
    /// ⚠️ 索引大於 2 時 <c>BaitManager.ChangeSwimbait</c> 會回 <c>InvalidBait</c> ⇒ 本函式回
    /// <see langword="false"/>，不會送任何指令。執行緒閘門的理由同 <see cref="SwapBaitById"/>。
    /// </remarks>
    [EzIPC]
    public bool SwapSwimbaitByIndex(byte index)
    {
        if (!EnsureFrameworkThread(nameof(SwapSwimbaitByIndex)))
            return false;

        return Service.BaitManager.ChangeSwimbait(index)
            is BaitManager.ChangeBaitReturn.Success or BaitManager.ChangeBaitReturn.AlreadyEquipped;
    }

    /// <summary>已經回報過「不在 Framework 執行緒」的端點名。同一支只寫一次。</summary>
    private static readonly HashSet<string> OffThreadReported = new(StringComparer.Ordinal);

    /// <summary>只保護 <see cref="OffThreadReported"/>。</summary>
    private static readonly object OffThreadGate = new();

    /// <summary>
    /// 本次呼叫是不是在 Framework 執行緒上。不是的話寫一行 Information 並回 <see langword="false"/>。
    /// </summary>
    /// <remarks>
    /// 🔴 訊息寫 <c>Information</c>：使用者的記錄等級收得到，而這種失敗完全沒有其他徵兆
    /// （呼叫端只會看到「換餌一直不成功」）。同一支端點只寫一次，不洗版。
    /// <br/>🔴 直接用 <c>Service.PluginLog</c>，<b>不要用 <c>Service.PrintInfo</c></b>——
    /// 後者會塞進 <c>Service.LogMessages</c> 這個沒有同步的 <c>Queue</c>，
    /// 而這裡本來就可能在別的執行緒上（跟 EzThrottler 那條紅線完全同形狀）。
    /// </remarks>
    private static bool EnsureFrameworkThread(string endpoint)
    {
        if (Service.Framework.IsInFrameworkUpdateThread)
            return true;

        bool first;
        lock (OffThreadGate)
            first = OffThreadReported.Add(endpoint);

        if (first)
            Service.PluginLog.Information(
                $"[IPC] {endpoint} 是從 Framework 以外的執行緒呼叫的，已拒絕並回傳 false。" +
                @"換餌會呼叫遊戲的原生函式，跨執行緒踩下去是 try/catch 攔不到的 AccessViolation。" +
                @"請在 Framework 執行緒（例如 IFramework.RunOnFrameworkThread）上呼叫。" +
                @"這行訊息每支端點只會出現一次。");

        return false;
    }

    // -- 暫停租約（見 Configurations/PluginEnabledLeases.cs）-------------------------
    // 🔴 這一組是<b>純新增</b>：上面的 SetPluginState／SetPluginStatePersistent／
    //    SetAutoGigState 一個字都沒改，舊消費端完全不受影響。要改端點的形狀時
    //    正解是「換新名字」而不是「同名改型別」——端點不存在時兩邊都攔得住
    //    IpcNotReadyError 並乾淨落回 fail-safe，而同名改型別會讓舊消費端撞上
    //    IpcTypeMismatchError（SafeWrapper.IPCException 攔不住它）。
    // 🔑 用法：AcquireSuppression 拿一把 Guid 憑證 → SetLeasedPluginState(憑證, false) 壓住 →
    //    每 30 秒 RenewSuppression(憑證) 心跳 → 做完 ReleaseSuppression(憑證)。
    // 🔴 Acquire 本身就開始壓制（與 YesAlready／AutoRetainer 同形狀），
    //    SetLeasedPluginState 是選配。
    //    ⚠️ 租期上限 5 分鐘，續約間隔必須明顯短於租期（建議 30 秒）；
    //       間隔接近租期時第一次心跳必定回 false（那把已經被掃掉了，不是競態）。
    // 🔑 不用記得還：租用者當掉／被卸載／忘了放開，逾時就自動還原成使用者的值，
    //    並在使用者的 log 寫一行 Information 指名是誰。
    // 📌 全部端點回的都是不可為 null 的值型別，失敗回 Guid.Empty / false，永不回 null
    //    （回 null 時 CallGateChannel 對值型別擲的是看起來與 IPC 無關的 NullReferenceException）。

    /// <summary>拿一把預設長度（5 分鐘）的暫停租約。<see cref="Guid.Empty"/>＝沒拿到。</summary>
    /// <param name="owner">租用者名字（慣例是自己的 InternalName）。空白會被拒絕。</param>
    /// <remarks>
    /// 🔴 <b>拿到租約就已經開始壓制</b>（refcount），不需要再呼叫別的端點——
    /// 與 <c>YesAlready.AcquireSuppression</c>／<c>AutoRetainer.AcquireSuppressionFor</c> 同一個形狀。
    /// <see cref="SetLeasedPluginState"/> 是選配（不交回租約但暫時不壓制）。
    /// </remarks>
    [EzIPC]
    public Guid AcquireSuppression(string owner)
        => PluginEnabledLeases.Acquire(owner, PluginEnabledLeases.DefaultLeaseMilliseconds);

    /// <summary>拿一把指定長度的暫停租約（毫秒，夾在 1～300000）。</summary>
    /// <remarks>⚠️ 被夾時會對同一個租用者寫一次 Information，<b>不要假設拿到了要求的時長</b>。</remarks>
    [EzIPC]
    public Guid AcquireSuppressionFor(string owner, int milliseconds)
        => PluginEnabledLeases.Acquire(owner, milliseconds);

    /// <summary>交回一把租約。回 <see langword="false"/>＝這把不存在（已放開或已逾時）。</summary>
    [EzIPC]
    public bool ReleaseSuppression(Guid lease) => PluginEnabledLeases.Release(lease);

    /// <summary>續約（心跳）。回 <see langword="false"/>＝這把已經不在了，<b>必須重新 Acquire</b>。</summary>
    [EzIPC]
    public bool RenewSuppression(Guid lease) => PluginEnabledLeases.Renew(lease);

    /// <summary>續約並換一個時長。</summary>
    [EzIPC]
    public bool RenewSuppressionFor(Guid lease, int milliseconds) => PluginEnabledLeases.Renew(lease, milliseconds);

    /// <summary>
    /// 用這把租約押住啟用開關。<paramref name="enabled"/> 傳 <see langword="false"/>＝
    /// 「我這把要求 AutoHook 停手」；傳 <see langword="true"/>＝「我這把不再要求停手」。
    /// </summary>
    /// <remarks>
    /// 🔴 傳 <see langword="true"/> <b>不是「我要求啟用」</b>——使用者自己在設定頁取消勾選的
    /// 「Enable AutoHook」不會被 IPC 蓋掉。要真的幫使用者打開請用 <see cref="SetPluginState"/>。
    /// </remarks>
    [EzIPC]
    public bool SetLeasedPluginState(Guid lease, bool enabled) => PluginEnabledLeases.SetPluginEnabled(lease, enabled);

    /// <summary>
    /// <b>實際生效</b>的啟用狀態（使用者的值疊上目前的租約）。
    /// 問「AutoHook 現在會不會動」的人要問這支，不是 <see cref="GetPluginState"/>。
    /// </summary>
    /// <remarks>
    /// 🔴 <b>刻意另開新名字而不是改 <see cref="GetPluginState"/> 的語意。</b>
    /// Questionable 的 <c>DoFish</c> 是拿 <c>GetPluginState</c> 當「事後要還原成什麼」的快照
    /// （<c>_wasAutoHookEnabled</c>）；若那支改成回疊加後的值，它在有租約壓著的瞬間拍到 false，
    /// 之後就會把 false 「還」給使用者。這是本檔與 vnavmesh
    /// （那邊 <c>Path.GetMovementAllowed</c> 刻意回疊加後的值）的刻意的差異，<b>不是疏漏</b>。
    /// </remarks>
    [EzIPC]
    public bool GetEffectivePluginState() => _cfg.EffectivePluginEnabled;

    // -- 把「會改動設定」的工作轉送到 Framework 執行緒 ------------------------------

    /// <summary>轉送到 Framework 執行緒時，呼叫端最多等多久（毫秒）。</summary>
    /// <remarks>
    /// 🔴 <b>一定要有逾時。</b>無限等待在「Framework 執行緒正好被這個呼叫端擋住」的情況下就是死結，
    /// 而死結的表現是<b>整個遊戲凍結</b>。寧可讓這一次匯入失敗——呼叫端的合約本來就允許失敗。
    /// </remarks>
    private const int ForwardTimeoutMs = 5000;

    /// <summary>
    /// 把「會改動設定」的工作轉送到 Framework 執行緒上執行，並<b>同步等它做完</b>再回傳。
    /// </summary>
    /// <remarks>
    /// 🔴🔴 <b>為什麼需要它</b>：<c>[EzIPC]</c> 端點跑在<b>呼叫端外掛的執行緒</b>上，
    /// 沒有任何「一定在 Framework 執行緒」的保證。而
    /// <c>Configuration.HookPresets.CustomPresets</c>／<c>Folders</c>／
    /// <c>SpearFishingPresets.Presets</c> 都是裸的 <c>List&lt;T&gt;</c>（零同步），同時被三種執行緒碰：
    /// <list type="bullet">
    /// <item>繪製執行緒——<c>TabFishingPresets</c>／<c>SubTabFish</c>／<c>SubTabExtra</c> 每幀迭代它們；</item>
    /// <item>Framework 執行緒與遊戲自己的執行緒——<c>FishingManager</c> 的換 preset 判斷每幀在查；</item>
    /// <item><b>呼叫端的執行緒</b>——就是本檔這些端點。</item>
    /// </list>
    /// 並行 <c>Add</c>／<c>RemoveAll</c> 時迭代端擲的是 <c>InvalidOperationException</c>，
    /// resize 途中讀到的是撕裂的內容——<b>失敗形式不是「少一筆」而是清單本身壞掉</b>，
    /// 跟 <c>ECommons.Throttlers.EzThrottler</c>／裸 <c>Dictionary</c> 那條紅線完全同形狀。
    /// <br/>🔴 <c>Service.Save()</c> 也算在內：它是<b>整份設定序列化一次</b>，
    /// 等於從頭到尾走訪每一個 <c>List</c>——並行改動時 <c>foreach</c> 會擲例外，
    /// 而那個例外常被吞成「存檔失敗」，使用者的設定就這樣沒存到。
    /// <para>
    /// 🔑 <b>這裡刻意不用鎖。</b>要保護的清單散在整個外掛（UI、FishingManager、序列化），
    /// 加鎖等於要在每一個讀取點都上鎖，而其中一部分讀取點就在 ImGui 繪製迴圈裡
    /// ——鎖內呼叫 ImGui 是另一條紅線。<b>把寫入端搬到既有的讀取執行緒上</b>才是零改動的解。
    /// </para>
    /// <para>
    /// 📌 <b>已經在 Framework 執行緒上就直接做</b>，語意與改動前逐字相同。這一條涵蓋的比想像中多：
    /// Dalamud 的繪製（<c>Present</c> hook）與 <c>Framework::Update</c> 是<b>同一條遊戲主執行緒</b>
    /// （<c>Framework.HandleFrameworkUpdate</c> 把 <c>BoundThread</c> 綁成當下這條，
    /// 而 <c>IsInFrameworkUpdateThread</c> 就是 <c>Thread.CurrentThread == BoundThread</c>），
    /// 所以從別的外掛的 UI 或它的 <c>Framework.Update</c> 處理器裡呼叫進來的，一律走就地執行這條。
    /// </para>
    /// <para>
    /// 🔴 <b>死結面已經逐行查過本 pin 的碼</b>：
    /// <c>Dalamud/Game/Framework.cs:167-168</c> 是
    /// <c>IsInFrameworkUpdateThread || IsFrameworkUnloading ? Task.FromResult(func()) : RunOnTick(func)</c>
    /// ——<b><c>RunOnFrameworkThread</c> 自己就會就地執行</b>，無條件排隊的是 <c>RunOnTick</c>（<c>:200-224</c>）。
    /// 所以「在 Framework 執行緒上同步等 <c>RunOnFrameworkThread</c>」不會自我死結。
    /// 上面那個 <c>IsInFrameworkUpdateThread</c> 檢查是<b>刻意的重複</b>：它讓「就地執行」
    /// 不依賴 Dalamud 內部的實作細節，日後那一行改掉也不會變成凍結遊戲。
    /// </para>
    /// <para>
    /// 🟢 <b>逾時之後那份工作會被取消</b>：工作開頭有一道取消閘門（<c>claim</c> 的
    /// <c>Interlocked.CompareExchange</c>），逾時那一方先拿到取消權的話，
    /// 排在佇列裡的 lambda 之後跑到時會直接返回，<b>一行都不會執行</b>。
    /// <br/>🔑 <b>為什麼不是「先看旗標再跑」而是 CAS</b>：前者在「檢查」與「執行」之間有空隙，
    /// 逾時剛好落在那個空隙時兩邊會同時認為自己贏了 —— 呼叫端以為已取消而重試，工作卻照樣跑完，
    /// 於是匯入兩份。CAS 把「宣告開跑」與「檢查有沒有被取消」合成同一個不可分割的動作。
    /// <br/>⚠️ <b>已經開跑的工作取消不掉</b>（任何取消機制都一樣）。這兩種結尾在逾時訊息裡
    /// <b>分開寫</b>：取消成功時明說「直接重試是安全的」，取消不掉時明說「重試有可能做兩次」。
    /// </para>
    /// </remarks>
    /// <param name="endpoint">端點名，只用在逾時訊息上。</param>
    /// <param name="work">實際要做的事。<b>整段都會在 Framework 執行緒上跑。</b></param>
    /// <param name="onTimeout">逾時或工作被取消時要回傳的值。</param>
    private static T ForwardToFramework<T>(string endpoint, Func<T> work, T onTimeout)
    {
        if (Service.Framework.IsInFrameworkUpdateThread)
            return work();

        // 取消旗標：0＝還沒開始、1＝已經在 Framework 執行緒上開跑（來不及取消）、
        // 2＝逾時那一方先拿到取消權（工作永遠不會跑）。
        var claim = 0;

        var task = Service.Framework.RunOnFrameworkThread(() =>
        {
            // 🔴 工作開頭的取消檢查。CAS 把「宣告開跑」與「檢查有沒有被取消」變成同一個
            //    不可分割的動作 —— 這就是「工作開頭檢查取消旗標」的無競態版本。
            //    刻意不另外帶 CancellationTokenSource：token 單獨使用時，「看旗標」與「開跑」
            //    之間仍有空隙，而且排在佇列裡的 lambda 可能很久之後才跑，沒有安全的 Dispose 時點。
            if (Interlocked.CompareExchange(ref claim, 1, 0) != 0)
                return onTimeout;

            return work();
        });

        // 🔴 刻意用 Task.WaitAny 而不是 task.Wait(逾時)：後者在工作擲例外時會就地把例外包成
        //    AggregateException 丟出來，呼叫端看到的例外型別就跟改動前不一樣了。
        //    WaitAny 只等「完成」、不看結果，例外統一交給下面的 GetAwaiter().GetResult() 原樣重擲。
        if (Task.WaitAny(new Task[] { task }, ForwardTimeoutMs) < 0)
        {
            // 逾時：先搶下取消權。搶到＝工作一行都還沒跑、之後也永遠不會跑（重試安全）；
            // 搶不到＝它已經在跑了，取消不掉（重試有可能做兩次）。兩種結尾必須分開告訴使用者。
            var cancelled = Interlocked.CompareExchange(ref claim, 2, 0) == 0;

            Service.PluginLog.Information(
                $"[IPC] {endpoint}：等 Framework 執行緒超過 {ForwardTimeoutMs} 毫秒，這一次放棄。" +
                @"通常代表遊戲主執行緒正被擋住；請把呼叫改到 Framework 執行緒上做。" +
                (cancelled
                    ? @"這份工作已經取消，一行都沒有跑到，直接重試是安全的。"
                    : @"這份工作已經開始跑了，取消不掉，之後仍會跑完 —— 直接重試有可能做兩次。"));
            return onTimeout;
        }

        if (task.IsCanceled)
        {
            Service.PluginLog.Information(
                $"[IPC] {endpoint}：轉送到 Framework 執行緒的工作被取消（通常是外掛正在卸載），這一次什麼都沒做。");
            return onTimeout;
        }

        // 例外原樣往上拋給呼叫端（不包成 AggregateException），與改動前逐字相同。
        return task.GetAwaiter().GetResult();
    }

    /// <summary>回傳型別是 <c>void</c> 的版本。逾時／取消時什麼都不做。</summary>
    private static void ForwardToFramework(string endpoint, Action work)
        => ForwardToFramework(endpoint, () =>
        {
            work();
            return true;
        }, false);
}