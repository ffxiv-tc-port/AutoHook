using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoHook.Utils;

/// <summary>
/// 宇宙探索任務剩餘時間不夠一竿時擋下拋竿（含拋竿前的增益動作）。
/// 剩餘時間讀任務面板 WKSMissionInfomation 節點清單第 24 個（ICE 交件判獎章讀的同一格）；
/// 只在框架執行緒呼叫，指標當場取、當場用完。
/// </summary>
public static class CosmicCastTimeGate
{
    private const string MissionInfoAddon = @"WKSMissionInfomation";
    private const int TimerNodeIndex = 24;

    private static ushort _logMission;
    private static int _lastSeconds;
    private static bool _loggedRead;
    private static bool _loggedUnreadable;
    private static bool _loggedHold;

    /// <summary>true＝這一刻不要拋竿。設定關、不在宇宙探索、沒有任務、讀不到剩餘時間都回 false。</summary>
    public static bool ShouldHoldCast()
    {
        if (!Service.Configuration.HoldCastWhenCosmicTimeShort || !CosmicMissionInfo.IsInCosmicZone())
            return false;

        var mission = CosmicMissionInfo.GetCurrentMissionId();
        if (mission == 0)
            return false;

        if (mission != _logMission)
        {
            _logMission = mission;
            _loggedRead = _loggedUnreadable = _loggedHold = false;
        }

        var text = ReadTimerText(out var why);
        if (!CosmicCastTimeRules.TryParseRemainingSeconds(text, out var seconds))
        {
            if (!_loggedUnreadable)
            {
                _loggedUnreadable = true;
                Service.PrintInfo(@$"[宇宙拋竿時限] 任務#{mission} 讀不到剩餘時間（{why ?? @$"計時列「{text}」解析不出分:秒"}），照常拋竿");
            }
            return false;
        }

        // 同一個任務重接時 id 不變，以計時列往回跳當成新的一場，讓每場各記一次。
        if (seconds > _lastSeconds + 30)
            _loggedRead = _loggedUnreadable = _loggedHold = false;
        _lastSeconds = seconds;

        if (!_loggedRead)
        {
            _loggedRead = true;
            Service.PrintInfo(@$"[宇宙拋竿時限] 任務#{mission} 讀到剩餘 {Clock(seconds)}（計時列「{text}」）；" +
                              @$"剩不到 {CosmicCastTimeRules.MinSecondsForOneCast} 秒時不再拋竿");
        }

        if (!CosmicCastTimeRules.ShouldHoldCast(seconds))
            return false;

        Service.Status = @$"宇宙探索任務剩餘 {Clock(seconds)}，不夠一竿：不再拋竿";
        if (!_loggedHold)
        {
            _loggedHold = true;
            Service.PrintInfo(@$"[宇宙拋竿時限] 任務#{mission} 剩餘 {Clock(seconds)}，不到 " +
                              @$"{CosmicCastTimeRules.MinSecondsForOneCast} 秒、來不及釣完一竿：不再拋竿（含拋竿前的動作）");
        }
        return true;
    }

    private static string Clock(int seconds) => @$"{seconds / 60}:{seconds % 60:00}";

    /// <summary>回 null 時 <paramref name="why"/> 說明哪一步讀不到。面板關著或隱藏＝讀不到（不拿舊字當真）。</summary>
    private static unsafe string? ReadTimerText(out string? why)
    {
        why = null;
        var addon = (AtkUnitBase*)Service.GameGui.GetAddonByName(MissionInfoAddon, 1).Address;
        if (addon == null || !addon->IsVisible)
        {
            why = @"任務面板沒開";
            return null;
        }

        var uld = addon->UldManager;
        if (uld.NodeList == null || uld.NodeListCount <= TimerNodeIndex)
        {
            why = @"任務面板節點不足";
            return null;
        }

        var node = uld.NodeList[TimerNodeIndex];
        if (node == null || node->Type != NodeType.Text || !node->IsVisible())
        {
            why = @"計時列節點不是可見的文字";
            return null;
        }

        return MemoryHelper.ReadSeString(&((AtkTextNode*)node)->NodeText).TextValue;
    }
}
