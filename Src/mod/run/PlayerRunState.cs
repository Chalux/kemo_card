using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run;

public sealed class PlayerRunState
{
    public CharacterInstance? ActiveCharacter { get; private set; }
    public int Gold { get; private set; }
    public List<RunModifierDto> Modifiers { get; } = [];
    public Dictionary<string, object> EventFlags { get; } = new(StringComparer.Ordinal);

    /// <summary>潜能直充余额：仅本槽位可消费、无需表决、可返还；消费先扣这里再扣团队池。</summary>
    public int PotentialDirectCredit { get; private set; }

    /// <summary>本槽位的潜能消费流水（解锁的被动），逐笔可返还。</summary>
    public List<PotentialSpendEntryDto> PotentialSpent { get; } = [];

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

    public void AddPotentialDirectCredit(int amount)
    {
        if (amount <= 0)
            return;
        PotentialDirectCredit += amount;
    }

    /// <summary>潜能消费/返还在 <c>PotentialService</c> 编排，这里只提供最小的额度读写。</summary>
    internal void ConsumePotentialDirectCredit(int amount)
    {
        PotentialDirectCredit = Math.Max(0, PotentialDirectCredit - amount);
    }

    /// <summary>存档恢复前清零（避免对既有运行态重复叠加）。</summary>
    internal void ResetPotentialDirectCredit() => PotentialDirectCredit = 0;

    internal void RestorePotentialDirectCredit(int amount)
    {
        PotentialDirectCredit += amount;
    }

    internal void AddPotentialSpendEntry(PotentialSpendEntryDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        PotentialSpent.Add(entry);
    }

    internal bool RemovePotentialSpendEntry(string entryId)
    {
        return PotentialSpent.RemoveAll(entry => string.Equals(entry.EntryId, entryId, StringComparison.Ordinal)) > 0;
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
            PotentialDirectCredit = PotentialDirectCredit,
            PotentialSpent = [.. PotentialSpent],
        };
    }
}