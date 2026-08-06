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

    [DefaultValue(true)] public bool PluginEnabled = true;

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
        if (!import.StartsWith(ExportPrefixFolder))
            return null;

        try
        {
            var folderData = JsonConvert.DeserializeObject<FolderExport>(DecompressString(import),
                new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });

            if (folderData == null)
                return null;

            var folder = new PresetFolder(folderData.FolderName);

            // Generate new GUIDs for all presets to avoid conflicts
            foreach (var preset in folderData.Presets)
            {
                preset.UniqueId = Guid.NewGuid();
                folder.AddPreset(preset.UniqueId);
            }

            return (folder, folderData.Presets);
        }
        catch (Exception e)
        {
            Service.PluginLog.Error($"Failed to import folder: {e.Message}");
            return null;
        }
    }

    public static BasePresetConfig? ImportPreset(string import)
    {
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

        if (import.StartsWith(ExportPrefixSf))
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

    [NonSerialized] private const string ExportPrefixSf = "AHSF1_";
    [NonSerialized] private const string ExportPrefixFolder = "AHFOLDER_";


    // ⚠️ 順序有意義：DecompressString 用 First(s.StartsWith) 取前綴。
    // 目前沒有任何一個前綴是另一個的前綴（"AH_" 的第三個字元是 '_'，
    // 而 AH3_/AH4_/AH6_ 的第三個字元是數字），所以怎麼排都不會誤判；
    // 之後要加新前綴時請重新確認這一點。
    [NonSerialized] private static readonly List<string> ExportPrefixes =
    [
        ExportPrefixV2, ExportPrefixV3, ExportPrefixV4, ExportPrefixV6, ExportPrefixSf, ExportPrefixFolder
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
        if (!ExportPrefixes.Any(s.StartsWith))
            throw new ApplicationException(UIStrings.DecompressString_Invalid_Import);

        var prefix = ExportPrefixes.First(s.StartsWith);
        var data = Convert.FromBase64String(s[prefix.Length..]);
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