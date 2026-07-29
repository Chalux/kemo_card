using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 规格 §5：主动技蓄力链——<c>S</c> 计数器、累计阈值 <c>T_k</c>、自动最高档、档位目标校验。
/// 对照 §5.3 示例表：<c>C0=4, C1=6, C2=6</c>，<c>Cap=16</c>。
/// </summary>
[TestFixture]
public sealed class ActiveSkillChainTests
{
	private static readonly IReadOnlyList<CombatTargetRef> Enemy0 = [new CombatTargetRef(ECombatSide.Enemy, 0)];
	private static readonly IReadOnlyList<CombatTargetRef> BothEnemies =
	[
		new CombatTargetRef(ECombatSide.Enemy, 0),
		new CombatTargetRef(ECombatSide.Enemy, 1),
	];

	private sealed class RecordingScriptHost : IContentEffectScriptHost
	{
		public List<string> Invocations { get; } = [];

		public bool TryExecute(
			string modId,
			string scriptPath,
			string scriptEntry,
			IReadOnlyDictionary<string, object>? context,
			out IReadOnlyList<Dictionary<string, object>> proposedEffects)
		{
			Invocations.Add(scriptPath);
			proposedEffects = [];
			return true;
		}
	}

	#region 档位阈值与扣费

	[Test]
	public void Cap_is_the_sum_of_tier_cooldowns()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);

		Assert.That(sim.PlayerTeam.Characters[0].SkillCounterCap, Is.EqualTo(16));
	}

	[Test]
	public void Tier_thresholds_are_cumulative()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];

		Assert.That(character.GetTierThreshold(0), Is.EqualTo(4));
		Assert.That(character.GetTierThreshold(1), Is.EqualTo(10));
		Assert.That(character.GetTierThreshold(2), Is.EqualTo(16));
	}

	[Test]
	public void Counter_below_base_cooldown_rejects_the_cast()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 3);

		var result = sim.TryApply(new CastActiveSkillCommand(0, Enemy0));

		Assert.That(result.Success, Is.False);
		Assert.That(character.SkillCounter, Is.EqualTo(3), "拒绝时不消耗 S");
		Assert.That(host.Invocations, Is.Empty);
	}

	[Test]
	[TestCase(4)]
	[TestCase(9)]
	public void Counter_inside_base_band_casts_the_base_tier(int counter)
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, counter);

		var result = sim.TryApply(new CastActiveSkillCommand(0, Enemy0));

		Assert.That(result.Success, Is.True, result.Error);
		Assert.That(host.Invocations, Is.EqualTo(new[] { "tier0.js" }));
		Assert.That(character.SkillCounter, Is.EqualTo(counter - 4), "扣累计阈值 T_0");
	}

	[Test]
	[TestCase(10)]
	[TestCase(15)]
	public void Counter_inside_first_charge_band_casts_the_first_charge_tier(int counter)
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, counter);

		var result = sim.TryApply(new CastActiveSkillCommand(0, BothEnemies));

		Assert.That(result.Success, Is.True, result.Error);
		Assert.That(host.Invocations, Is.EqualTo(new[] { "tier1.js" }));
		Assert.That(character.SkillCounter, Is.EqualTo(counter - 10), "扣累计阈值 T_1");
	}

	[Test]
	public void Counter_at_cap_casts_the_highest_charge_tier()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 16);

		var result = sim.TryApply(new CastActiveSkillCommand(0, []));

		Assert.That(result.Success, Is.True, result.Error);
		Assert.That(host.Invocations, Is.EqualTo(new[] { "tier2.js" }));
		Assert.That(character.SkillCounter, Is.Zero);
	}

	[Test]
	public void Overflow_counter_survives_the_payment_and_allows_another_base_cast()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 15);

		Assert.That(sim.TryApply(new CastActiveSkillCommand(0, BothEnemies)).Success, Is.True);
		Assert.That(character.SkillCounter, Is.EqualTo(5), "S=15 放蓄力Ⅰ → 溢出资保留");

		Assert.That(sim.TryApply(new CastActiveSkillCommand(0, Enemy0)).Success, Is.True);

		Assert.That(host.Invocations, Is.EqualTo(new[] { "tier1.js", "tier0.js" }));
		Assert.That(character.SkillCounter, Is.EqualTo(1));
	}

	[Test]
	public void Skill_effect_granting_skill_counter_enables_a_follow_up_cast()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(
			host,
			chain:
			[
				new ActiveSkillChainEntryDto { SkillId = "skill.tier0", Cooldown = 4 },
				new ActiveSkillChainEntryDto { SkillId = "skill.tier1_burst", Cooldown = 6 },
			]);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 10);

		Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.True);

		Assert.That(character.SkillCounter, Is.EqualTo(4), "先扣 T_1=10 归零，效果再 +4");
		Assert.That(sim.TryApply(new CastActiveSkillCommand(0, Enemy0)).Success, Is.True, "可再放基础档");
		Assert.That(character.SkillCounter, Is.Zero);
	}

	[Test]
	public void TickSkillCounter_stops_growing_at_the_chain_cap()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];

		for (var i = 0; i < 20; i++)
			character.TickSkillCounter();

		Assert.That(character.SkillCounter, Is.EqualTo(16));
	}

	#endregion

	#region 指令语义

	[Test]
	public void Character_without_chain_rejects_the_cast()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host, chain: []);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 20);

		var result = sim.TryApply(new CastActiveSkillCommand(0, Enemy0));

		Assert.That(result.Success, Is.False);
		Assert.That(host.Invocations, Is.Empty);
	}

	[Test]
	public void Confirmed_character_may_still_cast_and_stays_confirmed()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 4);
		character.SetHasActed(true);

		var result = sim.TryApply(new CastActiveSkillCommand(0, Enemy0));

		Assert.That(result.Success, Is.True, result.Error);
		Assert.That(character.HasActed, Is.True, "主动技不占已行动");
	}

	[Test]
	public void Cast_does_not_consume_available_energy()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		character.RefillAvailableEnergy();
		var energyBefore = character.AvailableEnergy;
		SetCounter(character, 4);

		Assert.That(sim.TryApply(new CastActiveSkillCommand(0, Enemy0)).Success, Is.True);

		Assert.That(character.AvailableEnergy, Is.EqualTo(energyBefore));
	}

	[Test]
	public void Cast_rejects_invalid_character_index()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);

		Assert.That(sim.TryApply(new CastActiveSkillCommand(9, Enemy0)).Success, Is.False);
	}

	[Test]
	public void Card_skill_refs_do_not_move_the_skill_counter()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 5);
		sim.CardQueue.Enqueue(new QueuedCardEntry(
			CharacterIndex: 0,
			CardId: "card.tier0",
			RuntimeInstanceId: "rt-x",
			Priority: 100,
			Targets: Enemy0,
			Sequence: sim.AllocateQueueSequence()));
		sim.TransitionTo(ECombatPhase.CardExecution);

		sim.AdvancePhase();

		Assert.That(host.Invocations, Is.EqualTo(new[] { "tier0.js" }), "卡牌照常执行技能载荷");
		Assert.That(character.SkillCounter, Is.EqualTo(5), "卡牌 skillRefs 不走 S 计数器");
	}

	#endregion

	#region 档位目标校验（2026-07-28 决议）

	[Test]
	public void Base_tier_single_scope_rejects_multi_target_input()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 4);

		var result = sim.TryApply(new CastActiveSkillCommand(0, BothEnemies));

		Assert.That(result.Success, Is.False, "低档为单体，传全体目标应拒绝");
		Assert.That(character.SkillCounter, Is.EqualTo(4), "校验失败不消耗 S");
		Assert.That(host.Invocations, Is.Empty);
	}

	[Test]
	public void Charge_tier_all_scope_rejects_partial_target_input()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 10);

		var result = sim.TryApply(new CastActiveSkillCommand(0, Enemy0));

		Assert.That(result.Success, Is.False, "高档为全体，传单体目标应拒绝");
		Assert.That(character.SkillCounter, Is.EqualTo(10));
	}

	[Test]
	public void Charge_tier_all_scope_accepts_empty_targets_and_expands_to_the_legal_pool()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 10);

		Assert.That(sim.TryApply(new CastActiveSkillCommand(0, [])).Success, Is.True);

		Assert.That(host.Invocations, Is.EqualTo(new[] { "tier1.js" }));
	}

	[Test]
	public void Single_scope_rejects_a_dead_target()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		sim.EnemyTeam.Enemies[0].ApplyDamage(999);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 4);

		var result = sim.TryApply(new CastActiveSkillCommand(0, Enemy0));

		Assert.That(result.Success, Is.False);
		Assert.That(character.SkillCounter, Is.EqualTo(4));
	}

	[Test]
	public void Missing_target_override_is_treated_as_self_single()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 16);

		var rejected = sim.TryApply(new CastActiveSkillCommand(0, Enemy0));
		Assert.That(rejected.Success, Is.False, "缺省 self 单体不接受敌方目标");
		Assert.That(character.SkillCounter, Is.EqualTo(16));

		var accepted = sim.TryApply(new CastActiveSkillCommand(0, [new CombatTargetRef(ECombatSide.Player, 0)]));
		Assert.That(accepted.Success, Is.True, accepted.Error);
		Assert.That(host.Invocations, Is.EqualTo(new[] { "tier2.js" }));
	}

	[Test]
	public void Missing_target_override_rejects_another_ally_slot()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(host);
		var character = sim.PlayerTeam.Characters[0];
		SetCounter(character, 16);

		var result = sim.TryApply(
			new CastActiveSkillCommand(0, [new CombatTargetRef(ECombatSide.Player, 1)]));

		Assert.That(result.Success, Is.False, "缺省 self 只接受指向施法者自己的目标");
		Assert.That(character.SkillCounter, Is.EqualTo(16));
	}

	#endregion

	#region 内容校验器

	[Test]
	public void Validator_accepts_an_empty_chain()
	{
		var character = new CharacterDto { Id = "no_active" };

		Assert.That(CombatContentValidator.TryValidateActiveSkillChain(character, out var error), Is.True);
		Assert.That(error, Is.Null);
	}

	[Test]
	public void Validator_accepts_a_well_formed_chain()
	{
		var character = new CharacterDto
		{
			Id = "hero",
			ActiveSkillChain =
			[
				new ActiveSkillChainEntryDto { SkillId = "skill.a", Cooldown = 4 },
				new ActiveSkillChainEntryDto { SkillId = "skill.b", Cooldown = 1 },
			],
		};

		Assert.That(CombatContentValidator.TryValidateActiveSkillChain(character, out var error), Is.True, error);
	}

	[Test]
	[TestCase(0)]
	[TestCase(-2)]
	public void Validator_rejects_a_tier_cooldown_below_one(int cooldown)
	{
		var character = new CharacterDto
		{
			Id = "hero",
			ActiveSkillChain = [new ActiveSkillChainEntryDto { SkillId = "skill.a", Cooldown = cooldown }],
		};

		Assert.That(CombatContentValidator.TryValidateActiveSkillChain(character, out var error), Is.False);
		Assert.That(error, Is.Not.Null);
	}

	[Test]
	public void Validator_rejects_a_blank_skill_id()
	{
		var character = new CharacterDto
		{
			Id = "hero",
			ActiveSkillChain = [new ActiveSkillChainEntryDto { SkillId = "  ", Cooldown = 4 }],
		};

		Assert.That(CombatContentValidator.TryValidateActiveSkillChain(character, out _), Is.False);
	}

	#endregion

	#region 从角色定义读取链

	[Test]
	public void TryCreate_reads_the_chain_and_the_cap_from_the_character_definition()
	{
		var registry = CombatTestHelper.CreateRegistry(
			new CardDto { Id = "strike", Stats = new CardStatBlockDto { HpCap = 4 } });
		var source = new CharacterInstance(new CharacterDto
		{
			Id = "kemo",
			Cards = ["strike"],
			ActiveSkillChain =
			[
				new ActiveSkillChainEntryDto { SkillId = "skill.tier0", Cooldown = 4 },
				new ActiveSkillChainEntryDto { SkillId = "skill.tier1", Cooldown = 6 },
			],
		});

		var battle = CharacterBattleInstance.TryCreate(source, registry, new HostRng(1, "deck"), out var error);

		Assert.That(error, Is.Null);
		Assert.That(battle!.SkillCounterCap, Is.EqualTo(10));
		Assert.That(battle.ActiveSkillChain.Select(tier => tier.SkillId), Is.EqualTo(new[] { "skill.tier0", "skill.tier1" }));
	}

	[Test]
	public void TryCreate_rejects_a_character_whose_chain_is_misconfigured()
	{
		var registry = CombatTestHelper.CreateRegistry(
			new CardDto { Id = "strike", Stats = new CardStatBlockDto { HpCap = 4 } });
		var source = new CharacterInstance(new CharacterDto
		{
			Id = "kemo",
			Cards = ["strike"],
			ActiveSkillChain = [new ActiveSkillChainEntryDto { SkillId = "skill.tier0", Cooldown = 0 }],
		});

		var battle = CharacterBattleInstance.TryCreate(source, registry, new HostRng(1, "deck"), out var error);

		Assert.That(battle, Is.Null);
		Assert.That(error, Is.Not.Null);
	}

	#endregion

	private static void SetCounter(CharacterBattleInstance character, int target)
	{
		for (var i = 0; i < target; i++)
			character.TickSkillCounter();
	}

	private static CombatSimulation Build(
		IContentEffectScriptHost scriptHost,
		IReadOnlyList<ActiveSkillChainEntryDto>? chain = null)
	{
		chain ??=
		[
			new ActiveSkillChainEntryDto { SkillId = "skill.tier0", Cooldown = 4 },
			new ActiveSkillChainEntryDto { SkillId = "skill.tier1", Cooldown = 6 },
			new ActiveSkillChainEntryDto { SkillId = "skill.tier2", Cooldown = 6 },
		];

		var registry = CombatTestHelper.CreateFullRegistry(
			cards: new Dictionary<string, CardDto>
			{
				["card.tier0"] = new()
				{
					Id = "card.tier0",
					TargetSide = ETargetSide.Enemy,
					TargetScope = ETargetScope.Single,
					TargetCount = 1,
					Priority = 100,
					SkillRefs = [new SkillRefDto { SkillId = "skill.tier0" }],
				},
			},
			skills: new Dictionary<string, SkillDto>
			{
				["skill.tier0"] = new()
				{
					Id = "skill.tier0",
					ActionRefs = [new SkillActionRefDto { ActionId = "action.tier0" }],
					TargetOverride = new TargetSpecDto
					{
						Side = ETargetSide.Enemy,
						Scope = ETargetScope.Single,
						TargetCount = 1,
					},
				},
				["skill.tier1"] = new()
				{
					Id = "skill.tier1",
					ActionRefs = [new SkillActionRefDto { ActionId = "action.tier1" }],
					TargetOverride = new TargetSpecDto { Side = ETargetSide.Enemy, Scope = ETargetScope.All },
				},
				["skill.tier2"] = new()
				{
					Id = "skill.tier2",
					ActionRefs = [new SkillActionRefDto { ActionId = "action.tier2" }],
				},
				["skill.tier1_burst"] = new()
				{
					Id = "skill.tier1_burst",
					ActionRefs = [new SkillActionRefDto { ActionId = "action.gain_counter" }],
				},
			},
			skillActions: new Dictionary<string, SkillActionDto>
			{
				["action.tier0"] = ScriptAction("action.tier0", "tier0.js"),
				["action.tier1"] = ScriptAction("action.tier1", "tier1.js"),
				["action.tier2"] = ScriptAction("action.tier2", "tier2.js"),
				["action.gain_counter"] = new()
				{
					Id = "action.gain_counter",
					Kind = ESkillActionKind.GainResource,
					Params = new Dictionary<string, object>
					{
						["resource"] = "SkillCounter",
						["amount"] = 4,
					},
				},
			});

		var attributes = new Dictionary<string, float>(StringComparer.Ordinal)
		{
			[AttributeIds.MaxHealth] = 10f,
			[AttributeIds.InitialEnergy] = 3f,
			[AttributeIds.MaxEnergy] = 3f,
		};
		var characters = Enumerable.Range(0, 4)
			.Select(i => CharacterBattleInstance.CreateForTests(
				$"c{i}",
				attributes,
				activeSkillChain: chain))
			.ToArray();

		return new CombatSimulation(
			new PlayerTeamState(characters, sharedMaxHp: 40),
			new EnemyTeamState(
			[
				new EnemyUnit("e0", "slime", maxHp: 100),
				new EnemyUnit("e1", "slime", maxHp: 100),
			]),
			new CombatRuleEngine([]),
			registry,
			initialPhase: ECombatPhase.Player,
			scriptHost: scriptHost);
	}

	private static SkillActionDto ScriptAction(string id, string scriptPath) => new()
	{
		Id = id,
		Kind = ESkillActionKind.ExecuteScript,
		ScriptPath = scriptPath,
	};
}
