using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run;

public sealed class PlayerRunState
{
    public CharacterInstance? ActiveCharacter { get; private set; }
    public int Gold { get; private set; }
    public List<RunModifierDto> Modifiers { get; } = [];
    public Dictionary<string, object> EventFlags { get; } = new(StringComparer.Ordinal);

    /// <summary>槽位已分配潜能（进度值，不消费）：≥ 被动门槛即自动解锁，扣除低于门槛即重新锁定。</summary>
    public int AllocatedPotential { get; private set; }

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

    /// <summary>分配 / 扣除的额度读写由 <c>PotentialService</c> 编排（含团队池联动与表决）。</summary>
    public void AllocatePotential(int amount)
    {
        if (amount <= 0)
            return;
        AllocatedPotential += amount;
    }

    /// <summary>扣除已分配潜能（退回团队池由 <c>PotentialService</c> 记账）。</summary>
    internal void DeductPotential(int amount)
    {
        AllocatedPotential = Math.Max(0, AllocatedPotential - amount);
    }

    /// <summary>存档恢复前清零（避免对既有运行态重复叠加）。</summary>
    internal void ResetAllocatedPotential() => AllocatedPotential = 0;

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
            PotentialDirectCredit = AllocatedPotential,
        };
    }
}