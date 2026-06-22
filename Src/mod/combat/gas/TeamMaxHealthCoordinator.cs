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

		RecomputeAndFollowDelta();
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
		RecomputeAndFollowDelta();
	}

	private void RecomputeAndFollowDelta()
	{
		var oldMax = _team.Asc.GetCurrentValue(AttributeIds.MaxHealth);
		var oldHealth = _team.Asc.GetCurrentValue(AttributeIds.Health);
		var newMax = _characters.Sum(character => character.Asc.GetCurrentValue(AttributeIds.MaxHealth));
		var delta = newMax - oldMax;
		var newHealth = delta > 0f
			? oldHealth + delta
			: MathF.Min(oldHealth, newMax);

		_team.Asc.Attributes.SetBaseValue(AttributeIds.MaxHealth, newMax);
		_team.Asc.Attributes.SetCurrentValue(AttributeIds.Health, MathF.Max(0f, newHealth));
	}
}
