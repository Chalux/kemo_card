using System.Text.Json;

namespace KemoCard.Frame.Condition;

public static class CondTypeHandler
{
    public delegate bool TryParseDelegate<TArgs>(
        JsonElement args,
        string sourcePath,
        out TArgs? parsed,
        out string? error);

    public static ICondTypeHandler<TContext> Create<TContext, TArgs>(
        string id,
        string shortTipKey,
        string longTipKey,
        TryParseDelegate<TArgs> tryParse,
        Func<TArgs, TContext, LeafEvalData> check)
    {
        return new Handler<TContext, TArgs>(id, shortTipKey, longTipKey, tryParse, check);
    }

    private sealed class Handler<TContext, TArgs> : ICondTypeHandler<TContext>
    {
        private readonly TryParseDelegate<TArgs> _tryParse;
        private readonly Func<TArgs, TContext, LeafEvalData> _check;

        public Handler(
            string id,
            string shortTipKey,
            string longTipKey,
            TryParseDelegate<TArgs> tryParse,
            Func<TArgs, TContext, LeafEvalData> check)
        {
            Id = id;
            ShortTipKey = shortTipKey;
            LongTipKey = longTipKey;
            _tryParse = tryParse;
            _check = check;
        }

        public string Id { get; }
        public string ShortTipKey { get; }
        public string LongTipKey { get; }

        public bool TryParse(JsonElement args, string sourcePath, out object? parsedArgs, out string? error)
        {
            if (!_tryParse(args, sourcePath, out var typed, out error))
            {
                parsedArgs = null;
                return false;
            }

            parsedArgs = typed;
            return true;
        }

        public LeafEvalData Check(object parsedArgs, TContext context)
        {
            return _check((TArgs)parsedArgs, context);
        }
    }
}