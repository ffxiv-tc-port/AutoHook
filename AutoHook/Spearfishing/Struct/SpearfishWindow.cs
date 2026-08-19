using System.Runtime.InteropServices;
using AutoHook.Spearfishing.Enums;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoHook.Spearfishing.Struct;

[StructLayout(LayoutKind.Explicit)]
public struct SpearfishWindow
{
    [FieldOffset(0)]
    public AtkUnitBase Base;

    [StructLayout(LayoutKind.Explicit)]
    public struct Info
    {
        [FieldOffset(8)]
        public bool Available;

        [FieldOffset(16)]
        public bool InverseDirection;

        [FieldOffset(17)]
        public bool GuaranteedLarge;

        [FieldOffset(18)]
        public SpearfishSize Size;

        [FieldOffset(20)]
        public SpearfishSpeed Speed;
    }

    [FieldOffset(0x294)]
    public Info Fish1;

    [FieldOffset(0x2B0)]
    public Info Fish2;

    [FieldOffset(0x2CC)]
    public Info Fish3;


    /// <summary>
    /// 取節點清單裡的第 index 個節點,取不到就回 <c>null</c>。
    /// 🔴 原本每個存取子都直接寫 <c>Base.UldManager.NodeList[n]</c>,裡面有兩個沒被驗證過的假設:
    /// ①節點清單已經配置好 —— 視窗剛開的那幾幀 <c>NodeList</c> 是 null,對 null 做索引再解參考
    /// 是 AccessViolationException,在 .NET Core 屬 corrupted-state exception,try/catch 攔不到;
    /// ②清單長度大於這裡寫死的索引 —— 索引是照 addon 版面數出來的,台服的版面沒有逐版驗證過,
    /// 越界讀到的是一個垃圾指標,拿去解參考比 null 更難查,而且失敗方式是靜默的。
    /// ⇒ 兩個假設都在這裡擋掉,取不到就回 null,由呼叫端決定這一幀要怎麼辦。
    /// </summary>
    private unsafe AtkResNode* GetNode(int index)
    {
        var nodeList = Base.UldManager.NodeList;
        if (nodeList == null || index < 0 || index >= Base.UldManager.NodeListCount)
            return null;

        return nodeList[index];
    }

    public unsafe AtkResNode* FishLines
        => GetNode(3);

    public unsafe AtkResNode* Fish1Node
        => GetNode(15);

    public unsafe AtkResNode* Fish2Node
        => GetNode(16);

    public unsafe AtkResNode* Fish3Node
        => GetNode(17);

    public unsafe AtkComponentGaugeBar* GaugeBar
        => (AtkComponentGaugeBar*)GetNode(35);


}