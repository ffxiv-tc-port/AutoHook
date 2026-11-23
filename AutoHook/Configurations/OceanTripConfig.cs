using System.Collections.Generic;
using System.ComponentModel;
using AutoHook.Fishing.Ocean;

namespace AutoHook.Configurations;

/// <summary>出海垂釣整趟自動化的設定。是否在跑是執行期狀態、不存檔（載入／登入不會自己開始）。</summary>
public class OceanTripConfig
{
    /// <summary>站上不能拋竿時用 vnavmesh 走到船邊。</summary>
    [DefaultValue(true)] public bool WalkToRailing = true;

    /// <summary>結束時把 preset 換回按下開始前選的那一個（只在途中換過時）。</summary>
    [DefaultValue(true)] public bool RestorePresetAfterTrip = true;

    public List<OceanStopRule> Rules = new();
}
