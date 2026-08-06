using System;
using System.Diagnostics;
using System.Linq;
using AutoHook.Classes;
using AutoHook.Configurations;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.SeFunctions;
using AutoHook.Utils;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;


namespace AutoHook.Fishing;

public partial class FishingManager : IDisposable
{
    // todo: refactor this entire class
    private static readonly FishingPresets Presets = Service.Configuration.HookPresets;

    private double _timeout;
    private readonly Stopwatch _fishingTimer = new();

    private FishingState _lastState = FishingState.NotFishing;
    private FishingSteps _lastStep = 0;

    private BaitFishClass? _lastCatch;

    public static IntuitionStatus IntuitionStatus { get; private set; } = IntuitionStatus.NotActive;

    private SpectralCurrentStatus _spectralCurrentStatus = SpectralCurrentStatus.NotActive;

    private bool _isMooching;

    /// <summary>
    /// 「體型鎖定」旗標（LogMessage 5565／5569 那兩則三驚嘆號訊息）。
    /// ⚠️ 語意刻意維持原樣：**每拋一竿就在 <see cref="OnBeganFishing"/> 清掉**，
    ///    而且只有 <see cref="LureTarget.Any"/> 時才會因為鎖定訊息而設起來。
    ///    要跨竿保留的是魚影，不是鎖定 —— 見 <see cref="_lureShadowFish"/>。
    /// </summary>
    private bool _lureSuccess;

    /// <summary>
    /// 目前**已觸發且尚未結束**的魚影（null＝沒有魚影）。
    ///
    /// 🔴 這個狀態刻意**跨拋竿**。原本魚影是靠 <see cref="_lureSuccess"/> 表達的，而那個旗標
    ///    每一竿都會被重設 —— 所以只要在魚影還在的時候釣起了**別的**魚，下一竿就會重新施放引誘，
    ///    把已經觸發的魚影最高咬餌權重毀掉（再次使用引誘會失去最高機率）。
    ///
    /// 🔑 **清除時機三個都要，缺一會卡死在「永遠不再引誘」**：
    ///    ①收到魚影消失訊息（<c>Unknown_70_2</c>）②收到釣起訊息（<c>Unknown_70_3</c>）
    ///    ③離開釣魚（<see cref="FishingState.Quit"/> ／ <see cref="FishingState.NotFishing"/>）當兜底。
    ///
    /// ⚠️ 「魚影跨竿持續」這件事來自第三方文件、**我們沒有實機驗證**。就算它是錯的（魚影只活一竿），
    ///    ①仍會在那一竿結束前發、加上③的兜底 —— 行為退回等同修改前，不會卡死。
    ///    設定／清除各寫一行 <c>Information</c> 級 log，實機跑一次就能從 log 判斷 ① 到底會不會發。
    /// </summary>
    private BaitFishClass? _lureShadowFish;

    /// <summary>
    /// 「引誘的目的已經達成，不要再放引誘」＝ 體型鎖定（每竿）**或** 魚影已觸發（跨竿）。
    /// </summary>
    private bool LureStopRequested => _lureSuccess || _lureShadowFish != null;

    /// <summary>記下魚影已觸發。<paramref name="source"/> 會進 log，用來事後判斷是哪條路徑設起來的。</summary>
    private void SetLureShadow(BaitFishClass fish, string source)
    {
        var previous = _lureShadowFish;
        _lureShadowFish = fish;

        if (previous != null && previous.Id == fish.Id)
        {
            Service.PrintInfo(
                @$"[魚影] 重複收到出現訊息：{fish.Name} (id {fish.Id})｜來源＝{source}（狀態不變，仍暫停引誘）");
            return;
        }

        var replaced = previous == null ? @"無" : @$"{previous.Name} (id {previous.Id})";
        Service.PrintInfo(
            @$"[魚影] 設定：{fish.Name} (id {fish.Id})｜來源＝{source}｜原本的魚影＝{replaced}" +
            @"｜在消失/釣起/離開釣魚之前都會暫停施放引誘（跨拋竿）");
    }

    /// <summary>清除魚影狀態。已經是 null 時完全不做事，所以放在每幀路徑上也不會洗版。</summary>
    private void ClearLureShadow(string source)
    {
        var previous = _lureShadowFish;
        if (previous == null)
            return;

        _lureShadowFish = null;
        Service.PrintInfo(@$"[魚影] 清除：{previous.Name} (id {previous.Id})｜來源＝{source}｜恢復施放引誘");
    }

    /// <summary>
    /// 上一次咬鉤的力道，**純量測用**（<see cref="CosmicCatchLog"/>），不參與任何決策。
    /// ⚠️ 一定要在咬鉤當下存起來：<c>UpdateCatch</c> 觸發時魚已經上岸，
    ///    <see cref="SeTugType"/> 那個位址讀到的早就不是這一竿的值了。
    /// </summary>
    private BiteType _lastBiteType = BiteType.Unknown;

    private delegate bool UseActionDelegate(IntPtr manager, ActionType actionType, uint actionId, ulong targetId,
        uint a4, uint a5,
        uint a6, IntPtr a7);

    private Hook<UseActionDelegate>? _useActionHook;

    public delegate void UpdateCatchDelegate(IntPtr module, uint fishId, bool large, ushort size, byte amount,
        byte level, byte unk7, byte unk8, byte unk9, byte unk10,
        byte unk11, byte unk12);

    public Hook<UpdateCatchDelegate>? UpdateCatch = null!;

    public FishingManager()
    {
        try
        {
            Service.TaskManager.EnqueueDelay(200);
            Service.TaskManager.Enqueue(() => CreateDalamudHooks());
            //CreateDalamudHooks();
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(@$"{e.Message}");
        }
    }

    public void Dispose()
    {
        Disable();
        _useActionHook?.Dispose();
        UpdateCatch?.Dispose();
    }

    public unsafe void CreateDalamudHooks()
    {
        UpdateCatch = Service.GameInteropProvider.HookFromSignature<UpdateCatchDelegate>(
            @"48 89 6C 24 ?? 56 41 56 41 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 8B 01",
            UpdateCatchDetour);
        var hookPtr = (IntPtr)ActionManager.MemberFunctionPointers.UseAction;
        _useActionHook = Service.GameInteropProvider.HookFromAddress<UseActionDelegate>(hookPtr, OnUseAction);

        Enable();
    }

    private void Enable()
    {
        Service.Framework.Update += OnFrameworkUpdate;
        Service.Chat.CheckMessageHandled += OnMessageDelegate;
        UpdateCatch?.Enable();
        _useActionHook?.Enable();
    }

    private void Disable()
    {
        Service.Framework.Update -= OnFrameworkUpdate;
        Service.Chat.CheckMessageHandled -= OnMessageDelegate;
        _useActionHook?.Disable();
        UpdateCatch?.Disable();
    }

    public void StartFishing()
    {
        if (!PlayerRes.IsCastAvailable())
        {
            Service.PrintChat(@"[AutoHook] You can't cast right now.");
            return;
        }

        var extraCfg = GetExtraCfg();
        if (extraCfg is { ForceBaitSwap: true, Enabled: true })
        {
            var result = Service.BaitManager.ChangeBait((uint)extraCfg.ForcedBaitId);

            if (result == BaitManager.ChangeBaitReturn.Success)
            {
                Service.PrintChat(
                    @$"[AutoHook] Starting with bait: {MultiString.GetItemName(extraCfg.ForcedBaitId)}");
                Service.Save();
            }
        }

        _lastStep = FishingSteps.StartedCasting;
        UseAutoCasts();
        //Service.TaskManager.Enqueue(() => UseAutoCasts());
    }

    private int GetCurrentBaitMoochId()
    {
        if (_isMooching)
            return _lastCatch?.Id ?? 0;

        if (Service.BaitManager.CurrentSwimBait is { } fishId)
            return (int)fishId;

        return (int)Service.BaitManager.Current;
    }

    // The current config is updates two times: When we began fishing (to get the config based on the mooch/bait) and when we hooked the fish (in case the user updated their configs).
    private void UpdateStatusAndTimer()
    {
        ResetAfkTimer();

        var selected = GetHookCfg();
        var hookset = selected.GetHookset();
        if (selected.Enabled)
        {
            _timeout = PlayerRes.HasStatus(IDs.Status.Chum)
                ? hookset.ChumTimeoutMax
                : hookset.TimeoutMax;
        }
        else
            _timeout = 0;

        if (Service.Configuration.ShowStatus)
        {
            string buffStatus = "";

            if (hookset.RequiredStatus != 0)
            {
                buffStatus = MultiString.GetStatusName(hookset.RequiredStatus);
                buffStatus = @$"({buffStatus})";
            }

            var hookCfgName = GetPresetName();

            string message = !selected.Enabled
                ? @$"No hooking option found. Make sure to add/enable your bait/mooch settings"
                : @$"Hooking with: {hookCfgName} {buffStatus}";

            Service.Status = message;
            Service.PrintDebug(@$"[HookManager] {message}");
        }
    }

    public string GetPresetName()
    {
        var customHook = Presets.SelectedPreset?.GetCfgById(GetCurrentBaitMoochId(), _isMooching);

        var globalHook = _isMooching
            ? Presets.DefaultPreset.ListOfMooch.FirstOrDefault()
            : Presets.DefaultPreset.ListOfBaits.FirstOrDefault();

        var presetName = customHook?.Enabled ?? false
            ? @$"{customHook.BaitFish.Name} ({Presets.SelectedPreset?.PresetName})"
            : globalHook?.Enabled ?? false
                ? @$"{(_isMooching ? UIStrings.All_Mooches : UIStrings.All_Baits)} ({Presets.DefaultPreset.PresetName})"
                : @"None";

        return presetName;
    }

    public HookConfig GetHookCfg()
    {
        var custom = Presets.SelectedPreset?.GetCfgById(GetCurrentBaitMoochId(), _isMooching);
        
        var defaultHook = _isMooching
            ? Presets.DefaultPreset.ListOfMooch.FirstOrDefault()
            : Presets.DefaultPreset.ListOfBaits.FirstOrDefault();

        var currentHook = custom?.Enabled ?? false ? custom : defaultHook!;

        return currentHook;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var currentState = Service.BaitManager.FishingState;

        if (!Service.Configuration.PluginEnabled || currentState == FishingState.NotFishing)
        {
            // 🔴 魚影是跨拋竿的狀態，只靠聊天訊息清除會有漏網（訊息被別的外掛吃掉、玩家直接收竿、
            //    切區、外掛中途被關掉…）。這裡是**兜底**：只要人不在釣魚就一定清掉，
            //    否則會永遠停在「不再施放引誘」。已經是 null 時 ClearLureShadow 直接返回，不會洗版。
            if (currentState == FishingState.NotFishing)
                ClearLureShadow(@"離開釣魚 (NotFishing)");

            return;
        }

        if (currentState != FishingState.Quit && _lastStep.HasFlag(FishingSteps.Quitting))
        {
            if (PlayerRes.IsCastAvailable())
            {
                PlayerRes.CastActionDelayed(IDs.Actions.Quit, ActionType.Action, @"Quit");
                currentState = FishingState.Quit;
            }
        }

        //CheckFishingState();

        if (!_lastStep.HasFlag(FishingSteps.Quitting) && currentState == FishingState.PoleReady)
            CheckPluginActions();

        if (currentState == FishingState.NormalFishing || currentState == FishingState.LureFishing)
        {
            // 純蒐證：量狀態列比伺服器階梯訊息慢多少。沒有待量的樣本時 HasPending 是個 bool 判斷，
            // 連狀態列都不會去讀 —— 不在沒事的時候付每幀成本。
            if (LureLadderLog.HasPending)
                LureLadderLog.Poll();

            CheckWhileFishingActions();
            CheckTimeout();
        }

        if (_lastState == currentState)
            return;
        
        _lastState = currentState;

        switch (currentState)
        {
            case FishingState.PullPoleIn: // If a hook is manually used before a bite, don't use auto cast
                if (_lastStep.HasFlag(FishingSteps.BeganFishing))
                    _lastStep = FishingSteps.None;
                else AnimationCancel();
                _fishingTimer.Reset();
                break;
            case FishingState.PoleOut:
                InitFinishing();
                break;
            case FishingState.Bite:
                if (!_lastStep.HasFlag(FishingSteps.FishBit)) Service.TaskManager.Enqueue(OnBite);
                break;
            case FishingState.Quit:
                OnFishingStop();
                break;
        }
    }

    private void InitFinishing()
    {
        if (!_fishingTimer.IsRunning) 
            _fishingTimer.Start();
        
        UpdateStatusAndTimer();
    }

    FishConfig? lastCatchCfg = null;
    private void CheckPluginActions()
    {
        if (!EzThrottler.Throttle(@"CheckPluginActions", 500))
            return;
        
        if (!PlayerRes.IsCastAvailable())
            return;

        lastCatchCfg ??= GetLastCatchConfig();
       
        var extraCfg = GetExtraCfg();

        if (_lastStep.HasFlag(FishingSteps.FishCaught) && (_lastStep & (FishingSteps.None | FishingSteps.Quitting)) == 0)
            CheckStopCondition();

        // the order matters
        CheckExtraActions(extraCfg);

        var casted = false;
        if (_lastStep.HasFlag(FishingSteps.FishCaught) && !_lastStep.HasFlag(FishingSteps.Quitting))
        {
            casted = UseFishCaughtActions(lastCatchCfg);
            CheckFishCaughtSwap(lastCatchCfg);
        }
        
        FishingHelper.RemoveGuidQueue();

        if (!casted)
            UseAutoCasts();
    }

    private void OnBeganFishing(bool mooching)
    {
        if (_lastStep.HasFlag(FishingSteps.BeganFishing) &&
            (_lastState != FishingState.PoleReady || _lastState != FishingState.NotFishing))
            return;

        _isMooching = mooching;
        _lureSuccess = false;

        // 純蒐證：重設「距拋竿／距上一則階梯訊息」的基準。不影響任何行為。
        LureLadderLog.OnCastStarted();

        var baitname = MultiString.GetItemName(GetCurrentBaitMoochId());
        if (!_isMooching)
        {
            _isMooching = Service.BaitManager.CurrentSwimBait != null;
            Service.PrintDebug(@$"Started fishing with {(_isMooching ? @"Swimbait" : @"normal bait")}: {baitname}");
        }
        else
            Service.PrintDebug(@$"Started mooching with {baitname}");

        _lastStep = FishingSteps.BeganFishing;
        lastCatchCfg = null;

        Service.TaskManager.EnqueueDelay(2500);
        Service.TaskManager.Enqueue(CastCollect);

        UpdateStatusAndTimer();
    }

    private void CheckTimeout()
    {
        if (!_fishingTimer.IsRunning)
            _fishingTimer.Start();

        double maxTime = Math.Truncate(_timeout * 100) / 100;

        var currentTime = Math.Truncate(_fishingTimer.ElapsedMilliseconds / 1000.0 * 100) / 100;

        if (!(maxTime > 0) || !(currentTime > maxTime) || _lastStep.HasFlag(FishingSteps.TimeOut) ||
            _lastStep.HasFlag(FishingSteps.Reeling))
            return;

        
        Service.Status = @$"Timeout reached - using Rest";
        PlayerRes.CastActionDelayed(IDs.Actions.Rest, ActionType.Action, UIStrings.Hook);
        _lastStep = FishingSteps.TimeOut;
    }

    private void OnBite()
    {
        UpdateStatusAndTimer();
        var currentHook = GetHookCfg();
        _fishingTimer.Stop();
        
        if (PlayerRes.HasStatus(IDs.Status.Salvage) && GetAutoCastCfg().ChumAnimationCancel)
            PlayerRes.CastAction(IDs.Actions.Salvage);

        _lastCatch = null;
        _lastStep = FishingSteps.FishBit;

        // 跟原本一樣只讀一次 TugType，只是順手留一份給量測用（見 _lastBiteType）。
        _lastBiteType = Service.TugType?.Bite ?? BiteType.Unknown;
        HookFish(_lastBiteType, currentHook);

    }

    private void HookFish(BiteType bite, HookConfig currentHook)
    {
        var delay = new Random().Next(Service.Configuration.DelayBetweenHookMin,
            Service.Configuration.DelayBetweenHookMax);

        if (!currentHook.Enabled)
            return;

        var timePassed = Math.Truncate(_fishingTimer.ElapsedMilliseconds / 1000.0 * 100) / 100;

        var hook = currentHook.GetHook(bite, timePassed);

        if (hook is null or HookType.None)
        {
            delay = new Random().Next(Service.Configuration.DelayBeforeCancelMin,
                Service.Configuration.DelayBeforeCancelMax);

            Service.TaskManager.EnqueueDelay(delay);
            Service.TaskManager.Enqueue(() => PlayerRes.CastAction(IDs.Actions.Rest));
            //_lastStep = FishingSteps.Reeling;
            Service.PrintDebug(@$"[HookManager] No hook found, using Rest");
            return;
        }

        Service.TaskManager.EnqueueDelay(delay);
        Service.TaskManager.Enqueue(() =>
            PlayerRes.CastActionDelayed((uint)hook, ActionType.Action, @$"{hook.ToString()}"));
        Service.Status = (@$"Using {hook.ToString()} hook. (Bite: {bite})");
    }

    /// <param name="large">遊戲傳來的「大型」旗標。目前只給量測用，不影響任何決策。</param>
    /// <param name="size">遊戲傳來的尺寸。同上。</param>
    /// <param name="collectible">這一竿是不是以收藏品形式入手。同上。</param>
    private void OnCatch(uint fishId, uint amount, bool large, ushort size, bool collectible)
    {
        // 🔴 純量測，零行為變化：只寫 log。放在最前面是為了「就算下面的既有邏輯出事也已經量到了」。
        //    這裡刻意不讀任何任務分數 —— 那是 ICE 的職權，見 CosmicCatchLog 的權威邊界說明。
        // ⚠️ 咬鉤力道**用完就清**：OnBite 不見得每一竿都跑得到（例如外掛在拋竿後才被開啟），
        //    不清的話這一竿會被貼上「上一竿的力道」——那正好會污染我們要量的那個相關性，
        //    而且完全看不出來。清成 Unknown 至少讓「不知道」在 log 裡看得見。
        var biteType = _lastBiteType;
        _lastBiteType = BiteType.Unknown;
        CosmicCatchLog.Record(fishId, amount, large, size, collectible, biteType);

        _lastCatch = GameRes.Fishes.FirstOrDefault(fish => fish.Id == fishId) ?? new BaitFishClass(@"-", -1);
        var lastFishCatchCfg = GetLastCatchConfig();

        Service.LastCatch = _lastCatch;

        Service.PrintDebug(@$"[HookManager] Caught {_lastCatch.Name} (id {_lastCatch.Id})");

        _lastStep = FishingSteps.FishCaught;

        // 跨 preset 的「本次釣魚某魚 id 釣了幾條」。
        // ⚠️ 刻意用遊戲傳進來的 fishId 而不是 _lastCatch.Id —— 後者查不到魚時會是 -1，
        //    那會讓計數靜默落到一個不存在的鍵上。收藏品的 +500000 已經在 UpdateCatchDetour 扣掉了。
        // 這一份計數與下面的 per-FishConfig 計數**互不取代**，用途見 FishingHelper.SessionCatch。
        FishingHelper.AddSessionCatch((int)fishId, (int)amount);

        if (lastFishCatchCfg != null)
        {
            for (var i = 0; i < amount; i++)
            {
                FishingHelper.AddFishCount(lastFishCatchCfg.UniqueId);
            }
        }

        var hook = GetHookCfg();
        if (hook.Enabled)
            FishingHelper.AddFishCount(hook.UniqueId);
    }

    private void CheckStopCondition()
    {
        var lastFishCatchCfg = GetLastCatchConfig();
        var currentHook = GetHookCfg();
        var hookset = currentHook.GetHookset();
        var extra = GetExtraCfg();

        if (lastFishCatchCfg?.StopAfterCaught ?? false)
        {
            var guid = lastFishCatchCfg.UniqueId;
            var total = FishingHelper.GetFishCount(guid);

            if (total >= lastFishCatchCfg.StopAfterCaughtLimit)
            {
                Service.PrintChat(string.Format(UIStrings.Caught_Limited_Reached_Chat_Message,
                    @$"{lastFishCatchCfg.Fish.Name}: {lastFishCatchCfg.StopAfterCaughtLimit}"));

                _lastStep |= lastFishCatchCfg.StopFishingStep;
                if (lastFishCatchCfg.StopAfterResetCount) FishingHelper.ToBeRemoved.Add(guid);
            }
        }

        if (currentHook.Enabled && hookset.StopAfterCaught)
        {
            var guid = currentHook.UniqueId;
            var total = FishingHelper.GetFishCount(guid);

            if (total >= hookset.StopAfterCaughtLimit)
            {
                Service.PrintChat(string.Format(UIStrings.Hooking_Limited_Reached_Chat_Message,
                    @$"{currentHook.BaitFish.Name}: {hookset.StopAfterCaughtLimit}"));

                _lastStep |= hookset.StopFishingStep;
                if (hookset.StopAfterResetCount) FishingHelper.ToBeRemoved.Add(guid);
            }
        }

        if (extra.StopAfterAnglersArt && extra.Enabled)
        {
            if (!PlayerRes.HasAnglersArtStacks(extra.AnglerStackQtd))
                return;

            _lastStep |= extra.AnglerStopFishingStep;
            Service.PrintChat(@$"[Extra] Angler's Stack Reached: Stopping fishing");
        }
    }

    private void OnFishingStop()
    {
        _lastStep = FishingSteps.None;

        // 魚影跨拋竿，但**不跨釣魚**：收竿就清（三個清除時機的第③個）。
        ClearLureShadow(@"離開釣魚 (Quit)");

        LureLadderLog.OnFishingStopped();

        if (_fishingTimer.IsRunning)
            _fishingTimer.Reset();

        Service.Status = "";

        FishingHelper.Reset();

        PlayerRes.CastActionNoDelay(IDs.Actions.Quit);
        PlayerRes.DelayNextCast(0);
    }

    private bool OnUseAction(IntPtr manager, ActionType actionType, uint actionId, ulong targetId, uint a4,
        uint a5, uint a6, IntPtr a7)
    {
        try
        {
            if (actionType == ActionType.Action && Service.Configuration.PluginEnabled &&
                PlayerRes.ActionTypeAvailable(actionId))
            {
                switch (actionId)
                {
                    case IDs.Actions.Rest:
                        // till call will make sure Collectors glove is off
                        if (PlayerRes.HasStatus(IDs.Status.CollectorsGlove)) AnimationCancel();
                        _lastStep = FishingSteps.Reeling;
                        break;
                    case IDs.Actions.Cast:
                        OnBeganFishing(false);
                        break;
                    case IDs.Actions.Mooch:
                    case IDs.Actions.Mooch2:
                        OnBeganFishing(true);
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Service.PrintDebug(@$"[HookManager] Error: {e.Message}");
        }

        return _useActionHook!.Original(manager, actionType, actionId, targetId, a4, a5, a6, a7);
    }

    private void UpdateCatchDetour(IntPtr module, uint fishId, bool large, ushort size, byte amount, byte level,
        byte unk7,
        byte unk8, byte unk9, byte unk10, byte unk11, byte unk12)
    {
        UpdateCatch!.Original(module, fishId, large, size, amount, level, unk7, unk8, unk9, unk10, unk11, unk12);

        // Check against collectibles.
        var collectible = fishId > 500000;
        if (collectible)
            fishId -= 500000;

        OnCatch(fishId, amount, large, size, collectible);
    }
}