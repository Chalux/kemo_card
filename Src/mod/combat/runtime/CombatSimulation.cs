using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Ai;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Combat.Gas;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class CombatSimulation : IDisposable
{
	private readonly CombatStateMachine _stateMachine;
	private readonly TeamMaxHealthCoordinator _teamMaxHealthCoordinator;
	private long _nextQueueSequence = 1;

	public PlayerTeamState PlayerTeam { get; }
	public EnemyTeamState EnemyTeam { get; }
	public CombatRuleEngine Rules { get; }
	public GameDefinitionRegistry Definitions { get; }
	public CardExecutionQueue CardQueue { get; }
	public CombatEffectExecutor EffectExecutor { get; }
	public TeamDomainManager DomainManager { get; }
	public EnemyAiController EnemyAi { get; }
	public IContentEffectScriptHost ScriptHost { get; }
	public string ModId { get; }
	public BattleDto? Battle { get; }
	public int CurrentWaveIndex { get; private set; }
	public int WaveCount => Battle?.Waves.Count ?? 1;
	public bool HasMoreWaves => CurrentWaveIndex + 1 < WaveCount;
	public int TurnNumber { get; private set; } = 1;
	public int RunSeed { get; }
	public ECombatPhase Phase => _stateMachine.Phase;

	internal HostRng EnemyAiRng { get; }

	public CombatSimulation(
		PlayerTeamState playerTeam,
		EnemyTeamState enemyTeam,
		CombatRuleEngine rules,
		GameDefinitionRegistry definitions,
		ECombatPhase initialPhase = ECombatPhase.BattleStart,
		int runSeed = 1,
		BattleDto? battle = null,
		int currentWaveIndex = 0,
		EnemyAiScriptInvoker? enemyAiScriptInvoker = null,
		IContentEffectScriptHost? scriptHost = null,
		string modId = "test.mod")
	{
		ArgumentNullException.ThrowIfNull(playerTeam);
		ArgumentNullException.ThrowIfNull(enemyTeam);
		ArgumentNullException.ThrowIfNull(rules);
		ArgumentNullException.ThrowIfNull(definitions);
		ArgumentException.ThrowIfNullOrWhiteSpace(modId);
		PlayerTeam = playerTeam;
		EnemyTeam = enemyTeam;
		Rules = rules;
		Definitions = definitions;
		Battle = battle;
		CurrentWaveIndex = currentWaveIndex;
		RunSeed = runSeed;
		ScriptHost = scriptHost ?? new NullContentEffectScriptHost();
		ModId = modId;
		CardQueue = new CardExecutionQueue(new HostRng(runSeed, "combat.queue"));
		EnemyAiRng = new HostRng(runSeed, "combat.ai");
		EffectExecutor = new CombatEffectExecutor(definitions, rules);
		DomainManager = new TeamDomainManager(this);
		EnemyAi = new EnemyAiController(definitions, enemyAiScriptInvoker, modId, runSeed);
		_teamMaxHealthCoordinator = new TeamMaxHealthCoordinator(PlayerTeam);
		_stateMachine = new CombatStateMachine(initialPhase);
	}

	public CombatApplyResult TryApply(ICombatCommand command)
	{
		ArgumentNullException.ThrowIfNull(command);
		return _stateMachine.TryApply(this, command);
	}

	public CombatContext CreateContext() => new(this, TurnNumber);

	public void TransitionTo(ECombatPhase phase) => _stateMachine.TransitionTo(phase);

	public void AdvancePhase() => _stateMachine.Advance(this);

	internal long AllocateQueueSequence() => _nextQueueSequence++;

	internal void IncrementTurnNumber() => TurnNumber++;

	public void CheckEndConditions()
	{
		if (Phase is ECombatPhase.Victory or ECombatPhase.Defeat)
			return;

		var decision = new EndDecision { Kind = EEndDecisionKind.None };
		Rules.DispatchCheckEndCondition(CreateContext(), ref decision);

		if (decision.Kind == EEndDecisionKind.Defeat)
		{
			TransitionTo(ECombatPhase.Defeat);
			return;
		}

		if (decision.Kind == EEndDecisionKind.Victory)
		{
			TransitionTo(ECombatPhase.Victory);
			return;
		}

		if (PlayerTeam.IsDefeated)
		{
			TransitionTo(ECombatPhase.Defeat);
			return;
		}

		if (!EnemyTeam.AllDefeated)
			return;

		if (HasMoreWaves)
		{
			AdvanceToNextWave();
			return;
		}

		TransitionTo(ECombatPhase.Victory);
	}

	private void AdvanceToNextWave()
	{
		if (Battle is null)
			return;

		TransitionTo(ECombatPhase.WaveTransition);
		CurrentWaveIndex++;
		var enemies = CombatSimulationFactory.SpawnWaveEnemies(
			Battle.Waves[CurrentWaveIndex],
			Definitions,
			out var error);
		if (enemies is null)
			throw new InvalidOperationException(error ?? "下一波敌人生成失败。");

		EnemyTeam.ReplaceEnemies(enemies);
		TransitionTo(ECombatPhase.Player);
	}

	public void Dispose()
	{
		_teamMaxHealthCoordinator.Dispose();
	}
}
