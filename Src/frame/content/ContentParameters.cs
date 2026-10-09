using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace KemoCard.Frame.Content;

/// <summary>内容参数的统一解析；JSON 与内存参数使用同一文化和范围检查。</summary>
internal static class ContentParameters
{
    #region 参数解析与合并
    public static IReadOnlyDictionary<string, object> Merge(
        IReadOnlyDictionary<string, object>? defaults, IReadOnlyDictionary<string, object>? overrides,
        IReadOnlyDictionary<string, object>? payloadOverrides = null)
    {
        var merged = defaults is null ? new Dictionary<string, object>(StringComparer.Ordinal)
            : new Dictionary<string, object>(defaults, StringComparer.Ordinal);
        if (overrides is not null)
            foreach (var (key, value) in overrides)
                merged[key] = value;
        if (payloadOverrides is not null)
            foreach (var (key, value) in payloadOverrides)
                merged[key] = value;
        return new ReadOnlyDictionary<string, object>(merged);
    }

    public static float ReadFloat(IReadOnlyDictionary<string, object> parameters, string key, float fallback) =>
        parameters.TryGetValue(key, out var value) && TryFloat(value, out var number) ? number : fallback;

    public static int ReadInt(IReadOnlyDictionary<string, object> parameters, string key, int fallback) =>
        parameters.TryGetValue(key, out var value) && TryInt(value, out var number) ? number : fallback;

    public static bool TryFloat(object? value, out float number)
    {
        number = 0f;
        if (value is null)
            return false;
        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.TryGetSingle(out number) && float.IsFinite(number);
            if (element.ValueKind != JsonValueKind.String)
                return false;
            value = element.GetString();
        }
        switch (value)
        {
            case float numeric: number = numeric; break;
            case double numeric: number = (float)numeric; break;
            case decimal numeric: number = (float)numeric; break;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                number = Convert.ToSingle(value, CultureInfo.InvariantCulture); break;
            case string text:
                return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && float.IsFinite(number);
            default: return false;
        }
        return float.IsFinite(number);
    }

    public static bool TryInt(object? value, out int number)
    {
        number = 0;
        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.TryGetDecimal(out var numeric) && TryInt(numeric, out number);
            if (element.ValueKind != JsonValueKind.String)
                return false;
            value = element.GetString();
        }
        if (value is decimal precise)
        {
            if (precise < int.MinValue || precise > int.MaxValue || decimal.Truncate(precise) != precise)
                return false;
            number = (int)precise;
            return true;
        }
        if (value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double)
        {
            var numeric = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(numeric) || numeric < int.MinValue || numeric > int.MaxValue || Math.Truncate(numeric) != numeric)
                return false;
            number = (int)numeric;
            return true;
        }
        return value is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
    }

    public static Dictionary<string, float>? ToSetByCaller(IReadOnlyDictionary<string, object>? parameters)
    {
        if (parameters is null)
            return null;
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
            if (TryFloat(value, out var number))
                values[key] = number;
        return values.Count == 0 ? null : values;
    }
    #endregion
}