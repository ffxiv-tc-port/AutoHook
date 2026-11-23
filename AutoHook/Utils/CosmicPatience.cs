using System.Collections.Generic;
using AutoHook.Classes;
using AutoHook.Configurations;
using AutoHook.Data;
using AutoHook.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Utils;

/// <summary>
/// 宇宙探索尺寸計分任務的耐心策略。
/// ⚠️ <see cref="Auto"/> 一定要是 0：舊設定檔沒有這個鍵時反序列化會落在 <c>default</c>。
/// </summary>
public enum CosmicPatienceMode
{
    /// <summary>preset 沒有在尺寸上花 GP（沒有耐心、大魚獵手、雙重／三重提鉤）時才補放耐心II。</summary>
    Auto = 0,

    /// <summary>完全照 preset，連耐心期間的提鉤保護也不做。</summary>
    Off = 1,

    /// <summary>尺寸總和型任務優先放耐心II（即使 preset 用大魚獵手或雙重／三重提鉤）；最大尺寸型同 <see cref="Auto"/>。</summary>
    PreferForSizeSum = 2,
}

/// <summary><see cref="CosmicPatience.Decide"/> 需要的全部輸入；拆出來是為了讓判斷本身不碰遊戲狀態。</summary>
public readonly record struct CosmicPatienceInputs(
    CosmicPatienceMode Mode,
    CosmicSizeScoring Size,
    bool AutoCastsEnabled,
    bool PresetCastsPatience,
    bool PresetCastsPrizeCatch,
    bool PresetUsesMultiHook,
    bool PatienceActive,
    bool PrizeCatchActive,
    bool MakeshiftActive,
    bool MoochPending,
    uint Gp,
    int HookGpReserve,
    bool PatienceUsable);

/// <summary>判斷結果。<see cref="LogKey"/> 為 null＝暫時性的略過（狀態已在、以小釣大機會），不寫 log。</summary>
public readonly record struct CosmicPatienceDecision(bool Cast, string? LogKey, string Reason);

/// <summary>
/// 耐心／耐心II 期間（狀態 764「提鉤成功率降低」）一般提鉤成功率 −40%／−75%，雙重／三重提鉤也一樣會失敗；
/// 精準提鉤（弱咬）、強力提鉤（強咬／傳說咬）、華麗提鉤不受影響。精準／強力提鉤只有耐心期間能用。
/// 本類別兩件事：①尺寸計分任務在條件允許時自動放耐心II ②耐心期間把提鉤換成不受影響的那一種。
/// 只在宇宙探索、而且任務說明是尺寸計分（WKSMissionText 117／119／120）時生效；其餘情況完全不碰。
/// </summary>
public static class CosmicPatience
{
    public const int PatienceIICost = 560;
    public const int DefaultHookGpReserve = 150;
    private const int SafeHookCost = 50;

    private static ushort _logMission = ushort.MaxValue;
    private static readonly HashSet<string> LoggedKeys = new();

    /// <summary>純判斷：不讀遊戲、不寫 log。</summary>
    public static CosmicPatienceDecision Decide(in CosmicPatienceInputs i)
    {
        if (i.Mode == CosmicPatienceMode.Off || i.Size == CosmicSizeScoring.None)
            return new(false, null, "");

        if (!i.AutoCastsEnabled)
            return new(false, "acoff", "這個 preset 的自動施放總開關關著，不自動放耐心II");

        if (i.PresetCastsPatience)
            return new(false, "preset-patience", "preset 自己會放耐心，交給 preset");

        var prefer = i.Mode == CosmicPatienceMode.PreferForSizeSum && i.Size == CosmicSizeScoring.Sum;
        if (!prefer)
        {
            if (i.PresetCastsPrizeCatch)
                return new(false, "preset-prize", "preset 用大魚獵手逐竿（每竿必定大尺寸），不改用耐心");

            if (i.PresetUsesMultiHook)
                return new(false, "preset-multi", "preset 用雙重／三重提鉤（耐心期間會失敗），不放耐心");
        }

        if (i.PatienceActive || i.PrizeCatchActive || i.MakeshiftActive || i.MoochPending)
            return new(false, null, "");

        var need = PatienceIICost + i.HookGpReserve;
        if (i.Gp < need)
            return new(false, "gp", $"GP 不足，先不放耐心II（目前 {i.Gp}，需要 {PatienceIICost}＋保留給提鉤 {i.HookGpReserve}＝{need}）");

        if (!i.PatienceUsable)
            return new(false, "unusable", "遊戲回報耐心II現在不能用（等級不足或狀態不允許），不放");

        return new(true, null, $"放耐心II（GP {i.Gp}）");
    }

    /// <summary>目前任務的尺寸計分型別；不在宇宙探索或不是尺寸計分＝<see cref="CosmicSizeScoring.None"/>。</summary>
    public static CosmicSizeScoring CurrentSize()
        => CosmicMissionInfo.IsInCosmicZone() ? CosmicMissionInfo.GetCurrentProfile().Size : CosmicSizeScoring.None;

    /// <summary>耐心期間要不要改用不受「提鉤成功率降低」影響的提鉤。</summary>
    public static bool ShouldGuardHooks()
    {
        if (Service.Configuration.CosmicPatienceMode == CosmicPatienceMode.Off)
            return false;

        if (!PlayerRes.HasStatus(IDs.Status.InefficientHooking) && !PlayerRes.HasStatus(IDs.Status.AnglersFortune))
            return false;

        return CurrentSize() != CosmicSizeScoring.None;
    }

    /// <summary>
    /// 耐心期間這一咬該用的單鉤：弱咬精準提鉤、強咬／傳說咬強力提鉤。
    /// GP 不夠（或遊戲說不能用）就退回一般提鉤 —— 魚餌在拋竿時就用掉了，提了還有機會，放掉是零。
    /// </summary>
    public static HookType SafeSingleHook(BiteType bite)
    {
        var wanted = bite == BiteType.Weak ? HookType.Precision : HookType.Powerful;
        if (PlayerRes.ActionTypeAvailable((uint)wanted))
        {
            LogOnce("guard", "耐心期間：雙重／三重提鉤暫停，弱咬改用精準提鉤、強咬／傳說咬改用強力提鉤（華麗提鉤冷卻好時優先）");
            return wanted;
        }

        LogOnce("guard-gp", $"耐心期間 {(wanted == HookType.Precision ? "精準" : "強力")}提鉤放不出來（GP {PlayerRes.GetCurrentGp()}，每竿要 {SafeHookCost}），這一竿改用一般提鉤，成功率會降低");
        return HookType.Normal;
    }

    /// <summary>
    /// 條件成立就放耐心II。回傳 true＝已送出施放，呼叫端這一輪不要再做別的事。
    /// <paramref name="beforePreset"/>：「優先耐心」模式在 preset 的自動施放之前問，其餘在之後問。
    /// </summary>
    public static bool TryAutoCast(AutoCastsConfig acCfg, BaseHookset hookset, bool ignoreMooch, bool beforePreset)
    {
        var mode = Service.Configuration.CosmicPatienceMode;
        var size = CurrentSize();
        if (size == CosmicSizeScoring.None)
            return false;

        var preferPhase = mode == CosmicPatienceMode.PreferForSizeSum && size == CosmicSizeScoring.Sum;
        if (preferPhase != beforePreset)
            return false;

        var inputs = new CosmicPatienceInputs(
            mode,
            size,
            acCfg.EnableAll && acCfg.AllowsTimedCast(),
            acCfg.CastPatience.Enabled,
            acCfg.CastPrizeCatch.Enabled,
            UsesMultiHook(hookset),
            PlayerRes.HasStatus(IDs.Status.AnglersFortune),
            PlayerRes.HasStatus(IDs.Status.PrizeCatch),
            PlayerRes.HasStatus(IDs.Status.MakeshiftBait),
            !ignoreMooch && acCfg.DontCancelMooch && PlayerRes.IsMoochAvailable(),
            PlayerRes.GetCurrentGp(),
            Service.Configuration.CosmicPatienceHookGpReserve,
            PlayerRes.ActionTypeAvailable(IDs.Actions.Patience2));

        LogStrategyOnce(inputs);

        var decision = Decide(inputs);
        if (!decision.Cast)
        {
            if (decision.LogKey is { } key)
                LogOnce(key, decision.Reason);
            return false;
        }

        Service.PrintInfo($"[宇宙耐心] 任務#{CosmicMissionInfo.GetCurrentMissionId()} {decision.Reason}");
        PlayerRes.CastActionDelayed(IDs.Actions.Patience2, ActionType.Action, @"耐心II");
        return true;
    }

    private static bool UsesMultiHook(BaseHookset h)
        => (h.UseTripleHook && (h.TripleWeak.HooksetEnabled || h.TripleStrong.HooksetEnabled || h.TripleLegendary.HooksetEnabled))
           || (h.UseDoubleHook && (h.DoubleWeak.HooksetEnabled || h.DoubleStrong.HooksetEnabled || h.DoubleLegendary.HooksetEnabled));

    private static void LogStrategyOnce(in CosmicPatienceInputs i)
    {
        var mission = CosmicMissionInfo.GetCurrentMissionId();
        ResetIfMissionChanged(mission);
        if (!LoggedKeys.Add("strategy"))
            return;

        var mode = i.Mode switch
        {
            CosmicPatienceMode.Off => "關閉",
            CosmicPatienceMode.PreferForSizeSum => "尺寸總和型優先耐心",
            _ => "自動",
        };

        var plan = Decide(i with { PatienceActive = false, PrizeCatchActive = false, MakeshiftActive = false,
                                   MoochPending = false, Gp = uint.MaxValue, PatienceUsable = true });
        var strategy = i.Mode == CosmicPatienceMode.Off
            ? "照 preset，不做任何調整"
            : plan.Cast
                ? $"GP ≥ {PatienceIICost}＋{i.HookGpReserve} 時放耐心II"
                : plan.Reason;

        Service.PrintInfo($"[宇宙耐心] 任務#{mission} {CosmicMissionInfo.SizeLabel(i.Size)}｜模式＝{mode}｜策略＝{strategy}"
                          + (i.Mode == CosmicPatienceMode.Off ? "" : "｜耐心期間：雙重／三重提鉤暫停、弱咬精準提鉤、強咬／傳說咬強力提鉤"));
    }

    private static void LogOnce(string key, string message)
    {
        var mission = CosmicMissionInfo.GetCurrentMissionId();
        ResetIfMissionChanged(mission);
        if (LoggedKeys.Add(key))
            Service.PrintInfo($"[宇宙耐心] 任務#{mission} {message}");
    }

    private static void ResetIfMissionChanged(ushort mission)
    {
        if (mission == _logMission)
            return;

        _logMission = mission;
        LoggedKeys.Clear();
    }
}
