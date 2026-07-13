using System.Text.RegularExpressions;

namespace KemoCard.Frame.Content.Keywords;

public static partial class KeywordTextFormatter
{
    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();

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
