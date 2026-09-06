using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using AutoHook.Resources.Localization;

namespace AutoHook.Utils;

/// <summary>
/// 在地化字串的安全閘門。
///
/// 🔴 <b>把空字串交給 ImGui 會讓整個遊戲當場結束，不是畫面破圖而已。</b>
///
/// 機制（2026-08-06 實機崩潰 crash-20260806153115 逐項對過）：
///   1. <c>ImU8String</c> 對長度 0 的字串，<c>Span</c> 是一個零長度 span。
///   2. 原生邊界是用 <c>fixed</c> 取指標的，而 C# 規格規定
///      <b>零長度 span 取出來的指標就是 null</b>。
///   3. 原生 <c>igFindRenderedTextEnd(text, text_end)</c> 收到 text=NULL、text_end=NULL，
///      看到 text_end 是 null 就把它換成 <c>(const char*)-1</c>，
///      於是從位址 0 開始一路往上掃 → C0000005。
///      （崩潰現場暫存器：RCX=0、RDX=0、R8=FFFFFFFFFFFFFFFF，位置 igFindRenderedTextEnd+0x13。）
///
/// 🔑 空字串是怎麼跑出來的：<b>資源檔裡「鍵存在，但 value 是空的」</b>。
///    <see cref="ResourceManager"/> 遇到這種條目<b>不會</b>退回中性資源 ——
///    它認為那個語系有答案，答案就是空字串。
///    （上游 crowdin 匯出的 <c>UIStrings.zh.resx</c> 就有 4 個這種條目，
///     其中 <c>StellarHookset</c> 正是把使用者遊戲帶走的那一個。）
///    ⚠️ 注意「鍵不存在」是安全的 —— 那才會正常 fallback 到英文。危險的只有「存在但空」。
/// </summary>
public static class Loc
{
    /// <summary>
    /// 保證交給 ImGui 的字串不是 null 也不是空字串。
    /// 順序：目前語系的值 → 中性資源（英文）→ 呼叫端給的字面值 → 鍵名。
    /// <b>寧可畫出英文甚至畫出鍵名，也絕不把空字串交給原生層。</b>
    /// </summary>
    /// <param name="value">通常是 <c>UIStrings.SomeKey</c>。</param>
    /// <param name="key">資源鍵名，用來重查中性資源。</param>
    /// <param name="fallback">最後的英文字面值退路。</param>
    public static string Safe(string? value, string key, string fallback)
    {
        if (!string.IsNullOrEmpty(value))
            return value;

        // 目前語系給了空的 → 直接問中性資源（英文），繞過那個壞掉的語系條目。
        var neutral = SafeLookup(key, CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(neutral))
            return neutral;

        if (!string.IsNullOrEmpty(fallback))
            return fallback;

        // 走到這裡代表資源整個壞了。畫出鍵名很醜，但不會崩，而且一眼看得出是哪個鍵。
        return string.IsNullOrEmpty(key) ? @"?" : key;
    }

    private static string? SafeLookup(string key, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        try
        {
            return UIStrings.ResourceManager.GetString(key, culture);
        }
        catch
        {
            // 資源查詢本身就不該把遊戲弄掛。查不到就查不到。
            return null;
        }
    }

    /// <summary>
    /// 啟動時掃一次「目前語系底下有哪些鍵是空的」，寫成 Information。
    ///
    /// 為什麼要做：這一類條目在被畫出來之前完全沒有徵兆，而畫出來的那一刻就是崩潰。
    /// 使用者的記錄等級是 1，所以刻意用 Information —— 這行的用途就是讓人回報得出來。
    /// 這裡<b>只是診斷</b>，真正的防護是 <see cref="Safe"/> 與資源檔本身不留空值。
    /// </summary>
    public static void ReportEmptyResourceKeys()
    {
        try
        {
            var culture = UIStrings.Culture ?? CultureInfo.CurrentUICulture;
            var set = UIStrings.ResourceManager.GetResourceSet(culture, true, false);
            if (set == null)
                return;

            var empty = new List<string>();
            foreach (System.Collections.DictionaryEntry entry in set)
            {
                if (entry.Value is string s && s.Length == 0 && entry.Key is string k)
                    empty.Add(k);
            }

            if (empty.Count == 0)
                return;

            Service.PrintInfo(
                $@"[在地化] 語系 {culture.Name} 有 {empty.Count} 個資源鍵的值是空字串：" +
                $@"{string.Join(@", ", empty.OrderBy(x => x).Take(20))}" +
                (empty.Count > 20 ? @" …" : string.Empty) +
                @"。空字串在 ImGui 的原生邊界會變成空指標，畫出來就會讓遊戲崩潰；" +
                @"這些鍵已經被自動改用英文顯示。");
        }
        catch
        {
            // 診斷失敗就算了，不要因為診斷把載入弄掛。
        }
    }
}
