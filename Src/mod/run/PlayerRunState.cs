using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run;

public sealed class PlayerRunState
{
    public CharacterInstance? ActiveCharacter { get; private set; }
    public int Gold { get; private set; }
    public List<RunModifierDto> Modifiers { get; } = [];
    public Dictionary<string, object> EventFlags { get; } = new(StringComparer.Ordinal);

    public void SetActiveCharacter(CharacterInstance? character)
    {
        ActiveCharacter = character;
    }

    public void SetGold(int amount)
    {
        Gold = Math.Max(0, amount);
    }

    public void AddGold(int amount)
    {
        if (amount <= 0)
            return;
        Gold += amount;
    }

    public bool SpendGold(int amount)
    {
        if (amount <= 0)
            return true;
        if (Gold < amount)
            return false;
        Gold -= amount;
        return true;
    }

    public void AddModifier(RunModifierDto modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        Modifiers.Add(modifier);
    }

    public void SetEventFlag(string key, object value)
    {
        EventFlags[key] = value;
    }

    public T? GetEventFlag<T>(string key)
    {
        if (EventFlags.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    public PlayerRunStateDto ToDto()
    {
        return new PlayerRunStateDto
        {
            ActiveCharacterIndex = null,
            Gold = Gold,
            Modifiers = [.. Modifiers],
            EventFlags = new Dictionary<string, object>(EventFlags, StringComparer.Ordinal),
        };
    }
}