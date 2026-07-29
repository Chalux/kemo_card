using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>?? �6.1 / �1.2?BattleStart ???? ? ???? SharedHp ? ???? 5 ? ???????</summary>
[TestFixture]
public sealed class BattleStartPipelineTests
{
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

	private sealed class LockProbeScriptHost : IContentEffectScriptHost
	{
		public PlayerTeamState? Team { get; set; }
		public List<bool> Observed { get; } = [];

		public bool TryExecute(
			string modId,
			string scriptPath,
			string scriptEntry,
			IReadOnlyDictionary<string, object>? context,
			out IReadOnlyList<Dictionary<string, object>> proposedEffects)
		{
			Observed.Add(Team?.SharedHpLocked ?? false);
			proposedEffects = [];
			return true;
		}
	}

	#region ????

	[Test]
	public void Injected_skills_execute_once_each_in_list_order()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(
			[
				new BattleStartSkillEntry("skill.passive_a", 0),
				new BattleStartSkillEntry("skill.passive_b", 1),
				new BattleStartSkillEntry("skill.modifier", -1),
			],
			scriptHost: host);

		sim.RunBattleStart();

		Assert.That(host.Invocations, Is.EqualTo(new[] { "passive_a.js", "passive_b.js", "modifier.js" }));
	}

	[Test]
	public void Unknown_skill_id_is_skipped_softly()
	{
		var host = new RecordingScriptHost();
		using var sim = Build(
			[
				new BattleStartSkillEntry("skill.does_not_exist", 0),
				new BattleStartSkillEntry("skill.passive_a", 0),
			],
			scriptHost: host);

		sim.RunBattleStart();

		Assert.That(host.Invocations, Is.EqualTo(new[] { "passive_a.js" }));
		Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
	}

	#endregion

	#region SharedHp ???????

	[Test]
	public void Locked_shared_hp_blocks_damage_and_counts_the_attempt()
	{
		using var sim = Build([]);
		var team = sim.PlayerTeam;
		var before = team.SharedHp;

		team.SharedHpLocked = true;
		team.ApplySharedDamage(7);

		Assert.That(team.SharedHp, Is.EqualTo(before), "?????????");
		Assert.That(team.BlockedSharedHpWriteCount, Is.EqualTo(1));
	}

	[Test]
	public void Locked_shared_hp_blocks_heal_and_counts_the_attempt()
	{
		using var sim = Build([]);
		var team = sim.PlayerTeam;
		team.ApplySharedDamage(5);
		var damaged = team.SharedHp;

		team.SharedHpLocked = true;
		team.HealShared(7);

		Assert.That(team.SharedHp, Is.EqualTo(damaged), "?????????");
		Assert.That(team.BlockedSharedHpWriteCount, Is.EqualTo(1));
	}

	[Test]
	public void Injected_skills_leave_shared_hp_at_max_after_the_fill()
	{
		using var sim = Build(
			[
				new BattleStartSkillEntry("skill.shared_damage", 0),
				new BattleStartSkillEntry("skill.shared_heal", 0),
			]);

		sim.RunBattleStart();

		Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp), "????????");
	}

	[Test]
	public void Injected_slot_damage_is_blocked_end_to_end_by_the_write_lock()
	{
		using var sim = Build([new BattleStartSkillEntry("skill.shared_damage", 0)]);

		sim.RunBattleStart();

		Assert.That(
			sim.PlayerTeam.BlockedSharedHpWriteCount,
			Is.EqualTo(1),
			"??????????????????? �1.2 BattleStart ?????");
		Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp));
	}

	[Test]
	public void Injected_team_damage_and_heal_are_blocked_end_to_end_by_the_write_lock()
	{
		using var sim = Build(
			[
				new BattleStartSkillEntry("skill.team_damage", 0),
				new BattleStartSkillEntry("skill.team_heal", 0),
			]);

		sim.RunBattleStart();

		Assert.That(sim.PlayerTeam.BlockedSharedHpWriteCount, Is.EqualTo(2), "Team ?????????????");
		Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp));
	}

	[Test]
	public void Shared_hp_is_locked_while_injected_skills_run()
	{
		var probe = new LockProbeScriptHost();
		using var sim = Build([new BattleStartSkillEntry("skill.passive_a", 0)], scriptHost: probe);
		probe.Team = sim.PlayerTeam;

		sim.RunBattleStart();

		Assert.That(probe.Observed, Is.EqualTo(new[] { true }), "????????????????");
		Assert.That(sim.PlayerTeam.SharedHpLocked, Is.False, "?????");
	}

	[Test]
	public void Shared_hp_lock_is_released_after_battle_start()
	{
		using var sim = Build([]);

		sim.RunBattleStart();
		sim.PlayerTeam.ApplySharedDamage(3);

		Assert.That(sim.PlayerTeam.SharedHpLocked, Is.False);
		Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp - 3));
	}

	[Test]
	public void Max_health_change_from_injection_is_reflected_before_the_fill()
	{
		using var sim = Build([new BattleStartSkillEntry("skill.buff_max_health", 0)]);
		var maxBefore = sim.PlayerTeam.MaxHp;

		sim.RunBattleStart();

		Assert.That(sim.PlayerTeam.MaxHp, Is.EqualTo(maxBefore + 20), "MaxSharedHp ???????");
		Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp), "?????????");
	}

	[Test]
	public void FreezeAndFillSharedHp_is_blocked_by_nothing_and_tops_up_to_max()
	{
		using var sim = Build([]);
		sim.PlayerTeam.ApplySharedDamage(10);

		sim.PlayerTeam.FreezeAndFillSharedHp();

		Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp));
	}

	#endregion

	#region ???????????

	[Test]
	public void Every_character_draws_a_full_hand_of_five()
	{
		using var sim = Build([]);

		sim.RunBattleStart();

		foreach (var character in sim.PlayerTeam.Characters)
		{
			Assert.That(
				character.HandSlots.Count(slot => !slot.IsEmpty),
				Is.EqualTo(CombatConstants.HandSlotCount));
		}
	}

	[Test]
	public void Battle_start_enters_player_phase_and_runs_the_first_phase_pipeline()
	{
		using var sim = Build([], initialEnergy: 2, maxEnergy: 5, skillCounterCap: 3);

		sim.RunBattleStart();

		Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
		var character = sim.PlayerTeam.Characters[0];
		Assert.That(character.CurrentEnergy, Is.EqualTo(2), "???????? +1");
		Assert.That(character.AvailableEnergy, Is.EqualTo(2));
		Assert.That(character.SkillCounter, Is.EqualTo(1), "??? S ? +1");
		Assert.That(sim.IsFirstPlayerPhase, Is.False, "????????");
	}

	[Test]
	public void Opening_draw_does_not_consume_the_first_phase_shuffle_budget()
	{
		// ?? 5 ??????????????????? 5 ??????????????????
		using var sim = Build([], deckSize: 5);
		sim.RunBattleStart();
		foreach (var character in sim.PlayerTeam.Characters)
			DumpHandToGraveyard(character, 5);

		PlayerPhasePipeline.Run(sim, isFirstPlayerPhase: false);

		Assert.That(sim.PlayerTeam.Characters[0].HandSlots.Count(s => !s.IsEmpty), Is.EqualTo(1));
	}

	[Test]
	public void Advance_from_battle_start_runs_the_full_pipeline()
	{
		using var sim = Build([], phase: ECombatPhase.BattleStart);

		sim.AdvancePhase();

		Assert.That(sim.Phase, Is.EqualTo(ECombatPhase.Player));
		Assert.That(
			sim.PlayerTeam.Characters[0].HandSlots.Count(slot => !slot.IsEmpty),
			Is.EqualTo(CombatConstants.HandSlotCount));
		Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(sim.PlayerTeam.MaxHp));
	}

	#endregion

	private static void DumpHandToGraveyard(CharacterBattleInstance character, int count)
	{
		var dumped = 0;
		foreach (var slot in character.HandSlots)
		{
			if (dumped >= count || slot.IsEmpty) continue;
			character.MoveHandCardToGraveyard(slot.RuntimeInstanceId!);
			dumped++;
		}
	}

	private static CombatSimulation Build(
		IReadOnlyList<BattleStartSkillEntry> battleStartSkills,
		int initialEnergy = 0,
		int maxEnergy = 3,
		int deckSize = 8,
		int skillCounterCap = 0,
		int characterCount = 4,
		ECombatPhase phase = ECombatPhase.BattleStart,
		IContentEffectScriptHost? scriptHost = null)
	{
		var registry = CombatTestHelper.CreateFullRegistry(
			skills: new Dictionary<string, SkillDto>
			{
				["skill.passive_a"] = ScriptSkill("skill.passive_a", "passive_a.js"),
				["skill.passive_b"] = ScriptSkill("skill.passive_b", "passive_b.js"),
				["skill.modifier"] = ScriptSkill("skill.modifier", "modifier.js"),
				["skill.shared_damage"] = new()
				{
					Id = "skill.shared_damage",
					EffectRefs = [new EffectRefDto { EffectId = "effect.shared_damage" }],
					TargetOverride = new TargetSpecDto { Side = ETargetSide.Self, Scope = ETargetScope.Self },
				},
				["skill.shared_heal"] = new()
				{
					Id = "skill.shared_heal",
					EffectRefs = [new EffectRefDto { EffectId = "effect.shared_heal" }],
					TargetOverride = new TargetSpecDto { Side = ETargetSide.Self, Scope = ETargetScope.Self },
				},
				["skill.team_damage"] = new()
				{
					Id = "skill.team_damage",
					EffectRefs = [new EffectRefDto { EffectId = "effect.shared_damage" }],
					TargetOverride = new TargetSpecDto { Side = ETargetSide.Ally, Scope = ETargetScope.Team },
				},
				["skill.team_heal"] = new()
				{
					Id = "skill.team_heal",
					EffectRefs = [new EffectRefDto { EffectId = "effect.shared_heal" }],
					TargetOverride = new TargetSpecDto { Side = ETargetSide.Ally, Scope = ETargetScope.Team },
				},
				["skill.buff_max_health"] = new()
				{
					Id = "skill.buff_max_health",
					ActionRefs = [new SkillActionRefDto { ActionId = "action.buff_max_health" }],
				},
			},
			effects: new Dictionary<string, EffectDto>
			{
				["effect.shared_damage"] = new()
				{
					Id = "effect.shared_damage",
					Kind = EEffectKind.Damage,
					Params = new Dictionary<string, object> { ["amount"] = 7 },
				},
				["effect.shared_heal"] = new()
				{
					Id = "effect.shared_heal",
					Kind = EEffectKind.Heal,
					Params = new Dictionary<string, object> { ["amount"] = 7 },
				},
			},
			attributes: new Dictionary<string, AttributeDefDto>
			{
				[AttributeIds.MaxHealth] = new() { Id = AttributeIds.MaxHealth },
			},
			gameplayEffects: new Dictionary<string, GameplayEffectDefDto>
			{
				["ge.buff_max_health"] = new()
				{
					Id = "ge.buff_max_health",
					DurationPolicy = EDurationPolicy.Instant,
					StackingPolicy = EStackingPolicy.None,
					MaxStacks = 1,
					Modifiers =
					[
						new AttributeModifierDefDto
						{
							AttributeId = AttributeIds.MaxHealth,
							Operation = EAttributeModifierOp.Add,
							Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 20f },
						},
					],
				},
			},
			skillActions: new Dictionary<string, SkillActionDto>
			{
				["action.buff_max_health"] = new()
				{
					Id = "action.buff_max_health",
					Kind = ESkillActionKind.ApplyGameplayEffect,
					Params = new Dictionary<string, object> { ["gameplayEffectId"] = "ge.buff_max_health" },
				},
				["action.passive_a.js"] = ScriptAction("action.passive_a.js", "passive_a.js"),
				["action.passive_b.js"] = ScriptAction("action.passive_b.js", "passive_b.js"),
				["action.modifier.js"] = ScriptAction("action.modifier.js", "modifier.js"),
			});

		var characters = Enumerable.Range(0, characterCount)
			.Select(i => CharacterBattleInstance.CreateForTests(
				$"c{i}",
				new Dictionary<string, float>(StringComparer.Ordinal)
				{
					[AttributeIds.MaxHealth] = 10f,
					[AttributeIds.InitialEnergy] = initialEnergy,
					[AttributeIds.MaxEnergy] = maxEnergy,
				},
				Enumerable.Range(0, deckSize).Select(n => new CardRuntimeEntry("test.card", $"rt-c{i}-{n}")),
				skillCounterCap))
			.ToArray();

		return new CombatSimulation(
			new PlayerTeamState(characters, sharedMaxHp: 10 * characterCount),
			new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 100)]),
			new CombatRuleEngine([]),
			registry,
			initialPhase: phase,
			scriptHost: scriptHost,
			battleStartSkills: battleStartSkills);
	}

	private static SkillDto ScriptSkill(string id, string scriptPath) => new()
	{
		Id = id,
		ActionRefs = [new SkillActionRefDto { ActionId = $"action.{scriptPath}" }],
	};

	private static SkillActionDto ScriptAction(string id, string scriptPath) => new()
	{
		Id = id,
		Kind = ESkillActionKind.ExecuteScript,
		ScriptPath = scriptPath,
	};
}
