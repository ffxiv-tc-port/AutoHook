using System.Linq;
using System.Runtime.InteropServices;
using AutoHook.Classes;
using AutoHook.Enums;
using AutoHook.Utils;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FishingState = AutoHook.Enums.FishingState;

namespace AutoHook.SeFunctions;

public unsafe class BaitManager
{
    public BaitManager()
    {
        Service.GameInteropProvider.InitializeFromAttributes(this);

        if (_executeCommand == null)
            Service.PluginLog.Warning(@$"[BaitManager] {ChangeBaitUnavailableNotice}");
    }

    private delegate byte ExecuteCommandDelegate(int id, int unk1, uint baitId, int unk2, int unk3);

    // null＝特徵碼對不上。刻意宣告成 Fallible：只停換餌，不讓整個插件載入失敗。
    [Signature("E8 ?? ?? ?? ?? 41 C6 04 24", Fallibility = Fallibility.Fallible)]
    private readonly ExecuteCommandDelegate? _executeCommand = null;

    public const string ChangeBaitUnavailableNotice =
        @"遊戲改版後「切換釣餌」的遊戲函式對不上：換餌功能停用（/bait、/ahbait、preset 的自動換餌、其他插件要求的換餌），其餘功能照常。請等插件更新。";

    /// <summary>false＝換餌的遊戲函式沒解到，<see cref="ChangeBait(uint)"/>／<see cref="ChangeSwimbait"/> 一律回 <see cref="ChangeBaitReturn.Unavailable"/>。</summary>
    public bool CanChangeBait => _executeCommand != null;

    private long _nextUnavailableNoticeTick;

    /// <summary>送出換餌指令；函式沒解到時不送、回 <see cref="ChangeBaitReturn.Unavailable"/>（聊天提示最多 10 秒一次）。</summary>
    private ChangeBaitReturn SendChangeBait(int subCommand, uint id)
    {
        var executeCommand = _executeCommand;
        if (executeCommand != null)
            return executeCommand(701, subCommand, id, 0, 0) == 1 ? ChangeBaitReturn.Success : ChangeBaitReturn.UnknownError;

        var now = System.Environment.TickCount64;
        if (now >= _nextUnavailableNoticeTick)
        {
            _nextUnavailableNoticeTick = now + 10_000;
            Service.PrintChat(@$"[AutoHook] {ChangeBaitUnavailableNotice}");
        }

        return ChangeBaitReturn.Unavailable;
    }

    // 版面跟著 CS 的 FishingEventHandler 走，不手抄偏移：遊戲改版時隨 CS 一起換。
    internal FishingEventHandler* FishingMan
    {
        get
        {
            // 🔴 先判 EventFramework 實例本身：它還沒建好時取欄位＝解參考近零位址，try/catch 攔不到。
            var eventFramework = EventFramework.Instance();

            if (eventFramework == null)
                return null;

            return (FishingEventHandler*)eventFramework->EventHandlerModule.FishingEventHandler;
        }
    }

    public FishingState FishingState
    {
        get
        {
            // CS 的 State 是 4 位元組列舉；刻意只讀最低位元組，與原本的單位元組讀法相同。
            var ptr = FishingMan;
            return ptr != null ? (FishingState)(*(byte*)&ptr->State) : FishingState.NotFishing;
        }
    }

    public uint? CurrentSwimBait
    {
        get
        {
            var ptr = FishingMan;
            if (ptr == null)
                return null;

            return ptr->CurrentSelectedSwimBait switch
            {
                0x00 when ptr->SwimBaitId1 != 0 => ptr->SwimBaitId1,
                0x01 when ptr->SwimBaitId2 != 0 => ptr->SwimBaitId2,
                0x02 when ptr->SwimBaitId3 != 0 => ptr->SwimBaitId3,
                _ => null,
            };
        }
    }

    /// <summary>
    /// 三個泳餌欄位目前放的魚 id（0 ＝ 空欄位）。給 <c>SwimbaitCountCD</c> 條件用。
    /// </summary>
    /// <remarks>
    /// 🔴 一次解參考、當場把三個值複製出來。**不保存指標**，也不快取結果 ——
    ///    這個值每一竿都會變，而原生指標跨幀一律不能留。
    /// </remarks>
    public uint[] SwimBaitIds
    {
        get
        {
            var ptr = FishingMan;
            if (ptr == null)
                return [];

            return [ptr->SwimBaitId1, ptr->SwimBaitId2, ptr->SwimBaitId3];
        }
    }

    //public uint Current => PlayerState.Instance()->FishingBait;

    public uint CurrentBaitSwimBait => CurrentSwimBait ?? Current;

    /// <summary>
    /// 目前是不是站在宇宙探索地區。
    /// 判定本體在 <see cref="CosmicMissionInfo.IsInCosmicZone(WKSManager*)"/>
    /// （寫死的地區 ID 只出現在一個地方 —— 那種東西一旦分岔就是靜默失效）。
    /// 這裡刻意保留「傳入呼叫端已取得的指標」這個形狀，避免多一次 <c>Instance()</c>
    /// 而讓 null 檢查跟後面實際使用的指標變成兩個不同的值。
    /// </summary>
    private bool IsInCosmicZone(WKSManager* cosmicManager)
        => CosmicMissionInfo.IsInCosmicZone(cosmicManager);

    public uint Current
    {
        get
        {
            var cosmicManager = WKSManager.Instance();
            if (IsInCosmicZone(cosmicManager))
            {
                // ⚠️ 餌的欄位是 CS 的 WKSManager.FishingBait，不要寫死偏移：讀錯位置不會崩，只會一直給錯的餌 ID，
                //    而 GetCurrentBaitMoochId() 用這個值去 preset 裡挑「這個餌的設定」，值不對就只會退回 All Baits 的預設設定。
                var cosmicBait = cosmicManager->FishingBait;
                LogCosmicBait(cosmicBait);
                return cosmicBait;
            }

            var playerState = PlayerState.Instance();
            return playerState == null ? 0 : playerState->FishingBait;
        }
    }

    private uint _lastLoggedCosmicBait = uint.MaxValue;

    /// <summary>
    /// 宇宙探索的餌 ID 只在「換餌」那一刻寫一行 Information。
    /// 刻意不用 Debug：這是要使用者回報的診斷，
    /// 而這個值一旦讀錯，症狀是「preset 選錯」這種完全沒有錯誤訊息的形狀。
    /// </summary>
    private void LogCosmicBait(uint baitId)
    {
        if (baitId == _lastLoggedCosmicBait)
            return;

        _lastLoggedCosmicBait = baitId;
        Service.PrintInfo(baitId == 0
            ? @"[BaitManager] 宇宙探索：目前沒有掛餌（WKSManager.FishingBait = 0）。"
            : @$"[BaitManager] 宇宙探索：目前掛的餌 ID = {baitId}（{MultiString.GetItemName((int)baitId)}）。");
    }

    public ChangeBaitReturn ChangeBait(uint baitId)
    {
        if (baitId == Current)
            return ChangeBaitReturn.AlreadyEquipped;

        if (baitId == 0 || GameRes.Baits.All(b => b.Id != baitId))
            return ChangeBaitReturn.InvalidBait;

        if (PlayerRes.HasItem(baitId) <= 0)
        {
            WarnIfInventoryUnreadable(baitId);
            return ChangeBaitReturn.NotInInventory;
        }

        return SendChangeBait(4, baitId);
    }

    /// <summary>
    /// <c>InventoryManager.GetInventoryItemCount</c> 在換區／傳送期間（BetweenAreas / BetweenAreas51）
    /// 對**所有**道具都回 0，不報錯。所以「身上沒有這個餌」有兩種完全不同的成因，
    /// 而它們在 log 裡長得一模一樣。
    /// 這裡刻意**不改行為**（這種時候拒絕換餌本來就是對的），只把成因寫成 Information，
    /// 讓兩種成因在 log 裡分得開。
    /// </summary>
    private static void WarnIfInventoryUnreadable(uint baitId)
    {
        if (!Service.Condition[ConditionFlag.BetweenAreas] && !Service.Condition[ConditionFlag.BetweenAreas51])
            return;

        Service.PrintInfo(
            @$"[BaitManager] 換餌被判定為「身上沒有餌 {baitId}」，但目前正在換區／傳送中 —— " +
            @"這段期間道具數量一律讀成 0，所以這個判定不可信。等傳送結束後會自動重試。");
    }


    public ChangeBaitReturn ChangeSwimbait(uint id)
    {
        if (id > 2)
            return ChangeBaitReturn.InvalidBait;

        return SendChangeBait(25, id);
    }

    public ChangeBaitReturn ChangeBait(BaitFishClass bait)
    {
        if (bait.Id == Current)
        {
            Service.PrintChat($"釣餌「{bait.Name}」已經裝在身上了。");
            return ChangeBaitReturn.AlreadyEquipped;
        }

        if (bait.Id == 0 || GameRes.Baits.All(b => b.Id != bait.Id))
        {
            Service.PrintChat($"「{bait.Name}」不是可以使用的釣餌。");
            return ChangeBaitReturn.InvalidBait;
        }

        if (PlayerRes.HasItem((uint)bait.Id) <= 0)
        {
            Service.PrintChat($"背包裡沒有釣餌「{bait.Name}」。");
            WarnIfInventoryUnreadable((uint)bait.Id);
            return ChangeBaitReturn.NotInInventory;
        }

        return SendChangeBait(4, (uint)bait.Id);
    }

    public enum ChangeBaitReturn
    {
        Success,
        AlreadyEquipped,
        NotInInventory,
        InvalidBait,
        UnknownError,
        Unavailable,
    }
}