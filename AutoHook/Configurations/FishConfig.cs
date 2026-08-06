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
    ///
    /// 🔴 這個屬性沒有宣告之前，匯入的多階段 preset **一定會卡在第一階段**：
    ///    上游的 JSON 裡沒有 <c>SwapPresets</c> 這個鍵 → Newtonsoft 保留欄位初始值 false
    ///    → <c>CheckFishCaughtSwap</c> 的第一道閘門永遠不成立。
    ///    而 <see cref="PresetToSwap"/>（要換去哪一份）是有進來的，所以症狀是
    ///    「設定看起來都對，就是不換」，不會有任何錯誤訊息。
    ///    ⚠️ 這正是 ICE 匯入 [495] 之後全程停在階段 1 的原因（離線解碼 495 那 5 份 preset 直接證實：
    ///    每一份的 <c>PresetToSwap</c> 都指著下一階段，而條件全在這個鍵裡）。
    ///
    /// 📌 相容性：<c>null</c>（既有使用者、以及所有 AH4_ 匯出字串）時完全走舊路徑，行為逐位元相同。
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