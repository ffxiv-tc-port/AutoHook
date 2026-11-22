// 匯入字串裡「本插件吃不下」的設定清單：Newtonsoft 靜默略過沒宣告的鍵，匯入成功但行為不存在。
// 只拿型別的序列化契約對 JSON 逐鍵比對、不建構設定物件（建構子會呼叫遊戲函式），所以離線也能跑。
// 契約：只回報會改變行為的值——true 的布林、Enabled 為 true 的物件、帶條件的條件組；沒宣告的數字／字串不回報。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AutoHook.Conditions;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace AutoHook.Configurations;

public static class PresetFeatureAudit
{
    private static readonly DefaultContractResolver Resolver = new();

    /// <summary>
    /// 沒宣告也不算缺功能的鍵：OnlyUseWhenNoMoochAvailable 只在同層 UseSwimbait 為 true 時有意義（那時會回報 UseSwimbait）；
    /// RestoresGp 是上游的唯讀屬性，匯出時順手寫出來、匯入不讀。
    /// </summary>
    private static readonly HashSet<string> DependentKeys = new(StringComparer.Ordinal)
    {
        @"OnlyUseWhenNoMoochAvailable",
        @"RestoresGp",
    };

    /// <summary>回傳不支援的項目（已去重、依出現順序）。空清單＝全部吃得下；解不開時回一筆說明。</summary>
    public static List<string> Audit(string export)
    {
        var found = new List<string>();
        var trimmed = (export ?? string.Empty).Trim();

        JToken root;
        try
        {
            root = JToken.Parse(Configuration.DecompressString(trimmed));
        }
        catch (Exception e)
        {
            found.Add(@$"無法解碼：{e.GetType().Name}");
            return found;
        }

        if (trimmed.StartsWith(@"AHFOLDER", StringComparison.Ordinal))
            WalkFolder(root, found, 0);
        else if (trimmed.StartsWith(@"AHSF", StringComparison.Ordinal))
            Walk(root, typeof(AutoGigConfig), string.Empty, found);
        else
            Walk(root, typeof(CustomPresetConfig), string.Empty, found);

        return found.Distinct().ToList();
    }

    private static void WalkFolder(JToken folder, List<string> found, int depth)
    {
        if (folder is not JObject obj || depth > 16)
            return;

        if (obj[@"Presets"] is JArray presets)
            foreach (var preset in presets)
                Walk(preset, typeof(CustomPresetConfig), string.Empty, found);

        if (obj[@"ChildFolders"] is JArray children)
            foreach (var child in children)
                WalkFolder(child, found, depth + 1);
    }

    private static void Walk(JToken token, Type type, string path, List<string> found)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(ConditionSet))
        {
            AuditConditionSet(token, path, found);
            return;
        }

        if (token is JArray array)
        {
            var element = ElementType(type);
            if (element != null)
                foreach (var item in array)
                    Walk(item, element, path + @"[]", found);
            return;
        }

        if (token is not JObject obj || Resolver.ResolveContract(type) is not JsonObjectContract contract)
            return;

        foreach (var prop in obj.Properties())
        {
            var member = contract.Properties.GetClosestMatchProperty(prop.Name);
            var childPath = path.Length == 0 ? prop.Name : path + @"." + prop.Name;

            if (member == null || member.Ignored || !member.Writable || member.PropertyType == null)
            {
                if (!DependentKeys.Contains(prop.Name) && ChangesBehavior(prop.Value))
                    found.Add(@$"未支援的設定 {childPath}");
                continue;
            }

            Walk(prop.Value, member.PropertyType, childPath, found);
        }
    }

    private static Type? ElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
            return type.GetGenericArguments().FirstOrDefault();

        return null;
    }

    private static void AuditConditionSet(JToken token, string path, List<string> found)
    {
        if (token is not JObject set)
            return;

        if (set[@"e"] is JValue { Type: JTokenType.String } expr && !string.IsNullOrWhiteSpace((string?)expr))
            found.Add(@$"進階運算式 {path}");

        if (set[@"g"] is not JArray groups)
            return;

        foreach (var group in groups.OfType<JObject>())
        {
            if (group[@"a"] is JValue { Type: JTokenType.Boolean } ga && !(bool)ga)
                continue;

            if (group[@"c"] is not JArray conditions)
                continue;

            foreach (var condition in conditions.OfType<JObject>())
            {
                if (condition[@"e"] is JValue { Type: JTokenType.Boolean } ce && !(bool)ce)
                    continue;

                var typeId = (string?)condition[@"t"] ?? string.Empty;
                if (!ConditionRegistry.IsKnown(typeId))
                    found.Add(@$"條件型別 {typeId}（{path}）");
            }
        }
    }

    /// <summary>沒宣告的值會不會改變行為。見檔頭契約。</summary>
    private static bool ChangesBehavior(JToken value)
    {
        switch (value)
        {
            case JValue { Type: JTokenType.Boolean } b:
                return (bool)b;
            case JArray arr:
                return arr.Any(ChangesBehavior);
            case JObject obj:
                if (obj[@"g"] is JArray groups)
                    return groups.OfType<JObject>().Any(g => g[@"c"] is JArray { Count: > 0 });
                if (obj[@"Enabled"] is JValue { Type: JTokenType.Boolean } enabled)
                    return (bool)enabled;
                return obj.Properties().Any(p => ChangesBehavior(p.Value));
            default:
                return false;
        }
    }
}
