using System;
using AutoHook.Classes;
using AutoHook.Data;
using AutoHook.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.Utils;

/// <summary>
/// 宇宙探索「專一垂釣後等華麗提鉤」：專一垂釣生效時下一竿必定是同一條魚，那條魚最早 N 秒才咬
/// （內建魚池資料；放不了引誘，所以是不加引誘的秒數）。華麗提鉤冷卻還剩超過 N 秒就先不拋竿，
/// 等到剩 N 秒再拋，魚咬的時候剛好轉好。剩餘冷卻＝遊戲復唱計時與「本插件上次放華麗提鉤後 60 秒」取大。
/// 只在框架執行緒呼叫；只有 preset 開了 <see cref="BaseHookset.WaitStellarForIdenticalCast"/> 才會等。
/// </summary>
public static class CosmicStellarWait
{
    public const double StellarRecastSeconds = 60.0;

    /// <summary>單次最多等這麼久，超過就照常拋竿：任何一個輸入讀錯都不會變成一直不拋竿。</summary>
    public const double MaxWaitSeconds = 45.0;

    private static long _lastStellarTick;
    private static ushort _lastStellarMission;
    private static bool _waitLogged;

    public static void RecordStellarHook()
    {
        _lastStellarTick = Environment.TickCount64;
        _lastStellarMission = CosmicMissionInfo.IsInCosmicZone() ? CosmicMissionInfo.GetCurrentMissionId() : (ushort)0;
    }

    /// <summary>true＝這一刻先不要拋竿（含拋竿前的動作）。</summary>
    public static bool ShouldHoldCast(BaseHookset hookset, int lastCatchFishId)
    {
        if (!hookset.WaitStellarForIdenticalCast || !hookset.UseStellarHook || lastCatchFishId <= 0
            || !PlayerRes.HasStatus(IDs.Status.IdenticalCast) || !CosmicMissionInfo.IsInCosmicZone())
            return NotWaiting();

        var mission = CosmicMissionInfo.GetCurrentMissionId();
        if (mission == 0)
            return NotWaiting();

        var biteMin = double.NaN;
        foreach (var p in CosmicFishPrior.ForMission(mission))
        {
            if (p.FishId == (uint)lastCatchFishId)
            {
                biteMin = p.TimeMin;
                break;
            }
        }

        var own = mission == _lastStellarMission && _lastStellarTick != 0
            ? StellarRecastSeconds - (Environment.TickCount64 - _lastStellarTick) / 1000.0
            : 0;
        var remaining = Math.Max(PlayerRes.GetCooldown((uint)HookType.Stellar, ActionType.Action), own);
        var wait = WaitSeconds(biteMin, remaining, PlayerRes.GetStatusTime(IDs.Status.IdenticalCast));
        if (wait <= 0)
            return NotWaiting();

        Service.Status = @$"專一垂釣中：等華麗提鉤冷卻（約 {wait:0} 秒）再拋竿";
        if (!_waitLogged)
        {
            _waitLogged = true;
            Service.PrintInfo(@$"[宇宙華麗等待] 任務#{mission} 專一垂釣中（魚#{lastCatchFishId} 最早 {biteMin:0} 秒咬），" +
                              @$"華麗提鉤冷卻還剩 {remaining:0.0} 秒：先等 {wait:0.0} 秒再拋竿");
        }

        return true;
    }

    private static bool NotWaiting()
    {
        _waitLogged = false;
        return false;
    }

    /// <summary>
    /// 純判斷：還要等幾秒（≤0＝不用等）。魚的咬鉤秒數不知道、要等超過 <see cref="MaxWaitSeconds"/>、
    /// 專一垂釣有倒數而且撐不到魚咬，都回 0。<paramref name="identicalCastRemaining"/> ≤0＝沒有倒數。
    /// </summary>
    internal static double WaitSeconds(double fishBiteMin, double stellarRemaining, double identicalCastRemaining)
    {
        if (double.IsNaN(fishBiteMin) || fishBiteMin <= 0 || double.IsNaN(stellarRemaining))
            return 0;

        var wait = stellarRemaining - fishBiteMin;
        if (wait <= 0 || wait > MaxWaitSeconds)
            return 0;

        if (identicalCastRemaining > 0 && identicalCastRemaining < wait + fishBiteMin)
            return 0;

        return wait;
    }
}
