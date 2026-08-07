using System.Collections.Generic;
using System.Linq;
using AutoHook.Enums;
using AutoHook.Spearfishing.Enums;
using AutoHook.Utils;

namespace AutoHook.Classes;

/// <summary>
/// <c>Data/FishData/fish_list.json</c> 的一列。整份清單載進 <see cref="Utils.GameRes.ImportedFishes"/>。
///
/// <para>
/// 🔴 <b>出貨不變量：每一列的 <see cref="ItemId"/> 都必須在台服 <c>Item</c> 表查得到「非空的名字」。</b>
/// <see cref="Name"/> 走 <c>MultiString.GetItemName</c>，查不到名字時回的是
/// <c>UIStrings.None</c>（「無」）而不是空字串 —— 所以塞進查不到的道具不會崩潰，
/// 而是讓選魚下拉選單裡出現一堆長得一模一樣的「無」，使用者無從分辨。
/// 2026-08-07 量測：現有 2166 列<b>全部</b>查得到名字，選單裡目前一個「無」都沒有。
/// </para>
///
/// <para>
/// 🔴 <b>增列會改變執行期行為，不是「順手加資料」。</b>
/// <c>PresetCreator</c> 解 <see cref="Mooches"/>／<see cref="Predators"/> 走的是
/// <c>ImportedFishes.FirstOrDefault(f =&gt; f.ItemId == x)</c>——查的是<b>整份清單</b>。
/// 只要新增的 ItemId 剛好是某條既有魚引用過、但以前查不到的 mooch／predator，
/// 那條魚產生出來的 preset 內容就會從此不同，而且完全沒有徵兆。
/// ⇒ 動這個檔一律走 <c>~/.claude/tools/ahfish/</c>：
/// <c>merge_upstream_rows.py</c>（增列，含台服可得性閘門與「增列無副作用」閘門）、
/// <c>fishdiff</c>（用出貨的型別逐條比對<b>解析後</b>的判斷結果，證明既有魚零改變）。
/// </para>
///
/// <para>
/// ⚠️ <b>上游的 schema 與我們的不同，照抄會靜默吃掉資料</b>：上游的
/// <see cref="Predators"/> 是 <c>{ItemId, Quantity}</c>、我們的是 <c>{itemId, qtd}</c>
/// （System.Text.Json 預設大小寫敏感 → 兩個欄位靜默變 0）；上游沒有 <see cref="Interval"/>，
/// 改用 <c>Spawn</c>/<c>Duration</c>；上游的 <c>SpotIds</c> 是<b>釣點</b>，
/// 與我們的 <see cref="Nodes"/>（<b>魚叉採集點</b>）不是同一件事，不可互換。
/// </para>
/// </summary>
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