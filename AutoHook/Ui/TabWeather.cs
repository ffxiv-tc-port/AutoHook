using System;
using System.Collections.Generic;
using System.Linq;
using AutoHook.Classes;
using AutoHook.Enums;
using AutoHook.Time;
using AutoHook.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using ECommons.ExcelServices;

namespace AutoHook.Ui;

/// <summary>
/// 天氣與窗口的<b>唯讀</b>顯示頁。
///
/// <para>
/// 🔴 <b>這一頁不做任何決策。</b> 不會自動預備、不會自動換餌、不會因為窗口開關而
/// 開始或停止釣魚。魚→天氣是社群量測資料不是遊戲資料表（台服的 <c>FishParameter</c>
/// 根本沒有天氣欄位），拿沒在台服驗證過的資料去自動停手，比什麼都不做還糟。
/// </para>
///
/// <para>
/// 顯示規則（艦隊慣例）：<b>「隨時要掃視的」放列上，「起疑才查的」放 tooltip；
/// 但「不知道」本身一定要在列上看得見</b> —— 所以未知一律畫成灰色的「?」，
/// 絕不畫成 0 或「關閉」。
/// </para>
/// </summary>
public class TabWeather : BaseTab
{
    public override string TabName { get; } = @"天氣與時段";
    public override bool Enabled { get; } = true;
    public override OpenWindow Type { get; } = OpenWindow.Weather;

    /// <summary>預報表格要畫幾個天氣時段。8 格 ≈ 3 小時 6 分真實時間。</summary>
    private const int ForecastRows = 8;

    /// <summary>清單一次最多畫幾列，避免過濾條件太寬時整頁都是魚。</summary>
    private const int MaxListRows = 60;

    private string _filter = string.Empty;
    /// <summary>上一次建清單用的過濾字串。null = 還沒建過。
    /// 刻意用 null 而不是某個「不可能出現的字串」當哨兵 —— 哨兵值總有一天會被撞到。</summary>
    private string? _cachedFilter;

    /// <summary>上一次建清單用的餌。uint.MaxValue 是無效的 item id，代表還沒建過。</summary>
    private uint _cachedBait = uint.MaxValue;
    private List<(ImportedFish Fish, string Name)> _cachedList = new();

    /// <summary>魚名快取。<c>MultiString.GetItemName</c> 每次都要查 Excel，2166 條逐幀查會很痛。</summary>
    private static List<(ImportedFish Fish, string Name)>? _allNames;

    public override void DrawHeader()
    {
        DrawTabDescription(
            @"顯示目前與未來的天氣，以及有時間／天氣限制的魚現在開不開得成。" + Environment.NewLine +
            @"這一頁只顯示，不會自動做任何事（不會自動預備、不會自動換餌、不會自動開關釣魚）。" + Environment.NewLine +
            @"⚠️ 魚對應到哪種天氣是社群整理的資料，不是遊戲自己的資料表，台服可能有出入；" +
            @"天氣本身則是直接問遊戲的，那個是準的。");
    }

    public override void Draw()
    {
        try
        {
            DrawWeatherSection();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawFishSection();
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e.Message);
        }
    }

    private static void DrawWeatherSection()
    {
        var territory = Service.ClientState.TerritoryType;

        ImGui.TextColored(ImGuiColors.DalamudViolet, @"地區");
        ImGui.SameLine();
        ImGui.Text(territory == 0 ? @"?" : ExcelTerritoryHelper.GetName(territory, true));

        if (!WeatherForecast.Ready)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed,
                @"天氣查詢目前不可用（還沒進遊戲，或 WeatherManager 的特徵碼失效）。");
            ImGui.TextColored(ImGuiColors.DalamudGrey,
                @"特徵碼失效時啟動記錄裡會有一行 Information，回報時附上那行就查得出來。");
            return;
        }

        var now = TimeStamp.UtcNow;
        var (etHour, etMinute) = now.CurrentEorzeaTimeOfDay();

        var current = WeatherForecast.GetCurrentWeatherId();
        var forecastNow = WeatherForecast.GetWeatherIdForPeriod(territory, 0);

        ImGui.TextColored(ImGuiColors.DalamudViolet, @"艾奧傑亞時間");
        ImGui.SameLine();
        ImGui.Text($@"{etHour:D2}:{etMinute:D2}");

        ImGui.TextColored(ImGuiColors.DalamudViolet, @"目前天氣");
        ImGui.SameLine();
        DrawWeatherValue(current);

        // 預報第 0 格與 GetCurrentWeather() 通常一樣。不一樣的時候多半是特殊天氣覆寫
        // （紅色警戒那類），那是真實存在的狀況，不是 bug —— 但一定要看得見，
        // 因為下面的窗口計算走的是預報那一條線。
        if (current != null && forecastNow != null && current.Value != forecastNow.Value)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudYellow,
                $@"（預報序列上是「{WeatherForecast.GetWeatherName(forecastNow.Value)}」，兩者不一致）");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    @"目前天氣走的是遊戲的 GetCurrentWeather()，會把特殊天氣覆寫算進去；" + Environment.NewLine +
                    @"預報序列走的是純推算。下面的時段計算用的是預報序列。");
        }

        // 天氣時段邊界對齊 Unix epoch，所以「下次換天氣」不必問遊戲，自己算就準。
        var periodEnd = WeatherTimeline.PeriodAt(now, 0).End;
        ImGui.TextColored(ImGuiColors.DalamudViolet, @"下次換天氣");
        ImGui.SameLine();
        ImGui.Text(FishWindow.FormatCountdown(periodEnd - now));

        ImGui.Spacing();

        var forecast = WeatherForecast.GetPeriodForecast(territory, ForecastRows);
        if (forecast == null)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, @"預報：?（查不到）");
            return;
        }

        using var table = ImRaii.Table(@"###AhWeatherForecast", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        if (!table)
            return;

        ImGui.TableSetupColumn(@"開始於", ImGuiTableColumnFlags.WidthFixed, 140f);
        ImGui.TableSetupColumn(@"天氣", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        for (var i = 0; i < forecast.Count; i++)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (i == 0)
                ImGui.TextColored(ImGuiColors.HealerGreen, @"現在");
            else
                ImGui.Text(FishWindow.FormatCountdown(WeatherTimeline.PeriodAt(now, i).Start - now) + @" 後");

            ImGui.TableNextColumn();
            DrawWeatherValue(forecast[i]);
        }
    }

    private static void DrawWeatherValue(byte? weatherId)
    {
        if (weatherId == null)
        {
            // 「不知道」要在列上看得見。畫成 0 或空白會直接誤導。
            ImGui.TextColored(ImGuiColors.DalamudGrey, @"?");
            return;
        }

        var name = WeatherForecast.GetWeatherName(weatherId.Value);
        if (string.IsNullOrEmpty(name))
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, $@"? (id {weatherId.Value})");
            return;
        }

        ImGui.Text(name);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($@"Weather 表列號 {weatherId.Value}");
    }

    private void DrawFishSection()
    {
        ImGui.TextColored(ImGuiColors.DalamudViolet, @"有天氣或時間條件的魚");

        ImGui.SetNextItemWidth(240f);
        ImGui.InputTextWithHint(@"###AhWeatherFishFilter", @"輸入魚名過濾（留空＝看目前餌能釣的）", ref _filter, 64);

        var bait = Service.BaitManager.CurrentBaitSwimBait;
        RefreshList(bait);

        if (_cachedList.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey,
                string.IsNullOrWhiteSpace(_filter)
                    ? $@"目前的餌（{MultiString.GetItemName(bait)}）沒有對應到任何有時間／天氣限制的魚。"
                    : @"沒有符合的魚。");
            return;
        }

        var territory = Service.ClientState.TerritoryType;
        var now = TimeStamp.UtcNow;

        using var table = ImRaii.Table(@"###AhFishWindows", 3,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY,
            new System.Numerics.Vector2(0, 260f));
        if (!table)
            return;

        ImGui.TableSetupColumn(@"魚", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn(@"狀態", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn(@"條件", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var (fish, name) in _cachedList.Take(MaxListRows))
        {
            var result = FishWindow.Evaluate(fish, territory, now);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(name);

            ImGui.TableNextColumn();
            DrawState(result, now);

            ImGui.TableNextColumn();
            ImGui.Text(ConditionText(fish));
        }

        if (_cachedList.Count > MaxListRows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(ImGuiColors.DalamudGrey, $@"（還有 {_cachedList.Count - MaxListRows} 條沒顯示，縮小過濾條件）");
        }
    }

    private static void DrawState(FishWindowResult result, TimeStamp now)
    {
        switch (result.State)
        {
            case FishWindowState.Always:
                ImGui.TextColored(ImGuiColors.DalamudGrey, @"隨時");
                break;

            case FishWindowState.Open:
                ImGui.TextColored(ImGuiColors.HealerGreen,
                    $@"開啟中 · 剩 {FishWindow.FormatCountdown(result.Window.End - now)}");
                break;

            case FishWindowState.Closed:
                ImGui.TextColored(ImGuiColors.DalamudYellow,
                    $@"{FishWindow.FormatCountdown(result.Window.Start - now)} 後");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(
                        $@"時段長度 {FishWindow.FormatCountdown(result.Window.End - result.Window.Start)}");
                break;

            default:
                // 「不知道」畫成灰色的 ?，原因放 tooltip。
                ImGui.TextColored(ImGuiColors.DalamudGrey, @"?");
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(result.UnknownReason);
                break;
        }
    }

    private static string ConditionText(ImportedFish fish)
    {
        var parts = new List<string>();

        if (FishWindow.HasTimeCondition(fish))
            parts.Add(FishWindow.Uptime(fish).PrintHours());

        if (fish.Weathers.Count > 0)
            parts.Add(@"天氣：" + string.Join(@" / ", fish.Weathers.Select(w => WeatherName(w))));

        if (fish.WeathersFrom.Count > 0)
            parts.Add(@"前置天氣：" + string.Join(@" / ", fish.WeathersFrom.Select(w => WeatherName(w))));

        return parts.Count == 0 ? @"-" : string.Join(@"　", parts);
    }

    private static string WeatherName(int weatherId)
    {
        var name = WeatherForecast.GetWeatherName((uint)weatherId);
        return string.IsNullOrEmpty(name) ? $@"?({weatherId})" : name;
    }

    /// <summary>
    /// 只有在過濾字串或餌換掉的時候才重建清單。
    /// 逐幀對 2166 條魚查 Excel 名稱會很痛，而這份資料在執行期不會變。
    /// </summary>
    private void RefreshList(uint bait)
    {
        if (_cachedFilter == _filter && _cachedBait == bait)
            return;

        _cachedFilter = _filter;
        _cachedBait = bait;

        _allNames ??= GameRes.ImportedFishes
            .Where(FishWindow.HasAnyCondition)
            .Select(f => (Fish: f, Name: MultiString.GetItemName(f.ItemId)))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        if (string.IsNullOrWhiteSpace(_filter))
        {
            _cachedList = _allNames
                .Where(x => x.Fish.InitialBait == (int)bait || x.Fish.Mooches.Contains((int)bait))
                .ToList();
            return;
        }

        var needle = _filter.Trim();
        _cachedList = _allNames
            .Where(x => x.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
