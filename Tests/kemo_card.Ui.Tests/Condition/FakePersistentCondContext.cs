using KemoCard.Frame.Condition;

namespace KemoCard.Ui.Tests.Condition;

public sealed class FakePersistentCondContext : IPersistentCondContext
{
    public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Items { get; } = new(StringComparer.Ordinal);

    public bool HasFlag(string flagId) => Flags.Contains(flagId);

    public int GetItemCount(string itemId) =>
        Items.TryGetValue(itemId, out var n) ? n : 0;
}