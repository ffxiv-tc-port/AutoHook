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
    }

    private delegate byte ExecuteCommandDelegate(int id, int unk1, uint baitId, int unk2, int unk3);

    [Signature("E8 ?? ?? ?? ?? 41 C6 04 24")]
    private readonly ExecuteCommandDelegate _executeCommand = null!;

    private const int FishingManagerOffset = 0x70;

    internal FishingManagerStruct* FishingMan
    {
        get
        {
            // 🔴 原本是先算 `(nint)EventFramework.Instance() + 0x70` 再檢查那個和是不是 0。
            //    加了 0x70 之後**永遠不可能是 0**，所以那兩個檢查（而且還重複寫了兩次）
            //    等於沒有：EventFramework 還沒建好時會去解參考位址 0x70 → AccessViolation。
            //    ⚠️ AVE 是 corrupted-state exception，try/catch 攔不到，直接讓遊戲當掉。
            //    要檢查的是**實例本身**，不是加了偏移之後的位址。
            var eventFramework = EventFramework.Instance();

            if (eventFramework == null)
                return null;

            return *(FishingManagerStruct**)((nint)eventFramework + FishingManagerOffset);
        }
    }

    public FishingState FishingState
    {
        get
        {
            var ptr = FishingMan;
            return ptr != null ? ptr->FishingState : FishingState.NotFishing;
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
    /// 判定本體搬到 <see cref="CosmicMissionInfo.IsInCosmicZone(WKSManager*)"/>（邏輯逐字相同，
    /// 只是不想讓寫死的地區 ID 出現在兩個地方 —— 那種東西一旦分岔就是靜默失效）。
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
                // ⚠️ 這裡原本是寫死的 `*(uint*)((byte*)cosmicManager + 0xC9C)`。
                //    在目前釘住的 FFXIVClientStructs 裡，餌的欄位是 WKSManager+0xC4C（FishingBait），
                //    而 0xC9C 落在 0xC55~0xCDD 的 _missionCompletionFlags 中間 —— 讀出來是
                //    任務完成旗標被當成 uint 解讀的垃圾值，不會崩，只會一直給錯的餌 ID。
                //    影響不只顯示：GetCurrentBaitMoochId() 用這個值去 preset 裡挑「這個餌的設定」，
                //    值不對就永遠挑不到，只會退回 All Baits 的預設設定。
                //    台服實機佐證：ICE 讀同一個 CS 欄位印出 45952（宇宙幼蟲），是合法的月面餌。
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
    /// 刻意不用 Debug：使用者的記錄等級是 1，Debug 收得到但單檔數十萬行會淹沒，
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

        return _executeCommand(701, 4, baitId, 0, 0) == 1 ? ChangeBaitReturn.Success : ChangeBaitReturn.UnknownError;
    }

    /// <summary>
    /// <c>InventoryManager.GetInventoryItemCount</c> 在換區／傳送期間（BetweenAreas / BetweenAreas51）
    /// 對**所有**道具都回 0，不報錯。所以「身上沒有這個餌」有兩種完全不同的成因，
    /// 而它們在 log 裡長得一模一樣。
    ///
    /// 這裡刻意**不改行為**（這種時候拒絕換餌本來就是對的），只把成因寫成 Information，
    /// 免得下次又要從「ICE 一直重送 /ahbait」倒推回來。
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

        return _executeCommand(701, 25, id, 0, 0) == 1 ? ChangeBaitReturn.Success : ChangeBaitReturn.UnknownError;
    }

    public ChangeBaitReturn ChangeBait(BaitFishClass bait)
    {
        if (bait.Id == Current)
        {
            Service.PrintChat($"Bait \"{bait.Name}\" is already equipped.");
            return ChangeBaitReturn.AlreadyEquipped;
        }

        if (bait.Id == 0 || GameRes.Baits.All(b => b.Id != bait.Id))
        {
            Service.PrintChat($"Bait \"{bait.Name}\" is not a valid bait.");
            return ChangeBaitReturn.InvalidBait;
        }

        if (PlayerRes.HasItem((uint)bait.Id) <= 0)
        {
            Service.PrintChat($"Bait \"{bait.Name}\" is not in your inventory.");
            WarnIfInventoryUnreadable((uint)bait.Id);
            return ChangeBaitReturn.NotInInventory;
        }

        return _executeCommand(701, 4, (uint)bait.Id, 0, 0) == 1
            ? ChangeBaitReturn.Success
            : ChangeBaitReturn.UnknownError;
    }

    public enum ChangeBaitReturn
    {
        Success,
        AlreadyEquipped,
        NotInInventory,
        InvalidBait,
        UnknownError,
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct FishingManagerStruct
    {
        [FieldOffset(0x228)] public FishingState FishingState;

        [FieldOffset(0x23C)] public byte CurrentSelectedSwimBait;

        [FieldOffset(0x240)] public uint SwimBaitId1;

        [FieldOffset(0x244)] public uint SwimBaitId2;

        [FieldOffset(0x248)] public uint SwimBaitId3;
    }
}