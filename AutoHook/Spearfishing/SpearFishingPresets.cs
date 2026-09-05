using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AutoHook.Classes;
using AutoHook.Classes.AutoCasts;
using AutoHook.Configurations;
using Newtonsoft.Json;

namespace AutoHook.Spearfishing;

public class SpearFishingPresets : BasePreset
{
    /// <summary>
    /// 使用者的「啟用自動魚叉」開關。<b>執行期真值</b>——所有讀取端都讀這個欄位。
    /// </summary>
    /// <remarks>
    /// ⚠️ 不直接序列化；寫進設定檔的是 <see cref="AutoGigEnabledPersisted"/>。
    /// 與 <c>Configuration.PluginEnabled</c> 完全同一個形狀，理由見 <see cref="IpcConfigOverrides"/>。
    /// </remarks>
    [JsonIgnore]
    public bool AutoGigEnabled = false;

    /// <summary><see cref="AutoGigEnabled"/> 的序列化替身。JSON 鍵名維持 <c>AutoGigEnabled</c> 不變。</summary>
    [JsonProperty(nameof(AutoGigEnabled))]
    public bool AutoGigEnabledPersisted
    {
        get => IpcConfigOverrides.ValueForSave(IpcConfigOverrides.AutoGigEnabledKey, AutoGigEnabled);
        set => AutoGigEnabled = value;
    }

    public bool AutoGigHideOverlay = false;
    
    [DefaultValue(true)]
    public bool AutoGigDrawFishHitbox = true;
    
    [DefaultValue(true)]
    public bool AutoGigDrawGigHitbox = true;
    
    public AutoThaliaksFavor ThaliaksFavor = new(true);
    
    public bool CatchAll = false;
    public bool CatchAllNaturesBounty = false;

    public bool NatureBountyBeforeFish = false;
    
    public List<AutoGigConfig> Presets = new();
    
    [JsonIgnore] public override List<BasePresetConfig> PresetList => Presets.Cast<BasePresetConfig>().ToList();

    [JsonIgnore] public override AutoGigConfig? SelectedPreset => base.SelectedPreset as AutoGigConfig; 
    
    public override void AddNewPreset(string presetName) 
    {
        var newPreset = new AutoGigConfig(presetName);
        Presets.Add(newPreset);
        SelectedGuid = newPreset.UniqueId.ToString();
        Service.Save();
    }
    
    public override void AddNewPreset(BasePresetConfig preset) 
    {
        var json = JsonConvert.SerializeObject(preset); 
        var copy = JsonConvert.DeserializeObject<AutoGigConfig>(json);
        copy!.UniqueId = Guid.NewGuid();
        Presets.Add(copy);
        SelectedGuid = copy.UniqueId.ToString();
        Service.Save();
    }

    public override void RemovePreset(Guid value)
    {
        var preset = Presets.Find(p => p.UniqueId == value);
        if (preset == null)
            return;
        
        Presets.Remove(preset);
        Service.Save();
    }
    
    public override void SwapIndex(int itemIndex, int targetIndex)
    {
        var moved = Presets[itemIndex];
        
        if(moved == null)
            return;
        
        RemovePreset(moved.UniqueId);
        Presets.Insert(targetIndex, moved);
        Service.Save();
    }
}