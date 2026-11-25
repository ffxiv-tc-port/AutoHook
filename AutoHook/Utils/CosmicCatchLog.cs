using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AutoHook.Enums;

namespace AutoHook.Utils;

/// <summary>
/// 宇宙探索釣魚的**純量測**記錄器：只寫 log，**不參與任何決策**。
/// 要量的是「弱咬＝小魚」這個假設（咬鉤力道跟尺寸的關係只能在實機觀察）；假設不成立的話
/// 「跳過弱咬」會讓分數變低而且**完全沒有徵兆**，所以先量測、後改行為。
/// 🔴 **權威邊界：這裡不讀、也永遠不會讀任務分數。**任務進度／分數是 ICE 的職權，
///    AutoHook 只負責「這一竿發生了什麼」；要把兩邊兜起來請走 IPC。
/// 輸出：每一竿一行明細（含拋竿／提鉤前的 GP 與狀態），與每 <see cref="SummaryIntervalMs"/> 毫秒三種咬鉤力道並排的彙總行。
/// 明細刻意每竿都印（每竿一行）：GP 換分數的分析要逐竿資料，只印新組合會讓樣本有偏差。
/// </summary>
public static class CosmicCatchLog
{
    /// <summary>彙總行的間隔。太短會洗版，太長則短任務結束前印不出來。</summary>
    private const int SummaryIntervalMs = 60_000;

    private sealed class TugStats
    {
        public int Count;
        public ushort MinSize = ushort.MaxValue;
        public ushort MaxSize;
        public long SizeSum;
        public int LargeCount;
        public int CollectibleCount;
    }

    private sealed class FishStats
    {
        public readonly Dictionary<BiteType, TugStats> ByTug = new();
        public CosmicMissionProfile Profile;
        public bool Dirty;
    }

    /// <summary>
    /// <c>UpdateCatch</c> 是遊戲主執行緒上的 hook，理論上不會併發；
    /// 這個 lock 純粹是保險 —— 一般 <see cref="Dictionary{TKey,TValue}"/> 在極少見的併發寫入下
    /// 會壞成無窮迴圈（表現是整個遊戲卡住），代價比一個 lock 高太多。
    /// </summary>
    private static readonly object Gate = new();

    private static readonly Dictionary<(ushort Mission, uint FishId), FishStats> Buckets = new();

    /// <summary>用 <see cref="ushort.MaxValue"/> 當哨兵，這樣「任務 id 0」也算得上是一次換任務。</summary>
    private static ushort _currentMission = ushort.MaxValue;

    private static long _lastSummaryTick = Environment.TickCount64;

    /// <summary>
    /// 記一竿。**不在宇宙探索地區就直接返回**，一般釣魚完全不受影響。
    /// </summary>
    /// <param name="fishId">已經扣掉收藏品偏移（500000）的 item id。</param>
    /// <param name="amount">這一竿起了幾條（雙重／三重提鉤會 &gt; 1）。</param>
    /// <param name="large">遊戲傳來的「大型」旗標。</param>
    /// <param name="size">遊戲傳來的尺寸。單位沒有離線證據，先照原值記錄。</param>
    /// <param name="collectible">這一竿是不是以收藏品形式入手（fishId 原值 &gt; 500000）。</param>
    /// <param name="bite">咬鉤力道。⚠️ 必須是**咬鉤當下**存下來的值，不能在這裡現讀。</param>
    /// <param name="atCast">拋竿那一刻（拋竿動作送出前）的 GP 與狀態。</param>
    /// <param name="atBite">咬鉤後、提鉤動作送出前的 GP 與狀態。</param>
    /// <param name="hook">AutoHook 這一竿決定用的提鉤；null＝沒經過 AutoHook 的提鉤判斷。</param>
    public static void Record(uint fishId, uint amount, bool large, ushort size, bool collectible, BiteType bite,
        FishingSnapshot atCast, FishingSnapshot atBite, HookType? hook)
    {
        List<string>? pending = null;

        try
        {
            if (!CosmicMissionInfo.IsInCosmicZone())
                return;

            var mission = CosmicMissionInfo.GetCurrentMissionId();
            var profile = CosmicMissionInfo.GetCurrentProfile();

            lock (Gate)
            {
                if (mission != _currentMission)
                {
                    // 換任務：先把上一個任務的統計倒出來，再清掉（key 帶任務 id，不清會無限長大）。
                    FlushLocked(ref pending);
                    Buckets.Clear();
                    _currentMission = mission;
                }

                var key = (mission, fishId);
                if (!Buckets.TryGetValue(key, out var fish))
                {
                    fish = new FishStats();
                    Buckets[key] = fish;
                }

                if (!fish.ByTug.TryGetValue(bite, out var tug))
                {
                    tug = new TugStats();
                    fish.ByTug[bite] = tug;
                }

                fish.Profile = profile;
                fish.Dirty = true;

                tug.Count++;
                tug.SizeSum += size;
                if (size < tug.MinSize) tug.MinSize = size;
                if (size > tug.MaxSize) tug.MaxSize = size;
                if (large) tug.LargeCount++;
                if (collectible) tug.CollectibleCount++;

                // 前段格式不可改：分析工具以「…起獲=N」為前綴解析，新欄位一律接在後面。
                QueueLine(ref pending,
                    $"[宇宙釣魚量測] 任務#{mission} 計分={CosmicMissionInfo.ShortScoreLabel(profile)}" +
                    $" ｜ 魚#{fishId} {MultiString.GetItemName(fishId)}" +
                    $" ｜ 咬鉤={BiteLabel(bite)} 尺寸={size} 大型={YesNo(large)} 收藏={YesNo(collectible)} 起獲={amount}" +
                    $" ｜ 提鉤={FishingSnapshot.HookLabel(hook)}" +
                    $" ｜ GP 拋竿={atCast.GpText} 提鉤前={atBite.GpText}" +
                    $" ｜ 拋竿時狀態={atCast.StatusText} ｜ 提鉤前狀態={atBite.StatusText}");

                if (Environment.TickCount64 - _lastSummaryTick >= SummaryIntervalMs)
                    FlushLocked(ref pending);
            }

            // 出鎖之後才真的送出去。留在 try 裡面，所以送出時萬一出錯，
            // 接住它的仍然是下面那個 catch。
            EmitQueued(pending);
        }
        catch (Exception e)
        {
            // 量測失敗絕對不能影響釣魚本身。
            Service.PluginLog.Error($"[宇宙釣魚量測] 記錄失敗：{e.Message}");
        }
    }

    /// <summary>把有變動的魚各印一行彙總。呼叫端必須已經持有 <see cref="Gate"/>。</summary>
    /// <remarks>訊息只排進 <paramref name="pending"/>，實際送出由呼叫端在鎖外做。</remarks>
    private static void FlushLocked(ref List<string>? pending)
    {
        _lastSummaryTick = Environment.TickCount64;

        foreach (var (key, fish) in Buckets)
        {
            if (!fish.Dirty)
                continue;

            fish.Dirty = false;

            var sb = new StringBuilder();
            sb.Append($"[宇宙釣魚量測][彙總] 任務#{key.Mission} 計分={CosmicMissionInfo.ShortScoreLabel(fish.Profile)}");
            sb.Append($" ｜ 魚#{key.FishId} {MultiString.GetItemName(key.FishId)}");

            foreach (var (bite, tug) in fish.ByTug.OrderBy(kv => TugRank(kv.Key)))
            {
                var avg = (tug.SizeSum / (double)tug.Count).ToString("0.0", CultureInfo.InvariantCulture);
                sb.Append($" ｜ {BiteLabel(bite)} n={tug.Count} 尺寸 {tug.MinSize}~{tug.MaxSize} 均{avg}" +
                          $" 大型{tug.LargeCount} 收藏{tug.CollectibleCount}");
            }

            QueueLine(ref pending, sb.ToString());
        }
    }

    /// <summary>
    /// 把一行排進佇列，**不送出**。
    /// <see cref="Service.PrintInfo"/> 會先推進 <c>Service</c> 自己的環形緩衝區（那是另一把鎖），
    /// 再寫 <c>PluginLog</c>（Dalamud 的 Serilog sink，最後會落地成檔案）——
    /// 巢狀鎖加檔案 I/O，都不該在持有 <see cref="Gate"/> 的時候做。
    /// </summary>
    private static void QueueLine(ref List<string>? pending, string line)
        => (pending ??= new List<string>()).Add(line);

    /// <summary>
    /// 送出 <see cref="QueueLine"/> 排好的訊息。**必須在鎖外呼叫。**
    /// </summary>
    private static void EmitQueued(List<string>? pending)
    {
        if (pending is null)
            return;

        foreach (var line in pending)
            Service.PrintInfo(line);
    }

    private static int TugRank(BiteType bite) => bite switch
    {
        BiteType.Weak => 0,
        BiteType.Strong => 1,
        BiteType.Legendary => 2,
        _ => 3,
    };

    public static string BiteLabel(BiteType bite) => bite switch
    {
        BiteType.Weak => @"弱咬",
        BiteType.Strong => @"強咬",
        BiteType.Legendary => @"傳說咬",
        BiteType.None => @"無咬鉤",
        // 「不知道」要看得見，不能靜靜當成某一種。
        _ => @"未知咬鉤",
    };

    private static string YesNo(bool value) => value ? @"是" : @"否";
}
