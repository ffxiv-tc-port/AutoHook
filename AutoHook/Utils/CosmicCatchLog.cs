using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AutoHook.Enums;

namespace AutoHook.Utils;

/// <summary>
/// 宇宙探索釣魚的**純量測**記錄器：只寫 log，**不參與任何決策**。
///
/// 為什麼存在：宇宙探索任務裡「Size 型任務要不要跳過弱咬」這個想法，整個價值前提是
/// 「弱咬＝小魚」。這件事**沒有任何離線證據** —— 遊戲沒有 per-fish 分數表，尺寸／收藏度
/// 才是計分軸，而咬鉤力道跟尺寸之間的關係只能在實機觀察。
/// 🔑 假設不成立的話，「跳過弱咬」會讓分數變低而且**完全沒有徵兆**（少釣到的魚不會有錯誤訊息）。
///    所以先量測、後改行為 —— 這個檔是量測那一步，行為變化是零。
///
/// 🔴 **權威邊界：這裡不讀、也永遠不會讀任務分數。**
///    任務進度／分數是 ICE 的職權（它有 WKS 任務狀態的完整讀取路徑與 UI）。
///    AutoHook 只負責「這一竿發生了什麼」——咬鉤力道、尺寸、大型旗標、收藏品旗標。
///    要把兩邊兜起來請走 IPC，不要在這裡長出第二套任務狀態讀取。
///
/// 輸出兩種行：
///   1. 明細行 —— 每個「魚 × 咬鉤力道 × 尺寸」的**新組合**印一次（同組合重複出現不再印）。
///      這樣就算只釣了十幾竿也看得到原始樣本，而不必等彙總。
///   2. 彙總行 —— 每 <see cref="SummaryIntervalMs"/> 毫秒，把有變動的魚各印一行，
///      **三種咬鉤力道並排**放在同一行。tug↔size 的相關性就是靠這一行用肉眼對出來的：
///      如果「弱咬＝小魚」成立，弱咬那一段的尺寸區間會明顯低於強咬／傳說咬。
/// </summary>
public static class CosmicCatchLog
{
    /// <summary>彙總行的間隔。太短會洗版，太長則短任務結束前印不出來。</summary>
    private const int SummaryIntervalMs = 60_000;

    /// <summary>
    /// 每種魚最多印幾行明細。尺寸是連續值，不設上限的話長時間掛機會把 log 洗掉；
    /// 超過上限之後仍然**照常統計**，只是不再印明細行。
    /// </summary>
    private const int MaxDetailLinesPerFish = 24;

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
        public readonly HashSet<(BiteType Bite, ushort Size)> SeenCombos = new();
        public CosmicScoreFocus Focus = CosmicScoreFocus.Unknown;
        public int DetailLines;
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
    public static void Record(uint fishId, uint amount, bool large, ushort size, bool collectible, BiteType bite)
    {
        try
        {
            if (!CosmicMissionInfo.IsInCosmicZone())
                return;

            var mission = CosmicMissionInfo.GetCurrentMissionId();
            var focus = CosmicMissionInfo.GetCurrentFocus();

            lock (Gate)
            {
                if (mission != _currentMission)
                {
                    // 換任務：先把上一個任務的統計倒出來，再清掉（key 帶任務 id，不清會無限長大）。
                    FlushLocked();
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

                fish.Focus = focus;
                fish.Dirty = true;

                tug.Count++;
                tug.SizeSum += size;
                if (size < tug.MinSize) tug.MinSize = size;
                if (size > tug.MaxSize) tug.MaxSize = size;
                if (large) tug.LargeCount++;
                if (collectible) tug.CollectibleCount++;

                var novelCombo = fish.SeenCombos.Add((bite, size));
                if (novelCombo && fish.DetailLines < MaxDetailLinesPerFish)
                {
                    fish.DetailLines++;
                    Service.PrintInfo(
                        $"[宇宙釣魚量測] 任務#{mission} 計分={FocusLabel(focus)}" +
                        $" ｜ 魚#{fishId} {MultiString.GetItemName(fishId)}" +
                        $" ｜ 咬鉤={BiteLabel(bite)} 尺寸={size} 大型={YesNo(large)} 收藏={YesNo(collectible)} 起獲={amount}");
                }

                if (Environment.TickCount64 - _lastSummaryTick >= SummaryIntervalMs)
                    FlushLocked();
            }
        }
        catch (Exception e)
        {
            // 量測失敗絕對不能影響釣魚本身。
            Service.PluginLog.Error($"[宇宙釣魚量測] 記錄失敗：{e.Message}");
        }
    }

    /// <summary>把有變動的魚各印一行彙總。呼叫端必須已經持有 <see cref="Gate"/>。</summary>
    private static void FlushLocked()
    {
        _lastSummaryTick = Environment.TickCount64;

        foreach (var (key, fish) in Buckets)
        {
            if (!fish.Dirty)
                continue;

            fish.Dirty = false;

            var sb = new StringBuilder();
            sb.Append($"[宇宙釣魚量測][彙總] 任務#{key.Mission} 計分={FocusLabel(fish.Focus)}");
            sb.Append($" ｜ 魚#{key.FishId} {MultiString.GetItemName(key.FishId)}");

            foreach (var (bite, tug) in fish.ByTug.OrderBy(kv => TugRank(kv.Key)))
            {
                var avg = (tug.SizeSum / (double)tug.Count).ToString("0.0", CultureInfo.InvariantCulture);
                sb.Append($" ｜ {BiteLabel(bite)} n={tug.Count} 尺寸 {tug.MinSize}~{tug.MaxSize} 均{avg}" +
                          $" 大型{tug.LargeCount} 收藏{tug.CollectibleCount}");
            }

            Service.PrintInfo(sb.ToString());
        }
    }

    private static int TugRank(BiteType bite) => bite switch
    {
        BiteType.Weak => 0,
        BiteType.Strong => 1,
        BiteType.Legendary => 2,
        _ => 3,
    };

    private static string BiteLabel(BiteType bite) => bite switch
    {
        BiteType.Weak => @"弱咬",
        BiteType.Strong => @"強咬",
        BiteType.Legendary => @"傳說咬",
        BiteType.None => @"無咬鉤",
        // 「不知道」要看得見，不能靜靜當成某一種。
        _ => @"未知咬鉤",
    };

    private static string FocusLabel(CosmicScoreFocus focus) => focus switch
    {
        CosmicScoreFocus.Quantity => @"數量/速度",
        CosmicScoreFocus.Evaluation => @"評價/收藏",
        _ => @"判不出",
    };

    private static string YesNo(bool value) => value ? @"是" : @"否";
}
