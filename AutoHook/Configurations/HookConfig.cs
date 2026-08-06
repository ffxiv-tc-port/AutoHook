using System;
using System.Collections.Generic;
using System.ComponentModel;
using AutoHook.Classes;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.Fishing;
using AutoHook.Resources.Localization;
using AutoHook.Utils;

namespace AutoHook.Configurations;

public class HookConfig : BaseOption
{
    [DefaultValue(true)] public bool Enabled = true;

    public BaitFishClass BaitFish = new();

    public BaseHookset NormalHook = new(IDs.Status.None);
    public BaseHookset IntuitionHook = new(IDs.Status.FishersIntuition);

    //todo enable more hook settings based on the current status
    //List<BaseHookset> CustomHooksets = new();

    public HookConfig()
    {
    }

    public HookConfig(BaitFishClass baitFish)
    {
        BaitFish = baitFish;
    }

    public HookConfig(int baitFishId)
    {
        BaitFish = new BaitFishClass(baitFishId);
    }

    public void SetBiteAndHookType(BiteType bite, HookType hookType, bool isIntuition = false)
    {
        BaseHookset hookset = isIntuition ? IntuitionHook : NormalHook;
        var hookDictionary = new Dictionary<BiteType, (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph)>
        {
            { BiteType.Weak, (hookset.TripleWeak, hookset.DoubleWeak, hookset.PatienceWeak) },
            { BiteType.Strong, (hookset.TripleStrong, hookset.DoubleStrong, hookset.PatienceStrong) },
            { BiteType.Legendary, (hookset.TripleLegendary, hookset.DoubleLegendary, hookset.PatienceLegendary) }
        };

        if (hookDictionary.TryGetValue(bite, out var hook))
        {
            hook.ph.HooksetEnabled = true;
            hook.ph.HooksetType = hookType;

            hook.dh.HooksetEnabled = true;
            hook.th.HooksetEnabled = true;
        }
    }

    public void SetHooksetTimer(BiteType bite, double min, double max, bool isIntuition = false)
    {
        BaseHookset hookset = isIntuition ? IntuitionHook : NormalHook;
        var hookDictionary = new Dictionary<BiteType, (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph)>
        {
            { BiteType.Weak, (hookset.TripleWeak, hookset.DoubleWeak, hookset.PatienceWeak) },
            { BiteType.Strong, (hookset.TripleStrong, hookset.DoubleStrong, hookset.PatienceStrong) },
            { BiteType.Legendary, (hookset.TripleLegendary, hookset.DoubleLegendary, hookset.PatienceLegendary) }
        };

        if (hookDictionary.TryGetValue(bite, out var hook))
        {
            hook.ph.MinHookTimer = min;
            hook.ph.MaxHookTimer = max + 1;
            hook.ph.HookTimerEnabled = true;

            hook.dh.MinHookTimer = min;
            hook.dh.MaxHookTimer = max + 1;
            hook.dh.HookTimerEnabled = true;

            hook.th.MinHookTimer = min;
            hook.th.MaxHookTimer = max + 1;
            hook.th.HookTimerEnabled = true;
        }
    }

    public void ResetAllHooksets()
    {
        ResetHooksets(NormalHook);
        ResetHooksets(IntuitionHook);
    }

    private void ResetHooksets(BaseHookset hookset)
    {
        var hookDictionary = new Dictionary<BiteType, (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph)>
        {
            { BiteType.Weak, (hookset.TripleWeak, hookset.DoubleWeak, hookset.PatienceWeak) },
            { BiteType.Strong, (hookset.TripleStrong, hookset.DoubleStrong, hookset.PatienceStrong) },
            { BiteType.Legendary, (hookset.TripleLegendary, hookset.DoubleLegendary, hookset.PatienceLegendary) }
        };

        foreach (var hookDisable in hookDictionary)
        {
            hookDisable.Value.ph.HooksetEnabled = false;
            hookDisable.Value.dh.HooksetEnabled = false;
            hookDisable.Value.th.HooksetEnabled = false;
        }
    }

    public BaseHookset GetHookset()
    {
        /*
            var requiredStatusPreset = new List<BaseHookset> { IntuitionHook };

            foreach (var preset in requiredStatusPreset)
            {
                if (PlayerRes.HasStatus(preset.RequiredStatus) && preset.UseCustomStatusHook)
                {
                    return preset;
                }
            }*/

        if (FishingManager.IntuitionStatus == IntuitionStatus.Active && IntuitionHook.UseCustomStatusHook)
        {
            return IntuitionHook;
        }

        return NormalHook;
    }

    public HookType? GetHook(BiteType bite, double timePassed)
    {
        var hookset = GetHookset();

        var hookDictionary = new Dictionary<BiteType, (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph)>
        {
            { BiteType.Weak, (hookset.TripleWeak, hookset.DoubleWeak, hookset.PatienceWeak) },
            { BiteType.Strong, (hookset.TripleStrong, hookset.DoubleStrong, hookset.PatienceStrong) },
            { BiteType.Legendary, (hookset.TripleLegendary, hookset.DoubleLegendary, hookset.PatienceLegendary) }
        };
        
        var stellarDictionary = new Dictionary<BiteType, BaseBiteConfig>
        {
            { BiteType.Weak, hookset.StellarWeak },
            { BiteType.Strong, hookset.StellarStrong },
            { BiteType.Legendary, hookset.StellarLegendary },
        };

        stellarDictionary.TryGetValue(bite, out var stellar);

        // 「自動」模式會去讀目前任務的計分方式（判不出來就退回使用者手動設定的那一態）。
        // 一次算完存起來，保證底下兩個分支看到的是同一個決定。
        var stellarFirst = hookset.ResolveStellarFirst();

        // 🔴 這是 2026-08-06 實機回報「一直不提鉤，只有華麗提鉤 CD 完才會提」的修法。
        //    根因不在華麗提鉤，而在 preset 的提鉤時間窗：ICE 內建的
        //    [488] EX: Coexisting Species I 把 Weak 限制在 16~20 秒、Strong 限制在 20~25 秒，
        //    而且 UseDoubleHook / UseTripleHook 都是 false。窗外的咬鉤一律 "No hook found, using Rest"。
        //    實機 log 逐筆對得上：13.0 秒的咬鉤放生、18.5 秒（Strong）放生、
        //    21.5 秒（Strong，落在窗內）用普通提鉤、16.0／23.5 秒落在窗內時用華麗提鉤。
        //
        //    問題是那份 preset 的時間窗是拿來「挑特定魚種」的（名字就叫 HEAVY rng），
        //    但這個任務（WKSMissionText 121）的計分是「每條魚均給予評價」——
        //    任何一條魚都算分，放掉六成的咬鉤是純粹的損失。
        //
        //    所以只在任務說明**自己講明**「任意／每條魚都算分」時（115、121）忽略時間窗。
        //    113/114/141 那種「特定／目標水產品」的任務**不套用** —— 那裡的時間窗是選魚用的。
        var ignoreTimers = hookset.ShouldIgnoreHookTimers();

        Service.Status = "";

        if (hookDictionary.TryGetValue(bite, out var hook))
        {
            // 華麗提鉤（宇宙探索）—— 排在雙重／三重之前。
            if (stellarFirst && ShouldUseStellarHook(hookset, hook, stellar, timePassed, ignoreTimers))
                return HookType.Stellar;

            // Triple Hook
            if (hookset.UseTripleHook && hook.th.HooksetEnabled)
            {
                if (CheckHookCondition(hook.th, timePassed, ignoreTimers))
                    if (IsHookAvailable(hook.th))
                        return hook.th.HooksetType;

                if (hookset.LetFishEscapeTripleHook && PlayerRes.GetCurrentGp() < 700)
                {
                    Service.Status = "Not enough GP to use Triple Hook, Letting fish escape is enabled";
                    return HookType.None;
                }

                Service.Status = $"(Triple Hook) {Service.Status}";
            }

            // Double Hook
            if (hookset.UseDoubleHook && hook.dh.HooksetEnabled)
            {
                if (CheckHookCondition(hook.dh, timePassed, ignoreTimers))
                    if (IsHookAvailable(hook.dh))
                        return hook.dh.HooksetType;

                if (hookset.LetFishEscapeDoubleHook && PlayerRes.GetCurrentGp() < 400)
                {
                    Service.Status = "Not enough GP to use Double Hook, Letting fish escape is enabled";
                    return HookType.None;
                }
                
                Service.Status = $"(Triple Hook) {Service.Status}";
            }

            // 華麗提鉤（宇宙探索）—— 另一種順位：雙重／三重沒有出手時才補位，
            // 但仍然排在精準／強力提鉤之前（它不耗 GP，而且評價比較高）。
            if (!stellarFirst && ShouldUseStellarHook(hookset, hook, stellar, timePassed, ignoreTimers))
                return HookType.Stellar;

            // Normal - Patience
            if (hook.ph.HooksetEnabled)
            {
                if (CheckHookCondition(hook.ph, timePassed, ignoreTimers))
                    return IsHookAvailable(hook.ph) ? hook.ph.HooksetType : HookType.Normal;
                
                Service.Status = $"(Normal/Patience Hook) {Service.Status}";
            }
            else if (Service.Status == "")
                Service.Status = UIStrings.Status_NoHookEnabled;
        }

        //Service.Status = "Skipping bite - No hook for this bite is enabled";
        return HookType.None;
    }

    /// <summary>
    /// 這一咬要不要改用華麗提鉤（動作 ID 41278，宇宙探索的探索任務專用）。
    ///
    /// 🔑「可不可以用」完全交給遊戲自己判斷（<c>GetActionStatus</c> + 復唱時間），
    ///    不用地區 ID、也不用計時去猜。所以：
    ///    * 在宇宙探索以外，這個判斷永遠是 false → 這個功能不會改變任何既有行為。
    ///    * 假設不成立時（例如某天它變成別的地方也能用），最壞情況是「多放了一次提鉤動作」，
    ///      不會亂放到別的技能，也不會有指標相關的風險。
    ///
    /// 回傳 false 時**不會**降級成普通提鉤，而是讓呼叫端繼續往下走原本設定好的提鉤順序。
    /// </summary>
    /// <param name="hook">同一個 bite 型別原本的三重／雙重／精準設定，用來判斷這一咬本來要不要提鉤。</param>
    private bool ShouldUseStellarHook(BaseHookset hookset,
        (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph) hook,
        BaseBiteConfig? stellar, double timePassed, bool ignoreTimers)
    {
        if (!hookset.UseStellarHook || stellar is not { HooksetEnabled: true })
            return false;

        // 🔴 這個閘門不能拿掉。preset 把某個 bite 型別的三個提鉤全部關掉，
        //    意思是「這一咬**故意**放它跑」（例如只要傳說級、小咬一律不理）。
        //    華麗提鉤預設是開的，少了這道閘門就會把本來該放走的魚也勾起來 ——
        //    這是回退既有行為，而且對使用者表現成「怎麼一直釣到不要的魚」。
        //    實測：ICE 內建的 94 份宇宙 preset 裡，光是 Patience 那三格就有
        //    141 個欄位是刻意關掉的（Weak 60、Legendary 56、Strong 25）。
        var biteIsHookedAtAll = hook.ph.HooksetEnabled
                                || (hookset.UseTripleHook && hook.th.HooksetEnabled)
                                || (hookset.UseDoubleHook && hook.dh.HooksetEnabled);

        if (!biteIsHookedAtAll)
            return false;

        // 先問可用性再檢查條件：CheckHookCondition 失敗時會寫 Service.Status，
        // 而「華麗提鉤只是還在 CD」不該把使用者看得到的狀態文字洗掉。
        if (!PlayerRes.ActionTypeAvailable((uint)HookType.Stellar))
            return false;

        // ⚠️ 條件要沿用「這一咬原本那一格」的設定，不能用華麗提鉤自己那格的空白預設。
        //    華麗提鉤只是換一個提鉤**動作**，不該自帶一套條件 ——
        //    原本用 stellar 自己的空白設定，結果是 preset 設好的時間窗對華麗提鉤完全無效，
        //    表現成「preset 說要放掉的咬鉤，只有華麗提鉤會去勾」。
        var conditionSource = hook.ph.HooksetEnabled ? hook.ph : stellar;

        var statusBefore = Service.Status;
        if (!CheckHookCondition(conditionSource, timePassed, ignoreTimers))
        {
            Service.Status = statusBefore;
            return false;
        }

        // Information 等級：使用者跑 LogLevel 2，Debug 收不到。
        // 華麗提鉤有 60 秒 CD，所以這行最多每分鐘一次，不需要另外節流。
        Service.PrintInfo(@$"[HookManager] 華麗提鉤可用且條件符合，本次改用華麗提鉤（動作 ID {(uint)HookType.Stellar}）。");
        return true;
    }

    private bool CheckHookCondition(BaseBiteConfig hookType, double timePassed, bool ignoreTimers)
    {
        if (!CheckIdenticalCast(hookType))
            return false;

        if (!CheckSurfaceSlap(hookType))
            return false;
        
        if (!CheckPrizeCatch(hookType))
            return false;

        if (!ignoreTimers && !CheckTimer(hookType, timePassed))
            return false;

        return true;
    }

    private bool IsHookAvailable(BaseBiteConfig hookType)
    {
        if (!PlayerRes.ActionTypeAvailable((uint)hookType.HooksetType))
        {
            Service.Status = $"Not available. Normal hook will be used instead";
            return false;
        }

        return true;
    }

    private bool CheckIdenticalCast(BaseBiteConfig hookType)
    {
        if (hookType.OnlyWhenActiveIdentical && !PlayerRes.HasStatus(IDs.Status.IdenticalCast))
        {
            Service.Status = UIStrings.Status_IdenticalCastRequired;
            return false;
        }

        if (hookType.OnlyWhenNotActiveIdentical && PlayerRes.HasStatus(IDs.Status.IdenticalCast))
        {
            Service.Status = UIStrings.Status_IdenticalCastNotRequired;
            return false;
        }

        return true;
    }

    private bool CheckPrizeCatch(BaseBiteConfig hookType)
    {
        if (hookType.PrizeCatchReq && !PlayerRes.HasStatus(IDs.Status.PrizeCatch))
        {
            Service.Status = UIStrings.Status_PrizeCatchRequired;
            return false;
        }

        if (hookType.PrizeCatchNotReq && PlayerRes.HasStatus(IDs.Status.PrizeCatch))
        {
            Service.Status = UIStrings.Status_PrizeCatchNotRequired;
        }
        
        return true;
    }

    private bool CheckSurfaceSlap(BaseBiteConfig hookType)
    {
        if (hookType.OnlyWhenActiveSlap && !PlayerRes.HasStatus(IDs.Status.SurfaceSlap))
        {
            Service.Status = UIStrings.Status_SurfaceSlapRequired;
            return false;
        }

        if (hookType.OnlyWhenNotActiveSlap && PlayerRes.HasStatus(IDs.Status.SurfaceSlap))
        {
            Service.Status = UIStrings.Status_SurfaceSlapNotRequired;
            return false;
        }

        return true;
    }

    private bool CheckTimer(BaseBiteConfig hookType, double timePassed)
    {
        double minimumTime = 0;
        double maximumTime = 0;

        if (PlayerRes.HasStatus(IDs.Status.Chum))
        {
            if (hookType.ChumTimerEnabled)
            {
                minimumTime = hookType.ChumMinHookTimer;
                maximumTime = hookType.ChumMaxHookTimer;
            }
        }
        else if (hookType.HookTimerEnabled)
        {
            minimumTime = hookType.MinHookTimer;
            maximumTime = hookType.MaxHookTimer;
        }

        if (minimumTime > 0 && timePassed < minimumTime)
        {
            Service.Status = $"Skipping bite - Minimum time has not been met - Current: {timePassed} < Min: {minimumTime}";
            return false;
        }

        if (maximumTime > 0 && timePassed > maximumTime)
        {
            Service.Status = $"Skipping bite - Maximum time has been exceeded - Current: {timePassed} > Max: {maximumTime}";
            return false;
        }

        return true;
    }

    public override void DrawOptions()
    {
    }

    public override bool Equals(object? obj)
    {
        return obj is HookConfig settings &&
               BaitFish == settings.BaitFish;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(UniqueId);
    }
}