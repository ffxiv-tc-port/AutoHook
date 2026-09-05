using System;
using AutoHook.Classes;
using AutoHook.Configurations;
using System.Collections.Generic;
using System.Linq;
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

    [EzIPC]
    public void SetPreset(string preset)
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
    {
        Service.Save();
        _cfg.AutoGigConfig.SelectedPreset =
            _cfg.AutoGigConfig.Presets.FirstOrDefault(x => x.PresetName == preset);
        Service.Save();
    }

    [EzIPC]
    public void CreateAndSelectAnonymousPreset(string preset)
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
    {
        var selected = _cfg.HookPresets.SelectedPreset;
        if (selected == null) return;
        _cfg.HookPresets.RemovePreset(selected.UniqueId);
        _cfg.HookPresets.SelectedPreset = null;
        Service.Save();
    }

    [EzIPC]
    public void DeleteAllAnonymousPresets()
    {
        _cfg.HookPresets.CustomPresets.RemoveAll(p => p.PresetName.StartsWith(AnonPrefix));

        // 🔴 資料夾也要收。<see cref="CreateAndSelectAnonymousFolder"/> 會建一個 anon_ 前綴的資料夾，
        //    只刪 preset 的話那個（已經空掉的）資料夾會永遠留在使用者的設定檔裡，
        //    而 ICE 是**每接一次釣魚任務就呼叫一次**匯入 —— 那是會累積的。
        //    ⚠️ 只刪 anon_ 前綴的：使用者自己建的資料夾一個都不能碰。
        _cfg.HookPresets.Folders.RemoveAll(f => f.FolderName != null && f.FolderName.StartsWith(AnonPrefix));

        Service.Save();
    }
}