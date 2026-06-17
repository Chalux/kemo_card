using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Content;

public sealed class GameDefinitionStore
{
	private readonly Dictionary<string, CharacterDto> _characters = new(StringComparer.Ordinal);
	private readonly Dictionary<string, EnemyDto> _enemies = new(StringComparer.Ordinal);
	private readonly Dictionary<string, BattleDto> _battles = new(StringComparer.Ordinal);
	private readonly Dictionary<string, EventDto> _events = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ItemDto> _items = new(StringComparer.Ordinal);
	private readonly Dictionary<string, CardDto> _cards = new(StringComparer.Ordinal);
	private readonly Dictionary<string, SkillDto> _skills = new(StringComparer.Ordinal);
	private readonly Dictionary<string, BuffDto> _buffs = new(StringComparer.Ordinal);
	private readonly Dictionary<string, EffectDto> _effects = new(StringComparer.Ordinal);

	public void Rebuild(
		IReadOnlyList<ModContentBundle> bundles,
		IReadOnlyList<ContentIdConflictEntry> idConflicts)
	{
		_characters.Clear();
		_enemies.Clear();
		_battles.Clear();
		_events.Clear();
		_items.Clear();
		_cards.Clear();
		_skills.Clear();
		_buffs.Clear();
		_effects.Clear();

		var loserKeys = BuildLoserKeys(idConflicts);
		foreach (var bundle in bundles)
		{
			MergeDefinitions(bundle, loserKeys);
		}
	}

	public bool TryGetCharacter(string id, out CharacterDto dto) => _characters.TryGetValue(id, out dto!);

	public bool TryGetEnemy(string id, out EnemyDto dto) => _enemies.TryGetValue(id, out dto!);

	public bool TryGetBattle(string id, out BattleDto dto) => _battles.TryGetValue(id, out dto!);

	public bool TryGetEvent(string id, out EventDto dto) => _events.TryGetValue(id, out dto!);

	public bool TryGetItem(string id, out ItemDto dto) => _items.TryGetValue(id, out dto!);

	public bool TryGetCard(string id, out CardDto dto) => _cards.TryGetValue(id, out dto!);

	public bool TryGetSkill(string id, out SkillDto dto) => _skills.TryGetValue(id, out dto!);

	public bool TryGetBuff(string id, out BuffDto dto) => _buffs.TryGetValue(id, out dto!);

	public bool TryGetEffect(string id, out EffectDto dto) => _effects.TryGetValue(id, out dto!);

	public IReadOnlyDictionary<string, CharacterDto> Characters => _characters;

	public IReadOnlyDictionary<string, EnemyDto> Enemies => _enemies;

	public IReadOnlyDictionary<string, BattleDto> Battles => _battles;

	public IReadOnlyDictionary<string, EventDto> Events => _events;

	public IReadOnlyDictionary<string, ItemDto> Items => _items;

	public IReadOnlyDictionary<string, CardDto> Cards => _cards;

	public IReadOnlyDictionary<string, SkillDto> Skills => _skills;

	public IReadOnlyDictionary<string, BuffDto> Buffs => _buffs;

	public IReadOnlyDictionary<string, EffectDto> Effects => _effects;

	internal void Remove(EContentCategory category, string id)
	{
		switch (category)
		{
			case EContentCategory.Character:
				_characters.Remove(id);
				break;
			case EContentCategory.Enemy:
				_enemies.Remove(id);
				break;
			case EContentCategory.Battle:
				_battles.Remove(id);
				break;
			case EContentCategory.Event:
				_events.Remove(id);
				break;
			case EContentCategory.Item:
				_items.Remove(id);
				break;
			case EContentCategory.Card:
				_cards.Remove(id);
				break;
			case EContentCategory.Skill:
				_skills.Remove(id);
				break;
			case EContentCategory.Buff:
				_buffs.Remove(id);
				break;
			case EContentCategory.Effect:
				_effects.Remove(id);
				break;
		}
	}

	private static HashSet<(EContentCategory Category, string Id, string ModId)> BuildLoserKeys(
		IReadOnlyList<ContentIdConflictEntry> conflicts)
	{
		var losers = new HashSet<(EContentCategory, string, string)>();
		foreach (var conflict in conflicts)
		{
			losers.Add((conflict.Category, conflict.ContentId, conflict.LoserModId));
		}

		return losers;
	}

	private void MergeDefinitions(
		ModContentBundle bundle,
		HashSet<(EContentCategory Category, string Id, string ModId)> loserKeys)
	{
		MergeCategory(bundle.ModId, EContentCategory.Character, bundle.Definitions.Characters, _characters, loserKeys);
		MergeCategory(bundle.ModId, EContentCategory.Enemy, bundle.Definitions.Enemies, _enemies, loserKeys);
		MergeCategory(bundle.ModId, EContentCategory.Battle, bundle.Definitions.Battles, _battles, loserKeys);
		MergeCategory(bundle.ModId, EContentCategory.Event, bundle.Definitions.Events, _events, loserKeys);
		MergeCategory(bundle.ModId, EContentCategory.Item, bundle.Definitions.Items, _items, loserKeys);
		MergeCategory(bundle.ModId, EContentCategory.Card, bundle.Definitions.Cards, _cards, loserKeys);
		MergeCategory(bundle.ModId, EContentCategory.Skill, bundle.Definitions.Skills, _skills, loserKeys);
		MergeCategory(bundle.ModId, EContentCategory.Buff, bundle.Definitions.Buffs, _buffs, loserKeys);
		MergeCategory(bundle.ModId, EContentCategory.Effect, bundle.Definitions.Effects, _effects, loserKeys);
	}

	private static void MergeCategory<T>(
		string modId,
		EContentCategory category,
		IReadOnlyDictionary<string, T> source,
		Dictionary<string, T> target,
		HashSet<(EContentCategory Category, string Id, string ModId)> loserKeys)
	{
		foreach (var (id, dto) in source)
		{
			if (loserKeys.Contains((category, id, modId)))
			{
				continue;
			}

			target.TryAdd(id, dto);
		}
	}
}
