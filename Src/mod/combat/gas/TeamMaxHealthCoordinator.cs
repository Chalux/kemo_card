using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Gas;

public sealed class TeamMaxHealthCoordinator : IDisposable
{
    private readonly PlayerTeamState _team;
    private readonly CharacterBattleInstance[] _characters;
    private bool _disposed;

    public TeamMaxHealthCoordinator(PlayerTeamState team)
    {
        _team = team ?? throw new ArgumentNullException(nameof(team));
        _characters = _team.Characters.ToArray();
        foreach (var character in _characters)
            character.Asc.Attributes.AttributeChanged += OnCharacterAttributeChanged;

        RecomputeAndClamp();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var character in _characters)
            character.Asc.Attributes.AttributeChanged -= OnCharacterAttributeChanged;
        _disposed = true;
    }

    private void OnCharacterAttributeChanged(object? sender, AttributeChangedEventArgs args)
    {
        if (!string.Equals(args.AttributeId, AttributeIds.MaxHealth, StringComparison.Ordinal))
            return;
        RecomputeAndClamp();
    }

    /// <summary>
    /// 规格 §1.2：重算 <c>MaxSharedHp</c> 时 **不** 按比例缩放、也不按 delta 跟涨 SharedHp；
    /// 仅在 <c>SharedHp &gt; MaxSharedHp</c> 越界时 clamp。
    /// </summary>
    private void RecomputeAndClamp()
    {
        var oldHealth = _team.Asc.GetCurrentValue(AttributeIds.Health);
        var newMax = _characters.Sum(character => character.Asc.GetCurrentValue(AttributeIds.MaxHealth));
        var newHealth = MathF.Min(oldHealth, newMax);

        _team.Asc.Attributes.SetBaseValue(AttributeIds.MaxHealth, newMax);
        _team.Asc.Attributes.SetCurrentValue(AttributeIds.Health, MathF.Max(0f, newHealth));
    }
}