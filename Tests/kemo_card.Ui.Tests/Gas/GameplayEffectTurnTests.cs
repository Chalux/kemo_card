using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class GameplayEffectTurnTests
{
	[Test]
	public void HasDuration_expires_after_two_turn_ends()
	{
		var asc = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
		var def = new GameplayEffectDefDto
		{
			Id = "ge.duration.turn",
			DurationPolicy = EDurationPolicy.HasDuration,
			DurationTurns = 2,
			StackingPolicy = EStackingPolicy.None,
			MaxStacks = 1,
			Modifiers =
			[
				new AttributeModifierDefDto
				{
					AttributeId = AttributeIds.PhysicalAttack,
					Operation = EAttributeModifierOp.Add,
					Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 5f },
				},
			],
		};
		var apply = asc.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: asc));

		Assert.That(apply.Success, Is.True);
		Assert.That(asc.ActiveEffects, Has.Count.EqualTo(1));
		Assert.That(asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(15f));

		asc.OnTurnEnd();

		Assert.That(asc.ActiveEffects, Has.Count.EqualTo(1));
		Assert.That(asc.ActiveEffects[0].RemainingTurns, Is.EqualTo(1));

		asc.OnTurnEnd();

		Assert.That(asc.ActiveEffects, Is.Empty);
		Assert.That(asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10f));
	}

	[Test]
	public void Periodic_fires_every_n_turns()
	{
		var asc = GasTestHelper.CreateAscWithAttributes((AttributeIds.MaxHealth, 100f));
		var def = new GameplayEffectDefDto
		{
			Id = "ge.periodic.test",
			DurationPolicy = EDurationPolicy.Infinite,
			PeriodTurns = 2,
			StackingPolicy = EStackingPolicy.None,
			MaxStacks = 1,
		};
		asc.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: asc));

		var periodicCount = 0;
		asc.OnPeriodicTriggered += _ => periodicCount++;

		asc.OnTurnStart();
		Assert.That(periodicCount, Is.EqualTo(0));

		asc.OnTurnStart();
		Assert.That(periodicCount, Is.EqualTo(1));

		asc.OnTurnStart();
		Assert.That(periodicCount, Is.EqualTo(1));

		asc.OnTurnStart();
		Assert.That(periodicCount, Is.EqualTo(2));
	}

	[Test]
	public void OnTurnStart_fires_turn_start_hooks_via_dispatcher()
	{
		var asc = GasTestHelper.CreateAscWithAttributes((AttributeIds.MaxHealth, 100f));
		var def = new GameplayEffectDefDto
		{
			Id = "ge.hooks.turn_start",
			DurationPolicy = EDurationPolicy.Infinite,
			StackingPolicy = EStackingPolicy.None,
			MaxStacks = 1,
			Hooks = new GameplayEffectHooksDto
			{
				OnTurnStart = [new SkillActionRefDto { ActionId = "tick_damage" }],
			},
		};
		asc.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: asc));

		var dispatcher = new RecordingHookDispatcher();
		asc.HookDispatcher = dispatcher;

		asc.OnTurnStart();

		Assert.That(dispatcher.TurnStartCount, Is.EqualTo(1));
		Assert.That(dispatcher.LastTurnStartActionId, Is.EqualTo("tick_damage"));
	}

	private sealed class RecordingHookDispatcher : IGameplayEffectHookDispatcher
	{
		public int TurnStartCount { get; private set; }
		public string? LastTurnStartActionId { get; private set; }

		public void DispatchTurnStart(ActiveGameplayEffect effect, IReadOnlyList<SkillActionRefDto> actions)
		{
			TurnStartCount++;
			LastTurnStartActionId = actions.Count > 0 ? actions[0].ActionId : null;
		}

		public void DispatchTurnEnd(ActiveGameplayEffect _, IReadOnlyList<SkillActionRefDto> __) { }

		public void DispatchRemove(ActiveGameplayEffect _, IReadOnlyList<SkillActionRefDto> __) { }
	}
}
