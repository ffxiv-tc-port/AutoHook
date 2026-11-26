using System;
using System.Collections.Generic;
using System.ComponentModel;
using AutoHook.Classes;
using AutoHook.Conditions;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.Fishing;
using AutoHook.Resources.Localization;
using AutoHook.Utils;

namespace AutoHook.Configurations;

/// <summary>上游 v6+：釣到這條魚之後，下一竿要不要改用游動餌欄位裡的牠當餌。</summary>
public class SwimbaitConfig
{
    public bool UseSwimbait = false;
    public ConditionSet? ConditionSet { get; set; }
}

public class HookConfig : BaseOption
{
    [DefaultValue(true)] public bool Enabled = true;

    public BaitFishClass BaitFish = new();

    public BaseHookset NormalHook = new(IDs.Status.None);
    public BaseHookset IntuitionHook = new(IDs.Status.FishersIntuition);

    // 匯入字串可能寫 null；一律換成預設（不用游動餌），呼叫端不必再判 null。
    public SwimbaitConfig SwimbaitNormal { get => _swimbaitNormal; set => _swimbaitNormal = value ?? new(); }
    public SwimbaitConfig SwimbaitIntuition { get => _swimbaitIntuition; set => _swimbaitIntuition = value ?? new(); }
    private SwimbaitConfig _swimbaitNormal = new();
    private SwimbaitConfig _swimbaitIntuition = new();

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

    /// <summary>與 <see cref="GetHookset"/> 同一個「漁師之識中」判斷。</summary>
    public SwimbaitConfig GetSwimbaitConfig()
        => FishingManager.IntuitionStatus == IntuitionStatus.Active && IntuitionHook.UseCustomStatusHook
            ? SwimbaitIntuition
            : SwimbaitNormal;

    public HookType? GetHook(BiteType bite, double timePassed) => GetHook(bite, timePassed, out _);

    /// <param name="noHookReason">回傳 <see cref="HookType.None"/> 時「為什麼不提」的說明（進 log 用）；有提鉤時為 null。</param>
    public HookType? GetHook(BiteType bite, double timePassed, out string? noHookReason)
    {
        noHookReason = null;
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

        // 🔴 只在任務說明**自己講明**「任意／每條魚都算分」時（WKSMissionText 115、121）忽略時間窗：
        //    宇宙 preset 的時間窗多半是拿來「挑特定魚種」的，在每條魚都算分的任務裡放掉咬鉤是純粹的損失。
        //    113/114/141 那種「特定／目標水產品」的任務**不套用** —— 那裡的時間窗是選魚用的。
        var ignoreTimers = hookset.ShouldIgnoreHookTimers();

        Service.Status = "";

        if (hookDictionary.TryGetValue(bite, out var hook))
        {
            var hookedAtAll = IsBiteHookedAtAll(hookset, hook);
            var unconfiguredNote = string.Empty;

            if (!hookedAtAll)
            {
                var blocker = UnconfiguredBiteBlocker(hookset);
                if (blocker == null)
                {
                    Service.PrintInfo(
                        @$"[HookManager] 每條魚都算分的任務（任務#{CosmicMissionInfo.GetCurrentMissionId()}）：" +
                        @$"preset 沒開{CosmicCatchLog.BiteLabel(bite)}的提鉤，改用一般提鉤、不歇竿" +
                        @$"｜拋竿後 {timePassed.ToString(@"0.00", System.Globalization.CultureInfo.InvariantCulture)} 秒");
                    return HookType.Normal;
                }

                unconfiguredNote = blocker;
            }

            if (CosmicPatience.ShouldGuardHooks())
            {
                var guarded = GetPatienceSafeHook(hookset, hook, stellar, bite, timePassed, ignoreTimers);
                if (guarded == HookType.None)
                    noHookReason = DescribeSkip(hookedAtAll, false, unconfiguredNote);
                return guarded;
            }

            LogPresetMultiHookOff(hookset, hook, bite);

            var multiHookUnavailable = false;
            var noMultiHook = ShouldSkipMultiHook(CosmicMissionInfo.GetCurrentProfile().DistinctSpecies,
                Service.Configuration.NoMultiHookWhenDistinctSpecies);
            var multiHookWanted = false;

            // 華麗提鉤（宇宙探索）—— 排在雙重／三重之前。
            if (stellarFirst && ShouldUseStellarHook(hookset, hook, stellar, timePassed, ignoreTimers))
                return HookType.Stellar;

            var yieldGate = noMultiHook ? MultiHookGate.NoData : CosmicMultiHookYield.GateForBite(bite, timePassed);
            var skipDouble = noMultiHook || !yieldGate.DoubleWorth;
            var skipTriple = noMultiHook || !yieldGate.TripleWorth
                             || (yieldGate.PreferDouble && !skipDouble && hookset.UseDoubleHook && hook.dh.HooksetEnabled
                                 && CheckHookCondition(hook.dh, timePassed, ignoreTimers)
                                 && PlayerRes.ActionTypeAvailable((uint)hook.dh.HooksetType));

            if (skipTriple || skipDouble)
            {
                // 只記「preset 想在這一咬放、但這裡不放的雙重／三重」，提鉤種類在下面換成一般提鉤；GP 不足放掉魚的設定也不套用。
                multiHookWanted = (skipTriple && hookset.UseTripleHook && hook.th.HooksetEnabled && CheckHookCondition(hook.th, timePassed, ignoreTimers))
                                  || (skipDouble && hookset.UseDoubleHook && hook.dh.HooksetEnabled && CheckHookCondition(hook.dh, timePassed, ignoreTimers));

                if (multiHookWanted && !noMultiHook)
                    Service.PrintInfo(
                        @$"[HookManager] 每條魚都算分的任務（任務#{CosmicMissionInfo.GetCurrentMissionId()}）：" +
                        @$"{CosmicCatchLog.BiteLabel(bite)}拋竿後 {timePassed.ToString(@"0.00", System.Globalization.CultureInfo.InvariantCulture)} 秒，" +
                        @$"不放{(skipTriple && skipDouble ? @"雙重／三重" : skipTriple ? @"三重" : @"雙重")}提鉤｜{CosmicMultiHookYield.Describe(yieldGate)}");
            }

            // Triple Hook
            if (!skipTriple && hookset.UseTripleHook && hook.th.HooksetEnabled)
            {
                if (CheckHookCondition(hook.th, timePassed, ignoreTimers))
                {
                    if (IsHookAvailable(hook.th))
                        return hook.th.HooksetType;
                    multiHookUnavailable = true;
                }

                if (hookset.LetFishEscapeTripleHook && PlayerRes.GetCurrentGp() is var gp3 && gp3 < 700)
                {
                    Service.Status = "Not enough GP to use Triple Hook, Letting fish escape is enabled";
                    noHookReason = @$"GP 不夠放三重提鉤（{gp3}＜700），preset 設定這時放掉魚";
                    return HookType.None;
                }

                Service.Status = $"(Triple Hook) {Service.Status}";
            }

            // Double Hook
            if (!skipDouble && hookset.UseDoubleHook && hook.dh.HooksetEnabled)
            {
                if (CheckHookCondition(hook.dh, timePassed, ignoreTimers))
                {
                    if (IsHookAvailable(hook.dh))
                        return hook.dh.HooksetType;
                    multiHookUnavailable = true;
                }

                if (hookset.LetFishEscapeDoubleHook && PlayerRes.GetCurrentGp() is var gp2 && gp2 < 400)
                {
                    Service.Status = "Not enough GP to use Double Hook, Letting fish escape is enabled";
                    noHookReason = @$"GP 不夠放雙重提鉤（{gp2}＜400），preset 設定這時放掉魚";
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

            if (multiHookWanted)
            {
                if (noMultiHook)
                    Service.PrintInfo(
                        @$"[HookManager] 要湊幾種不同魚的任務（任務#{CosmicMissionInfo.GetCurrentMissionId()}）：" +
                        @$"preset 在{CosmicCatchLog.BiteLabel(bite)}要放雙重／三重提鉤，改用一般提鉤" +
                        @$"｜拋竿後 {timePassed.ToString(@"0.00", System.Globalization.CultureInfo.InvariantCulture)} 秒");
                return HookType.Normal;
            }

            noHookReason = DescribeSkip(hookedAtAll, multiHookUnavailable && !hook.ph.HooksetEnabled, unconfiguredNote);
            return HookType.None;
        }

        noHookReason = @$"讀不到咬鉤力道（{bite}），沒有對應的提鉤設定";
        //Service.Status = "Skipping bite - No hook for this bite is enabled";
        return HookType.None;
    }

    private static ushort _multiHookOffLogMission;
    private static int _multiHookOffLoggedBites;

    /// <summary>
    /// 只算條數的任務：preset 沒開這種咬鉤的雙重／三重提鉤、內建魚池資料卻有這種咬鉤起獲 2 條以上的魚時，
    /// 每個任務每種咬鉤記一次（要使用者回報的診斷；只記不改提鉤）。
    /// </summary>
    private static void LogPresetMultiHookOff(BaseHookset hookset,
        (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph) hook, BiteType bite)
    {
        if ((hookset.UseTripleHook && hook.th.HooksetEnabled) || (hookset.UseDoubleHook && hook.dh.HooksetEnabled))
            return;
        if (!CosmicMissionInfo.GetCurrentProfile().AnyFishCountOnly)
            return;

        var mission = CosmicMissionInfo.GetCurrentMissionId();
        if (mission != _multiHookOffLogMission)
        {
            _multiHookOffLogMission = mission;
            _multiHookOffLoggedBites = 0;
        }

        var bit = 1 << ((int)bite & 31);
        if ((_multiHookOffLoggedBites & bit) != 0)
            return;
        _multiHookOffLoggedBites |= bit;

        var best = 0.0;
        foreach (var p in CosmicFishPrior.ForMission(mission))
            if (p.Bite == bite && p.DoubleYield > best)
                best = p.DoubleYield;
        if (best < 2)
            return;

        Service.PrintInfo(
            @$"[HookManager] 只算條數的任務（任務#{mission}）：preset 沒開{CosmicCatchLog.BiteLabel(bite)}的雙重／三重提鉤，" +
            @$"這種咬鉤照 preset 只用一般提鉤；內建魚池資料（魚糕 v2）裡這種咬鉤的魚雙重提鉤最多起獲 {best:0} 條。");
    }

    /// <summary>湊種類任務且設定開著＝不放雙重／三重提鉤（同種多條對種類數沒有幫助）。</summary>
    internal static bool ShouldSkipMultiHook(bool distinctSpecies, bool settingOn) => distinctSpecies && settingOn;

    /// <summary>preset 對這種咬鉤力道有沒有開任何一種提鉤（雙重／三重要連同總開關一起看）。</summary>
    private static bool IsBiteHookedAtAll(BaseHookset hookset,
        (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph) hook)
        => hook.ph.HooksetEnabled
           || (hookset.UseTripleHook && hook.th.HooksetEnabled)
           || (hookset.UseDoubleHook && hook.dh.HooksetEnabled);

    /// <summary>
    /// preset 完全沒開的咬鉤要不要改用一般提鉤：null＝要；否則是不改的理由（給 log，空字串＝不是這類任務）。
    /// 只在每條魚都算分的任務（WKSMissionText 115、121）放寬；條件不符、GP 不足放掉魚照舊。
    /// 身上有大魚獵手／大魚知識時不放寬：魚一上岸效果就用掉，preset 可能正留給它要的咬鉤。
    /// </summary>
    private static string? UnconfiguredBiteBlocker(BaseHookset hookset)
    {
        var everyFish = CosmicMissionInfo.GetCurrentProfile().EveryFishCounts;
        if (everyFish && !hookset.HookUnconfiguredWhenEveryFishCounts)
            return @"（每條魚都算分的任務，但這份 preset 設定只提它有開的咬鉤）";
        var enabled = Service.Configuration.HookUnconfiguredBitesWhenEveryFishCounts;
        var bigFishBuff = everyFish && enabled
                          && (PlayerRes.HasStatus(IDs.Status.PrizeCatch) || PlayerRes.HasStatus(IDs.Status.BigGameFishing));
        return UnconfiguredBiteDecision(everyFish, enabled, bigFishBuff);
    }

    /// <summary><see cref="UnconfiguredBiteBlocker"/> 的純判斷（不讀遊戲），離線探針也走這裡。</summary>
    internal static string? UnconfiguredBiteDecision(bool everyFishCounts, bool enabled, bool bigFishBuff)
    {
        if (!everyFishCounts)
            return string.Empty;

        if (!enabled)
            return @"（每條魚都算分的任務，但「preset 沒開的咬鉤也提」已關閉）";

        if (bigFishBuff)
            return @"（每條魚都算分的任務，但身上有大魚獵手／大魚知識，留給 preset 要的咬鉤）";

        return null;
    }

    private static string DescribeSkip(bool hookedAtAll, bool multiHookUnavailable, string unconfiguredNote)
    {
        if (!hookedAtAll)
            return @$"preset 沒開這種咬鉤（一般／雙重／三重提鉤都關）{unconfiguredNote}";

        if (multiHookUnavailable)
            return @$"雙重／三重提鉤現在放不出來（GP {PlayerRes.GetCurrentGp()}），這種咬鉤又沒開一般提鉤";

        var status = string.IsNullOrEmpty(Service.Status) ? @"?" : Service.Status;
        return @$"preset 有開這種咬鉤但條件不符：{status}";
    }

    /// <summary>
    /// 宇宙尺寸計分任務、耐心期間的提鉤：preset「要不要提這一咬」照舊（三格任一啟用且條件成立），
    /// 「用哪一招」換成不受提鉤成功率降低影響的那一種。華麗提鉤不耗 GP，可用時優先；
    /// 雙重／三重提鉤在耐心期間會失敗，改成單鉤 —— 少起幾條，但不會整竿落空。
    /// </summary>
    private HookType GetPatienceSafeHook(BaseHookset hookset,
        (BaseBiteConfig th, BaseBiteConfig dh, BaseBiteConfig ph) hook,
        BaseBiteConfig? stellar, BiteType bite, double timePassed, bool ignoreTimers)
    {
        if (ShouldUseStellarHook(hookset, hook, stellar, timePassed, ignoreTimers))
            return HookType.Stellar;

        var wanted = (hookset.UseTripleHook && hook.th.HooksetEnabled && CheckHookCondition(hook.th, timePassed, ignoreTimers))
                     || (hookset.UseDoubleHook && hook.dh.HooksetEnabled && CheckHookCondition(hook.dh, timePassed, ignoreTimers))
                     || (hook.ph.HooksetEnabled && CheckHookCondition(hook.ph, timePassed, ignoreTimers));

        if (!wanted)
        {
            if (Service.Status == "")
                Service.Status = UIStrings.Status_NoHookEnabled;
            return HookType.None;
        }

        Service.Status = "";
        return CosmicPatience.SafeSingleHook(bite);
    }

    /// <summary>
    /// 這一咬要不要改用華麗提鉤（動作 ID 41278，宇宙探索的探索任務專用）。
    /// 🔑「可不可以用」完全交給遊戲自己判斷（<c>GetActionStatus</c> + 復唱時間），不用地區 ID、也不用計時去猜：
    ///    在宇宙探索以外這個判斷永遠是 false；假設不成立時最壞情況是「多放了一次提鉤動作」。
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
        if (!IsBiteHookedAtAll(hookset, hook))
            return false;

        // 先問可用性再檢查條件：CheckHookCondition 失敗時會寫 Service.Status，
        // 而「華麗提鉤只是還在 CD」不該把使用者看得到的狀態文字洗掉。
        if (!PlayerRes.ActionTypeAvailable((uint)HookType.Stellar))
            return false;

        // ⚠️ 條件要沿用「這一咬原本那一格」的設定，不能只看華麗提鉤自己那格的空白預設。
        //    華麗提鉤只是換一個提鉤**動作**，不該自帶一套條件 —— 否則 preset 設好的時間窗對華麗提鉤完全無效，
        //    表現成「preset 說要放掉的咬鉤，只有華麗提鉤會去勾」。一般提鉤沒開時看三重／雙重那格（規則見 HookDecisionRules）。
        var statusBefore = Service.Status;
        var normalOn = hook.ph.HooksetEnabled;
        var tripleOn = hookset.UseTripleHook && hook.th.HooksetEnabled;
        var doubleOn = hookset.UseDoubleHook && hook.dh.HooksetEnabled;
        var passes = HookDecisionRules.StellarConditionPasses(normalOn,
            normalOn && CheckHookCondition(hook.ph, timePassed, ignoreTimers),
            !normalOn && CheckHookCondition(stellar, timePassed, ignoreTimers),
            tripleOn, !normalOn && tripleOn && CheckHookCondition(hook.th, timePassed, ignoreTimers),
            doubleOn, !normalOn && doubleOn && CheckHookCondition(hook.dh, timePassed, ignoreTimers));
        Service.Status = statusBefore;
        if (!passes)
            return false;

        // 要使用者回報的診斷。華麗提鉤有 60 秒 CD，所以這行最多每分鐘一次，不需要另外節流。
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

        if (!CheckConditionSet(hookType, timePassed, ignoreTimers))
            return false;

        return true;
    }

    /// <summary>
    /// 上游 config v6／v7 把提鉤的時間窗（以及「要有某狀態才提」之類的限制）從固定欄位
    /// 改成了一份條件。這裡是它的求值點，對應上游的 <c>hook.xx.ConditionSet.PassesOrUnconfigured()</c>。
    /// </summary>
    /// <remarks>
    /// 🔴 三個保底規則，缺一個都會變成「外掛看起來壞掉」：
    /// ①沒有條件（<c>null</c>／空群組）＝通過。既有使用者與所有 AH4_ preset 走這一條。
    /// ②含有我們還沒實作的條件＝**通過**（不擋）。這條路徑上「不認得就不提鉤」會讓每一咬都放生。
    /// ③<paramref name="ignoreTimers"/> 為 true 時，條件裡的時間窗（BiteTimerCD／ChumTimerCD）一律視為成立
    ///   —— 否則「每條魚都算分的任務忽略時間窗」會在帶條件的 preset 上靜默失效。
    /// </remarks>
    private bool CheckConditionSet(BaseBiteConfig hookType, double timePassed, bool ignoreTimers)
    {
        var set = hookType.ConditionSet;

        if (!ConditionEvaluator.HasGroups(set))
            return true;

        var ctx = ConditionContext.OnBite(timePassed, ignoreTimers);

        if (ConditionEvaluator.Passes(set, ctx, unconfiguredResult: true, unsupportedResult: true,
                where: @"提鉤條件"))
            return true;

        Service.Status = $"Skipping bite - {ConditionEvaluator.Describe(set, ctx)}";
        return false;
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