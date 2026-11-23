using System;
using System.Linq;
using AutoHook.Configurations;
using AutoHook.Enums;
using AutoHook.Fishing.Ocean;
using AutoHook.Utils;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;

namespace AutoHook.Ui;

/// <summary>出海垂釣整趟自動化：開始／停止、船上現況、各站規則。</summary>
public class TabOceanTrip : BaseTab
{
    public override string TabName { get; } = @"出海垂釣";
    public override bool Enabled { get; } = true;
    public override OpenWindow Type { get; } = OpenWindow.OceanTrip;

    private const string NoSwap = @"-";
    private const string AnyRoute = @"任何航線";
    private string _baitFilter = @"";

    private static OceanTripConfig Cfg => Service.Configuration.OceanTrip;

    private static float Em(float px) => px * ImGui.GetFontSize() / 16f;

    public override void DrawHeader()
    {
        DrawTabDescription(
            @"上船之後的整趟自動化：每一站開放時自動走到船邊拋竿、依海域與時段換 preset 或釣餌、幻海流出現與結束時切換，航程結束就收手。" + Environment.NewLine +
            @"每一趟都要自己按「開始」（或 /ahocean start）；載入、進區、登入都不會自己開始。報名上船與結算畫面請自己操作。" + Environment.NewLine +
            @"你收竿或走動時會讓位，站定或自己拋竿後才接手；AutoHook 被關閉或全艦隊急停時整個停下。" + Environment.NewLine +
            @"建議在使用的 preset 開「自動拋竿」：沒開時每竿之間會等 3 秒才由這裡補拋。");
    }

    public override void Draw()
    {
        DrawControls();
        DrawUtil.SpacingSeparator();
        DrawSettings();
        DrawUtil.SpacingSeparator();
        DrawRules();
    }

    private static void DrawControls()
    {
        var trip = AutoHook.Plugin.OceanTrip;
        var running = trip.Phase != OceanTripPhase.Idle;
        if (running ? ImGui.Button(@"停止###oceanStop") : ImGui.Button(@"開始###oceanStart"))
        {
            if (running)
                trip.RequestStop(@"你按了停止");
            else
                trip.RequestStart();
        }

        ImGui.SameLine();
        ImGui.TextColored(running ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey, PhaseText(trip.Phase));
        if (trip.LastNote.Length > 0)
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
            ImGui.TextWrapped(trip.LastNote);
        }

        var view = OceanTrip.ReadView();
        if (!view.OceanActive)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, @"目前不在出海垂釣的船上");
            return;
        }

        var routeName = OceanData.RouteName(view.Route);
        ImGui.TextUnformatted(@$"{(routeName.Length > 0 ? routeName : @$"航線 #{view.Route}")}　第 {view.Zone + 1} 站");
        ImGui.SameLine();
        if (view.SpotId == 0)
            ImGui.TextColored(ImGuiColors.DalamudGrey, @"?");
        else
            ImGui.TextUnformatted(@$"{OceanData.SpotName(view.SpotId)}（{OceanData.TimeName(view.TimeId)}）");
        if (view.Spectral)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.ParsedBlue, @"幻海流中");
        }

        ImGui.SameLine();
        if (view.TimeLeft is { } left)
            ImGui.TextUnformatted(@$"這一站剩 {left:F0} 秒");
        else
            ImGui.TextColored(ImGuiColors.DalamudGrey, @"這一站剩 ? 秒");
    }

    private static string PhaseText(OceanTripPhase phase) => phase switch
    {
        OceanTripPhase.Idle => @"未執行",
        OceanTripPhase.WaitingToBoard => @"等你報名上船",
        OceanTripPhase.WaitingForZone => @"等下一站開放",
        OceanTripPhase.Walking => @"走到船邊中",
        OceanTripPhase.WaitingForUser => @"等你站定",
        OceanTripPhase.Starting => @"準備拋竿",
        OceanTripPhase.Fishing => @"釣魚中",
        OceanTripPhase.Yielded => @"這一站讓位中",
        _ => phase.ToString(),
    };

    private static void DrawSettings()
    {
        if (DrawUtil.Checkbox(@"不能拋竿時自動走到船邊", ref Cfg.WalkToRailing,
                @"需要 vnavmesh。每一站最多走一次；你自己移動時會讓位，站定後在你站的位置拋竿。"))
            Service.Save();

        if (DrawUtil.Checkbox(@"結束時換回開始前的 preset", ref Cfg.RestorePresetAfterTrip,
                @"只在途中依規則換過 preset 時才換回。"))
            Service.Save();
    }

    private void DrawRules()
    {
        ImGui.TextColored(ImGuiColors.DalamudYellow, @"各站規則");
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.DalamudGrey, @"(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(@"沒有規則的站維持目前的 preset 與釣餌。" + Environment.NewLine +
                             @"航線、海域、時段都可以設「任何」；同時符合好幾條時，取指定得最多的那條（一樣多取上面那條）。" + Environment.NewLine +
                             @"例如整條航線用同一組 preset：只指定航線、海域與時段留「任何」。" + Environment.NewLine +
                             @"「幻海流」兩欄在幻海流出現時套用，結束時換回「一般」兩欄；設成不切換就不動。");

        if (ImGui.Button(@"新增規則"))
        {
            Cfg.Rules.Add(new OceanStopRule());
            Service.Save();
        }

        var view = OceanTrip.ReadView();
        var currentRoute = view.OceanActive ? OceanData.RouteName(view.Route) : @"";
        if (currentRoute.Length > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button(@"新增目前這條航線"))
            {
                Cfg.Rules.Add(new OceanStopRule { RouteName = currentRoute });
                Service.Save();
            }
        }

        if (view.OceanActive && view.SpotId != 0)
        {
            ImGui.SameLine();
            if (ImGui.Button(@"新增目前這一站"))
            {
                Cfg.Rules.Add(new OceanStopRule { SpotId = view.SpotId, TimeId = view.TimeId });
                Service.Save();
            }
        }

        if (Cfg.Rules.Count == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, @"還沒有規則：每一站都用目前選的 preset 與釣餌。");
            return;
        }

        using var table = ImRaii.Table(@"###oceanRules", 8,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollX);
        if (!table)
            return;

        ImGui.TableSetupColumn(@"航線");
        ImGui.TableSetupColumn(@"海域");
        ImGui.TableSetupColumn(@"時段");
        ImGui.TableSetupColumn(@"一般 preset");
        ImGui.TableSetupColumn(@"一般釣餌");
        ImGui.TableSetupColumn(@"幻海流 preset");
        ImGui.TableSetupColumn(@"幻海流釣餌");
        ImGui.TableSetupColumn(@"");
        ImGui.TableHeadersRow();

        var remove = -1;
        for (var k = 0; k < Cfg.Rules.Count; k++)
        {
            var rule = Cfg.Rules[k];
            using var id = ImRaii.PushId(k);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawRouteCombo(rule);
            ImGui.TableNextColumn();
            DrawSpotCombo(rule);
            ImGui.TableNextColumn();
            DrawTimeCombo(rule);
            ImGui.TableNextColumn();
            DrawPresetCombo(@"##preset", ref rule.Preset);
            ImGui.TableNextColumn();
            DrawBaitCombo(@"##bait", ref rule.BaitId);
            ImGui.TableNextColumn();
            DrawPresetCombo(@"##spectralPreset", ref rule.SpectralPreset);
            ImGui.TableNextColumn();
            DrawBaitCombo(@"##spectralBait", ref rule.SpectralBaitId);
            ImGui.TableNextColumn();
            if (ImGui.SmallButton(@"刪除"))
                remove = k;
        }

        if (remove >= 0)
        {
            Cfg.Rules.RemoveAt(remove);
            Service.Save();
        }
    }

    private static void DrawRouteCombo(OceanStopRule rule)
    {
        ImGui.SetNextItemWidth(Em(150));
        using var combo = ImRaii.Combo(@"##route", rule.RouteName.Length > 0 ? rule.RouteName : AnyRoute);
        if (!combo)
            return;

        if (ImGui.Selectable(AnyRoute, rule.RouteName.Length == 0))
        {
            rule.RouteName = @"";
            Service.Save();
        }

        foreach (var name in OceanData.RouteNames)
        {
            if (ImGui.Selectable(name, name == rule.RouteName))
            {
                rule.RouteName = name;
                Service.Save();
            }
        }
    }

    private static void DrawSpotCombo(OceanStopRule rule)
    {
        ImGui.SetNextItemWidth(Em(140));
        using var combo = ImRaii.Combo(@"##spot", OceanData.SpotName(rule.SpotId));
        if (!combo)
            return;

        if (ImGui.Selectable(OceanData.SpotName(0), rule.SpotId == 0))
        {
            rule.SpotId = 0;
            Service.Save();
        }

        foreach (var (spotId, name) in OceanData.Spots)
        {
            if (ImGui.Selectable(name, spotId == rule.SpotId))
            {
                rule.SpotId = spotId;
                Service.Save();
            }
        }
    }

    private static void DrawTimeCombo(OceanStopRule rule)
    {
        ImGui.SetNextItemWidth(Em(100));
        using var combo = ImRaii.Combo(@"##time", OceanData.TimeName(rule.TimeId));
        if (!combo)
            return;

        for (uint t = 0; t <= 3; t++)
        {
            if (ImGui.Selectable(OceanData.TimeName(t), t == rule.TimeId))
            {
                rule.TimeId = t;
                Service.Save();
            }
        }
    }

    private static void DrawPresetCombo(string label, ref string presetName)
    {
        var current = presetName;
        var missing = current != NoSwap && Service.Configuration.HookPresets.CustomPresets.All(p => p.PresetName != current);
        ImGui.SetNextItemWidth(Em(160));
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey, current == NoSwap))
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed, missing))
        {
            var preview = current == NoSwap ? @"（不切換）" : missing ? @$"{current}（找不到）" : current;
            using var combo = ImRaii.Combo(label, preview);
            if (!combo)
                return;

            if (ImGui.Selectable(@"（不切換）", current == NoSwap))
                current = NoSwap;
            foreach (var preset in Service.Configuration.HookPresets.CustomPresets)
            {
                if (ImGui.Selectable(@$"{preset.PresetName}##{preset.UniqueId}", preset.PresetName == current))
                    current = preset.PresetName;
            }
        }

        if (current == presetName)
            return;

        presetName = current;
        Service.Save();
    }

    private void DrawBaitCombo(string label, ref int baitId)
    {
        var current = baitId;
        ImGui.SetNextItemWidth(Em(160));
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey, current == 0))
        {
            var preview = current == 0 ? @"（不換）" : MultiString.GetItemName(current);
            using var combo = ImRaii.Combo(label, preview);
            if (!combo)
                return;

            ImGui.SetNextItemWidth(Em(150));
            ImGui.InputTextWithHint(@"##baitFilter", @"搜尋", ref _baitFilter, 64);
            if (ImGui.Selectable(@"（不換）", current == 0))
                current = 0;
            foreach (var bait in GameRes.Baits)
            {
                if (_baitFilter.Length > 0 && !bait.Name.Contains(_baitFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ImGui.Selectable(@$"{bait.Name}##{bait.Id}", bait.Id == current))
                    current = bait.Id;
            }
        }

        if (current == baitId)
            return;

        baitId = current;
        _baitFilter = @"";
        Service.Save();
    }
}
