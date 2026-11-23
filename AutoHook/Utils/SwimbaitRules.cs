using System.Collections.Generic;
using AutoHook.SeFunctions;

namespace AutoHook.Utils;

/// <summary>
/// 游動餌與換餌的純判斷（不讀遊戲記憶體）：呼叫端當場讀好值再傳進來。
/// 判準照遊戲自己送「切換釣餌」指令前的本機檢查。
/// </summary>
public static class SwimbaitRules
{
    /// <summary><c>FishingEventHandler.CurrentSelectedSwimBait</c> 沒有選游動餌時的值。</summary>
    public const byte NoSwimbaitSelected = 0xFF;

    /// <summary>送出游動餌切換後，最多等這麼久確認選中；確認不到就照常拋竿。</summary>
    public const long ConfirmTimeoutMs = 1500;

    /// <summary>遊戲的判定：同一顆餌、而且沒有選游動餌，才算已經掛著（選了游動餌時換回同一顆一般餌仍要送）。</summary>
    public static bool IsBaitEquipped(uint baitId, uint currentBait, byte selectedSwimbait)
        => baitId == currentBait && selectedSwimbait == NoSwimbaitSelected;

    /// <summary>null＝可以送；否則是不送的理由。</summary>
    public static BaitManager.ChangeBaitReturn? SwimbaitPrecheck(uint index, IReadOnlyList<uint> slots,
        bool hasSlapOrIdenticalCast, byte selectedSwimbait)
    {
        if (index > 2)
            return BaitManager.ChangeBaitReturn.InvalidBait;

        if (index >= slots.Count || slots[(int)index] == 0)
            return BaitManager.ChangeBaitReturn.EmptySlot;

        // 遊戲在拍擊水面／專一垂釣期間不送這道指令，改印「目前無法使用游動餌。」。
        if (hasSlapOrIdenticalCast)
            return BaitManager.ChangeBaitReturn.BlockedByStatus;

        // 刻意不重送：已選中時再送一次的結果沒驗過（可能被當成取消選取）。
        if (selectedSwimbait == index)
            return BaitManager.ChangeBaitReturn.AlreadyEquipped;

        return null;
    }

    public enum ConfirmResult
    {
        None,
        Pending,
        Confirmed,
        TimedOut,
    }

    /// <summary><paramref name="selectedSwimbait"/> null＝讀不到遊戲狀態（照常等到逾時）。</summary>
    public static ConfirmResult Confirm(byte targetIndex, byte? selectedSwimbait, long elapsedMs)
    {
        if (selectedSwimbait == targetIndex)
            return ConfirmResult.Confirmed;

        return elapsedMs < ConfirmTimeoutMs ? ConfirmResult.Pending : ConfirmResult.TimedOut;
    }
}

/// <summary>
/// 同一格游動餌連續確認失敗 <see cref="Threshold"/> 次就先停用那一格，游動餌清單（三格內容）改變才解除。
/// 為什麼：被拒的切換若每竿都重送，只會每竿多等一次逾時。
/// </summary>
internal sealed class SwimbaitFailureGate
{
    public const int Threshold = 2;

    private (uint, uint, uint) _snapshot;
    private int _slot = -1;
    private int _failures;

    public int SuppressedSlot { get; private set; } = -1;

    private static (uint, uint, uint) Snap(IReadOnlyList<uint> slots)
        => (slots.Count > 0 ? slots[0] : 0, slots.Count > 1 ? slots[1] : 0, slots.Count > 2 ? slots[2] : 0);

    /// <summary>清單和記錄時不同就清掉計數；回 true＝這次解除了停用。</summary>
    public bool Refresh(IReadOnlyList<uint> slots)
    {
        if (_slot < 0 || Snap(slots) == _snapshot)
            return false;

        var lifted = SuppressedSlot >= 0;
        Reset();
        return lifted;
    }

    public bool IsSuppressed(int slot) => SuppressedSlot >= 0 && SuppressedSlot == slot;

    /// <summary>回 true＝這次失敗讓這一格進入停用。</summary>
    public bool RecordFailure(int slot, IReadOnlyList<uint> slots)
    {
        var snap = Snap(slots);
        if (slot != _slot || snap != _snapshot)
        {
            Reset();
            _slot = slot;
            _snapshot = snap;
        }

        _failures++;
        if (_failures < Threshold || SuppressedSlot == slot)
            return false;

        SuppressedSlot = slot;
        return true;
    }

    public void RecordSuccess() => Reset();

    private void Reset()
    {
        _slot = -1;
        _snapshot = default;
        _failures = 0;
        SuppressedSlot = -1;
    }
}
