using System.Collections.Generic;
using System.Linq;
using AutoHook.Enums;
using AutoHook.Spearfishing.Enums;
using AutoHook.Utils;

namespace AutoHook.Classes;

public class ImportedFish
{
    public int ItemId { get; set; }
    public HookType HookType { get; set; }
    public BiteType BiteType { get; set; }
    public int InitialBait { get; set; }
    public List<int> Mooches { get; set; } = new();
    public List<FishPredator> Predators { get; set; } = new();
    public List<int> Nodes { get; set; } = new();
    public bool IsSpearFish { get; set; } = new();
    public SpearfishSize Size { get; set; } = new();
    public SpearfishSpeed Speed { get; set; } = new();

    public int SurfaceSlap { get; set; } = new();
    public bool OceanFish { get; set; } = new();
    public FishInterval Interval { get; set; } = new();

    /// <summary>
    /// 這條魚需要的天氣（<c>Weather</c> 表的列號）。空集合＝不挑天氣。
    /// <para>
    /// ⚠️ <b>這是社群量測資料，不是遊戲資料表</b> —— 台服的 <c>FishParameter</c> 沒有天氣欄位，
    /// 所以這份對照是從國際服社群來的，在台服要假設「可能有錯」。
    /// 判斷式因此一律寫成「條件不成立就不顯示窗口」而不是「條件不成立就不給釣」。
    /// </para>
    /// </summary>
    public List<int> Weathers { get; set; } = new();

    /// <summary>
    /// 這條魚需要的<b>前一個時段</b>天氣（天氣轉換條件）。空集合＝不挑前置天氣。
    /// <para>
    /// 🔴 「前一個時段的天氣」<b>問不到遊戲</b>：<c>GetWeatherForHour</c> 餵負數 offset 會
    /// 靜默算出完全錯誤的答案。所以只有兩種情況拿得到：①要判斷的是未來時段（用預報的前一格）
    /// ②外掛從那個時段起就已經在跑（跨幀自己記錄）。兩者都不成立時答案是<b>「不知道」</b>，
    /// 必須照實顯示成不知道，不可以當成「不符合」。
    /// </para>
    /// </summary>
    public List<int> WeathersFrom { get; set; } = new();

    public string Name => MultiString.GetItemName(ItemId);
    
    public bool IsLureFish => GameRes.LureFishes.Any(f => f.Id == ItemId);

    public class FishPredator
    {
        public int itemId { get; set; }
        public int qtd { get; set; }
    }

    public class FishInterval
    {
        public int OnTime { get; set; }
        public int OffTime { get; set; }
        public int ShiftTime { get; set; }
    }
}