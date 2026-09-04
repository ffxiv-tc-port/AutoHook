using System;
using Dalamud.Plugin.Ipc.Exceptions;

namespace AutoHook.IPC;

/// <summary>
/// 單向橋接到「塔塔露誇獎」(TataruPraise)：釣到稀有魚時請它念一句，把掛機的人叫回電腦前。
/// </summary>
/// <remarks>
/// 🔴 <b>零組件相依。</b>只用 Dalamud 原生 CallGate 的字串契約，不引用 TataruPraise 的任何型別。
/// 兩邊裝／移除任一方都不會弄壞另一邊 —— 對方沒安裝時本檔的每一條路徑都是 no-op。
/// <para>
/// 🔴 契約名逐字取自 TataruPraise 的 <c>IpcContract.cs</c>。CallGate 是純字串比對，
/// 名字打錯不會有任何錯誤訊息，只會永遠得到「這個頻道沒有人註冊」——<b>靜默斷線</b>。
/// 所以字串都寫成常數，不散在呼叫點上，也不要「順手整理」大小寫。
/// </para>
/// <para>
/// 🔴 <b>只能從遊戲主執行緒呼叫。</b>IPC 的實作跑在<b>呼叫端</b>的執行緒上，從背景 Task 叫過去
/// 等於把對方的程式碼拉到背景執行緒。目前唯一的呼叫點在 <c>UpdateCatchDetour</c> → <c>OnCatch</c>
/// 這條鏈上，那是遊戲自己的函式被 hook 住的位置，必定在主執行緒。
/// </para>
/// <para>
/// ⚠️ 這是<b>單向通知</b>：回傳值只拿來寫記錄，<b>不影響釣魚流程的任何一步</b>，不重試、不節流釣魚。
/// </para>
/// <para>
/// ⚠️ 每次呼叫都重新取 subscriber，不快取。TataruPraise 可以在 AutoHook 載入之後才被裝上／重載，
/// 快取住的 subscriber 在那之後的行為沒有保證；重取的成本只是一次字典查詢。
/// </para>
/// </remarks>
internal static class TataruPraiseIPC
{
    /// <summary><c>Func&lt;bool&gt;</c>：總開關開著而且池裡真的有已合成的語音。</summary>
    private const string TagIsAvailable = "TataruPraise.IsAvailable";

    /// <summary><c>Func&lt;string, bool&gt;</c>：從指定情境的誇獎池挑一句來念。</summary>
    private const string TagPraise = "TataruPraise.Praise";

    /// <summary>
    /// 送過去的情境字串。
    /// ⚠️ TataruPraise 拿這個字串當池的鍵，<b>對不上就靜默不出聲</b>（它自己會寫一行 Information，
    /// AutoHook 這邊只看得到 <c>false</c>）。這個鍵是跨外掛約定好的，<b>一個字都不要改</b>。
    /// </summary>
    internal const string CategoryRareFish = "稀有魚";

    /// <summary>
    /// 請塔塔露念一句。對方沒裝、關著、或池裡沒東西，這裡都是安靜的 no-op。
    /// </summary>
    /// <param name="reason">寫進記錄用的來源描述，讓 log 分得出是哪一條判準觸發的。</param>
    /// <returns>對方真的接受了才回 <c>true</c>；任何失敗都回 <c>false</c>，<b>絕不擲例外</b>。</returns>
    internal static bool TryPraise(string reason)
    {
        try
        {
            // 先問 IsAvailable：對方的總開關關著、或池裡一句已合成的都沒有，就不要浪費它的冷卻。
            // 這一步同時兼作「對方在不在」的探測 —— 沒註冊就會在這裡擲 IpcNotReadyError。
            if (!Service.PluginInterface.GetIpcSubscriber<bool>(TagIsAvailable).InvokeFunc())
                return false;

            var accepted = Service.PluginInterface.GetIpcSubscriber<string, bool>(TagPraise)
                .InvokeFunc(CategoryRareFish);

            // Information 級：這是「使用者說沒出聲」時唯一問得出真相的一行。
            Service.PrintInfo(@$"[塔塔露誇獎] {reason}：Praise(「{CategoryRareFish}」) 回傳 {accepted}。");
            return accepted;
        }
        catch (IpcNotReadyError)
        {
            // 對方沒安裝／還沒載入。這是完全正常的狀態，刻意不寫 log —— 沒裝的人每一竿都會走到這裡。
            return false;
        }
        catch (Exception e)
        {
            // 其他狀況（型別不合、對方在自己的回呼裡爆掉之類）記一筆就好，
            // 絕不要讓它往上冒去打斷 OnCatch 後面的換餌／換 preset／停手判斷。
            Service.PrintInfo(@$"[塔塔露誇獎] 呼叫失敗（{reason}）：{e.Message}");
            return false;
        }
    }
}
