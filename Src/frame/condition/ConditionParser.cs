using System.Text.Json;

namespace KemoCard.Frame.Condition;

public static class ConditionParser
{
    public static bool TryParse<TContext>(
        JsonElement element,
        ConditionRegistry<TContext> registry,
        string sourcePath,
        out ConditionNode? expression,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(registry);
        sourcePath ??= "";

        return TryParseNode(element, registry, sourcePath, out expression, out error);
    }

    private static bool TryParseNode<TContext>(
        JsonElement element,
        ConditionRegistry<TContext> registry,
        string path,
        out ConditionNode? expression,
        out string? error)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return TryParseAndObject(element, registry, path, out expression, out error);
            case JsonValueKind.Array:
                return TryParseOrArray(element, registry, path, out expression, out error);
            default:
                expression = null;
                error = $"{path}: 条件表达式须为对象或数组，实际为 {element.ValueKind}";
                return false;
        }
    }

    private static bool TryParseAndObject<TContext>(
        JsonElement obj,
        ConditionRegistry<TContext> registry,
        string path,
        out ConditionNode? expression,
        out string? error)
    {
        var children = new List<ConditionNode>();
        foreach (var prop in obj.EnumerateObject())
        {
            var typeId = prop.Name;
            var leafPath = $"{path}.{typeId}";
            if (!registry.TryGet(typeId, out var handler) || handler is null)
            {
                expression = null;
                error = $"{leafPath}: 未知 CondType '{typeId}'";
                return false;
            }

            if (!handler.TryParse(prop.Value, leafPath, out var args, out var parseError))
            {
                expression = null;
                error = parseError ?? $"{leafPath}: 参数解析失败";
                return false;
            }

            children.Add(new LeafNode(typeId, args!));
        }

        expression = new AndNode(children);
        error = null;
        return true;
    }

    private static bool TryParseOrArray<TContext>(
        JsonElement arr,
        ConditionRegistry<TContext> registry,
        string path,
        out ConditionNode? expression,
        out string? error)
    {
        var children = new List<ConditionNode>();
        var index = 0;
        foreach (var item in arr.EnumerateArray())
        {
            var childPath = $"{path}[{index}]";
            if (!TryParseNode(item, registry, childPath, out var child, out error))
            {
                expression = null;
                return false;
            }

            children.Add(child!);
            index++;
        }

        expression = new OrNode(children);
        error = null;
        return true;
    }
}
