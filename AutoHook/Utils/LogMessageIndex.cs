using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace AutoHook.Utils;

/// <summary>
/// 聊天訊息全文 → <c>LogMessage</c> 列號。契約：與逐列比對
/// <c>sheet.FirstOrDefault(x =&gt; x.Text.ToString() == text).RowId</c> 逐筆同結果——
/// 多列同文取表內順序的第一列、比不中回 0、比較是逐字（ordinal）。
/// </summary>
public static class LogMessageIndex
{
    public static Dictionary<string, uint> Build(IEnumerable<LogMessage> sheet)
    {
        var map = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var row in sheet)
            map.TryAdd(row.Text.ToString(), row.RowId);
        return map;
    }

    public static uint Find(Dictionary<string, uint> map, string? text)
        => text != null && map.TryGetValue(text, out var rowId) ? rowId : 0;
}
