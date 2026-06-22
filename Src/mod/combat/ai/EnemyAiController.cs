using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Ai;

public sealed class EnemyAiController
{
	private readonly GameDefinitionRegistry _registry;
	private readonly EnemyAiScriptInvoker? _scriptInvoker;
	private readonly string _modId;
	private readonly int _runSeed;

	public EnemyAiController(
		GameDefinitionRegistry registry,
		EnemyAiScriptInvoker? scriptInvoker,
		string modId,
		int runSeed = 1)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentException.ThrowIfNullOrWhiteSpace(modId);
		_registry = registry;
		_scriptInvoker = scriptInvoker;
		_modId = modId;
		_runSeed = runSeed;
	}

	public string? ChooseSkill(EnemyUnit enemy, HostRng rng)
	{
		ArgumentNullException.ThrowIfNull(enemy);
		ArgumentNullException.ThrowIfNull(rng);

		if (!_registry.Store.TryGetEnemy(enemy.DefinitionId, out var def))
			return null;

		if (!string.IsNullOrWhiteSpace(def.ScriptPath) && _scriptInvoker is not null)
		{
			var callContext = new ScriptCallContext
			{
				RunSeed = _runSeed,
				StreamKey = "combat.ai",
				Registry = _registry,
				CallerId = enemy.RuntimeId,
			};
			if (_scriptInvoker.TryChooseSkill(_modId, def.ScriptPath, "execute", callContext, out var aiResult))
			{
				if (!string.IsNullOrWhiteSpace(aiResult.SkillId) && IsLegalSkill(def, aiResult.SkillId))
					return aiResult.SkillId;

				if (aiResult.Weights is not null)
				{
					var fromWeights = ChooseFromWeights(aiResult.Weights, def, rng);
					if (fromWeights is not null)
						return fromWeights;
				}
			}
		}

		var legal = GetLegalSkills(def);
		if (legal.Count == 0)
			return null;

		return legal[rng.NextInt(0, legal.Count)];
	}

	private bool IsLegalSkill(EnemyDto def, string skillId) =>
		GetLegalSkills(def).Contains(skillId, StringComparer.Ordinal);

	private List<string> GetLegalSkills(EnemyDto def) =>
		def.SkillRefs
			.Select(skillRef => skillRef.SkillId)
			.Where(skillId =>
				!string.IsNullOrWhiteSpace(skillId) &&
				_registry.Store.TryGetSkill(skillId, out _))
			.Distinct(StringComparer.Ordinal)
			.ToList();

	private string? ChooseFromWeights(
		IReadOnlyDictionary<string, int> weights,
		EnemyDto def,
		HostRng rng)
	{
		var legal = GetLegalSkills(def);
		var weighted = legal
			.Where(skillId => weights.TryGetValue(skillId, out var weight) && weight > 0)
			.Select(skillId => (skillId, weight: weights[skillId]))
			.ToList();
		if (weighted.Count == 0)
			return null;

		var total = weighted.Sum(entry => entry.weight);
		var roll = rng.NextInt(0, total);
		var cumulative = 0;
		foreach (var (skillId, weight) in weighted)
		{
			cumulative += weight;
			if (roll < cumulative)
				return skillId;
		}

		return weighted[^1].skillId;
	}
}
