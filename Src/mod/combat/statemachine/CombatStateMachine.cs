using KemoCard.Mod.Combat.Commands;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.StateMachine;

public sealed class CombatStateMachine
{
	public ECombatPhase Phase { get; private set; }

	public CombatStateMachine(ECombatPhase initialPhase = ECombatPhase.BattleStart)
	{
		Phase = initialPhase;
	}

	public void TransitionTo(ECombatPhase phase) => Phase = phase;

	public void Advance(CombatSimulation simulation)
	{
		ArgumentNullException.ThrowIfNull(simulation);

		switch (Phase)
		{
			case ECombatPhase.BattleStart:
				TransitionTo(ECombatPhase.Player);
				simulation.DomainManager.FireTurnStartHooks();
				break;
			case ECombatPhase.CardExecution:
				ExecuteCardExecutionPhase(simulation);
				break;
			case ECombatPhase.Enemy:
				ExecuteEnemyPhase(simulation);
				break;
		}
	}

	public CombatApplyResult TryApply(CombatSimulation simulation, ICombatCommand command)
	{
		ArgumentNullException.ThrowIfNull(simulation);
		ArgumentNullException.ThrowIfNull(command);

		return Phase switch
		{
			ECombatPhase.Player => TryApplyPlayerPhase(simulation, command),
			_ => new CombatApplyResult(false, $"阶段 {Phase} 不支持该指令。"),
		};
	}

	private static CombatApplyResult TryApplyPlayerPhase(CombatSimulation simulation, ICombatCommand command)
	{
		return command switch
		{
			PlayCardCommand playCard => ApplyPlayCard(simulation, playCard),
			CastInstantSkillCommand castSkill => ApplyCastInstantSkill(simulation, castSkill),
			ConfirmCharacterCommand confirm => ApplyConfirmCharacter(simulation, confirm),
			CancelQueuedCardCommand cancel => ApplyCancelQueuedCard(simulation, cancel),
			_ => new CombatApplyResult(false, "玩家阶段尚未实现该指令。"),
		};
	}

	private static CombatApplyResult ApplyPlayCard(CombatSimulation simulation, PlayCardCommand command)
	{
		if (!TryGetCharacter(simulation, command.CharacterIndex, out var character, out var error))
			return new CombatApplyResult(false, error);
		if (character.HasActed)
			return new CombatApplyResult(false, "角色已行动，无法出牌。");
		if (command.HandSlotIndex < 0 || command.HandSlotIndex >= character.HandSlots.Count)
			return new CombatApplyResult(false, "手牌槽位无效。");

		var slot = character.HandSlots[command.HandSlotIndex];
		if (slot.IsEmpty || slot.CardId is null || slot.RuntimeInstanceId is null)
			return new CombatApplyResult(false, "指定槽位没有卡牌。");
		if (!simulation.Definitions.Store.TryGetCard(slot.CardId, out var card))
			return new CombatApplyResult(false, "卡牌定义不存在。");
		if (card.CostType == ECostType.Energy && !character.TryConsumeEnergy(card.Cost))
			return new CombatApplyResult(false, "能量不足。");

		var entry = new QueuedCardEntry(
			command.CharacterIndex,
			slot.CardId,
			slot.RuntimeInstanceId,
			card.Priority,
			command.Targets,
			simulation.AllocateQueueSequence());
		simulation.CardQueue.Enqueue(entry);
		slot.ClearCard();
		return new CombatApplyResult(true);
	}

	private static CombatApplyResult ApplyCastInstantSkill(CombatSimulation simulation, CastInstantSkillCommand command)
	{
		if (!TryGetCharacter(simulation, command.CharacterIndex, out _, out var error))
			return new CombatApplyResult(false, error);
		if (!simulation.Definitions.Store.TryGetSkill(command.SkillId, out var skill))
			return new CombatApplyResult(false, "技能定义不存在。");

		var aliveEnemiesBefore = SnapshotAliveEnemyIndices(simulation);
		var source = new CombatTargetRef(ECombatSide.Player, command.CharacterIndex);
		ExecuteSkillPayload(simulation, skill, source, command.Targets);
		ApplyInstantSkillTargetLossRollback(simulation, aliveEnemiesBefore);
		return new CombatApplyResult(true);
	}

	private static HashSet<int> SnapshotAliveEnemyIndices(CombatSimulation simulation)
	{
		var alive = new HashSet<int>();
		for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
		{
			if (simulation.EnemyTeam.Enemies[i].IsAlive)
				alive.Add(i);
		}

		return alive;
	}

	private static void ApplyInstantSkillTargetLossRollback(
		CombatSimulation simulation,
		HashSet<int> aliveEnemiesBefore)
	{
		var lostEnemies = new HashSet<int>();
		foreach (var index in aliveEnemiesBefore)
		{
			if (!simulation.EnemyTeam.Enemies[index].IsAlive)
				lostEnemies.Add(index);
		}

		if (lostEnemies.Count == 0)
			return;

		var holdersToUnact = new HashSet<int>();
		foreach (var entry in simulation.CardQueue.PeekAllOrdered())
		{
			if (entry.Targets.Any(target =>
					target.Side == ECombatSide.Enemy && lostEnemies.Contains(target.Index)))
			{
				holdersToUnact.Add(entry.CharacterIndex);
			}
		}

		foreach (var holderIndex in holdersToUnact)
			simulation.PlayerTeam.Characters[holderIndex].SetHasActed(false);
	}

	private static CombatApplyResult ApplyConfirmCharacter(CombatSimulation simulation, ConfirmCharacterCommand command)
	{
		var index = command.CharacterIndex;
		var characters = simulation.PlayerTeam.Characters;
		if (index < 0 || index >= characters.Count)
			return new CombatApplyResult(false, "角色索引无效。");

		characters[index].SetHasActed(true);
		if (characters.Count == 4 && characters.All(c => c.HasActed))
			simulation.TransitionTo(ECombatPhase.CardExecution);
		return new CombatApplyResult(true);
	}

	private static CombatApplyResult ApplyCancelQueuedCard(CombatSimulation simulation, CancelQueuedCardCommand command)
	{
		if (!TryGetCharacter(simulation, command.CharacterIndex, out var character, out var error))
			return new CombatApplyResult(false, error);

		var removed = simulation.CardQueue.TryRemove(
			entry => MatchesCancelCommand(entry, command),
			out _);
		if (!removed)
			return new CombatApplyResult(false, "未找到可取消的卡牌。");

		character.SetHasActed(false);
		return new CombatApplyResult(true);
	}

	private static bool MatchesCancelCommand(QueuedCardEntry entry, CancelQueuedCardCommand command)
	{
		if (entry.CharacterIndex != command.CharacterIndex)
			return false;
		if (command.QueueSequence.HasValue && entry.Sequence != command.QueueSequence.Value)
			return false;
		if (!string.IsNullOrWhiteSpace(command.CardRuntimeInstanceId) &&
			!string.Equals(entry.RuntimeInstanceId, command.CardRuntimeInstanceId, StringComparison.Ordinal))
			return false;
		return true;
	}

	private static void ExecuteCardExecutionPhase(CombatSimulation simulation)
	{
		while (simulation.CardQueue.TryDequeue(out var dequeued) && dequeued is not null)
		{
			if (!simulation.Definitions.Store.TryGetCard(dequeued.CardId, out var card))
				continue;

			var resolvedTargets = ResolveCardTargets(simulation, dequeued, card);
			if (resolvedTargets.Count == 0)
				continue;

			var source = new CombatTargetRef(ECombatSide.Player, dequeued.CharacterIndex);
			foreach (var skillRef in card.SkillRefs)
			{
				if (!simulation.Definitions.Store.TryGetSkill(skillRef.SkillId, out var skill))
					continue;
				ExecuteSkillPayload(simulation, skill, source, resolvedTargets, skillRef?.Params);
			}
		}

		simulation.CheckEndConditions();
		if (simulation.Phase is ECombatPhase.Victory or ECombatPhase.Defeat or ECombatPhase.Player)
			return;

		simulation.DomainManager.FireTurnStartHooks();
		simulation.TransitionTo(ECombatPhase.Enemy);
	}

	private static void ExecuteEnemyPhase(CombatSimulation simulation)
	{
		for (var enemyIndex = 0; enemyIndex < simulation.EnemyTeam.Enemies.Count; enemyIndex++)
		{
			var enemy = simulation.EnemyTeam.Enemies[enemyIndex];
			if (!enemy.IsAlive)
				continue;

			var skillId = simulation.EnemyAi.ChooseSkill(enemy, simulation.EnemyAiRng);
			enemy.IntentSkillId = skillId;
			if (skillId is null)
				continue;

			ExecuteEnemySkill(simulation, enemyIndex, skillId);
		}

		simulation.Rules.DispatchTurnEnd(simulation.CreateContext());
		simulation.DomainManager.FireTurnEndHooks();
		foreach (var character in simulation.PlayerTeam.Characters)
			character.SetHasActed(false);

		simulation.CheckEndConditions();
		if (simulation.Phase is ECombatPhase.Victory or ECombatPhase.Defeat or ECombatPhase.Player)
			return;

		simulation.IncrementTurnNumber();
		simulation.DomainManager.FireTurnStartHooks();
		simulation.TransitionTo(ECombatPhase.Player);
	}

	private static void ExecuteEnemySkill(CombatSimulation simulation, int enemyIndex, string skillId)
	{
		if (!simulation.Definitions.Store.TryGetSkill(skillId, out var skill))
			return;

		SkillRefDto? skillRef = null;
		var enemy = simulation.EnemyTeam.Enemies[enemyIndex];
		if (simulation.Definitions.Store.TryGetEnemy(enemy.DefinitionId, out var enemyDef))
		{
			skillRef = enemyDef.SkillRefs.FirstOrDefault(
				entry => string.Equals(entry.SkillId, skillId, StringComparison.Ordinal));
		}

		var source = new CombatTargetRef(ECombatSide.Enemy, enemyIndex);
		var targets = ResolveEnemySkillTargets(simulation, skill, enemyIndex);
		ExecuteSkillPayload(simulation, skill, source, targets, skillRef?.Params);
	}

	private static void ExecuteSkillPayload(
		CombatSimulation simulation,
		SkillDto skill,
		CombatTargetRef source,
		IReadOnlyList<CombatTargetRef> targets,
		Dictionary<string, object>? skillParams = null)
	{
		if (skill.ActionRefs.Count > 0)
		{
			foreach (var actionRef in skill.ActionRefs)
			{
				var resolvedActionRef = skillParams is not null
					? MergeActionRefParams(actionRef, skillParams)
					: actionRef;
				simulation.EffectExecutor.ExecuteSkillActionRef(resolvedActionRef, simulation, source, targets);
			}
			return;
		}

		foreach (var effectRef in skill.EffectRefs)
		{
			var resolvedEffectRef = skillParams is not null
				? MergeEffectRefParams(effectRef, skillParams)
				: effectRef;
			simulation.EffectExecutor.ExecuteEffectRef(resolvedEffectRef, simulation, source, targets);
		}
	}

	private static IReadOnlyList<CombatTargetRef> ResolveEnemySkillTargets(
		CombatSimulation simulation,
		SkillDto skill,
		int enemyIndex)
	{
		var spec = skill.TargetOverride;
		if (spec is null)
			return [CombatTargetRef.PlayerTeam];

		return ResolveTargetsFromSpec(simulation, spec, enemyIndex);
	}

	private static IReadOnlyList<CombatTargetRef> ResolveTargetsFromSpec(
		CombatSimulation simulation,
		TargetSpecDto spec,
		int sourceEnemyIndex)
	{
		var legal = CollectLegalTargetsForEnemy(simulation, spec.Side, sourceEnemyIndex);
		if (legal.Count == 0)
			return [];

		return spec.Scope switch
		{
			ETargetScope.All => legal,
			ETargetScope.RandomN => legal.Take(Math.Max(1, spec.TargetCount)).ToList(),
			_ => legal.Count <= spec.TargetCount || spec.TargetCount <= 0
				? [legal[0]]
				: legal.Take(spec.TargetCount).ToList(),
		};
	}

	private static List<CombatTargetRef> CollectLegalTargetsForEnemy(
		CombatSimulation simulation,
		ETargetSide side,
		int sourceEnemyIndex)
	{
		var legal = new List<CombatTargetRef>();
		if (side is ETargetSide.Self)
		{
			if (sourceEnemyIndex >= 0 && sourceEnemyIndex < simulation.EnemyTeam.Enemies.Count &&
				simulation.EnemyTeam.Enemies[sourceEnemyIndex].IsAlive)
			{
				legal.Add(new CombatTargetRef(ECombatSide.Enemy, sourceEnemyIndex));
			}

			return legal;
		}

		if (side is ETargetSide.Ally or ETargetSide.Any)
		{
			for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
			{
				if (side == ETargetSide.Ally && i == sourceEnemyIndex)
					continue;
				if (!simulation.EnemyTeam.Enemies[i].IsAlive)
					continue;
				legal.Add(new CombatTargetRef(ECombatSide.Enemy, i));
			}
		}

		if (side is ETargetSide.Enemy or ETargetSide.Any)
		{
			if (!simulation.PlayerTeam.IsDefeated)
				legal.Add(CombatTargetRef.PlayerTeam);
		}

		return legal;
	}

	private static IReadOnlyList<CombatTargetRef> ResolveCardTargets(
		CombatSimulation simulation,
		QueuedCardEntry entry,
		CardDto card)
	{
		if (entry.Targets.Count == 0)
			return [];

		var validTargets = entry.Targets
			.Where(target => IsValidTarget(simulation, card, entry.CharacterIndex, target))
			.ToList();
		if (validTargets.Count == entry.Targets.Count)
			return validTargets;
		if (!IsSingleTargetCard(card))
			return [];
		if (validTargets.Count > 0)
			return [validTargets[0]];

		var retargeted = TryRetargetSingleTarget(simulation, card, entry.CharacterIndex);
		return retargeted.HasValue ? [retargeted.Value] : [];
	}

	private static EffectRefDto MergeEffectRefParams(EffectRefDto effectRef, Dictionary<string, object>? skillParams)
	{
		if (skillParams is null || skillParams.Count == 0)
			return effectRef;
		if (effectRef.Params is null || effectRef.Params.Count == 0)
		{
			return new EffectRefDto
			{
				EffectId = effectRef.EffectId,
				Params = new Dictionary<string, object>(skillParams, StringComparer.Ordinal),
			};
		}

		var merged = new Dictionary<string, object>(effectRef.Params, StringComparer.Ordinal);
		foreach (var (key, value) in skillParams)
			merged[key] = value;
		return new EffectRefDto
		{
			EffectId = effectRef.EffectId,
			Params = merged,
		};
	}

	private static SkillActionRefDto MergeActionRefParams(SkillActionRefDto actionRef, Dictionary<string, object>? skillParams)
	{
		if (skillParams is null || skillParams.Count == 0)
			return actionRef;
		if (actionRef.Params is null || actionRef.Params.Count == 0)
		{
			return new SkillActionRefDto
			{
				ActionId = actionRef.ActionId,
				Params = new Dictionary<string, object>(skillParams, StringComparer.Ordinal),
			};
		}

		var merged = new Dictionary<string, object>(actionRef.Params, StringComparer.Ordinal);
		foreach (var (key, value) in skillParams)
			merged[key] = value;
		return new SkillActionRefDto
		{
			ActionId = actionRef.ActionId,
			Params = merged,
		};
	}

	private static bool TryGetCharacter(
		CombatSimulation simulation,
		int characterIndex,
		out CharacterBattleInstance character,
		out string error)
	{
		var characters = simulation.PlayerTeam.Characters;
		if (characterIndex < 0 || characterIndex >= characters.Count)
		{
			character = null!;
			error = "角色索引无效。";
			return false;
		}

		character = characters[characterIndex];
		error = string.Empty;
		return true;
	}

	private static bool IsSingleTargetCard(CardDto card) =>
		card.TargetScope is ETargetScope.Single or ETargetScope.Self || card.TargetCount <= 1;

	private static CombatTargetRef? TryRetargetSingleTarget(CombatSimulation simulation, CardDto card, int sourceCharacterIndex)
	{
		if (card.RetargetPolicy == ERetargetPolicy.Skip)
			return null;

		var legal = CollectLegalTargets(simulation, card.TargetSide, sourceCharacterIndex);
		if (legal.Count == 0)
			return null;

		return card.RetargetPolicy switch
		{
			ERetargetPolicy.HighestHp => legal.MaxBy(target => GetTargetHp(simulation, target)),
			ERetargetPolicy.LowestHp => legal.MinBy(target => GetTargetHp(simulation, target)),
			_ => legal[0],
		};
	}

	private static List<CombatTargetRef> CollectLegalTargets(
		CombatSimulation simulation,
		ETargetSide side,
		int sourceCharacterIndex)
	{
		var legal = new List<CombatTargetRef>();
		if (side is ETargetSide.Self or ETargetSide.Ally or ETargetSide.Any)
		{
			for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
			{
				if (side == ETargetSide.Self && i != sourceCharacterIndex)
					continue;
				legal.Add(new CombatTargetRef(ECombatSide.Player, i));
			}
		}

		if (side is ETargetSide.Enemy or ETargetSide.Any)
		{
			for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
			{
				if (!simulation.EnemyTeam.Enemies[i].IsAlive)
					continue;
				legal.Add(new CombatTargetRef(ECombatSide.Enemy, i));
			}
		}

		return legal;
	}

	private static bool IsValidTarget(
		CombatSimulation simulation,
		CardDto card,
		int sourceCharacterIndex,
		CombatTargetRef target)
	{
		return card.TargetSide switch
		{
			ETargetSide.Self => target.Side == ECombatSide.Player && target.Index == sourceCharacterIndex,
			ETargetSide.Ally => target.Side == ECombatSide.Player &&
				target.Index >= 0 &&
				target.Index < simulation.PlayerTeam.Characters.Count,
			ETargetSide.Enemy => target.Side == ECombatSide.Enemy &&
				target.Index >= 0 &&
				target.Index < simulation.EnemyTeam.Enemies.Count &&
				simulation.EnemyTeam.Enemies[target.Index].IsAlive,
			ETargetSide.Any => IsValidAnyTarget(simulation, target),
			_ => false,
		};
	}

	private static bool IsValidAnyTarget(CombatSimulation simulation, CombatTargetRef target)
	{
		if (target.Side == ECombatSide.Player)
		{
			return target.Index >= 0 &&
				target.Index < simulation.PlayerTeam.Characters.Count;
		}

		return target.Side == ECombatSide.Enemy &&
			target.Index >= 0 &&
			target.Index < simulation.EnemyTeam.Enemies.Count &&
			simulation.EnemyTeam.Enemies[target.Index].IsAlive;
	}

	private static int GetTargetHp(CombatSimulation simulation, CombatTargetRef target)
	{
		if (target.Side == ECombatSide.Enemy)
			return simulation.EnemyTeam.Enemies[target.Index].CurrentHp;
		return simulation.PlayerTeam.SharedHp;
	}
}
