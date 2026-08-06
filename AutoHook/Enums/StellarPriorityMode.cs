namespace AutoHook.Enums;

/// <summary>
/// 華麗提鉤（宇宙探索）跟雙重／三重提鉤之間誰先。
///
/// ⚠️ <see cref="Auto"/> 一定要是 0：這個欄位在舊的設定檔／舊的 preset 匯出字串裡不存在，
///    反序列化時會落在 <c>default</c> 上。沒有零值的列舉會讓 <c>default</c> 停在無效值。
/// </summary>
public enum StellarPriorityMode
{
    /// <summary>依目前任務的計分方式自動決定；判不出來就退回使用者手動設定的順位。</summary>
    Auto = 0,

    /// <summary>一律華麗提鉤優先（適合分數／收藏價值型任務）。</summary>
    Evaluation = 1,

    /// <summary>一律雙重／三重提鉤優先（適合要數量、要速度的任務）。</summary>
    Quantity = 2,
}
