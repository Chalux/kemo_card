using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class ActiveGameplayEffectTests
{
    [Test]
    public void PeriodTurns_does_not_trigger_on_first_turn()
    {
        var def = new GameplayEffectDefDto
        {
            Id = "ge.periodic.three",
            DurationPolicy = EDurationPolicy.Infinite,
            PeriodTurns = 3,
            StackingPolicy = EStackingPolicy.None,
            MaxStacks = 1,
        };
        var effect = new ActiveGameplayEffect(new GameplayEffectSpec(def));

        Assert.That(effect.OnTurnStart(), Is.False);
        Assert.That(effect.OnTurnStart(), Is.False);
        Assert.That(effect.OnTurnStart(), Is.True);
        Assert.That(effect.OnTurnStart(), Is.False);
        Assert.That(effect.OnTurnStart(), Is.False);
        Assert.That(effect.OnTurnStart(), Is.True);
    }

    [Test]
    public void HasDuration_effect_decrements_until_expired()
    {
        var def = new GameplayEffectDefDto
        {
            Id = "ge.duration.test",
            DurationPolicy = EDurationPolicy.HasDuration,
            DurationTurns = 2,
            StackingPolicy = EStackingPolicy.None,
            MaxStacks = 1,
        };
        var spec = new GameplayEffectSpec(def);
        var effect = new ActiveGameplayEffect(spec);

        Assert.That(effect.RemainingTurns, Is.EqualTo(2));
        Assert.That(effect.IsExpired, Is.False);

        effect.OnTurnEnd();

        Assert.That(effect.RemainingTurns, Is.EqualTo(1));
        Assert.That(effect.IsExpired, Is.False);

        effect.OnTurnEnd();

        Assert.That(effect.RemainingTurns, Is.EqualTo(0));
        Assert.That(effect.IsExpired, Is.True);
    }

    [Test]
    public void AddStack_increases_stacks_up_to_max_and_refreshes_duration()
    {
        var def = new GameplayEffectDefDto
        {
            Id = "ge.stack.test",
            DurationPolicy = EDurationPolicy.HasDuration,
            DurationTurns = 3,
            StackingPolicy = EStackingPolicy.AggregateByTarget,
            MaxStacks = 2,
        };
        var spec = new GameplayEffectSpec(def);
        var effect = new ActiveGameplayEffect(spec);

        effect.OnTurnEnd();
        var changed = effect.AddStack();

        Assert.That(changed, Is.True);
        Assert.That(effect.Stacks, Is.EqualTo(2));
        Assert.That(effect.RemainingTurns, Is.EqualTo(3));
        Assert.That(effect.AddStack(), Is.False);
        Assert.That(effect.Stacks, Is.EqualTo(2));
    }

    [Test]
    public void Zero_duration_effect_starts_expired_not_suspended()
    {
        var def = new GameplayEffectDefDto
        {
            Id = "ge.zero.duration",
            DurationPolicy = EDurationPolicy.HasDuration,
            DurationTurns = 0,
            OngoingRequiredTags = ["state.combat"],
            StackingPolicy = EStackingPolicy.None,
            MaxStacks = 1,
        };
        var effect = new ActiveGameplayEffect(new GameplayEffectSpec(def));

        Assert.That(effect.RemainingTurns, Is.EqualTo(0));
        Assert.That(effect.IsExpired, Is.True);
        Assert.That(effect.IsSuspended, Is.False);
    }
}