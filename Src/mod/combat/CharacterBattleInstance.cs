using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;

namespace KemoCard.Mod.Combat;

public sealed class CharacterBattleInstance
{
	private readonly List<CardRuntimeEntry> _drawPile = [];
	private readonly List<CardRuntimeEntry> _graveyard = [];
	private readonly HandSlot[] _handSlots;

	public string SourceInstanceId { get; }
	public string DefinitionId { get; }
	public IReadOnlyList<CardRuntimeEntry> DrawPile => _drawPile;
	public IReadOnlyList<CardRuntimeEntry> Graveyard => _graveyard;
	public IReadOnlyList<HandSlot> HandSlots => _handSlots;
	public CharacterAttributes BaseAttributes { get; }
	public int CurrentEnergy { get; private set; }
	public int MaxEnergy { get; }
	public int EnergyCap { get; }
	public bool HasActed { get; private set; }

	private CharacterBattleInstance(
		string sourceInstanceId,
		string definitionId,
		CharacterAttributes baseAttributes,
		IEnumerable<CardRuntimeEntry> drawPile,
		int currentEnergy,
		int maxEnergy,
		int energyCap)
	{
		SourceInstanceId = sourceInstanceId;
		DefinitionId = definitionId;
		BaseAttributes = baseAttributes;
		_drawPile.AddRange(drawPile);
		CurrentEnergy = currentEnergy;
		MaxEnergy = maxEnergy;
		EnergyCap = energyCap;
		_handSlots = Enumerable.Range(0, CombatConstants.HandSlotCount)
			.Select(index => new HandSlot(index))
			.ToArray();
	}

	public static CharacterBattleInstance? TryCreate(
		CharacterInstance source,
		GameDefinitionRegistry definitions,
		HostRng rng,
		out string? error)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(definitions);
		ArgumentNullException.ThrowIfNull(rng);

		var deck = source.GetCurrentDeck();
		if (deck is null)
		{
			error = "当前角色没有可用卡组。";
			return null;
		}

		var validation = deck.Validate(source.GetBuildableCardIds(new HashSet<string>(StringComparer.Ordinal)));
		if (!validation.IsValid)
		{
			error = "当前卡组构筑非法。";
			return null;
		}

		var baseAttributes = source.ComputeAttributes(definitions);
		var drawPile = deck.CardIds
			.Select(cardId => new CardRuntimeEntry(cardId, Guid.NewGuid().ToString("N")))
			.ToList();

		Shuffle(drawPile, rng);

		error = null;
		return new CharacterBattleInstance(
			source.InstanceId,
			source.DefinitionId,
			baseAttributes,
			drawPile,
			baseAttributes.InitialEnergy,
			baseAttributes.MaxEnergy,
			baseAttributes.MaxEnergy);
	}

	public void SetHasActed(bool hasActed) => HasActed = hasActed;

	public void MoveTopDrawToGraveyard()
	{
		if (_drawPile.Count == 0)
			return;
		var entry = _drawPile[^1];
		_drawPile.RemoveAt(_drawPile.Count - 1);
		_graveyard.Add(entry);
	}

	private static void Shuffle(List<CardRuntimeEntry> entries, HostRng rng)
	{
		for (var i = entries.Count - 1; i > 0; i--)
		{
			var j = rng.NextInt(0, i + 1);
			(entries[i], entries[j]) = (entries[j], entries[i]);
		}
	}
}
