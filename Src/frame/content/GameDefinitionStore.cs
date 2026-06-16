using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Content;

public sealed class GameDefinitionStore
{
	private readonly Dictionary<string, CardDto> _cards = new(StringComparer.Ordinal);
	private readonly Dictionary<string, SkillDto> _skills = new(StringComparer.Ordinal);
	private readonly Dictionary<string, BuffDto> _buffs = new(StringComparer.Ordinal);
	private readonly Dictionary<string, EffectDto> _effects = new(StringComparer.Ordinal);

	public void Rebuild(
		IReadOnlyList<ModContentBundle> bundles,
		IReadOnlyList<ContentIdConflictEntry> idConflicts)
	{
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

	public bool TryGetCard(string id, out CardDto dto) => _cards.TryGetValue(id, out dto!);

	public bool TryGetSkill(string id, out SkillDto dto) => _skills.TryGetValue(id, out dto!);

	public bool TryGetBuff(string id, out BuffDto dto) => _buffs.TryGetValue(id, out dto!);

	public bool TryGetEffect(string id, out EffectDto dto) => _effects.TryGetValue(id, out dto!);

	public IReadOnlyDictionary<string, CardDto> Cards => _cards;

	public IReadOnlyDictionary<string, SkillDto> Skills => _skills;

	public IReadOnlyDictionary<string, BuffDto> Buffs => _buffs;

	public IReadOnlyDictionary<string, EffectDto> Effects => _effects;

	internal void Remove(ContentCategory category, string id)
	{
		switch (category)
		{
			case ContentCategory.Card:
				_cards.Remove(id);
				break;
			case ContentCategory.Skill:
				_skills.Remove(id);
				break;
			case ContentCategory.Buff:
				_buffs.Remove(id);
				break;
			case ContentCategory.Effect:
				_effects.Remove(id);
				break;
		}
	}

	private static HashSet<(ContentCategory Category, string Id, string ModId)> BuildLoserKeys(
		IReadOnlyList<ContentIdConflictEntry> conflicts)
	{
		var losers = new HashSet<(ContentCategory, string, string)>();
		foreach (var conflict in conflicts)
		{
			losers.Add((conflict.Category, conflict.ContentId, conflict.LoserModId));
		}

		return losers;
	}

	private void MergeDefinitions(
		ModContentBundle bundle,
		HashSet<(ContentCategory Category, string Id, string ModId)> loserKeys)
	{
		foreach (var (id, dto) in bundle.Definitions.Cards)
		{
			if (loserKeys.Contains((ContentCategory.Card, id, bundle.ModId)))
			{
				continue;
			}

			_cards.TryAdd(id, dto);
		}

		foreach (var (id, dto) in bundle.Definitions.Skills)
		{
			if (loserKeys.Contains((ContentCategory.Skill, id, bundle.ModId)))
			{
				continue;
			}

			_skills.TryAdd(id, dto);
		}

		foreach (var (id, dto) in bundle.Definitions.Buffs)
		{
			if (loserKeys.Contains((ContentCategory.Buff, id, bundle.ModId)))
			{
				continue;
			}

			_buffs.TryAdd(id, dto);
		}

		foreach (var (id, dto) in bundle.Definitions.Effects)
		{
			if (loserKeys.Contains((ContentCategory.Effect, id, bundle.ModId)))
			{
				continue;
			}

			_effects.TryAdd(id, dto);
		}
	}
}
