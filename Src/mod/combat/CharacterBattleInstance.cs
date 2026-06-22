using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Gas;

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
	public IReadOnlyDictionary<string, float> BaseAttributes { get; }
	public AbilitySystemComponent Asc { get; }
	public int CurrentEnergy { get; private set; }
	public int MaxEnergy => ReadAscEnergy(AttributeIds.MaxEnergy);
	public int EnergyCap => ReadAscEnergy(AttributeIds.MaxEnergy);
	public bool HasActed { get; private set; }

	private CharacterBattleInstance(
		string sourceInstanceId,
		string definitionId,
		IReadOnlyDictionary<string, float> baseAttributes,
		IEnumerable<CardRuntimeEntry> drawPile,
		AbilitySystemComponent asc,
		int currentEnergy)
	{
		SourceInstanceId = sourceInstanceId;
		DefinitionId = definitionId;
		BaseAttributes = new Dictionary<string, float>(baseAttributes, StringComparer.Ordinal);
		Asc = asc;
		_drawPile.AddRange(drawPile);
		CurrentEnergy = currentEnergy;
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

		var baseAttributes = source.ComputeAttributeMap(definitions);
		var ascFactory = new CombatAscFactory();
		var asc = ascFactory.CreateCharacterAsc(definitions.Store.Attributes, baseAttributes);
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
			asc,
			currentEnergy: Math.Clamp(
				(int)MathF.Round(asc.GetCurrentValue(AttributeIds.InitialEnergy)),
				0,
				Math.Max(0, (int)MathF.Round(asc.GetCurrentValue(AttributeIds.MaxEnergy)))));
	}

	public void SetHasActed(bool hasActed) => HasActed = hasActed;

	public bool TryConsumeEnergy(int amount)
	{
		if (amount <= 0)
			return true;
		if (CurrentEnergy < amount)
			return false;
		CurrentEnergy -= amount;
		return true;
	}

	public void MoveTopDrawToGraveyard()
	{
		if (_drawPile.Count == 0)
			return;
		var entry = _drawPile[^1];
		_drawPile.RemoveAt(_drawPile.Count - 1);
		_graveyard.Add(entry);
	}

	public int DrawCards(int count)
	{
		if (count <= 0)
			return 0;

		var drawn = 0;
		while (drawn < count && _drawPile.Count > 0)
		{
			var slot = FindFirstEmptyHandSlot();
			if (slot is null)
				break;

			var entry = _drawPile[^1];
			_drawPile.RemoveAt(_drawPile.Count - 1);
			slot.PlaceCard(entry.CardId, entry.RuntimeInstanceId);
			drawn++;
		}

		return drawn;
	}

	public int DiscardFromHand(int count)
	{
		if (count <= 0)
			return 0;

		var discarded = 0;
		for (var i = _handSlots.Length - 1; i >= 0 && discarded < count; i--)
		{
			var slot = _handSlots[i];
			if (slot.IsEmpty)
				continue;

			_graveyard.Add(new CardRuntimeEntry(slot.CardId!, slot.RuntimeInstanceId!));
			slot.ClearCard();
			discarded++;
		}

		return discarded;
	}

	public void GainEnergy(int amount)
	{
		if (amount <= 0)
			return;
		CurrentEnergy = Math.Min(EnergyCap, CurrentEnergy + amount);
	}

	private int ReadAscEnergy(string attributeId) =>
		Math.Max(0, (int)MathF.Round(Asc.GetCurrentValue(attributeId)));

	private HandSlot? FindFirstEmptyHandSlot()
	{
		foreach (var slot in _handSlots)
		{
			if (slot.IsEmpty)
				return slot;
		}

		return null;
	}

	private static void Shuffle(List<CardRuntimeEntry> entries, HostRng rng)
	{
		for (var i = entries.Count - 1; i > 0; i--)
		{
			var j = rng.NextInt(0, i + 1);
			(entries[i], entries[j]) = (entries[j], entries[i]);
		}
	}

	internal static CharacterBattleInstance CreateForTests(
		string definitionId,
		IReadOnlyDictionary<string, float> baseAttributes,
		IEnumerable<CardRuntimeEntry>? drawPile = null)
	{
		ArgumentNullException.ThrowIfNull(baseAttributes);
		var copiedAttributes = new Dictionary<string, float>(baseAttributes, StringComparer.Ordinal);
		var asc = new CombatAscFactory().CreateCharacterAsc(
			new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal),
			copiedAttributes);
		var initialEnergy = copiedAttributes.TryGetValue(AttributeIds.InitialEnergy, out var initial)
			? (int)MathF.Round(initial)
			: 0;
		var energyCap = copiedAttributes.TryGetValue(AttributeIds.MaxEnergy, out var maxEnergy)
			? (int)MathF.Round(maxEnergy)
			: 0;

		return new CharacterBattleInstance(
			sourceInstanceId: Guid.NewGuid().ToString("N"),
			definitionId,
			copiedAttributes,
			drawPile: drawPile ?? [],
			asc: asc,
			currentEnergy: Math.Clamp(initialEnergy, 0, Math.Max(0, energyCap)));
	}

	internal static CharacterBattleInstance CreateForTests(
		string definitionId,
		CharacterAttributes baseAttributes,
		IEnumerable<CardRuntimeEntry>? drawPile = null) =>
		CreateForTests(definitionId, baseAttributes.Values, drawPile);
}
