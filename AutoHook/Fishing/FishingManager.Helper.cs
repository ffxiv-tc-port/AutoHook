using System;
using System.Collections.Generic;
using System.Linq;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.Throttlers;
using Lumina.Excel.Sheets;

namespace AutoHook.Fishing;

public partial class FishingManager
{
    // ReSharper disable once UnusedMember.Local
    private void CheckFishingState()
    {
#if (DEBUG)
        if (!EzThrottler.Throttle(@"FishingState", 500))
            return;

        Service.PrintDebug(
            @$"[HookManager] Fishing State: {Service.BaitManager.FishingState}, LastStep: {_lastStep}");
#endif
    }

    private static void ResetAfkTimer()
    {
        if (!Service.Configuration.ResetAfkTimer)
            return;

        if (!InputUtil.TryFindGameWindow(out var windowHandle)) return;

        // Virtual key for Right Winkey. Can't be used by FFXIV normally, and in tests did not seem to cause any
        // unusual interference.
        InputUtil.SendKeycode(windowHandle, 0x5C);
    }

    private void AnimationCancel()
    {
        if (GetAutoCastCfg().RecastAnimationCancel)
            PlayerRes.CastAction(IDs.Actions.Collect);
        
        if (PlayerRes.HasStatus(IDs.Status.Salvage) && GetAutoCastCfg().ChumAnimationCancel)
            PlayerRes.CastAction(IDs.Actions.Salvage);
    }

    private const XivChatType FishingMessage = (XivChatType)2243;
    private const XivChatType SystemAlert = (XivChatType)2115; //idk what to call this
    
    private void OnMessageDelegate(XivChatType type, int timeStamp, ref SeString sender, ref SeString messageSe,
        ref bool isHandled)
    {
        try
        {
            if (type is FishingMessage)
            {
                var text = messageSe.TextValue;
                var logId = Service.DataManager.GetExcelSheet<LogMessage>()
                    ?.FirstOrDefault(x => x.Text.ToString() == text).RowId;

                // ── 魚影（跨拋竿）────────────────────────────────────────────────
                // 三則訊息都來自 FishParameter：_1 出現、_2 消失、_3 釣起。
                // ⚠️ LureFishes 是每次存取都重算的屬性（Where + ToList），這裡只取一次快照。
                var lureFishes = GameRes.LureFishes;

                var shadowAppeared = lureFishes.FirstOrDefault(f => f.LureMessage == text);
                if (shadowAppeared != null)
                {
                    SetLureShadow(shadowAppeared, @"魚影出現訊息 (FishParameter._1)");
                    _lureSuccess = true;
                    return;
                }

                var shadowGone = lureFishes.FirstOrDefault(f => f.LureGoneMessage == text);
                if (shadowGone != null)
                {
                    ClearLureShadow(@$"魚影消失訊息 (FishParameter._2)：{shadowGone.Name}");
                }
                else
                {
                    var shadowCaught = lureFishes.FirstOrDefault(f => f.LureCaughtMessage == text);
                    if (shadowCaught != null)
                        ClearLureShadow(@$"釣起訊息 (FishParameter._3)：{shadowCaught.Name}");
                }

                // ── 引誘階梯（純蒐證，零行為變化）───────────────────────────────
                // 伺服器每次引誘生效都會下發 5566-5568／5570-5572，那是精確的層數轉換時點；
                // AutoLures 目前是靠有延遲的狀態列推層數。這裡只把訊息接起來量差距，不改行為。
                LureLadderLog.Record(logId);

                // ── 體型鎖定（每竿）─────────────────────────────────────────────
                // ⚠️ 這一行與修改前**完全等價**：原本是「先無條件寫入魚影比對結果，命中就 return，
                //    沒命中再看鎖定訊息」。魚影那一支已經在上面 return 掉了，剩下的就是這個。
                //    LureTarget != Any 時不因鎖定訊息停手是刻意取捨（追特定魚影時不停），不要動。
                _lureSuccess = GetHookCfg().GetHookset().CastLures.LureTarget == LureTarget.Any &&
                               logId is XivChatLog.AmbLureSuccess or XivChatLog.ModLureSuccess;
            }
            else if (type is SystemAlert)
            {
                var text = messageSe.TextValue;
                var logId = Service.DataManager.GetExcelSheet<LogMessage>()
                    ?.FirstOrDefault(x => x.Text.ToString() == text).RowId;

                if (logId is XivChatLog.CantFish)
                    Service.Status = UIStrings.CantFishHere;
            }
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e.Message);
        }
    }

    // This is my stupid way of handling the counter for stop/quit fishing and bait/preset swap
    public static class FishingHelper
    {
        public static Dictionary<Guid, int> FishCount = new();
        public static List<Guid> FishPresetSwapped = new();
        public static List<Guid> FishBaitSwapped = new();

        public static List<Guid> ToBeRemoved = new();

        public static void AddFishCount(Guid guid)
        {
            FishCount.TryAdd(guid, 0);
            FishCount[guid]++;

            GetFishCount(guid);
        }

        public static void AddBaitSwap(Guid guid)
        {
            if (!FishBaitSwapped.Contains(guid))
                FishBaitSwapped.Add(guid);
        }

        public static void AddPresetSwap(Guid guid)
        {
            if (!FishPresetSwapped.Contains(guid))
                FishPresetSwapped.Add(guid);
        }

        public static int GetFishCount(Guid guid)
        {
            return !FishCount.ContainsKey(guid) ? 0 : FishCount[guid];
        }

        public static bool SwappedBait(Guid guid)
        {
            return FishBaitSwapped.Any(g => g == guid);
        }

        public static bool SwappedPreset(Guid guid)
        {
            return FishPresetSwapped.Any(g => g == guid);
        }

        public static void RemoveId(Guid guid)
        {
            if (FishCount.ContainsKey(guid))
                FishCount.Remove(guid);

            if (SwappedPreset(guid))
                FishPresetSwapped.Remove(guid);

            if (SwappedBait(guid))
                FishBaitSwapped.Remove(guid);
        }

        public static void RemoveGuidQueue()
        {
            foreach (var guid in ToBeRemoved)
            {
                if (FishCount.ContainsKey(guid))
                    FishCount.Remove(guid);

                if (SwappedPreset(guid))
                    FishPresetSwapped.Remove(guid);

                if (SwappedBait(guid))
                    FishBaitSwapped.Remove(guid);
            }
            
            ToBeRemoved.Clear();
        }

        public static void Reset()
        {
            FishCount = new Dictionary<Guid, int>();
            FishPresetSwapped = [];
            FishBaitSwapped = [];
        }
    }
}