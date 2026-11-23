using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AutoHook.Configurations;
using AutoHook.Enums;
using AutoHook.Utils;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FishingState = AutoHook.Enums.FishingState;

// 本檔假設與契約：OceanTripMachine 的遊戲端轉接。只在框架執行緒跑（Framework.Update、指令、設定視窗），
// 原生指標只在讀取當下用、不留到下一拍；跨拍只存數值（座標、列號、GUID 字串）。
// 不按任何原生視窗（報名、結算畫面一律不碰），只拋竿、換 preset／釣餌、透過 vnavmesh 走路。
namespace AutoHook.Fishing.Ocean;

internal sealed class OceanTrip : IOceanTripEffects, IDisposable
{
    private const uint FisherJobId = 18;
    private const long StepIntervalMs = 100;
    private const long MovingWindowMs = 300;
    private const float MoveEpsilon = 0.03f;
    private const string NavOwner = @"AutoHook.OceanTrip";
    private const string LogPrefix = @"[出海垂釣] ";

    // 船邊可拋竿的範圍（兩艘船同一套甲板），座標取自上游 AutoHook 的 ValidFishingRegions。
    private const float DeckY = 6.711f;
    private static readonly (float X, float MinZ, float MaxZ)[] Rails =
    [
        (7.1f, -12f, -4f),
        (7.1f, -2f, 3f),
        (-7.1f, -11f, 3.5f),
    ];

    private static OceanTripConfig Cfg => Service.Configuration.OceanTrip;

    private readonly OceanTripMachine _machine;
    private long _nextStepAt;
    private bool _hasPos;
    private Vector3 _lastPos;
    private long _lastMovedAt;
    private Vector3 _walkTarget;
    private string? _presetBefore;
    private bool _presetChanged;

    public OceanTripView View { get; private set; }
    public OceanTripPhase Phase => _machine.Phase;
    public string LastNote => _machine.LastNote;

    public OceanTrip()
    {
        _machine = new OceanTripMachine(this);
        Service.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Service.Framework.Update -= OnUpdate;
        _machine.Stop(@"AutoHook 正在卸載");
    }

    public void RequestStart()
    {
        var now = Environment.TickCount64;
        _hasPos = false;
        var inputs = Read(now);
        if (!_machine.Start(inputs, now, out var reason))
        {
            ChatQueue.Print(@$"[AutoHook] 出海垂釣沒有開始：{reason}");
            Service.PrintInfo(LogPrefix + @$"沒有開始：{reason}");
            return;
        }

        _presetBefore = Service.Configuration.HookPresets.SelectedGuid;
        _presetChanged = false;
        ChatQueue.Print(@"[AutoHook] 出海垂釣自動化已開始（/ahocean stop 或設定視窗的停止鈕可隨時停下）");
    }

    public void RequestStop(string reason) => _machine.Stop(reason);

    private void OnUpdate(IFramework _)
    {
        if (_machine.Phase == OceanTripPhase.Idle)
            return;

        var now = Environment.TickCount64;
        if (now < _nextStepAt)
            return;
        _nextStepAt = now + StepIntervalMs;

        var inputs = Read(now);
        _machine.Step(inputs, now);
    }

    private OceanTripInputs Read(long now)
    {
        var player = Service.Objects.LocalPlayer;
        var i = new OceanTripInputs
        {
            LoggedIn = Service.ClientState.IsLoggedIn && player != null,
            PluginEnabled = Service.Configuration.EffectivePluginEnabled,
            UnavailableReason = AutoHook.Plugin.HookManager.UnavailableReason,
            TerritoryId = Service.ClientState.TerritoryType,
            WalkToRailing = Cfg.WalkToRailing,
            HookBusy = Service.TaskManager.IsBusy,
        };
        i.OnBoatTerritory = OceanData.IsBoatTerritory(i.TerritoryId);

        if (player != null)
        {
            i.IsFisher = player.ClassJob.RowId == FisherJobId;
            var pos = player.Position;
            if (!_hasPos || Vector3.Distance(pos, _lastPos) > MoveEpsilon)
            {
                if (_hasPos)
                    _lastMovedAt = now;
                _lastPos = pos;
                _hasPos = true;
            }

            i.IsMoving = now - _lastMovedAt < MovingWindowMs;
            i.DistanceToWalkTarget = Vector2.Distance(new Vector2(pos.X, pos.Z), new Vector2(_walkTarget.X, _walkTarget.Z));
        }

        View = ReadView();
        i.OceanActive = View.OceanActive;
        i.Route = View.Route;
        i.Status = View.Status;
        i.Zone = View.Zone;
        i.Spectral = View.Spectral;
        i.TimeLeft = View.TimeLeft;

        var line = Service.BaitManager.FishingState;
        i.Line = line switch
        {
            FishingState.NotFishing => OceanLineState.NotFishing,
            FishingState.PoleReady => OceanLineState.LineIn,
            _ => OceanLineState.Busy,
        };
        i.CastAvailable = i.LoggedIn && PlayerRes.IsCastAvailable();
        if (_machine.Phase == OceanTripPhase.Walking)
            i.WalkRunning = NavIsRunning();

        return i;
    }

    /// <summary>船上狀態的數值快照（不在船上＝OceanActive false）。設定視窗沒在跑時也用它顯示。</summary>
    public static unsafe OceanTripView ReadView()
    {
        var eventFramework = EventFramework.Instance();
        var ocean = eventFramework == null ? null : eventFramework->GetInstanceContentOceanFishing();
        if (ocean == null)
            return default;

        var route = ocean->CurrentRoute;
        var zone = ocean->CurrentZone;
        var left = ocean->InstanceContentDirector.ContentDirector.ContentTimeLeft - ocean->TimeOffset;
        var (spot, time) = OceanData.Stop(route, zone);
        return new OceanTripView(true, route, (OceanStatus)(uint)ocean->Status, zone, spot, time,
            ocean->SpectralCurrentActive, left is >= 0f and <= 3600f ? left : null);
    }

    void IOceanTripEffects.Log(string message) => Service.PrintInfo(LogPrefix + message);

    void IOceanTripEffects.Cast(string reason)
    {
        Service.PrintInfo(LogPrefix + reason);
        AutoHook.Plugin.HookManager.TripCast();
    }

    bool IOceanTripEffects.StartWalk()
    {
        var player = Service.Objects.LocalPlayer;
        if (player == null)
            return false;

        var target = PickRailPoint(player);
        var approach = new Vector3(target.X > 0 ? target.X - 1.1f : target.X + 1.1f, DeckY, target.Z);
        _walkTarget = target;
        try
        {
            Service.PluginInterface.GetIpcSubscriber<List<Vector3>, bool, string, object>(@"vnavmesh.Path.MoveToOwned")
                .InvokeAction([approach, target], false, NavOwner);
        }
        catch (Exception e)
        {
            Service.PrintInfo(LogPrefix + @$"vnavmesh 走路要求送不出去（{e.GetType().Name}：{e.Message}）");
            return false;
        }

        Service.PrintInfo(LogPrefix + @$"走向船邊 {target.X:F1}, {target.Z:F1}（目前 {player.Position.X:F1}, {player.Position.Z:F1}）");
        return true;
    }

    void IOceanTripEffects.StopWalk()
    {
        try
        {
            Service.PluginInterface.GetIpcSubscriber<string, bool>(@"vnavmesh.Path.StopIfOwner").InvokeFunc(NavOwner);
        }
        catch (Exception e)
        {
            Service.PrintInfo(LogPrefix + @$"停止 vnavmesh 走路失敗（{e.GetType().Name}）");
        }
    }

    private static bool NavIsRunning()
    {
        try
        {
            return Service.PluginInterface.GetIpcSubscriber<string, bool>(@"vnavmesh.Path.IsRunningOwned").InvokeFunc(NavOwner);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // 選離其他玩家最遠（夠遠就好）的船邊點，平手時選離自己近的，避免和別人疊在同一格。
    private static Vector3 PickRailPoint(IPlayerCharacter self)
    {
        var me = new Vector2(self.Position.X, self.Position.Z);
        var others = Service.Objects.OfType<IPlayerCharacter>()
            .Where(p => p.EntityId != self.EntityId)
            .Select(p => new Vector2(p.Position.X, p.Position.Z))
            .ToList();

        var best = new Vector3(Rails[0].X, DeckY, (Rails[0].MinZ + Rails[0].MaxZ) / 2);
        var bestClear = -1f;
        var bestDist = float.MaxValue;
        foreach (var (x, minZ, maxZ) in Rails)
        {
            for (var k = 0; k <= 4; k++)
            {
                var z = minZ + (maxZ - minZ) * k / 4f;
                var p = new Vector2(x, z);
                var clear = others.Count == 0 ? 3f : Math.Min(3f, others.Min(o => Vector2.Distance(o, p)));
                var dist = Vector2.Distance(me, p);
                if (clear > bestClear + 0.01f || (Math.Abs(clear - bestClear) <= 0.01f && dist < bestDist))
                {
                    bestClear = clear;
                    bestDist = dist;
                    best = new Vector3(x, DeckY, z);
                }
            }
        }

        return best;
    }

    void IOceanTripEffects.ApplyRule(uint zone, bool spectral)
    {
        var (spot, time) = OceanData.Stop(View.Route, zone);
        var where = @$"第 {zone + 1} 站 {OceanData.SpotName(spot)}（{OceanData.TimeName(time)}）{(spectral ? @"幻海流" : @"一般")}";
        var rule = OceanStopRule.Match(Cfg.Rules, spot, time);
        if (rule == null)
        {
            Service.PrintInfo(LogPrefix + @$"{where}：沒有設定規則，維持目前的 preset 與釣餌");
            return;
        }

        var parts = new List<string>();
        var presetName = spectral ? rule.SpectralPreset : rule.Preset;
        if (!string.IsNullOrEmpty(presetName) && presetName != @"-")
            parts.Add(SwapPreset(presetName));

        var baitId = spectral ? rule.SpectralBaitId : rule.BaitId;
        if (baitId > 0)
        {
            var result = Service.BaitManager.ChangeBait((uint)baitId);
            parts.Add(@$"釣餌 → {MultiString.GetItemName(baitId)}：{result}");
        }

        Service.PrintInfo(LogPrefix + @$"{where}：" + (parts.Count == 0 ? @"規則設定為不切換" : string.Join(@"；", parts)));
    }

    private string SwapPreset(string name)
    {
        var presets = Service.Configuration.HookPresets;
        var preset = presets.CustomPresets.FirstOrDefault(p => p.PresetName == name);
        if (preset == null)
            return @$"找不到 preset「{name}」，沒有切換";
        if (presets.SelectedPreset == preset)
            return @$"preset 已經是「{name}」";

        presets.SelectedPreset = preset;
        _presetChanged = true;
        Service.Save();
        return @$"preset → 「{name}」";
    }

    void IOceanTripEffects.Ended(string reason, bool finished)
    {
        Service.PrintInfo(LogPrefix + (finished ? @"完成：" : @"停止：") + reason);
        ChatQueue.Print(@$"[AutoHook] 出海垂釣自動化{(finished ? @"結束" : @"已停止")}：{reason}");

        if (!_presetChanged || !Cfg.RestorePresetAfterTrip)
            return;

        _presetChanged = false;
        var presets = Service.Configuration.HookPresets;
        var before = presets.CustomPresets.FirstOrDefault(p => p.UniqueId.ToString() == _presetBefore);
        presets.SelectedPreset = before;
        Service.Save();
        Service.PrintInfo(LogPrefix + @$"preset 換回開始前的「{before?.PresetName ?? Service.GlobalPresetName}」");
    }
}

internal readonly record struct OceanTripView(
    bool OceanActive,
    uint Route,
    OceanStatus Status,
    uint Zone,
    uint SpotId,
    uint TimeId,
    bool Spectral,
    float? TimeLeft);
