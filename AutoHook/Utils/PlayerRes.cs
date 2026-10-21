using System;
using System.Linq;
using AutoHook.Data;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Task = System.Threading.Tasks.Task;

namespace AutoHook.Utils;

public static class PlayerRes
{
    public static bool IsMoochAvailable()
    {
        if (ActionTypeAvailable(IDs.Actions.Mooch))
            return true;

        if (ActionTypeAvailable(IDs.Actions.Mooch2))
            return true;

        return false;
    }

    public static bool HasStatus(uint statusID)
    {
        if (Service.Objects.LocalPlayer?.StatusList == null)
            return false;

        foreach (var buff in Service.Objects.LocalPlayer.StatusList)
        {
            if (buff.StatusId == statusID)
                return true;
        }

        return false;
    }
    
    public static bool HasAnyStatus(uint[] statusIDs)
    {
        if (Service.Objects.LocalPlayer?.StatusList == null)
            return false;

        return Service.Objects.LocalPlayer.StatusList.Any(buff => statusIDs.Contains(buff.StatusId));
    }

    public static unsafe bool IsInActiveSpectralCurrent()
    {
        // EventFramework.Instance() 是 [StaticAddress(..., isPointer: true)] —— 讀的是一個
        // **指標變數**,遊戲還沒把框架建起來時它就是 null,執行期為 null 是合法狀態不是例外。
        // ⚠️ 解參考 null 是 try/catch 攔不到的 AccessViolation ⇒ 取一次、判空、之後只用這份區域變數。
        var eventFramework = EventFramework.Instance();

        if (eventFramework == null)
            return false;

        var oceanFishing = eventFramework->GetInstanceContentOceanFishing();

        if (oceanFishing is null)
            return false;

        return oceanFishing->SpectralCurrentActive;
    }

    public static uint GetCurrentGp()
    {
        if (Service.Objects.LocalPlayer?.CurrentGp == null)
            return 0;

        return Service.Objects.LocalPlayer.CurrentGp;
    }

    public static uint GetMaxGp()
    {
        if (Service.Objects.LocalPlayer?.MaxGp == null)
            return 0;

        return Service.Objects.LocalPlayer.MaxGp;
    }
    
    public static int GetStatusStacks(uint status)
    {
        if (Service.Objects.LocalPlayer?.StatusList == null)
            return 0;

        foreach (var buff in Service.Objects.LocalPlayer.StatusList)
        {
            if (buff.StatusId == status)
                return buff.Param;
        }

        return 0;
    }

    public static bool HasAnglersArtStacks(int amount)
    {
        if (Service.Objects.LocalPlayer?.StatusList == null)
            return false;

        foreach (var buff in Service.Objects.LocalPlayer.StatusList)
        {
            if (buff.StatusId == IDs.Status.AnglersArt)
                return buff.Param >= amount;
        }

        return false;
    }

    public static float GetStatusTime(uint statusId)
    {
        if (Service.Objects.LocalPlayer?.StatusList == null)
            return 0;

        foreach (var buff in Service.Objects.LocalPlayer.StatusList)
        {
            if (buff.StatusId == statusId)
                return buff.RemainingTime;
        }

        return 0;
    }

    // status 0 == available to cast? not sure but it seems to be
    // Also make sure its the skill is not on cooldown (mainly for mooch2)
    public static unsafe bool ActionTypeAvailable(uint id, ActionType actionType = ActionType.Action)
    {
        return ActionStatus(id, actionType) == 0 && !ActionOnCoolDown(id, actionType);
    }

    public static unsafe bool IsCastAvailable()
    {
        return ActionStatus(IDs.Actions.Cast) == 0 && !ActionOnCoolDown(IDs.Actions.Cast) && !_blockCasting;
    }

    public static unsafe bool ActionOnCoolDown(uint id, ActionType actionType = ActionType.Action)
    {
        var group = GetRecastGroups(id, actionType);

        if (group == -1) // Im assuming -1 recast group has no CD
            return false;

        // 🔴 GetRecastGroupDetail **會回 null**,不是只有 group==-1 那一種失效:只有 group 0..79 走純指標算術
        //    (保證非 null),group >= 87 與 80..86 那段模組查詢失敗時都回 null,而解參考 null 是
        //    try/catch 攔不到的 AccessViolation。
        // 失敗方向沿用上面 group==-1 的既有語意(「當作沒有冷卻」)。這是每幀輪詢路徑 ⇒ 不寫 log。
        var recastDetail = ActionManager.Instance()->GetRecastGroupDetail(group);
        if (recastDetail == null)
            return false;

        return recastDetail->Total - recastDetail->Elapsed > 0;
    }

    public static unsafe uint ActionStatus(uint id, ActionType actionType = ActionType.Action)
    {
        return ActionManager.Instance()->GetActionStatus(actionType, id);
    }

    public static unsafe bool CastAction(uint id)
    {
        return ActionManager.Instance()->UseAction(ActionType.Action, id);
    }

    public static unsafe int GetRecastGroups(uint id, ActionType actionType = ActionType.Action)
    {
        return ActionManager.Instance()->GetRecastGroup((int)actionType, id);
    }
    
    public static unsafe int HasItem(uint itemId)
        => InventoryManager.Instance()->GetInventoryItemCount(itemId);

    /// <summary>
    /// 🔴 <c>AgentInventoryContext.Instance()</c> 的內部鏈 AgentModule → UIModule → Framework 任一層回 null
    /// 整條就回 null(登入前、切場景、登出後都是常態),而底層特徵碼失配時改為擲
    /// <c>InvalidOperationException</c>——兩種失效模式並存,只擋一種等於假防護。
    /// 裸解參考 null 是 <c>try/catch</c> 攔不到的 AccessViolation ⇒ 只能事前判空。
    /// fail-closed:取不到 agent 就不用道具,寫 Information(要使用者回報的診斷;
    /// 這條路徑由釣魚流程觸發,不是每幀)。
    /// </summary>
    public static unsafe void UseItems(uint id)
    {
        AgentInventoryContext* agent;
        try
        {
            agent = AgentInventoryContext.Instance();
        }
        catch (Exception e)
        {
            Service.PluginLog.Information(
                $"[AutoHook] 取得 AgentInventoryContext 失敗(特徵碼可能失配),道具 {id} 未使用:{e.Message}");
            return;
        }

        if (agent == null)
        {
            Service.PluginLog.Information(
                $"[AutoHook] AgentInventoryContext 尚未就緒,道具 {id} 未使用。");
            return;
        }

        agent->UseItem(id);
    }

    // RecastGroup 68 = Cordial pots
    public static unsafe bool IsPotOffCooldown()
    {
        // 68 落在 0..79 的純指標算術區段(見 ActionOnCoolDown 的註解),不會走到 return-null 的分支。
        // 仍然判空:成本是一個 test,而萬一台服日後把冷卻組上限改到 68 以下,
        // 失敗形式就是攔不到的 AVE 而不是一則錯誤訊息。
        // fail-closed:取不到就當作「還沒好」——寧可不喝強心劑,也不要在未知狀態下送出動作。
        var recast = ActionManager.Instance()->GetRecastGroupDetail(68);
        if (recast == null)
            return false;

        return recast->Total - recast->Elapsed == 0;
    }

    private static bool _actionCostMissingLogged;

    // 反序列化設定時就經 BaseActionCast 建構子走到這裡：位址解不出回 0（不自動帶 GP 下限），不讓插件載入失敗。
    public static unsafe uint CastActionCost(uint id, ActionType actionType = ActionType.Action)
    {
        if (ActionManager.MemberFunctionPointers.GetActionCost == null)
        {
            if (!_actionCostMissingLogged)
            {
                _actionCostMissingLogged = true;
                Service.PluginLog.Warning("[AutoHook] ActionManager.GetActionCost 位址解不出：技能 GP 消耗一律當 0，GP 下限不自動帶入");
            }
            return 0;
        }

        return (uint)ActionManager.GetActionCost(actionType, id, 0, 0, 0, 0);
    }

    public static unsafe float GetCooldown(uint id, ActionType actionType)
    {
        var group = GetRecastGroups(id, actionType);

        if (group == -1) // Im assuming -1 recast group has no CD
            return 0;

        // 同 ActionOnCoolDown:擋掉 -1 之後 GetRecastGroupDetail 仍可能回 null(group >= 87、
        // 或 80..86 那段模組查詢失敗),裸解參考是攔不到的 AVE。
        // 失敗方向沿用上面 group==-1 的既有語意(回 0 ＝沒有冷卻),不新增行為。
        var recast = ActionManager.Instance()->GetRecastGroupDetail(group);
        if (recast == null)
            return 0;

        return recast->Total - recast->Elapsed;
    }

    public static unsafe bool HaveItemInInventory(uint id, bool isHQ = false)
        => InventoryManager.Instance()->GetInventoryItemCount(id, isHQ) > 0;

    public static unsafe bool HaveCordialInInventory(uint id)
    {
        return InventoryManager.Instance()->GetInventoryItemCount(id) > 0;
    }


    private static bool _blockCasting = false;

    public static void CastActionDelayed(uint actionId, ActionType actionType = ActionType.Action,
        string actionName = "")
    {
        if (_blockCasting)
            return;

        if (actionType is ActionType.Action or ActionType.Ability)
        {
            if (!ActionTypeAvailable(actionId, actionType))
                return;

            _blockCasting = true;
            Service.PrintDebug(@$"[PlayerResources] Casting Action: {actionName}, Id: {actionId}");
            try
            {
                CastAction(actionId);
            }
            catch (Exception e)
            {
                Service.PrintDebug(@$"Error casting action: {actionName}, Id: {actionId}, {e}");
            }

            _ = DelayNextCast(actionId);
        }
        else if (actionType == ActionType.Item)
        {
            _blockCasting = true;
            Service.PrintDebug(@$"[PlayerResources] Using Item: {actionName}, Id: {actionId}");
            try
            {
                UseItems(actionId);
            }
            catch (Exception e)
            {
                Service.PrintDebug(@$"Error casting action: {actionName}, Id: {actionId}, {e}");
            }

            _ = DelayNextCast(actionId);
        }
    }

    private static bool _blockActionNoDelay = false;

    public static void CastActionNoDelay(uint actionId, ActionType actionType = ActionType.Action,
        string actionName = "")
    {
        // sometimes it tries to cast the same action while, this prevents that
        if (_blockActionNoDelay)
            return;

        _blockActionNoDelay = true;
        if (actionType == ActionType.Action)
        {
            if (ActionTypeAvailable(actionId, actionType))
            {
                var casted = CastAction(actionId);
                if (casted)
                    Service.PrintDebug(@$"[PlayerResources] Casting Action: {actionName}, Id: {actionId}");
            }
        }
        else if (actionType == ActionType.Item)
        {
            Service.PrintDebug(@$"[PlayerResources] Using Item: {actionName}, Id: {actionId}");
            UseItems(actionId);
        }

        _blockActionNoDelay = false;
    }

    // 呼叫端不等待；例外在方法內收掉，回傳的 Task 不會失敗。出錯也解除 _blockCasting，免得之後永遠不再拋竿。
    public static async Task DelayNextCast(uint actionId)
    {
        try
        {
            var delay = 0;
            try
            {
                delay = new Random().Next(Service.Configuration.DelayBetweenCastsMin,
                    Service.Configuration.DelayBetweenCastsMax);
            }
            catch (Exception e)
            {
                Service.PluginLog.Error(@$"Error getting delay between casts: {e}");
            }

            // 設定頁會夾到 ≥0，但設定檔讀進來的值沒有下限保證：Task.Delay(-1)＝永遠等待（之後不再拋竿）、其他負值擲例外。
            await Task.Delay(Math.Max(0, delay + ConditionalDelay(actionId)));
        }
        catch (Exception e)
        {
            Service.PluginLog.Warning(e, "DelayNextCast failed");
        }
        finally
        {
            _blockCasting = false;
        }
    }

    private static int ConditionalDelay(uint id) =>
        id switch
        {
            IDs.Actions.ThaliaksFavor => 1100,
            IDs.Actions.MakeshiftBait => 1100,
            IDs.Actions.NaturesBounty => 1100,
            IDs.Item.Cordial => 1100,
            IDs.Item.HQCordial => 1100,
            IDs.Item.HiCordial => 1100,
            IDs.Item.WateredCordial => 1100,
            IDs.Item.HQWateredCordial => 1100,
            _ => 0,
        };
}