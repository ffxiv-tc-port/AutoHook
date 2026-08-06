using System;
using System.Linq;
using System.Text.Json.Serialization;
using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Lumina.Excel.Sheets;
using FishRow = Lumina.Excel.Sheets.FishParameter;
using ItemRow = Lumina.Excel.Sheets.Item;

namespace AutoHook.Classes;

public class BaitFishClass : IComparable<BaitFishClass>
{
    [JsonIgnore] public string Name => Id switch
    {
        GameRes.AllMoochesId => UIStrings.All_Mooches,
        GameRes.AllBaitsId => UIStrings.All_Baits,
        _ => MultiString.GetItemName((uint)Id)
    };

    public int Id;

    /// <summary>魚影**出現**的訊息（<c>FishParameter.Unknown_70_1</c>）。非魚影魚為空字串。</summary>
    [JsonIgnore] public string LureMessage = "";

    /// <summary>
    /// 魚影**消失**的訊息（<c>FishParameter.Unknown_70_2</c>）。非魚影魚為空字串。
    /// 台服 7.20 的 12 條魚影魚三則訊息都齊全（離線查 <c>FishParameter.csv</c> 確認）。
    /// </summary>
    [JsonIgnore] public string LureGoneMessage = "";

    /// <summary>魚影魚被**釣起**的訊息（<c>FishParameter.Unknown_70_3</c>）。非魚影魚為空字串。</summary>
    [JsonIgnore] public string LureCaughtMessage = "";


    // check the bait type
    [JsonIgnore]
    public BaitType BaitType
    {
        get
        {
            return GameRes.Baits.Any(b => b.Id == Id) ? BaitType.Bait :
                GameRes.Fishes.Any(f => f.Id == Id) ? BaitType.Mooch : BaitType.Unknown;
        }
    }

    public BaitFishClass(Item data)
    {
        Id = (int)data.RowId;
    }

    public BaitFishClass(FishRow fishRow)
    {
        var itemData = fishRow.Item.GetValueOrDefault<ItemRow>() ?? new ItemRow();
        LureMessage = fishRow.Unknown_70_1.ToString();
        LureGoneMessage = fishRow.Unknown_70_2.ToString();
        LureCaughtMessage = fishRow.Unknown_70_3.ToString();
        Id = (int)itemData.RowId;
    }

    public BaitFishClass(string name, int id)
    {
        Id = id;
    }

    public BaitFishClass()
    {
        Id = -1;
    }

    public BaitFishClass(int id)
    {
        Id = id;
    }

    public int CompareTo(BaitFishClass? other)
        => Id.CompareTo(other?.Id ?? 0);
}