using System;
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
    /// 釣到「稀有魚」時請 TataruPraise 念一句。
    ///
    /// <para>
    /// 🔴 <b>這裡只做通知，不做任何自動化決策</b> —— 不換餌、不換 preset、不停手、不改狀態機。
    /// 整條路徑的任何失敗都只會表現成「沒出聲」，釣魚本身逐位元不受影響。
    /// </para>
    ///
    /// <para>
    /// 🔑 <b>「稀有」是怎麼定義的（這是本功能唯一需要人裁決的部分）</b>：
    /// AutoHook <b>本身沒有稀有度／品級的概念</b>（沒有 rarity 欄位、沒有「大魚」清單、
    /// 沒有首次釣獲記錄）。所以這裡<b>不發明新的分類</b>，只用它手上已經有的四個信號，
    /// 每一個都能單獨開關，預設只開最保守的那一個：
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     <b>大魚</b>（<see cref="Configurations.Configuration.TataruPraiseBigFish"/>，<b>預設開</b>）＝
    ///     這條魚在 AutoHook 自己的 <c>Data/FishData/fish_list.json</c> 裡
    ///     ①有 <c>Predators</c>（要先靠捕食魚累出漁人的直覺）②有 <c>WeathersFrom</c>（要天氣轉換）
    ///     ③或者是<b>魚影魚</b>（<c>FishParameter</c> 帶魚影訊息，遊戲自己的大魚機制）。
    ///     離線量測：非魚叉魚 1869 條裡符合①②的有 <b>135 條（7.2%）</b> ——
    ///     這正好是社群意義上的「大魚／傳說魚」，掛機一整天也不會誤觸幾次。
    ///   </description></item>
    ///   <item><description>
    ///     <b>遊戲的「大物」旗標</b>（<b>預設關</b>）＝ <c>UpdateCatch</c> 傳進來的 <c>large</c>。
    ///     這是遊戲自己給的，語意精確，但<b>觸發頻率我們沒有實機數據</b> ——
    ///     如果它其實很常見，開著會變成噪音，所以預設關、要的人自己勾。
    ///   </description></item>
    ///   <item><description>
    ///     <b>傳說咬「!!!」</b>（<b>預設關</b>）＝ <see cref="BiteType.Legendary"/>。
    ///     同上：某些餌／釣場的 <c>!!!</c> 是常態，不能當預設。
    ///   </description></item>
    ///   <item><description>
    ///     <b>收藏品</b>（<b>預設關</b>）＝ 收藏品釣魚時<b>每一條都是</b>，預設開就等於每竿出聲。
    ///   </description></item>
    /// </list>
    ///
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
            TataruPraiseIPC.TryPraise(@$"釣到{fishName}（判準：{reason}）");
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
