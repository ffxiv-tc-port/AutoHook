using System;
using System.ComponentModel;
using AutoHook.Classes;
using AutoHook.Classes.AutoCasts;
using AutoHook.Conditions;
using AutoHook.Enums;
using AutoHook.Resources.Localization;

namespace AutoHook.Configurations;

public class FishConfig : BaseOption
{
    [DefaultValue(true)]
    public bool Enabled = true;
    
    public bool IgnoreOnIntuition = false;

    public BaitFishClass Fish = new();
    
    public bool StopAfterCaught = false;
    public int StopAfterCaughtLimit = 1;
    public bool StopAfterResetCount = false;
    
    public AutoIdenticalCast IdenticalCast = new();
    public AutoSurfaceSlap SurfaceSlap = new();
    public AutoMooch Mooch = new();
    
    public bool SwapBait = false;
    public BaitFishClass BaitToSwap = new();
    public int SwapBaitCount = 1;
    
    public bool SwapPresets = false;
    public string PresetToSwap = "-";
    public int SwapPresetCount = 1;

    /// <summary>
    /// 上游 config v6／v7 起，「什麼時候換 preset」不再是
    /// <see cref="SwapPresets"/>＋<see cref="SwapPresetCount"/> 這組欄位，而是一份條件。
    /// 🔴 少了這個屬性，匯入的多階段 preset **一定會卡在第一階段**：上游的 JSON 裡沒有 <c>SwapPresets</c>
    ///    這個鍵 → 欄位留在初始值 false → <c>CheckFishCaughtSwap</c> 的第一道閘門永遠不成立，而且沒有任何錯誤訊息。
    /// 📌 相容性：<c>null</c>（既有使用者、以及所有 AH4_ 匯出字串）時完全走舊路徑。
    ///    兩者都有值時**以條件為準** —— 條件是比較晚出現、比較具體的那個表達方式。
    /// </summary>
    public ConditionSet? SwapPresetConditionSet { get; set; }
    
    public bool NeverMooch = false;
    
    public FishingSteps StopFishingStep = FishingSteps.None;
    
    public FishConfig(){}
    
    public FishConfig(BaitFishClass fish)
    {
        Fish = fish;
        // ok this is not the best way, but im tired, and it works for now so be nice to me
        Mooch.Name = UIStrings.Always_Mooch; 
    }
    
    public FishConfig(int fishId) 
    { 
        Fish = new BaitFishClass(fishId); 
    }
    
    
    public override void DrawOptions()
    {
        
    }
}