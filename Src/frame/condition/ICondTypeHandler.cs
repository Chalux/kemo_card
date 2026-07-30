using System.Text.Json;

namespace KemoCard.Frame.Condition;

public interface ICondTypeHandler<TContext>
{
    string Id { get; }
    string ShortTipKey { get; }
    string LongTipKey { get; }
    bool TryParse(JsonElement args, string sourcePath, out object? parsedArgs, out string? error);
    LeafEvalData Check(object parsedArgs, TContext context);
}