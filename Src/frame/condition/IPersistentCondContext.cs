namespace KemoCard.Frame.Condition;

public interface IPersistentCondContext
{
    bool HasFlag(string flagId);
    int GetItemCount(string itemId);
}