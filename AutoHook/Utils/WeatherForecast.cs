using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace AutoHook.Utils;

/// <summary>
/// 天氣預報查詢。<b>直接問遊戲自己的 <see cref="WeatherManager"/>，不自己重算天氣機率表。</b>
///
/// 為什麼不自己算：天氣是由「時間 → 亂數種子 → 各地區的累積機率表」決定的，
/// 那整套邏輯遊戲客戶端本來就有，而且是**遊戲自己在用的那一份**。
/// 自己搬一套過來的話，資料表一改版就會靜默給出錯誤的預報。
///
/// <para>
/// 🔴 <b>不要從 detour（原生 hook 的回呼）裡呼叫這個類別的任何東西。</b>
/// FFXIVClientStructs 的 <c>[MemberFunction]</c> 在特徵碼沒掃到時是
/// <b>擲 <see cref="System.InvalidOperationException"/></b>（<c>ThrowHelper.ThrowNullAddress</c>）
/// 而不是回 null；從原生堆疊往外擲例外會直接殺掉整個遊戲行程。
/// 只在每幀更新或 UI 繪製路徑上用。
/// </para>
///
/// <para>
/// 🔴 <b><see cref="WeatherManager.GetWeatherForHour"/> 不能餵負數 offset。</b>
/// 台服客戶端反組譯直證：它內部是 <c>imul ecx, esi, 0xE10</c>，這是 <b>32 位元</b>乘法，
/// 結果寫進 <c>ecx</c> 時會把 <c>rcx</c> 的高 32 位清掉 ——
/// 於是 offset = −8 會變成一個天文數字的正偏移，
/// <b>算出一個「合法但完全錯誤」的天氣，不擲例外、不崩潰、沒有任何徵兆。</b>
/// 所以「前一個時段的天氣」<b>不能</b>用負 offset 問，只能自己跨幀記錄
/// 或改用正 offset 從更早的基準往前推。
/// </para>
///
/// <para>
/// 📌 <c>GetPreviousWeather()</c> 那個擴充方法是 Dalamud API14 以後才有的，我們釘在 API13，
/// 所以這裡沒有那條路可以走（已對 Dalamud.dll / Lumina.Excel.dll 驗過，0 命中）。
/// </para>
/// </summary>
public static class WeatherForecast
{
    /// <summary>天氣每 8 個艾奧傑亞小時換一次。這是遊戲規則，不是可調參數。</summary>
    public const int EorzeaHoursPerPeriod = 8;

    /// <summary>
    /// 允許往後查幾個天氣時段。72 個時段 ≈ 28 小時真實時間，
    /// 對「下一個窗口什麼時候開」這種用途已經遠遠夠用。
    /// 上限存在的理由是避免呼叫端不小心傳進一個荒謬的數字而做出上萬次原生呼叫。
    /// </summary>
    public const int MaxPeriodOffset = 72;

    private static bool _reportedUnavailable;

    /// <summary>
    /// 四條特徵碼是否都已解析。<b>一定要先問這個再呼叫</b> ——
    /// 未解析的 CS 函式是擲例外不是回 null。
    /// </summary>
    private static bool AddressesResolved
        => WeatherManager.Addresses.Instance.Value != 0
           && WeatherManager.Addresses.GetCurrentWeather.Value != 0
           && WeatherManager.Addresses.GetWeatherForHour.Value != 0
           && WeatherManager.Addresses.GetWeatherForDaytime.Value != 0;

    /// <summary>天氣查詢現在可不可用。不可用時所有查詢都回 null，<b>不會</b>回 0。</summary>
    public static unsafe bool Ready
    {
        get
        {
            if (!AddressesResolved)
            {
                ReportUnavailableOnce();
                return false;
            }

            return WeatherManager.Instance() != null;
        }
    }

    /// <summary>
    /// 目前所在地區的天氣 id（<c>Weather</c> 表的列號）。查不到回 null。
    /// <para>
    /// 這條走的是 <c>GetCurrentWeather()</c>，它會把「特殊天氣覆寫」與「個人天氣」算進去；
    /// <see cref="GetWeatherIdForPeriod"/> 走的是純預報，兩者在紅色警戒之類的情況下
    /// <b>可能不一致</b>。UI 上兩個都畫出來，不要只畫一個然後假設它們相同。
    /// </para>
    /// </summary>
    public static unsafe byte? GetCurrentWeatherId()
    {
        if (!Ready)
            return null;

        return WeatherManager.Instance()->GetCurrentWeather();
    }

    /// <summary>
    /// 指定地區「往後第 n 個天氣時段」的天氣 id。<c>periodOffset = 0</c> 是現在這個時段。
    /// </summary>
    /// <param name="territoryTypeId">TerritoryType 列號。</param>
    /// <param name="periodOffset">時段偏移，<b>必須 ≥ 0</b>。負數一律回 null（理由見類別註解）。</param>
    public static unsafe byte? GetWeatherIdForPeriod(ushort territoryTypeId, int periodOffset)
    {
        if (periodOffset < 0 || periodOffset > MaxPeriodOffset)
            return null;

        if (!Ready)
            return null;

        return WeatherManager.Instance()->GetWeatherForDaytime(territoryTypeId, periodOffset);
    }

    /// <summary>
    /// 指定地區「往後第 n 個艾奧傑亞小時」的天氣 id。
    /// <b>hourOffset 必須 ≥ 0</b> —— 負數會讓遊戲算出一個合法但完全錯誤的答案，所以這裡直接擋掉回 null。
    /// </summary>
    public static unsafe byte? GetWeatherIdForHour(ushort territoryTypeId, int hourOffset)
    {
        if (hourOffset < 0 || hourOffset > MaxPeriodOffset * EorzeaHoursPerPeriod)
            return null;

        if (!Ready)
            return null;

        return WeatherManager.Instance()->GetWeatherForHour(territoryTypeId, hourOffset);
    }

    /// <summary>
    /// 從現在這個時段起，連續 <paramref name="count"/> 個天氣時段的天氣 id。
    /// 查不到（特徵碼失效、還沒進遊戲）時回 null —— <b>不會回一串 0</b>，
    /// 因為 0 是 <c>Weather</c> 表的合法列號，用它表示「不知道」會直接誤導。
    /// </summary>
    public static unsafe IReadOnlyList<byte>? GetPeriodForecast(ushort territoryTypeId, int count)
    {
        if (count <= 0)
            return null;

        if (count > MaxPeriodOffset + 1)
            count = MaxPeriodOffset + 1;

        if (!Ready)
            return null;

        var wm = WeatherManager.Instance();
        if (wm == null)
            return null;

        var result = new byte[count];
        for (var i = 0; i < count; i++)
            result[i] = wm->GetWeatherForDaytime(territoryTypeId, i);

        return result;
    }

    /// <summary>天氣 id → 台服天氣名稱。查不到回空字串（呼叫端要自己決定怎麼顯示「不知道」）。</summary>
    public static string GetWeatherName(uint weatherId)
        => MultiString.ParseSeString(Service.DataManager.GetExcelSheet<Weather>()?.GetRowOrDefault(weatherId)?.Name);

    /// <summary>天氣 id → 圖示 id。0 代表沒有圖示。</summary>
    public static uint GetWeatherIcon(uint weatherId)
    {
        var row = Service.DataManager.GetExcelSheet<Weather>()?.GetRowOrDefault(weatherId);
        return row == null ? 0u : (uint)row.Value.Icon;
    }

    /// <summary>
    /// 特徵碼掃不到的時候寫一行 Information。
    /// <b>刻意用 Information 不用 Debug</b> —— 使用者的記錄等級是 2，Debug 他們一行都收不到，
    /// 而這個故障的表現形式是「天氣欄永遠空白」，沒有這行 log 根本查不出原因。
    /// </summary>
    private static void ReportUnavailableOnce()
    {
        if (_reportedUnavailable)
            return;

        _reportedUnavailable = true;
        Service.PrintInfo(
            @"[天氣] WeatherManager 的特徵碼沒有解析成功，天氣預報功能整個停用（不會影響釣魚本身）。" +
            $@"Instance={WeatherManager.Addresses.Instance.Value:X} " +
            $@"GetCurrentWeather={WeatherManager.Addresses.GetCurrentWeather.Value:X} " +
            $@"GetWeatherForHour={WeatherManager.Addresses.GetWeatherForHour.Value:X} " +
            $@"GetWeatherForDaytime={WeatherManager.Addresses.GetWeatherForDaytime.Value:X}");
    }
}
