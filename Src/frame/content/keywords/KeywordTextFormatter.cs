using System.Text.RegularExpressions;

namespace KemoCard.Frame.Content.Keywords;

public static partial class KeywordTextFormatter
{
    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\[url=kw:([A-Za-z_][A-Za-z0-9_]*)\]", RegexOptions.CultureInvariant)]
    private static partial Regex KeywordRefRegex();

    /// <summary>
    /// 按出现顺序提取文本里 <c>[url=kw:id]…[/url]</c> 引用的词条 id（同 id 去重）；
    /// 供「关键词效果块」（buff 预览等）展开使用，非 kw 协议的 url 忽略。
    /// </summary>
    public static IReadOnlyList<string> ExtractKeywordIds(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in KeywordRefRegex().Matches(text))
        {
            var id = match.Groups[1].Value;
            if (seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// 将 <c>{name}</c> 替换为命名参数；缺参时保留原文占位符。
    /// </summary>
    public static string ApplyParams(string text, IReadOnlyDictionary<string, string>? parameters)
    {
        if (string.IsNullOrEmpty(text) || parameters == null || parameters.Count == 0)
        {
            return text ?? "";
        }

        return PlaceholderRegex().Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            return parameters.TryGetValue(name, out var value) ? value : match.Value;
        });
    }
}