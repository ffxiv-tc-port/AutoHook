using Dalamud.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text;
using AutoHook.Classes;
using AutoHook.Configurations.old_config;
using AutoHook.Fishing;
using AutoHook.Resources.Localization;
using AutoHook.Spearfishing;
using AutoHook.Utils;

namespace AutoHook.Configurations;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 5;
    // TC fork: default to Traditional Chinese; users can switch in settings
    public string CurrentLanguage { get; set; } = @"zh-Hant";

    public bool HideLocButtonn = true;

    /// <summary>
    /// 使用者的「啟用 AutoHook」開關。<b>執行期真值</b>——所有讀取端都讀這個欄位。
    /// </summary>
    /// <remarks>
    /// ⚠️ 這個欄位<b>不直接序列化</b>；寫進設定檔的是 <see cref="PluginEnabledPersisted"/>。
    /// 別的外掛用 <c>AutoHook.SetPluginState</c> 借走這個開關時，磁碟上要保留<b>使用者自己的值</b>
    /// ——理由與機制見 <see cref="IpcConfigOverrides"/>。
    /// </remarks>
    [JsonIgnore]
    [DefaultValue(true)] public bool PluginEnabled = true;

    /// <summary>
    /// <see cref="PluginEnabled"/> 的序列化替身。JSON 鍵名維持 <c>PluginEnabled</c> 不變，
    /// 既有設定檔照樣讀得回來、寫出去也還是同一個鍵。
    /// </summary>
    /// <remarks>
    /// 🔑 這裡刻意<b>不</b>用「存檔前把欄位換成使用者的值、存完再換回來」：那會留下一段
    /// 欄位值不對的時間窗，而 <c>FishingManager</c> 每幀都在讀它（<c>Service.Save()</c> 又可能
    /// 從非 Framework 執行緒進來）。改成序列化替身之後，執行期欄位<b>完全不被碰</b>。
    /// </remarks>
    [JsonProperty(nameof(PluginEnabled))]
    public bool PluginEnabledPersisted
    {
        get => IpcConfigOverrides.ValueForSave(IpcConfigOverrides.PluginEnabledKey, PluginEnabled);
        set => PluginEnabled = value;
    }

    /// <summary>
    /// 「沒在釣魚時自動拋竿」。<b>執行期真值</b>——讀取端讀這個欄位。
    /// </summary>
    /// <remarks>
    /// 📌 這是把上游的 <c>AutoStartFishing</c> 行為補回本 fork（本 fork 分岔自上游 2025-05-08，
    /// 當時還沒有這個設定）。<b>預設 <see langword="false"/>，與上游一致</b>：
    /// 既有使用者的設定檔裡沒有這個鍵 ⇒ 吃這裡的初值 ⇒ <b>行為零改變</b>。
    /// <br/>⚠️ 這個欄位<b>不直接序列化</b>；寫進設定檔的是 <see cref="AutoStartFishingPersisted"/>。
    /// 別的外掛用 <c>AutoHook.SetAutoStartFishing</c> 借走這個開關時，磁碟上要保留<b>使用者自己的值</b>
    /// ——理由與機制見 <see cref="IpcConfigOverrides"/>。
    /// </remarks>
    [JsonIgnore]
    public bool AutoStartFishing = false;

    /// <summary>
    /// <see cref="AutoStartFishing"/> 的序列化替身。JSON 鍵名維持 <c>AutoStartFishing</c> 不變。
    /// </summary>
    [JsonProperty(nameof(AutoStartFishing))]
    public bool AutoStartFishingPersisted
    {
        get => IpcConfigOverrides.ValueForSave(IpcConfigOverrides.AutoStartFishingKey, AutoStartFishing);
        set => AutoStartFishing = value;
    }

    public FishingPresets HookPresets = new();

    public SpearFishingPresets AutoGigConfig = new();

    public bool ShowDebugConsole = false;

    [DefaultValue(true)] public bool ShowChatLogs = true;

    public int DelayBetweenCastsMin = 600;
    public int DelayBetweenCastsMax = 1000;

    public int DelayBetweenHookMin = 100;
    public int DelayBetweenHookMax = 200;

    public int DelayBeforeCancelMin = 1500;
    public int DelayBeforeCancelMax = 2000;

    [DefaultValue(true)] public bool ShowStatus = true;
    public bool ShowPresetsAsSidebar = false;

    public bool HideTabDescription = false;

    public bool SwapToButtons = false;
    public int SwapType;

    [DefaultValue(true)] public bool DontHideOptionsDisabled = true;

    [DefaultValue(true)] public bool ResetAfkTimer = true;

    // ── 塔塔露誇獎（TataruPraise）通知 ────────────────────────────────────────
    // 掛機釣魚時被「有聲音」叫回電腦前用的。純通知：不換餌、不換 preset、不停手，
    // 對方沒安裝時整條路徑是 no-op。判準的取捨寫在 FishingManager.Praise.cs。
    // ⚠️ 新欄位對既有使用者是「設定檔裡沒有這個鍵」⇒ 吃這裡的初值，不必寫遷移。

    /// <summary>總開關。關掉之後連判準都不會算。</summary>
    [DefaultValue(true)] public bool TataruPraiseEnabled = true;

    /// <summary>
    /// 大魚（需要漁人的直覺／需要天氣轉換／魚影魚）。<b>唯一預設開的判準</b>——
    /// 離線量測非魚叉魚 1869 條裡只有 135 條（7.2%）符合。
    /// </summary>
    [DefaultValue(true)] public bool TataruPraiseBigFish = true;

    /// <summary>遊戲自己的「大物」旗標。頻率沒有實機數據，預設關。</summary>
    public bool TataruPraiseLargeFlag = false;

    /// <summary>傳說咬「!!!」。某些餌／釣場是常態，預設關。</summary>
    public bool TataruPraiseLegendaryBite = false;

    /// <summary>收藏品。收藏品釣魚時每一條都是，預設關。</summary>
    public bool TataruPraiseCollectible = false;

    /// <summary>兩次出聲之間的本機最短間隔（秒）。判準寫壞時的保險絲，不是功能本身。</summary>
    public int TataruPraiseMinIntervalSeconds = 60;

    // old config
    public List<BaitPresetConfig> BaitPresetList = new();

    public void Save()
    {
        Service.PluginInterface!.SavePluginConfig(this);
    }

    public void UpdateVersion()
    {
        if (Version == 1)
        {
            Version = 2;
        }

        if (Version == 2)
        {
            try
            {
                foreach (var preset in BaitPresetList)
                {
                    var newPreset = ConvertOldPreset(preset);
                    if (newPreset != null)
                        HookPresets.CustomPresets.Add(newPreset);
                }

                Version = 3;
            }
            catch (Exception e)
            {
                Service.PrintDebug(@$"[Configuration] {e.Message}");
            }
        }

        if (Version == 3)
        {
            Service.PrintDebug(@$"[Configuration] Updating to v4");

            Save();
            Version = 4;
        }

        if (Version == 4)
        {
            Service.PrintDebug(@$"[Configuration] Updating to v5");

            foreach (var gig in AutoGigConfig.Presets)
            {
                Service.PrintDebug($"Renaming {gig.PresetName} to {gig.Name}");
                gig.PresetName = gig.Name;
            }

            HookPresets.DefaultPreset.PresetName = Service.GlobalPresetName;

            Save();
            Version = 5;
        }
    }

    private static void SetFieldNewClass(HookConfig newOne, BaitConfig old)
    {
        var oldType = old.GetType();
        var newType = newOne.GetType();

        var oldFields = oldType.GetFields();
        var newFields = newType.GetFields();

        foreach (var sourceField in oldFields)
        {
            var targetField =
                newFields.FirstOrDefault(f => f.Name == sourceField.Name && f.FieldType == sourceField.FieldType);
            if (targetField != null)
            {
                var value = sourceField.GetValue(old);
                targetField.SetValue(newOne, value);
            }
        }
    }

    public void Initiate()
    {
        if (HookPresets.DefaultPreset.ListOfBaits.Count != 0)
            return;

        var bait = new BaitFishClass(UIStrings.All_Baits, 0);
        var mooch = new BaitFishClass(UIStrings.All_Mooches, 0);

        HookPresets.DefaultPreset.ListOfBaits.Add(new HookConfig(bait));
        HookPresets.DefaultPreset.ListOfMooch.Add(new HookConfig(mooch));
    }

    public static Configuration Load()
    {
        // 載入設定檔＝重新確立「使用者的值」，任何殘留的 IPC 覆寫都作廢。
        IpcConfigOverrides.ClearAll();

        try
        {
            if (Service.PluginInterface.GetPluginConfig() is Configuration config)
            {
                config.Initiate();
                config.UpdateVersion();
                config.Save();
                return config;
            }

            config = new Configuration();
            config.Initiate();
            config.Save();
            return config;
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(@$"[Configuration] {e.Message}");
            throw;
        }
    }

    public static void ResetConfig()
    {
    }

    // Got the export/import function from the UnknownX7's ReAction repo
    /*public static string ExportPreset(CustomPresetConfig preset)
    {
        return CompressString(JsonConvert.SerializeObject(preset,
            new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));
    }*/

    public static string ExportPreset(BasePresetConfig preset)
    {
        var exported = CompressString(JsonConvert.SerializeObject(preset,
            new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));

        // check if preset is type of AutoGigConfig or CustomPresetConfig
        if (preset is AutoGigConfig)
            return ExportPrefixSf + exported;
        else if (preset is CustomPresetConfig)
            return ExportPrefixV4 + exported;

        return "Something went wrong while exporting the preset";
    }

    public class FolderExport
    {
        public string FolderName { get; set; }
        public List<CustomPresetConfig> Presets { get; set; } = new();

        /// <summary>
        /// AHFOLDER2_ 才有的巢狀子資料夾。AHFOLDER_（我們自己匯出的那版）的 JSON 裡沒有這個鍵，
        /// 反序列化後就是空清單 ⇒ 舊格式的匯入行為完全沒變。
        /// ⚠️ 我們的 <see cref="PresetFolder"/> 沒有 ParentFolderId，**表達不了階層**，
        /// 所以匯入時是攤平的，見 <see cref="CollectFolderPresets"/>。
        /// </summary>
        public List<FolderExport> ChildFolders { get; set; } = new();

        public FolderExport(string name)
        {
            FolderName = name;
        }
    }

    public static string ExportFolder(PresetFolder folder, List<CustomPresetConfig> presets)
    {
        var folderExport = new FolderExport(folder.FolderName);

        foreach (var presetId in folder.PresetIds)
        {
            var preset = presets.FirstOrDefault(p => p.UniqueId == presetId);
            if (preset != null)
            {
                folderExport.Presets.Add(preset);
            }
        }

        var exported = CompressString(JsonConvert.SerializeObject(folderExport,
            new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore }));

        return ExportPrefixFolder + exported;
    }

    public static (PresetFolder Folder, List<CustomPresetConfig> Presets)? ImportFolder(string import)
    {
        import = import.Trim();

        if (!import.StartsWith(ExportPrefixFolder) && !import.StartsWith(ExportPrefixFolderV2))
            return null;

        try
        {
            var folderData = JsonConvert.DeserializeObject<FolderExport>(DecompressString(import),
                new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });

            if (folderData == null)
                return null;

            var folder = new PresetFolder(folderData.FolderName);
            var presets = new List<CustomPresetConfig>();
            var nestedFolders = 0;

            CollectFolderPresets(folderData, folder, presets, ref nestedFolders, string.Empty, 0);

            // 使用者回報用，寫 Information（使用者跑 LogLevel 1，Debug 收得到但單檔數十萬行會淹沒）。
            if (nestedFolders > 0)
                Service.PluginLog.Information(
                    $"[ImportFolder] 這份匯入含 {nestedFolders} 個子資料夾。本外掛的資料夾沒有階層，" +
                    $"已攤平成單一資料夾（共 {presets.Count} 個 preset）；子資料夾裡的 preset 名稱前面補上了來源路徑。");

            return (folder, presets);
        }
        catch (Exception e)
        {
            Service.PluginLog.Error($"Failed to import folder: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 上限只是為了擋掉手工捏出來的病態巢狀 —— 遞迴爆掉是 StackOverflowException，
    /// 那是**攔不到**的、直接把行程帶走。正常匯出不可能接近這個深度。
    /// </summary>
    private const int MaxFolderImportDepth = 16;

    /// <summary>
    /// 把 <see cref="FolderExport"/> 這棵樹上的 preset 全部收進單一資料夾（攤平）。
    /// 階層資訊改用「子資料夾名/」前綴留在 preset 名稱上 —— 匯入對話框本來就每個 preset
    /// 都有可編輯的名稱欄，使用者看得到也改得掉。
    /// 🔑 攤平只損失「分組」，**不會少匯任何一個 preset**。
    /// </summary>
    private static void CollectFolderPresets(FolderExport data, PresetFolder target,
        List<CustomPresetConfig> presets, ref int nestedFolders, string namePrefix, int depth)
    {
        foreach (var preset in data.Presets ?? new List<CustomPresetConfig>())
        {
            if (preset == null)
                continue;

            // Generate new GUIDs for all presets to avoid conflicts
            preset.UniqueId = Guid.NewGuid();

            if (namePrefix.Length > 0)
                preset.PresetName = namePrefix + preset.PresetName;

            target.AddPreset(preset.UniqueId);
            presets.Add(preset);
        }

        if (data.ChildFolders is not { Count: > 0 })
            return;

        if (depth >= MaxFolderImportDepth)
        {
            Service.PluginLog.Information(
                $"[ImportFolder] 子資料夾巢狀深度超過 {MaxFolderImportDepth} 層，更深的部分沒有匯入。");
            return;
        }

        foreach (var child in data.ChildFolders)
        {
            if (child == null)
                continue;

            nestedFolders++;
            CollectFolderPresets(child, target, presets, ref nestedFolders,
                $"{namePrefix}{child.FolderName}/", depth + 1);
        }
    }

    public static BasePresetConfig? ImportPreset(string import)
    {
        // 一定要跟 DecompressString 用同一個字串：那邊已經 Trim 過，這邊的 StartsWith
        // 若拿沒 Trim 的原字串去比，開頭帶空白時會選錯分支（例如 " AH_" 會漏掉舊版轉換）。
        import = import.Trim();

        if (import.StartsWith(ExportPrefixV2))
        {
            var old = JsonConvert.DeserializeObject<BaitPresetConfig>(DecompressString(import),
                new JsonSerializerSettings() { ObjectCreationHandling = ObjectCreationHandling.Replace });
            return ConvertOldPreset(old);
        }

        if (import.StartsWith(ExportPrefixV3))
        {
            var old = JsonConvert.DeserializeObject<OldPresetConfig>(DecompressString(import),
                new JsonSerializerSettings() { ObjectCreationHandling = ObjectCreationHandling.Replace });

            return ConvertOldPresetV3(old);
        }

        // AHSF2_ 是 AHSF1_ 的 Brotli 版，對應的 C# 型別一樣是 AutoGigConfig
        // （上游 ImportPreset 也是把兩個前綴併在同一條分支）。
        if (import.StartsWith(ExportPrefixSf) || import.StartsWith(ExportPrefixSf2))
        {
            var autogig = JsonConvert.DeserializeObject<AutoGigConfig>(DecompressString(import),
                new JsonSerializerSettings() { ObjectCreationHandling = ObjectCreationHandling.Replace });

            return autogig;
        }

        var importActionStack = JsonConvert.DeserializeObject<CustomPresetConfig>(DecompressString(import),
            new JsonSerializerSettings() { ObjectCreationHandling = ObjectCreationHandling.Replace });
        return importActionStack;
    }

    [NonSerialized] private const string ExportPrefixV2 = "AH_";
    [NonSerialized] private const string ExportPrefixV3 = "AH3_";
    [NonSerialized] private const string ExportPrefixV4 = "AH4_";

    /// <summary>
    /// 上游較新版本匯出的 preset 前綴。**只收不發** —— <see cref="ExportPreset"/> 仍然輸出
    /// <see cref="ExportPrefixV4"/>，所以既有使用者的匯出字串一個字都沒變。
    ///
    /// 之所以不需要專屬的反序列化分支：AH6_ 的酬載跟 AH4_ 一樣是 base64(gzip(utf8 json))
    /// （已離線驗證 14 段真實字串：gzip magic 1f8b 正確、末四位元組的 ISIZE 與實際解壓長度相符），
    /// 對應的 C# 型別同樣是 <see cref="CustomPresetConfig"/>，所以走
    /// <see cref="ImportPreset"/> 末尾那條共用路徑即可。
    ///
    /// ⚠️ **但 AH6_ 的 schema 比 AH4_ 大一圈，多出來的欄位會被靜默丟掉。**
    /// 離線比對 14 段 AH6_ 與我們出貨的 94 段 AH4_，AH6_ 多出的第二層鍵包含
    /// <c>NamedConditions</c>、<c>*.ConditionSet</c>、<c>AutoCastsCfg.TimeWindowConditionSet</c>、
    /// <c>ExtraCfg.Triggers</c>、<c>ExtraCfg.AutoOceanFish*</c>、<c>ListOfFish[*].Multihook</c> 等；
    /// 這些屬性我們的設定類別根本沒有宣告，Newtonsoft 預設 MissingMemberHandling.Ignore 會直接忽略
    /// —— **不會丟例外，但那些條件式行為等於沒有生效**。
    /// 反過來 AH4_ 才有的計數式欄位（<c>StopAfterCaught</c>／<c>SwapBaitCount</c> 等）在 AH6_ 已被
    /// ConditionSet 取代，所以匯入 AH6_ 時那些欄位會落在型別預設值上。
    ///
    /// 共用鍵路徑 217 條裡只有 <c>ListOfFish[*].SparefulHand.FishIdToCheck</c> 型別對不上
    /// （AH6_ 會出現 null），而該屬性我們的模型沒有宣告 ⇒ 同樣被忽略，不構成反序列化例外。
    /// </summary>
    [NonSerialized] private const string ExportPrefixV6 = "AH6_";

    /// <summary>
    /// 上游 2026-08 起改用的匯出前綴，酬載是 <b>base64(brotli(utf8 json))</b>，不是 gzip。
    /// 同樣**只收不發** —— <see cref="ExportPreset"/>／<see cref="ExportFolder"/> 仍舊輸出
    /// <see cref="ExportPrefixV4"/>／<see cref="ExportPrefixFolder"/>（gzip），既有使用者手上的
    /// 匯出字串與 ICE 內建的 preset 一個字都沒變。
    ///
    /// 🔴 **Brotli 沒有 gzip 的 ISIZE trailer** —— 舊的 <see cref="DecompressString"/> 是把
    /// 「最後四個位元組」當成解壓後長度、拿去 <c>new byte[uncompressedSize]</c> 的。
    /// 離線量過 65 段真實 Brotli 酬載：那四個位元組解讀成 int32 落在數 MB～數百 MB
    /// （65 段裡有 35 段超過 50MB），而實際 JSON 只有 3～4KB。也就是說舊路徑會為了一個 3KB 的
    /// preset 去要一塊上百 MB 的緩衝區 —— 失敗方式是 OutOfMemoryException（int32 為負時則是
    /// OverflowException），跟「格式不對」完全不像。
    /// 所以這三個前綴**必須**走不依賴長度的串流路徑，見 <see cref="BrotliExportPrefixes"/>。
    ///
    /// ⚠️ schema 落差與 AH6_ 同性質、而且更大：AH7_ 是上游 config v7 的序列化結果，
    /// 條件式行為已全面改成 ConditionSet／NamedConditions，我們的設定類別沒有宣告那些屬性，
    /// Newtonsoft 預設 MissingMemberHandling.Ignore 會靜默忽略 —— **不丟例外，但那些條件不會生效**。
    /// 反過來 AH4_ 才有的計數式欄位在 AH7_ 已不存在，匯入後會落在型別預設值上。
    /// （離線拿上游 wiki 的 55 段真實 AH7_／6 段 AHSF2_／4 段 AHFOLDER2_ 對過我方 DLL 的型別圖：
    /// 全數解得開、零型別衝突；55 段 AH7_ 每一段都仍帶著我們綁得住的
    /// ListOfBaits／ListOfMooch／ListOfFish／ExtraCfg／AutoCastsCfg，不是空殼。）
    /// </summary>
    [NonSerialized] private const string ExportPrefixV7 = "AH7_";

    [NonSerialized] private const string ExportPrefixSf = "AHSF1_";

    /// <inheritdoc cref="ExportPrefixV7"/>
    [NonSerialized] private const string ExportPrefixSf2 = "AHSF2_";

    [NonSerialized] private const string ExportPrefixFolder = "AHFOLDER_";

    /// <inheritdoc cref="ExportPrefixV7"/>
    [NonSerialized] private const string ExportPrefixFolderV2 = "AHFOLDER2_";


    // ⚠️ 順序有意義：DecompressString 用 First(s.StartsWith) 取前綴。
    // 目前沒有任何一個前綴是另一個的前綴，所以怎麼排都不會誤判：
    //   "AH_" 的第三個字元是 '_'，而 AH3_/AH4_/AH6_/AH7_ 的第三個字元是數字；
    //   "AHFOLDER_" 的第九個字元是 '_'，而 "AHFOLDER2_" 的是 '2'；
    //   "AHSF1_" 與 "AHSF2_" 在第五個字元就分開。
    // 🔴 之後要加新前綴時請重新確認這一點 —— 前綴互為前綴時，選錯的那個會讓
    //    BrotliExportPrefixes 的判斷跟著選錯，結果是拿 gzip 去解 brotli（或反之）。
    [NonSerialized] private static readonly List<string> ExportPrefixes =
    [
        ExportPrefixV2, ExportPrefixV3, ExportPrefixV4, ExportPrefixV6, ExportPrefixV7,
        ExportPrefixSf, ExportPrefixSf2, ExportPrefixFolder, ExportPrefixFolderV2
    ];

    // 走 Brotli 而非 gzip 的前綴（上游同一份清單叫 BroccoliExportPrefixes，是個雙關，
    // 跨檔比對上游碼時用那個名字搜）。
    [NonSerialized] private static readonly List<string> BrotliExportPrefixes =
    [
        ExportPrefixV7, ExportPrefixSf2, ExportPrefixFolderV2
    ];

    public static string CompressString(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        using var ms = new MemoryStream();
        using (var gs = new GZipStream(ms, CompressionMode.Compress))
            gs.Write(bytes, 0, bytes.Length);

        return Convert.ToBase64String(ms.ToArray());
    }

    public static string DecompressString(string s)
    {
        // 使用者是從 wiki／Discord 貼過來的，前後常帶換行。
        // （尾端空白其實無所謂 —— Convert.FromBase64String 本來就忽略空白字元 ——
        //  但**開頭**的空白會讓下面的 StartsWith 對不上，變成「無效的匯入資料」。）
        s = s.Trim();

        if (!ExportPrefixes.Any(s.StartsWith))
            throw new ApplicationException(UIStrings.DecompressString_Invalid_Import);

        var prefix = ExportPrefixes.First(s.StartsWith);
        var data = Convert.FromBase64String(s[prefix.Length..]);

        // 🔴 Brotli 串流沒有 gzip 的 ISIZE trailer，所以**不能**走下面那條
        // 「讀末四位元組當解壓後長度」的路徑 —— 對真實 Brotli 酬載那個值實測是數 MB～數百 MB
        // 的垃圾（見 ExportPrefixV7 的註解）。這裡改用不依賴長度的串流複製。
        if (BrotliExportPrefixes.Contains(prefix))
        {
            using var brotliInput = new MemoryStream(data);
            using var brotli = new BrotliStream(brotliInput, CompressionMode.Decompress);
            using var brotliOutput = new MemoryStream();
            brotli.CopyTo(brotliOutput);
            return Encoding.UTF8.GetString(brotliOutput.ToArray());
        }

        var lengthBuffer = new byte[4];
        Array.Copy(data, data.Length - 4, lengthBuffer, 0, 4);
        var uncompressedSize = BitConverter.ToInt32(lengthBuffer, 0);

        var buffer = new byte[uncompressedSize];
        using (var ms = new MemoryStream(data))
        {
            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            var totalRead = 0;
            while (totalRead < uncompressedSize)
            {
                var read = gzip.Read(buffer, totalRead, uncompressedSize - totalRead);
                if (read == 0)
                    break;
                totalRead += read;
            }
        }

        return Encoding.UTF8.GetString(buffer);
    }

    public static string DecompressBase64(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            using var compressedStream = new MemoryStream(bytes);
            using var zipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var resultStream = new MemoryStream();
            zipStream.CopyTo(resultStream);
            bytes = resultStream.ToArray();
            return Encoding.UTF8.GetString(bytes, 1, bytes.Length - 1);
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(@$"Failed to DecompressBase64: {e.Message}");
            return "";
        }
    }

    private static CustomPresetConfig? ConvertOldPreset(BaitPresetConfig? preset)
    {
        if (preset == null)
            return null;

        var filteredBaits = new List<HookConfig>();
        var filteredMooch = new List<HookConfig>();
        foreach (var old in preset.ListOfBaits)
        {
            var matchingBait = GameRes.Baits.FirstOrDefault(b => b.Name == old.BaitName);
            var matchingFish = GameRes.Fishes.FirstOrDefault(f => f.Name == old.BaitName);

            if (matchingBait != null)
            {
                var newOne = new HookConfig(matchingBait);
                SetFieldNewClass(newOne, old);
                filteredBaits.Add(newOne);
            }
            else if (matchingFish != null)
            {
                var newOne = new HookConfig(matchingFish);
                SetFieldNewClass(newOne, old);
                filteredMooch.Add(newOne);
            }
        }

        CustomPresetConfig newPreset = new(@$"[Old Version] {preset.PresetName}");
        newPreset.ListOfBaits = filteredBaits;
        newPreset.ListOfMooch = filteredMooch;
        return newPreset;
    }

    private static CustomPresetConfig? ConvertOldPresetV3(OldPresetConfig? old)
    {
        if (old == null)
            return null;

        var newPreset = new CustomPresetConfig(old.PresetName);

        Service.PrintDebug($"Converting v3 to v4: {old.PresetName}");
        foreach (var bait in old.ListOfBaits)
        {
            bait.ConvertV3ToV4();

            var newBait = new HookConfig(bait.BaitFish);

            newBait.Enabled = bait.Enabled;
            newBait.NormalHook = bait.NormalHook;
            newBait.IntuitionHook = bait.IntuitionHook;
            newBait.IntuitionHook.UseCustomStatusHook = bait.UseCustomIntuitionHook;

            newPreset.AddItem(newBait);
        }

        foreach (var mooch in old.ListOfMooch)
        {
            mooch.ConvertV3ToV4();
            var newMooch = new HookConfig(mooch.BaitFish);

            newMooch.Enabled = mooch.Enabled;
            newMooch.NormalHook = mooch.NormalHook;
            newMooch.IntuitionHook = mooch.IntuitionHook;
            newMooch.IntuitionHook.UseCustomStatusHook = mooch.UseCustomIntuitionHook;

            newPreset.AddItem(newMooch);
        }

        newPreset.ListOfFish = old.ListOfFish;
        newPreset.ExtraCfg = old.ExtraCfg;
        newPreset.AutoCastsCfg = old.AutoCastsCfg;

        return newPreset;
    }
}