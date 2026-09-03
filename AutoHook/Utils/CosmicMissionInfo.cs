using System.Collections.Concurrent;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using Lumina.Excel.Sheets;

namespace AutoHook.Utils;

/// <summary>目前這個宇宙探索釣魚任務「靠什麼拿分」。</summary>
public enum CosmicScoreFocus
{
    /// <summary>
    /// 不知道。讀不到任務、不是釣魚任務、或型別不在對照表裡。
    /// 🔑 這是「不猜」的那一格 —— 呼叫端看到它要退回使用者手動設定，不要自己選一邊。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 吞吐量決定分數：分數綁在「多快釣完」或「每條魚都算分」。
    /// 一竿多魚（雙重／三重提鉤）直接加分，華麗提鉤一次只起 1 條反而是淨損失。
    /// </summary>
    Quantity = 1,

    /// <summary>
    /// 單條魚的品質決定分數（收藏價值）。華麗提鉤「釣起的魚完好無損，能夠提高評價」直接對上。
    /// </summary>
    Evaluation = 2,
}

/// <summary>目前任務的計分側寫。</summary>
/// <param name="Focus">靠數量還是靠品質拿分。</param>
/// <param name="EveryFishCounts">
/// 任務說明是不是「任意／每一條魚都算分」（WKSMissionText 115、121）。
///
/// 🔑 這一格跟 <paramref name="Focus"/> 是**兩件事**，不能合併：
///    113/114/141 一樣是數量型，但它們要的是「幾種不同」「特定」「目標」水產品 ——
///    preset 的提鉤時間窗正是拿來挑那條魚的，繞過去只會一直釣到不要的魚。
///    只有「任意魚都算」的任務才適合忽略時間窗。
/// </param>
public readonly record struct CosmicMissionProfile(CosmicScoreFocus Focus, bool EveryFishCounts);

/// <summary>
/// 判斷目前的宇宙探索任務是「要數量」還是「要評價」，用來決定華麗提鉤的順位。
///
/// 資料鏈（每一段都在台服 exd-tc/7.20 上對過，不是從國際服抄的）：
///   WKSManager.CurrentMissionUnitRowId  （CS 具名欄位，+0xC10 ushort）
///     → WKSMissionUnit[id].MissionToDo[0]
///       → WKSMissionToDo[todo].WKSMissionText
///         → WKSMissionText[text].Text  ＝ 任務的計分方式說明（繁中原文）
///
/// 校準（🔑 沒有已知會命中的樣本就不能相信查表結果）：
///   實機 log 裡 ICE 回報「任務 469『西側小跳印記的生態調查』」；
///   台服 WKSMissionUnit row 469 的 Name 逐字相符 → row id 空間確認；
///   469 → MissionToDo 26 → WKSMissionText 113
///        =「盡快釣到幾種不同的水產品，剩餘時間越多評價越高。」
///   與任務名稱（生態調查＝釣不同種類）語意相符 → 整條鏈確認。
///
/// 另一個支撐：台服 52 個漁師任務用到的 15 個 WKSMissionText row，
/// **沒有任何一個**被非漁師任務用到（交集為空），所以這是乾淨的判別碼、不是共用代號。
/// </summary>
public static class CosmicMissionInfo
{
    /// <summary>
    /// <c>WKSMissionToDo.WKSMissionText</c> 的 row id → 這個任務靠什麼拿分。
    ///
    /// ⚠️ 只列「文字本身講得夠明白」的。判不出來的**刻意留白**讓它落到 <see cref="CosmicScoreFocus.Unknown"/>，
    ///    不要為了填滿表格去猜 —— 猜錯的表現是「自動模式默默選了錯的順位」，完全沒有徵兆。
    /// </summary>
    private static readonly Dictionary<uint, CosmicMissionProfile> ProfileByText = new()
    {
        // ── 分數綁在速度上：早點釣完 → 剩餘時間多 → 評價高。一竿多魚直接縮短總時間。
        //    113/114 要的是「幾種不同」「特定」水產品 —— 釣到別的魚沒有用，
        //    所以 EveryFishCounts = false（preset 的提鉤時間窗正是拿來挑那條魚的，不能繞過）。
        [113] = new(CosmicScoreFocus.Quantity, false), // 盡快釣到幾種不同的水產品，剩餘時間越多評價越高。
        [114] = new(CosmicScoreFocus.Quantity, false), // 盡快釣到指定數量的特定水產品，剩餘時間越多評價越高。
        [115] = new(CosmicScoreFocus.Quantity, true),  // 盡快釣到指定數量的「任意」水產品，剩餘時間越多評價越高。

        // ── 每條魚都算分 / 純數量門檻。
        [121] = new(CosmicScoreFocus.Quantity, true),  // 在限定時間內盡可能釣起水產品…「每條魚均給予評價」，大尺寸則給予高評價。
        [141] = new(CosmicScoreFocus.Quantity, false), // 在限定時間內釣到指定數量的「目標」道具。

        // ── 分數＝每條魚的收藏價值。華麗提鉤的「完好無損、提高評價」正好對上。
        [118] = new(CosmicScoreFocus.Evaluation, false), // 用有限的釣餌釣到收藏品…根據收藏價值給予評價。
        [122] = new(CosmicScoreFocus.Evaluation, false), // 在限定時間內盡可能釣起收藏品…根據收藏價值給予評價。

        // ── 以下**刻意不列**，一律當成 Unknown 退回手動設定：
        //    116 用有限的釣餌釣到幾種不同的水產品，種類越多評價越高。
        //        → 計分是「種類數」。雙重／三重一次起的是同一種魚，對種類沒有幫助；
        //          華麗提鉤的「完好無損」對種類也沒有幫助。兩邊都證不出好處。
        //    117 / 119 / 120 根據（最大）尺寸給予評價。
        //        → 「完好無損」跟「尺寸」是不是同一件事，離線沒有任何證據。
        //    135~138 以漁師釣獲水產品，以某某職業製作交貨道具…
        //        → 雙職業任務，評分發生在製作端，釣魚只是前置。
    };

    /// <summary>
    /// 任務 row id → 判定結果。任務表是靜態資料，同一個任務不必重查。
    /// 用 ConcurrentDictionary 純粹是保險：一般 Dictionary 在極少見的併發寫入下
    /// 會壞成無窮迴圈（表現是整個遊戲卡住），代價比這個型別高太多。
    /// </summary>
    private static readonly ConcurrentDictionary<ushort, CosmicMissionProfile> ProfileCache = new();

    private static ushort _lastLoggedMission;

    /// <summary>
    /// 台服目前唯一的宇宙探索地區（渴望灣，TerritoryType 1237）。
    /// 只當成「<see cref="WKSManager.TerritoryId"/> 讀不到時的退路」使用 ——
    /// 寫死地區 ID 在下一個探索地開放時會靜默失效，所以不能是唯一判準。
    /// </summary>
    public const ushort CosmicTerritoryFallback = 1237;

    /// <summary>
    /// 目前是不是站在宇宙探索地區。優先信 <see cref="WKSManager"/> 自己記的地區 ID
    /// （這樣新探索地開放時不必改碼），對不上再退回寫死的 <see cref="CosmicTerritoryFallback"/>。
    /// </summary>
    /// <param name="cosmicManager">
    /// 呼叫端已經拿到的 <see cref="WKSManager"/> 指標。
    /// ⚠️ 指標由呼叫端當幀取得、當幀用完，**不要存起來跨幀**。
    /// </param>
    public static unsafe bool IsInCosmicZone(WKSManager* cosmicManager)
    {
        if (cosmicManager == null)
            return false;

        var territory = Service.ClientState.TerritoryType;
        if (territory == 0)
            return false;

        return territory == cosmicManager->TerritoryId || territory == CosmicTerritoryFallback;
    }

    /// <summary>手上還沒有 <see cref="WKSManager"/> 指標時的版本。</summary>
    public static unsafe bool IsInCosmicZone() => IsInCosmicZone(WKSManager.Instance());

    /// <summary>
    /// 目前進行中的宇宙探索任務 row id（<c>WKSMissionUnit</c>）。沒有進行中的任務 → 0。
    /// 🔴 這裡只拿 id，**不讀任務進度／分數** —— 那是 ICE 的職權，跨過去就會變成兩套互相打架的狀態機。
    /// </summary>
    public static unsafe ushort GetCurrentMissionId()
    {
        var wks = WKSManager.Instance();
        return wks == null ? (ushort)0 : wks->CurrentMissionUnitRowId;
    }

    /// <summary>
    /// 目前進行中的宇宙探索任務靠什麼拿分。
    /// 不在宇宙探索、沒有進行中的任務、或型別判不出來 → <see cref="CosmicScoreFocus.Unknown"/>。
    /// </summary>
    public static CosmicScoreFocus GetCurrentFocus() => GetCurrentProfile().Focus;

    /// <summary>
    /// 目前進行中的宇宙探索任務的計分側寫。
    /// 不在宇宙探索、沒有進行中的任務、或型別判不出來 → <see cref="CosmicScoreFocus.Unknown"/> ＋
    /// <c>EveryFishCounts = false</c>（不知道就不要放寬任何限制）。
    /// </summary>
    public static unsafe CosmicMissionProfile GetCurrentProfile()
    {
        var wks = WKSManager.Instance();
        if (wks == null)
            return default;

        var missionId = wks->CurrentMissionUnitRowId;
        if (missionId == 0)
            return default;

        if (!ProfileCache.TryGetValue(missionId, out var profile))
        {
            profile = Resolve(missionId);
            ProfileCache[missionId] = profile;
        }

        LogOnMissionChange(missionId, profile);
        return profile;
    }

    private static CosmicMissionProfile Resolve(ushort missionId)
    {
        var unitSheet = Service.DataManager.GetExcelSheet<WKSMissionUnit>();
        if (unitSheet == null || !unitSheet.TryGetRow(missionId, out var unit))
            return default;

        var todoId = unit.MissionToDo[0].RowId;
        if (todoId == 0)
            return default;

        var todoSheet = Service.DataManager.GetExcelSheet<WKSMissionToDo>();
        if (todoSheet == null || !todoSheet.TryGetRow(todoId, out var todo))
            return default;

        // 只取 RowId，不碰 .Value —— 指向不存在的 row 時 .Value 會丟例外。
        var textId = todo.WKSMissionText.RowId;

        return ProfileByText.TryGetValue(textId, out var profile) ? profile : default;
    }

    /// <summary>
    /// 換任務的時候寫一行 Information。使用者跑 LogLevel 1，Debug 收得到但單檔數十萬行會淹沒，
    /// 而「自動模式選錯順位」這種事在遊戲裡完全看不出來 —— 沒有這行就無從查證。
    /// </summary>
    private static void LogOnMissionChange(ushort missionId, CosmicMissionProfile profile)
    {
        if (missionId == _lastLoggedMission)
            return;

        _lastLoggedMission = missionId;

        var focusText = profile.Focus switch
        {
            CosmicScoreFocus.Quantity => "數量／速度型（雙重、三重提鉤優先）",
            CosmicScoreFocus.Evaluation => "評價／收藏價值型（華麗提鉤優先）",
            _ => "判不出型別（改用你手動設定的順位）",
        };

        var everyFish = profile.EveryFishCounts
            ? "，而且任務說明是「任意／每條魚都算分」→ 會忽略 preset 的提鉤時間窗，每一咬都提"
            : string.Empty;

        Service.PrintInfo($"[宇宙探索] 目前任務 WKSMissionUnit#{missionId}，計分方式判定為：{focusText}{everyFish}。");
    }
}
