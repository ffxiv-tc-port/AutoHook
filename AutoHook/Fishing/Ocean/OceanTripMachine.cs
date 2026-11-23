using System;
using System.Collections.Generic;

// 本檔假設與契約：出海垂釣整趟自動化的決策核心。不碰遊戲、不碰 Dalamud（離線探針直接連結本檔），
// 每一拍只吃 OceanTripInputs、透過 IOceanTripEffects 下指令。Idle 時 Step 不做任何事；
// 只有 Start（使用者親手按）能離開 Idle，任何結束都回 Idle、不會自己再開始。
namespace AutoHook.Fishing.Ocean;

public enum OceanTripPhase
{
    Idle,
    WaitingToBoard,
    WaitingForZone,
    Walking,
    WaitingForUser,
    Starting,
    Fishing,
    Yielded,
}

/// <summary>與 CS <c>InstanceContentOceanFishing.OceanFishingStatus</c> 同值。</summary>
public enum OceanStatus : uint
{
    WaitingForPlayers = 0,
    SwitchingZone = 1,
    Fishing = 2,
    NewZone = 3,
    Finished = 4,
}

public enum OceanLineState
{
    NotFishing,
    LineIn,
    Busy,
}

public struct OceanTripInputs
{
    public bool LoggedIn;
    public bool PluginEnabled;
    public string? UnavailableReason;
    public bool IsFisher;
    public uint TerritoryId;
    public bool OnBoatTerritory;
    public bool OceanActive;
    public uint Route;
    public OceanStatus Status;
    public uint Zone;
    public bool Spectral;
    public float? TimeLeft;
    public OceanLineState Line;
    public bool CastAvailable;
    public bool IsMoving;
    public bool WalkRunning;
    public float DistanceToWalkTarget;
    public bool HookBusy;
    public bool WalkToRailing;
}

public interface IOceanTripEffects
{
    void Log(string message);

    /// <returns>false＝導航插件不在或沒接受，這一站不再自動走。</returns>
    bool StartWalk();

    void StopWalk();
    void Cast(string reason);
    void ApplyRule(uint zone, bool spectral);
    void Ended(string reason, bool finished);
}

/// <summary>一條「哪一站用什麼」的規則。TimeId 0＝任何時段；字串 "-"／餌 0＝不切換。</summary>
public class OceanStopRule
{
    public uint SpotId;
    public uint TimeId;
    public string Preset = @"-";
    public int BaitId;
    public string SpectralPreset = @"-";
    public int SpectralBaitId;

    public static OceanStopRule? Match(IReadOnlyList<OceanStopRule> rules, uint spotId, uint timeId)
    {
        OceanStopRule? anyTime = null;
        foreach (var rule in rules)
        {
            if (rule.SpotId != spotId)
                continue;
            if (rule.TimeId == timeId && timeId != 0)
                return rule;
            if (rule.TimeId == 0)
                anyTime ??= rule;
        }

        return anyTime;
    }
}

public sealed class OceanTripMachine(IOceanTripEffects fx)
{
    public const long StillMs = 2000;
    public const long CastRetryMs = 3000;
    public const long StartTimeoutMs = 25000;
    public const long ZoneSettleMs = 1500;
    public const long IdleRecastMs = 3000;
    public const long WalkTimeoutMs = 30000;
    public const long WalkGraceMs = 500;
    public const long RecentCastMs = 8000;
    public const int MaxCastAttempts = 5;
    public const float ArriveDistance = 1.0f;
    public const float MinTimeLeftToCast = 5f;

    public OceanTripPhase Phase { get; private set; } = OceanTripPhase.Idle;
    public int ActiveZone { get; private set; } = -1;
    public string LastNote { get; private set; } = @"";

    private uint startTerritory;
    private long phaseSince;
    private long settleUntil;
    private long lastCastAt = long.MinValue / 2;
    private long stillSince;
    private long lineInSince = -1;
    private int castAttempts;
    private bool walkedThisZone;
    private bool movedWhileYielded;
    private bool lastSpectral;
    private bool pendingRule;

    public bool Start(in OceanTripInputs i, long now, out string reason)
    {
        reason = @"";
        if (Phase != OceanTripPhase.Idle)
        {
            reason = @"已經在執行中";
            return false;
        }

        if (!i.LoggedIn)
            reason = @"角色還沒進入遊戲";
        else if (!i.PluginEnabled)
            reason = @"AutoHook 目前是關閉的（或被別的插件暫停中），先開啟 AutoHook 再按開始";
        else if (i.UnavailableReason != null)
            reason = i.UnavailableReason;
        else if ((i.OceanActive || i.OnBoatTerritory) && !i.IsFisher)
            reason = @"目前職業不是捕魚人";
        if (reason.Length > 0)
            return false;

        ActiveZone = -1;
        castAttempts = 0;
        pendingRule = false;
        lastCastAt = long.MinValue / 2;
        if (i.OceanActive || i.OnBoatTerritory)
        {
            Enter(OceanTripPhase.WaitingForZone, now);
            Note(@"開始：已在船上，等這一站開放釣魚就接手");
        }
        else
        {
            startTerritory = i.TerritoryId;
            Enter(OceanTripPhase.WaitingToBoard, now);
            Note(@"開始：等你報名上船（離開這個區域就取消）");
        }

        return true;
    }

    public void Stop(string reason)
    {
        if (Phase != OceanTripPhase.Idle)
            End(reason, false);
    }

    public void Step(in OceanTripInputs i, long now)
    {
        if (Phase == OceanTripPhase.Idle)
            return;

        if (!i.LoggedIn)
        {
            End(@"角色登出或不在遊戲中", false);
            return;
        }

        if (!i.PluginEnabled)
        {
            End(@"AutoHook 被關閉，或被別的插件暫停（例如全艦隊急停）", false);
            return;
        }

        if (i.UnavailableReason != null)
        {
            End(i.UnavailableReason, false);
            return;
        }

        if (!i.OceanActive)
        {
            StepAshore(i);
            return;
        }

        if (!i.IsFisher)
        {
            End(@"目前職業不是捕魚人", false);
            return;
        }

        if (Phase == OceanTripPhase.WaitingToBoard)
        {
            Enter(OceanTripPhase.WaitingForZone, now);
            Note(@$"已上船（航線 #{i.Route}），等第一站開放釣魚");
        }

        if (i.Status == OceanStatus.Finished)
        {
            End(@"航程結束（結算畫面請自己關）", true);
            return;
        }

        if (i.Status != OceanStatus.Fishing)
        {
            if (Phase == OceanTripPhase.Walking)
                fx.StopWalk();
            if (Phase != OceanTripPhase.WaitingForZone)
                Enter(OceanTripPhase.WaitingForZone, now);
            return;
        }

        if ((int)i.Zone != ActiveZone)
        {
            BeginZone(i, now);
            return;
        }

        if (Phase == OceanTripPhase.WaitingForZone)
        {
            Enter(OceanTripPhase.Starting, now);
            settleUntil = now + ZoneSettleMs;
        }

        StepSpectral(i);

        switch (Phase)
        {
            case OceanTripPhase.Walking:
                StepWalking(i, now);
                break;
            case OceanTripPhase.WaitingForUser:
                StepWaitingForUser(i, now);
                break;
            case OceanTripPhase.Starting:
                StepStarting(i, now);
                break;
            case OceanTripPhase.Fishing:
                StepFishing(i, now);
                break;
            case OceanTripPhase.Yielded:
                StepYielded(i, now);
                break;
        }
    }

    private void StepAshore(in OceanTripInputs i)
    {
        // 上下船的讀取畫面裡 director 會短暫不在：人還在船的區域就先等。
        if (i.OnBoatTerritory)
            return;

        if (Phase == OceanTripPhase.WaitingToBoard)
        {
            if (i.TerritoryId != 0 && i.TerritoryId != startTerritory)
                End(@"離開了按下開始時所在的區域，取消等待上船", false);
            return;
        }

        End(@"已經下船，航程結束", true);
    }

    private void BeginZone(in OceanTripInputs i, long now)
    {
        if (Phase == OceanTripPhase.Walking)
            fx.StopWalk();

        ActiveZone = (int)i.Zone;
        lastSpectral = i.Spectral;
        pendingRule = false;
        castAttempts = 0;
        walkedThisZone = false;
        movedWhileYielded = false;
        lineInSince = -1;
        Note(@$"第 {i.Zone + 1} 站開放釣魚" + (i.Spectral ? @"（幻海流中）" : @""));
        fx.ApplyRule(i.Zone, i.Spectral);
        Enter(OceanTripPhase.Starting, now);
        settleUntil = now + ZoneSettleMs;
    }

    private void StepSpectral(in OceanTripInputs i)
    {
        if (i.Spectral != lastSpectral)
        {
            lastSpectral = i.Spectral;
            pendingRule = true;
            Note(i.Spectral ? @"幻海流出現" : @"幻海流結束");
        }

        // 竿子在水裡時不換：換了 preset 會讓這一竿用錯的提鉤設定，換餌也送不出去。
        if (pendingRule && i.Line != OceanLineState.Busy)
        {
            pendingRule = false;
            fx.ApplyRule(i.Zone, lastSpectral);
        }
    }

    private void StepWalking(in OceanTripInputs i, long now)
    {
        if (i.WalkRunning)
        {
            if (now - phaseSince > WalkTimeoutMs)
            {
                fx.StopWalk();
                Note(@"走到船邊超過 30 秒還沒到，停下來等你：站定後在目前位置拋竿");
                EnterWaitingForUser(now);
            }

            return;
        }

        if (i.DistanceToWalkTarget <= ArriveDistance)
        {
            Note(@"已走到船邊");
            Enter(OceanTripPhase.Starting, now);
            settleUntil = now + WalkGraceMs;
            return;
        }

        if (now - phaseSince < WalkGraceMs)
            return;

        Note(@"走路被中斷（你手動移動，或導航被停止）：等你站定後在目前位置拋竿");
        EnterWaitingForUser(now);
    }

    private void StepWaitingForUser(in OceanTripInputs i, long now)
    {
        if (i.Line != OceanLineState.NotFishing)
        {
            Note(@"你自己拋竿了，繼續接手");
            Enter(OceanTripPhase.Fishing, now);
            return;
        }

        if (i.IsMoving)
        {
            stillSince = now;
            return;
        }

        if (now - stillSince < StillMs)
            return;

        Enter(OceanTripPhase.Starting, now);
        settleUntil = now;
    }

    private void StepStarting(in OceanTripInputs i, long now)
    {
        if (i.Line != OceanLineState.NotFishing)
        {
            Enter(OceanTripPhase.Fishing, now);
            return;
        }

        if (i.IsMoving)
        {
            EnterWaitingForUser(now);
            return;
        }

        if (now < settleUntil)
            return;

        if (!i.CastAvailable)
        {
            if (i.WalkToRailing && !walkedThisZone)
            {
                walkedThisZone = true;
                if (fx.StartWalk())
                {
                    Note(@"這裡不能拋竿，走到船邊");
                    Enter(OceanTripPhase.Walking, now);
                }
                else
                    Note(@"沒辦法自動走到船邊（需要 vnavmesh）：請自己走到船邊面向海面站好");

                return;
            }

            if (now - phaseSince > StartTimeoutMs)
                Yield(@"這裡一直不能拋竿（不在船邊、沒面向海面，或正被別的動作卡住）。走到船邊站定後會自動接手", now);
            return;
        }

        if (castAttempts >= MaxCastAttempts)
        {
            Yield(@$"連續 {MaxCastAttempts} 次拋竿都沒有成功。移動一下站定後會再試", now);
            return;
        }

        if (now - lastCastAt < CastRetryMs || TooLateToCast(i))
            return;

        lastCastAt = now;
        castAttempts++;
        fx.Cast(@$"第 {i.Zone + 1} 站拋竿（第 {castAttempts} 次嘗試）");
    }

    private void StepFishing(in OceanTripInputs i, long now)
    {
        if (i.Line == OceanLineState.NotFishing)
        {
            // 剛拋竿就回到沒在釣魚＝遊戲沒接受這一竿（船上第一竿偶爾會被拒），回去重試而不是當成你收竿。
            if (now - lastCastAt < RecentCastMs)
            {
                Note(@"拋竿沒有成功，重試");
                Enter(OceanTripPhase.Starting, now);
                settleUntil = now;
                return;
            }

            Yield(@"收竿了（你手動收竿，或 preset 設定的收竿）。這一站先讓位：你移動後站定、或自己拋竿，就會接手", now);
            return;
        }

        if (i.Line != OceanLineState.LineIn)
        {
            lineInSince = -1;
            return;
        }

        if (lineInSince < 0)
        {
            lineInSince = now;
            return;
        }

        if (now - lineInSince < IdleRecastMs || !i.CastAvailable || i.IsMoving || i.HookBusy)
            return;
        if (now - lastCastAt < CastRetryMs || TooLateToCast(i))
            return;

        lastCastAt = now;
        lineInSince = now;
        fx.Cast(@$"竿子閒置 {IdleRecastMs / 1000} 秒沒有再拋，補拋一竿");
    }

    private void StepYielded(in OceanTripInputs i, long now)
    {
        if (i.Line != OceanLineState.NotFishing)
        {
            Note(@"你自己拋竿了，繼續接手");
            Enter(OceanTripPhase.Fishing, now);
            return;
        }

        if (i.IsMoving)
        {
            movedWhileYielded = true;
            stillSince = now;
            return;
        }

        if (!movedWhileYielded || now - stillSince < StillMs)
            return;

        movedWhileYielded = false;
        castAttempts = 0;
        walkedThisZone = true;
        Note(@"你站定了，在目前位置接手拋竿");
        Enter(OceanTripPhase.Starting, now);
        settleUntil = now;
    }

    private static bool TooLateToCast(in OceanTripInputs i) => i.TimeLeft is { } t && t < MinTimeLeftToCast;

    private void EnterWaitingForUser(long now)
    {
        walkedThisZone = true;
        stillSince = now;
        Enter(OceanTripPhase.WaitingForUser, now);
    }

    private void Yield(string reason, long now)
    {
        movedWhileYielded = false;
        Note(reason);
        Enter(OceanTripPhase.Yielded, now);
    }

    private void Enter(OceanTripPhase phase, long now)
    {
        Phase = phase;
        phaseSince = now;
        if (phase != OceanTripPhase.Fishing)
            lineInSince = -1;
    }

    private void Note(string message)
    {
        LastNote = message;
        fx.Log(message);
    }

    private void End(string reason, bool finished)
    {
        if (Phase == OceanTripPhase.Walking)
            fx.StopWalk();
        Phase = OceanTripPhase.Idle;
        ActiveZone = -1;
        LastNote = reason;
        fx.Ended(reason, finished);
    }
}
