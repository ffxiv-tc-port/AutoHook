using System;
using System.Collections.Generic;
using System.Linq;
using AutoHook.Data;
using AutoHook.Enums;

namespace AutoHook.Utils;

/// <summary>
/// 某一刻的 GP 與釣魚相關狀態，純量測用、不參與任何決策。
/// 只存值（數字與字串），不存任何原生指標，所以可以跨幀保留到釣起那一刻再印。
/// <see cref="Known"/> 為 false＝沒拍到（例如插件在拋竿後才開），印成「?」而不是 0。
/// </summary>
public readonly record struct FishingSnapshot(bool Known, uint Gp, string Statuses)
{
    /// <summary>要記的狀態與標籤；順序就是印出來的順序。帶層數的會印成「名稱×層數」。</summary>
    private static readonly (uint Id, string Label, bool Stacks)[] Tracked =
    {
        (IDs.Status.PrizeCatch, @"大魚獵手", false),
        (IDs.Status.IdenticalCast, @"專一垂釣", false),
        (IDs.Status.AnglersFortune, @"耐心", false),
        (IDs.Status.InefficientHooking, @"提鉤成功率降低", false),
        (IDs.Status.Chum, @"撒餌", false),
        (IDs.Status.SurfaceSlap, @"拍擊水面", false),
        (IDs.Status.FishersIntuition, @"漁師之識", false),
        (IDs.Status.BigGameFishing, @"大魚知識", false),
        (IDs.Status.AmbitiousLure, @"雄心之餌", true),
        (IDs.Status.ModestLure, @"謙遜之餌", true),
        (IDs.Status.AnglersArt, @"漁師之計", true),
        (IDs.Status.FishEyes, @"魚眼", false),
        (IDs.Status.MakeshiftBait, @"熟練漁技", false),
        (IDs.Status.CollectorsGlove, @"收藏品採集", false),
    };

    private static readonly Dictionary<uint, int> TrackedIndex =
        Tracked.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i);

    /// <summary>
    /// 拍一份。要在遊戲主執行緒上呼叫（讀 ObjectTable）；讀不到角色或出錯＝<see cref="Known"/> false。
    /// 不擲例外：呼叫點夾在拋竿／提鉤的既有流程中間，量測失敗不可以讓那些流程半途中斷。
    /// </summary>
    public static FishingSnapshot Take()
    {
        try
        {
            var player = Service.Objects.LocalPlayer;
            if (player == null)
                return default;

            var found = new List<(int Index, string Text)>();
            foreach (var status in player.StatusList)
            {
                if (!TrackedIndex.TryGetValue(status.StatusId, out var index))
                    continue;

                var t = Tracked[index];
                found.Add((index, t.Stacks ? $"{t.Label}×{status.Param}" : t.Label));
            }

            var text = string.Join(@"、", found.OrderBy(f => f.Index).Select(f => f.Text));
            return new FishingSnapshot(true, player.CurrentGp, text);
        }
        catch (Exception e)
        {
            ReportFailureOnce(e);
            return default;
        }
    }

    private static int _failureReported;

    private static void ReportFailureOnce(Exception e)
    {
        if (System.Threading.Interlocked.Exchange(ref _failureReported, 1) == 0)
            Service.PluginLog.Warning($"[宇宙釣魚量測] 讀 GP／狀態失敗（之後不再記錄，量測行會印「?」）：{e.Message}");
    }

    public string GpText => Known ? Gp.ToString() : @"?";

    public string StatusText => !Known ? @"?" : Statuses.Length == 0 ? @"無" : Statuses;

    public static string HookLabel(HookType? hook) => hook switch
    {
        null => @"?",
        HookType.None => @"歇竿",
        HookType.Normal => @"一般提鉤",
        HookType.Precision => @"精準提鉤",
        HookType.Powerful => @"強力提鉤",
        HookType.Double => @"雙重提鉤",
        HookType.Triple => @"三重提鉤",
        HookType.Stellar => @"華麗提鉤",
        _ => @$"未知({(uint)hook})",
    };
}
