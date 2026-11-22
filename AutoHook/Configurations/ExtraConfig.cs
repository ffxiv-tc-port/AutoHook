using System;
using System.Collections.Generic;
using System.ComponentModel;
using AutoHook.Classes;
using AutoHook.Conditions;
using AutoHook.Enums;
using Newtonsoft.Json;

namespace AutoHook.Configurations;

/// <summary>上游 <c>ExtraStopAction</c>。數值順序必須與上游相同（匯出字串存的是整數）。</summary>
public enum ExtraStopAction
{
    None,
    StopOnly,
    QuitFishing,
    HardQuit,
}

/// <summary>
/// 上游 v6+ 的「規則」（ExtraCfg.Triggers）：條件成立時做一組動作。
/// 只宣告我們做得到的動作；上游另有的處理收藏品視窗／開始釣魚／精選／移除狀態刻意不宣告，
/// 匯入時被略過，由 <c>GetUnsupportedPresetFeatures</c> 回報。
/// </summary>
public class ExtraTrigger
{
    [DefaultValue(true)] public bool Enabled = true;

    public ConditionSet? ConditionSet { get; set; }

    public bool SwapPreset;

    [DefaultValue("-")] public string PresetToSwap = @"-";

    public bool SwapBait;
    public BaitFishClass BaitToSwap = new();

    public ExtraStopAction StopAction = ExtraStopAction.None;

    public bool ResetFishCaughtCounter;
}

public class ExtraConfig : BaseOption
{
    public bool Enabled = false;
    
    public bool SwapBaitIntuitionGain = false;
    public BaitFishClass BaitToSwapIntuitionGain = new();
    
    public bool SwapBaitIntuitionLost = false;
    public BaitFishClass BaitToSwapIntuitionLost = new();
    
    public bool SwapPresetIntuitionGain = false;
    public string PresetToSwapIntuitionGain = @"-";
    
    public bool SwapPresetIntuitionLost = false;
    public string PresetToSwapIntuitionLost = @"-";

    public bool SwapBaitSpectralCurrentGain = false;
    public BaitFishClass BaitToSwapSpectralCurrentGain = new();

    public bool SwapBaitSpectralCurrentLost = false;
    public BaitFishClass BaitToSwapSpectralCurrentLost = new();

    public bool SwapPresetSpectralCurrentGain = false;
    public string PresetToSwapSpectralCurrentGain = @"-";

    public bool SwapPresetSpectralCurrentLost = false;
    public string PresetToSwapSpectralCurrentLost = @"-";

    public bool ResetCounterPresetSwap = false;
    public bool QuitOnIntuitionLost = false;
    public bool StopOnIntuitionLost = false;
    
    public bool ForceBaitSwap;
    public int ForcedBaitId;
    
    // Angler's Art
    public bool StopAfterAnglersArt = false;
    public int AnglerStackQtd = 0;
    public FishingSteps AnglerStopFishingStep = FishingSteps.None;
    public bool SwapBaitAnglersArt = false;
    public BaitFishClass BaitToSwapAnglersArt = new();
    public bool SwapPresetAnglersArt = false;
    public string PresetToSwapAnglersArt = @"-";

    /// <summary>上游 v6+ 匯出才有；既有 preset 是空清單，規則求值整段不跑。</summary>
    public List<ExtraTrigger> Triggers
    {
        get => _triggers;
        set => _triggers = value ?? new();
    }

    private List<ExtraTrigger> _triggers = new();

    /// <summary>每條規則上一次的條件結果（只在「由假轉真」時觸發）。不存檔。</summary>
    [JsonIgnore] public List<bool> LastTriggerStates = new();
    
    
    public override void DrawOptions()
    {
        
    }
}