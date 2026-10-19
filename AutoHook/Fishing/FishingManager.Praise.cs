using System;
using System.Collections.Concurrent;
using System.Linq;
using AutoHook.Enums;
using AutoHook.IPC;
using AutoHook.Utils;

namespace AutoHook.Fishing;

public partial class FishingManager
{
    /// <summary>
    /// 上一次真的出聲的時間（<see cref="Environment.TickCount64"/>，0＝這次遊戲期間還沒出過聲）。
    /// 純粹是「就算判準寫壞了也不會變成每竿一句」的保險絲，不是功能本身。
    /// </summary>
    private long _lastPraiseTick;

    /// <summary>
    /// 待送出的誇獎（只放理由字串）。<b>入列在 <c>UpdateCatch</c> 的 detour 上，
    /// 出列與真正的 IPC 呼叫都在 Framework 執行緒上</b>。
    /// </summary>
    /// <remarks>
    /// 🔴🔴 <b>為什麼要分兩段</b>：IPC 的實作跑在<b>呼叫端的執行緒</b>上，所以從 detour 直接呼叫
    /// <c>TataruPraise.Praise</c> 等於把對方的程式碼拉進<b>我們 hook 住遊戲函式的那一格堆疊</b>裡跑。
    /// 對方在裡面做了什麼（讀原生指標、碰自己的 UI 狀態、再往下呼叫第三個外掛）我們管不到，
    /// 而 detour 是「遊戲正在處理釣獲」的中途 —— 它慢一點就是整個遊戲卡一下，它擲例外就是
    /// 從遊戲的呼叫框架裡往上竄。<b>hook detour 裡不做跨外掛呼叫</b>是通則，不是這一個對象的問題。
    /// <br/>📌 分兩段之後，detour 這一側只剩「判斷 ＋ 把字串放進佇列」，全是本外掛自己的碼。
    /// </remarks>
    private readonly ConcurrentQueue<string> _pendingPraises = new();

    /// <summary>
    /// 待送出上限。正常情況下每幀就排乾，而且判準本身還有 <see cref="_lastPraiseTick"/> 保險絲
    /// （預設 60 秒一次），所以這個上限只是「排乾那一端死了也不會無上限成長」的兜底。
    /// </summary>
    private const int MaxPendingPraises = 4;

    /// <summary>把這一次的誇獎排進佇列。<b>可以從任何執行緒呼叫</b>（實際上是 detour）。</summary>
    private void QueuePraise(string reason)
    {
        _pendingPraises.Enqueue(reason);

        while (_pendingPraises.Count > MaxPendingPraises && _pendingPraises.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// 把 detour 排下的誇獎送出去。<b>只在 Framework 執行緒上呼叫</b>
    /// （<c>OnFrameworkUpdate</c> 的最開頭，所有 early return 之前）。
    /// </summary>
    /// <remarks>
    /// 📌 判準、保險絲、<see cref="_lastPraiseTick"/> 的更新全部留在 detour 那一側，這裡只負責「送」：
    /// 最多晚一幀，而且不會再晚，因為排乾放在所有 early return 之前。
    /// <br/>⚠️ <c>TryPraise</c> 自己吞掉所有例外（含對方沒安裝時的 <c>IpcNotReadyError</c>），
    /// 所以這裡不再加一層 catch —— 沒裝 TataruPraise 的人在這條路徑上仍然是完全安靜的。
    /// </remarks>
    private void FlushPendingPraises()
    {
        while (_pendingPraises.TryDequeue(out var reason))
            TataruPraiseIPC.TryPraise(reason);
    }

    /// <summary>
    /// 釣到「稀有魚」時請 TataruPraise 念一句。
    /// <para>
    /// 🔴 <b>這裡只做通知，不做任何自動化決策</b> —— 不換餌、不換 preset、不停手、不改狀態機。
    /// 整條路徑的任何失敗都只會表現成「沒出聲」。
    /// </para>
    /// <para>
    /// 🔑 「稀有」不發明新的分類，只用手上已經有的四個信號，每一個都能單獨開關：<b>大魚</b>（預設開；
    /// <c>fish_list.json</c> 裡有 <c>Predators</c>／<c>WeathersFrom</c>，或是魚影魚）、遊戲的「大物」旗標、
    /// 傳說咬「!!!」、收藏品（後三者預設關，理由見各設定欄位）。
    /// </para>
    /// <para>
    /// ⚠️ <c>fish_list.json</c> 是<b>社群量測資料</b>，不是遊戲資料表 —— 台服要假設「可能有錯」。
    /// 錯的方向只有兩種：漏掉一條大魚（少出一次聲）或多算一條（多出一次聲），
    /// 都不會影響釣魚。載入失敗時 <see cref="GameRes.ImportedFishes"/> 是空的，
    /// 判準退化成「只認魚影魚」，一樣不會出事。
    /// </para>
    /// </summary>
    /// <param name="fishId">已經扣掉收藏品偏移（500000）的 item id。</param>
    /// <param name="large">遊戲傳來的「大物」旗標。</param>
    /// <param name="collectible">這一竿是不是以收藏品形式入手。</param>
    /// <param name="bite">咬鉤力道（<b>咬鉤當下</b>存下來的值，不是現讀的）。</param>
    private void NotifyRareCatch(uint fishId, bool large, bool collectible, BiteType bite)
    {
        try
        {
            var cfg = Service.Configuration;
            if (!cfg.TataruPraiseEnabled)
                return;

            var reason = ClassifyRareCatch(cfg, (int)fishId, large, collectible, bite);
            if (reason == null)
                return;

            // 保險絲：就算上面的判準因為資料檔出錯而變得太寬鬆，也不可能比這個間隔更頻繁。
            // （TataruPraise 那邊還有自己的逐情境冷卻，這裡只是不要白白去戳它。）
            var now = Environment.TickCount64;
            var minMs = Math.Max(0, cfg.TataruPraiseMinIntervalSeconds) * 1000L;
            if (_lastPraiseTick != 0 && now - _lastPraiseTick < minMs)
            {
                Service.PrintDebug(@$"[塔塔露誇獎] 判準命中（{reason}）但還在本機最短間隔內，跳過。");
                return;
            }

            _lastPraiseTick = now;

            var fishName = _lastCatch?.Name ?? MultiString.GetItemName(fishId);
            QueuePraise(@$"釣到{fishName}（判準：{reason}）");
        }
        catch (Exception e)
        {
            // 通知失敗絕對不能影響釣魚本身。
            Service.PrintInfo(@$"[塔塔露誇獎] 判斷稀有魚時失敗（不影響釣魚）：{e.Message}");
        }
    }

    /// <summary>
    /// 判斷這一竿算不算「稀有」。回傳 <c>null</c> ＝不算；回傳字串＝算，內容是給 log 看的理由。
    /// 判準的取捨寫在 <see cref="NotifyRareCatch"/> 的說明裡。
    /// </summary>
    private static string? ClassifyRareCatch(Configurations.Configuration cfg, int fishId, bool large,
        bool collectible, BiteType bite)
    {
        if (cfg.TataruPraiseBigFish)
        {
            // 魚影魚：遊戲自己的大魚機制（FishParameter 帶魚影出現／消失／釣起三則訊息）。
            // ⚠️ 不用 GameRes.LureFishes —— 那個屬性每次存取都 Where+ToList 整份魚表。
            if (GameRes.Fishes.Any(f => f.Id == fishId && f.LureMessage != ""))
                return @"魚影魚";

            var imported = GameRes.ImportedFishes.FirstOrDefault(f => f.ItemId == fishId);
            if (imported != null)
            {
                if (imported.Predators.Count > 0)
                    return @"大魚（需要漁人的直覺）";

                if (imported.WeathersFrom.Count > 0)
                    return @"大魚（需要天氣轉換）";
            }
        }

        if (cfg.TataruPraiseLargeFlag && large)
            return @"遊戲的大物旗標";

        if (cfg.TataruPraiseLegendaryBite && bite == BiteType.Legendary)
            return @"傳說咬";

        if (cfg.TataruPraiseCollectible && collectible)
            return @"收藏品";

        return null;
    }
}
