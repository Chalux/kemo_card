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
        _team.Asc.Attributes.AttributeChanged += OnTeamAttributeChanged;

        RecomputeAndClamp();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var character in _characters)
            character.Asc.Attributes.AttributeChanged -= OnCharacterAttributeChanged;
        _team.Asc.Attributes.AttributeChanged -= OnTeamAttributeChanged;
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
        var newMax = _characters.Sum(character => character.Asc.GetCurrentValue(AttributeIds.MaxHealth));

        // 必须走 ASC 的重算入口：直接写 Attributes.SetBaseValue 会把队伍 ASC 的 MaxHealth
        // 当前值覆盖成新 base，抹掉域 GE 提供的 MaxHealth 修饰符贡献。
        _team.Asc.SetBaseValue(AttributeIds.MaxHealth, newMax);
        ClampHealth();
    }

    private void OnTeamAttributeChanged(object? sender, AttributeChangedEventArgs args)
    {
        if (args.AttributeId == AttributeIds.MaxHealth)
            ClampHealth();
    }

    private void ClampHealth() => _team.Asc.Attributes.SetCurrentValue(AttributeIds.Health,
        Math.Clamp(_team.SharedHpExact, 0f, MathF.Max(0f, _team.Asc.GetCurrentValue(AttributeIds.MaxHealth))));
}