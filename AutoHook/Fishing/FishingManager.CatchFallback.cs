using System;
using System.Collections.Generic;
using System.Linq;
using AutoHook.Enums;
using AutoHook.Utils;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Utility;
using Lumina.Excel.Sheets;
using static AutoHook.Utils.CatchFallbackRules;

namespace AutoHook.Fishing;

// 本檔契約：釣獲通知（UpdateCatch detour）是換餌規則、計數、拍擊水面、停手條件的唯一來源。
// 同一函式上別的插件的 hook 不把呼叫往下傳時（例：GatherBuddy「隱藏魚尺寸彈窗」直接不呼叫原函式），
// 這裡用聊天的「成功釣上了」訊息補記那一竿，並試著重新掛一次 hook 讓本插件排到最外層。
// detour、聊天事件、Framework.Update 都在遊戲主執行緒上，欄位不另外加鎖。
public partial class FishingManager
{
    private long _lastUpdateCatchTick = long.MinValue / 2;
    private PendingChatCatch? _pendingChatCatch;
    private bool _catchStarvationReported;
    private bool _catchFallbackOnlyReported;
    private long _lastRehookTick = long.MinValue / 2;
    private long _lastCastTick = long.MaxValue;
    private FishingState _fallbackLastState = FishingState.NotFishing;
    private List<string[]>? _catchMessageLiterals;

    private readonly record struct PendingChatCatch(uint FishId, uint Amount, bool Collectible, long Tick, long CastTick);

    /// <summary>每幀呼叫：記下這一竿的拋竿時間（進入 PoleOut 的那一幀），給「這一竿的通知到了沒」當界線。</summary>
    private void TrackCastForCatchFallback(FishingState state)
    {
        if (state == FishingState.PoleOut && _fallbackLastState != FishingState.PoleOut)
            _lastCastTick = Environment.TickCount64;
        _fallbackLastState = state;
    }

    private List<string[]> CatchMessageLiterals()
    {
        if (_catchMessageLiterals != null)
            return _catchMessageLiterals;

        var list = new List<string[]>();
        var sheet = Service.DataManager.GetExcelSheet<LogMessage>();
        foreach (var id in CatchLogMessageIds)
        {
            if (sheet != null && sheet.TryGetRow(id, out var row))
                list.Add(Literals(row.Text));
        }

        return _catchMessageLiterals = list;
    }

    private void NoteChatCatch(SeString message, string text)
    {
        if (UpdateCatch == null || Service.BaitManager.FishingState == FishingState.NotFishing)
            return;

        if (!MatchesCatchTemplate(text, CatchMessageLiterals()))
            return;

        var now = Environment.TickCount64;
        if (CatchNotificationArrived(now, _lastUpdateCatchTick, _lastCastTick))
            return;

        uint fishId = 0;
        var collectible = false;
        foreach (var payload in message.Payloads)
        {
            if (payload is ItemPayload item && GameRes.Fishes.Any(f => f.Id == item.ItemId))
            {
                fishId = item.ItemId;
                collectible = item.Kind == ItemKind.Collectible;
                break;
            }
        }

        if (fishId == 0)
        {
            fishId = (uint)PickFishIdByName(text, GameRes.Fishes.Select(f => (f.Id, f.Name)),
                id => Presets.SelectedPreset?.GetFishById(id) != null);
        }

        if (fishId == 0)
            return;

        _pendingChatCatch = new PendingChatCatch(fishId, ParseCatchAmount(text), collectible, now, _lastCastTick);
    }

    private void ResolvePendingChatCatch(FishingState state)
    {
        var pending = _pendingChatCatch!.Value;
        var now = Environment.TickCount64;

        if (CatchNotificationArrived(pending.Tick, _lastUpdateCatchTick, pending.CastTick))
        {
            _pendingChatCatch = null;
            return;
        }

        // 收竿動畫還沒結束就等：補記要落在下一次拋竿之前，跟正常的釣獲通知同一個時點。
        if (state != FishingState.PoleReady && state != FishingState.NotFishing && now - pending.Tick < MaxAgeMs)
            return;

        _pendingChatCatch = null;
        if (now - pending.Tick >= MaxAgeMs)
        {
            Service.PrintInfo($"[釣獲通知備援] {MultiString.GetItemName(pending.FishId)} (#{pending.FishId}) 等不到收竿就已經再拋竿，這一竿不補記。");
            return;
        }

        ReportCatchStarvation();

        Service.PrintInfo($"[釣獲通知備援] 遊戲的釣獲通知沒有送到，從聊天訊息補記：{MultiString.GetItemName(pending.FishId)} (#{pending.FishId})×{pending.Amount}" +
                          $"{(pending.Collectible ? "（收藏品）" : string.Empty)}");
        try
        {
            OnCatch(pending.FishId, pending.Amount, false, 0, pending.Collectible, fromChat: true);
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e, "[釣獲通知備援] 補記時出錯，這一竿略過。");
        }
    }

    private void ReportCatchStarvation()
    {
        if (!_catchStarvationReported)
        {
            _catchStarvationReported = true;
            Service.PluginLog.Warning(
                "[釣獲通知備援] 釣到魚了，但遊戲的釣獲通知沒有送到 AutoHook：同一個遊戲函式上有別的插件的 hook 沒把呼叫往下傳" +
                "（例如 GatherBuddy 開著「隱藏魚尺寸彈窗」）。換餌規則、計數、拍擊水面與停手條件都靠這個通知，" +
                "之後改從聊天訊息補記，並重新掛一次釣獲 hook。");
        }

        if (ShouldRehook(_lastRehookTick, _lastUpdateCatchTick, _lastRehookTick > long.MinValue / 2))
        {
            RehookUpdateCatch();
            return;
        }

        if (!_catchFallbackOnlyReported)
        {
            _catchFallbackOnlyReported = true;
            Service.PluginLog.Warning(
                "[釣獲通知備援] 重新掛 hook 之後仍收不到釣獲通知；這次載入期間每一竿都改由聊天訊息補記" +
                "（尺寸與大型不明、不寫量測紀錄、不送誇獎）。關掉擋住通知的那個設定後重新載入 AutoHook 可恢復。");
        }
    }

    // 只在 Framework.Update 上呼叫（不在 detour 裡）：換掉的瞬間遊戲不會正在執行這個函式。
    private void RehookUpdateCatch()
    {
        var old = UpdateCatch;
        if (old == null)
            return;

        _lastRehookTick = Environment.TickCount64;
        try
        {
            var fresh = CreateUpdateCatchHook(old.Address);
            fresh.Enable();
            UpdateCatch = fresh;
            old.Dispose();
            Service.PrintInfo("[釣獲通知備援] 已重新掛上釣獲 hook（排到同一函式其他 hook 的外層）。");
        }
        catch (Exception e)
        {
            Service.PluginLog.Error(e, "[釣獲通知備援] 重新掛釣獲 hook 失敗，改由聊天訊息補記。");
        }
    }
}
