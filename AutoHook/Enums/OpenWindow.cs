namespace AutoHook.Enums;

public enum OpenWindow
{
    None,
    Global,
    FishingPreset,
    AutoGig,
    Settings,
    About,
    Debug,
    Community,

    /// <summary>天氣與窗口（唯讀顯示頁）。<b>附加在列舉尾端</b>，不動既有成員的值。</summary>
    Weather
}