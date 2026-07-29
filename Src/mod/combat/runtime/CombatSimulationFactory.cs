using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Gas;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Combat.Runtime;

public static class CombatSimulationFactory
{
	public static CombatSimulation? TryCreate(
		BattleDto battle,
		IReadOnlyList<CharacterInstance> party,
		GameDefinitionRegistry definitions,
		HostRng rng,
		int runSeed,
		IReadOnlyList<string>? runRuleIds,
		IContentEffectScriptHost? scriptHost,
		string modId,
		CombatRuleCatalog catalog,
		out string? error,
		IReadOnlyList<BattleStartSkillEntry>? battleStartSkills = null)
	{
		ArgumentNullException.ThrowIfNull(battle);
		ArgumentNullException.ThrowIfNull(party);
		ArgumentNullException.ThrowIfNull(definitions);
		ArgumentNullException.ThrowIfNull(rng);
		ArgumentNullException.ThrowIfNull(catalog);
		ArgumentException.ThrowIfNullOrWhiteSpace(modId);

		if (party.Count != 4)
		{
			error = "队伍必须包含 4 名角色。";
			return null;
		}

		if (battle.Waves.Count == 0)
		{
			error = "战斗至少需要一波敌人。";
			return null;
		}

		// 规格 §1.3：玩家侧治疗必须打账本；配错的内容在进战斗前拒绝，而不是运行期静默软失败。
		if (!CombatContentValidator.TryValidateHealTargeting(definitions, out var healError))
		{
			error = healError;
			return null;
		}

		var ruleIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var id in battle.CombatRuleIds)
			ruleIds.Add(id);
		if (runRuleIds is not null)
		{
			foreach (var id in runRuleIds)
				ruleIds.Add(id);
		}

		var rules = new List<ICombatRule>();
		foreach (var id in ruleIds)
		{
			var rule = catalog.TryCreate(id);
			if (rule is not null)
				rules.Add(rule);
		}

		var ruleEngine = new CombatRuleEngine(rules);

		var battleCharacters = new CharacterBattleInstance[party.Count];
		for (var i = 0; i < party.Count; i++)
		{
			var battleInstance = CharacterBattleInstance.TryCreate(party[i], definitions, rng, out var memberError);
			if (battleInstance is null)
			{
				error = memberError ?? "角色战斗实例创建失败。";
				return null;
			}

			battleCharacters[i] = battleInstance;
		}

		var sharedMaxHp = battleCharacters.Sum(character => character.Asc.GetCurrentValue(AttributeIds.MaxHealth));
		if (sharedMaxHp <= 0f)
		{
			error = "队伍共享 HP 上限无效。";
			return null;
		}

		var enemies = SpawnWaveEnemies(battle.Waves[0], definitions, out var spawnError);
		if (enemies is null)
		{
			error = spawnError;
			return null;
		}

		var ascFactory = new CombatAscFactory();
		var playerTeamAsc = ascFactory.CreateTeamAsc(definitions.Store.Attributes, sharedMaxHp);
		var playerTeam = new PlayerTeamState(
			battleCharacters,
			(int)MathF.Round(sharedMaxHp),
			teamAsc: playerTeamAsc);
		var enemyTotalMaxHp = enemies.Sum(enemy => enemy.Asc.GetCurrentValue(AttributeIds.MaxHealth));
		var enemyTeamAsc = ascFactory.CreateTeamAsc(definitions.Store.Attributes, enemyTotalMaxHp);
		var enemyTeam = new EnemyTeamState(enemies, teamAsc: enemyTeamAsc);
		error = null;
		return new CombatSimulation(
			playerTeam,
			enemyTeam,
			ruleEngine,
			definitions,
			battle: battle,
			runSeed: runSeed,
			scriptHost: scriptHost,
			modId: modId,
			battleStartSkills: battleStartSkills);
	}

	public static List<EnemyUnit>? SpawnWaveEnemies(
		BattleWaveDto wave,
		GameDefinitionRegistry definitions,
		out string? error)
	{
		var units = new List<EnemyUnit>();
		var runtimeIndex = 0;

		foreach (var spawn in wave.EnemySpawns)
		{
			if (!definitions.Store.TryGetEnemy(spawn.EnemyId, out var enemyDef))
			{
				error = $"未知敌人：{spawn.EnemyId}";
				return null;
			}

			var count = Math.Max(1, spawn.Count);
			var hpScale = spawn.HpScale ?? 1.0;
			var baseAttributes = AttributeContributionMapper.BuildEnemyBaseAttributes(enemyDef);
			if (!baseAttributes.TryGetValue(AttributeIds.MaxHealth, out var baseMaxHealth))
				baseMaxHealth = enemyDef.MaxHp;
			var scaledMaxHealth = Math.Max(1f, baseMaxHealth * (float)hpScale);
			baseAttributes[AttributeIds.MaxHealth] = scaledMaxHealth;

			for (var i = 0; i < count; i++)
			{
				units.Add(new EnemyUnit($"enemy-{runtimeIndex++}", spawn.EnemyId, baseAttributes));
			}
		}

		if (units.Count == 0)
		{
			error = "波次没有有效敌人生成配置。";
			return null;
		}

		error = null;
		return units;
	}
}
