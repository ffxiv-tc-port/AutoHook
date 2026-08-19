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
        // 原本兩行都直接解參考它(而且各呼叫一次 Instance() 與一次 GetInstanceContentOceanFishing(),
        // 第一行判過的東西第二行再重取一次)。同 repo 的 SeFunctions/BaitManager.cs FishingMan
        // 有這個修法的完整說明。⚠️ AVE 是 corrupted-state exception,try/catch 攔不到。
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

        var recastDetail = ActionManager.Instance()->GetRecastGroupDetail(group);

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
    /// 🔴 <c>AgentInventoryContext.Instance()</c> 由
    /// <c>[Agent(AgentId.InventoryContext)]</c> 產生:內部鏈 AgentModule → UIModule →
    /// Framework,任一層回 null 整條就回 null(登入前、切場景、登出後都是常態),而底層
    /// <c>[StaticAddress]</c>／<c>[MemberFunction]</c> 特徵碼失配時改為擲
    /// <c>InvalidOperationException</c>——兩種失效模式並存,只擋一種等於假防護。
    /// 裸解參考 null 原生指標是 AccessViolationException,在 .NET Core 屬 corrupted-state
    /// exception,<c>try/catch</c> 完全攔不到 ⇒ 只能事前判空。
    /// ⚠️ 呼叫端之一(<c>CastActionNoDelay</c>)完全沒有 try,原本連特徵碼失配的擲出都會
    /// 直接往上逸出;另一個呼叫端雖然有 try,但那對 AVE 一樣無效。
    /// fail-closed:取不到 agent 就不用道具,寫 Information 讓使用者回報得出來
    /// (這條路徑由釣魚流程觸發,不是每幀)。
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
        var recast = ActionManager.Instance()->GetRecastGroupDetail(68);
        return recast->Total - recast->Elapsed == 0;
    }

    public static unsafe uint CastActionCost(uint id, ActionType actionType = ActionType.Action)
    {
        return (uint)ActionManager.GetActionCost(actionType, id, 0, 0, 0, 0);
    }

    public static unsafe float GetCooldown(uint id, ActionType actionType)
    {
        var group = GetRecastGroups(id, actionType);

        if (group == -1) // Im assuming -1 recast group has no CD
            return 0;

        var recast = ActionManager.Instance()->GetRecastGroupDetail(group);

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

            DelayNextCast(actionId);
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

            DelayNextCast(actionId);
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

    public static async void DelayNextCast(uint actionId)
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

        await Task.Delay(delay + ConditionalDelay(actionId));

        _blockCasting = false;
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