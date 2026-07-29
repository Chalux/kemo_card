using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Gas;

[TestFixture]
public sealed class GameplayEffectTagTests
{
    [Test]
    public void Application_blocked_when_target_has_blocked_tag()
    {
        var target = new AbilitySystemComponent();
        target.Tags.AddTag("state.invulnerable");
        var def = new GameplayEffectDefDto
        {
            Id = "ge.blocked",
            ApplicationBlockedTags = ["state.invulnerable"],
            DurationPolicy = EDurationPolicy.Instant,
            Modifiers =
            [
                new AttributeModifierDefDto
                {
                    AttributeId = AttributeIds.MaxHealth,
                    Operation = EAttributeModifierOp.Add,
                    Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 5f },
                },
            ],
        };
        target.Attributes.InitAttribute(AttributeIds.MaxHealth, 10f);

        var result = target.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: target));

        Assert.That(result.Success, Is.False);
        Assert.That(target.GetBaseValue(AttributeIds.MaxHealth), Is.EqualTo(10f));
    }

    [Test]
    public void Application_requires_all_required_tags()
    {
        var target = new AbilitySystemComponent();
        target.Tags.AddTag("state.combat");
        target.Attributes.InitAttribute(AttributeIds.MaxHealth, 10f);
        var def = new GameplayEffectDefDto
        {
            Id = "ge.requires_tags",
            ApplicationRequiredTags = ["state.combat", "state.player_turn"],
            DurationPolicy = EDurationPolicy.Instant,
            Modifiers =
            [
                new AttributeModifierDefDto
                {
                    AttributeId = AttributeIds.MaxHealth,
                    Operation = EAttributeModifierOp.Add,
                    Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 5f },
                },
            ],
        };

        var blocked = target.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: target));
        Assert.That(blocked.Success, Is.False);

        target.Tags.AddTag("state.player_turn");
        var allowed = target.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: target));
        Assert.That(allowed.Success, Is.True);
        Assert.That(target.GetBaseValue(AttributeIds.MaxHealth), Is.EqualTo(15f));
    }

    [Test]
    public void Immunity_prevents_application()
    {
        var target = new AbilitySystemComponent();
        target.Tags.AddTag("immunity.debuff");
        target.Attributes.InitAttribute(AttributeIds.PhysicalAttack, 10f);
        var def = new GameplayEffectDefDto
        {
            Id = "ge.weak",
            ImmunityTags = ["debuff"],
            DurationPolicy = EDurationPolicy.Infinite,
            GrantedTags = ["debuff.weak"],
            Modifiers =
            [
                new AttributeModifierDefDto
                {
                    AttributeId = AttributeIds.PhysicalAttack,
                    Operation = EAttributeModifierOp.Add,
                    Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = -2f },
                },
            ],
        };

        var result = target.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: target));

        Assert.That(result.Success, Is.False);
        Assert.That(target.ActiveEffects, Is.Empty);
        Assert.That(target.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10f));
    }

    [Test]
    public void RemoveEffectsWithTags_dispels_matching_active_ge()
    {
        var target = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
        var poison = new GameplayEffectDefDto
        {
            Id = "ge.poison",
            DurationPolicy = EDurationPolicy.Infinite,
            GrantedTags = ["debuff.poison"],
            Modifiers =
            [
                new AttributeModifierDefDto
                {
                    AttributeId = AttributeIds.PhysicalAttack,
                    Operation = EAttributeModifierOp.Add,
                    Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = -3f },
                },
            ],
        };
        var poisonApply = target.ApplyGameplayEffect(new GameplayEffectSpec(poison, targetAsc: target));
        Assert.That(poisonApply.Success, Is.True);
        Assert.That(target.ActiveEffects, Has.Count.EqualTo(1));
        Assert.That(target.Tags.HasTag("debuff.poison"), Is.True);

        var cleanse = new GameplayEffectDefDto
        {
            Id = "ge.cleanse",
            DurationPolicy = EDurationPolicy.Instant,
            RemoveEffectsWithTags = ["debuff"],
        };
        var cleanseResult = target.ApplyGameplayEffect(new GameplayEffectSpec(cleanse, targetAsc: target));

        Assert.That(cleanseResult.Success, Is.True);
        Assert.That(target.ActiveEffects, Is.Empty);
        Assert.That(target.Tags.HasTag("debuff.poison"), Is.False);
        Assert.That(target.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10f));
    }

    [Test]
    public void Active_ge_grants_tags_via_refresh()
    {
        var target = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
        var def = new GameplayEffectDefDto
        {
            Id = "ge.weak",
            DurationPolicy = EDurationPolicy.Infinite,
            GrantedTags = ["debuff.weak"],
            Modifiers =
            [
                new AttributeModifierDefDto
                {
                    AttributeId = AttributeIds.PhysicalAttack,
                    Operation = EAttributeModifierOp.Add,
                    Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = -1f },
                },
            ],
        };

        target.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: target));

        Assert.That(target.Tags.HasTag("debuff.weak"), Is.True);
        Assert.That(target.Tags.HasTag("debuff"), Is.True);
    }

    [Test]
    public void RemoveActiveEffect_clears_granted_tags()
    {
        var target = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
        var def = new GameplayEffectDefDto
        {
            Id = "ge.weak",
            DurationPolicy = EDurationPolicy.Infinite,
            GrantedTags = ["debuff.weak"],
        };
        var apply = target.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: target));
        Assert.That(target.Tags.HasTag("debuff.weak"), Is.True);

        target.RemoveActiveEffect(apply.Handle!.Value);

        Assert.That(target.Tags.HasTag("debuff.weak"), Is.False);
    }

    [Test]
    public void OngoingRequiredTags_suspend_and_resume_on_turn_start()
    {
        var target = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
        var def = new GameplayEffectDefDto
        {
            Id = "ge.conditional_buff",
            DurationPolicy = EDurationPolicy.Infinite,
            OngoingRequiredTags = ["state.combat"],
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
        target.Tags.AddTag("state.combat");
        var apply = target.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: target));
        Assert.That(apply.Success, Is.True);
        Assert.That(target.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(15f));

        target.Tags.RemoveTag("state.combat");
        target.OnTurnStart();
        Assert.That(target.ActiveEffects[0].IsSuspended, Is.True);
        Assert.That(target.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10f));

        target.Tags.AddTag("state.combat");
        target.OnTurnStart();
        Assert.That(target.ActiveEffects[0].IsSuspended, Is.False);
        Assert.That(target.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(15f));
    }

    [Test]
    public void Suspended_ge_cascades_tag_requirements_within_same_turn_start()
    {
        var target = GasTestHelper.CreateAscWithAttributes(
            (AttributeIds.PhysicalAttack, 10f),
            (AttributeIds.MaxHealth, 20f));
        var provider = new GameplayEffectDefDto
        {
            Id = "ge.provider",
            DurationPolicy = EDurationPolicy.Infinite,
            OngoingRequiredTags = ["state.combat"],
            GrantedTags = ["buff.provider"],
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
        var dependent = new GameplayEffectDefDto
        {
            Id = "ge.dependent",
            DurationPolicy = EDurationPolicy.Infinite,
            OngoingRequiredTags = ["buff.provider"],
            Modifiers =
            [
                new AttributeModifierDefDto
                {
                    AttributeId = AttributeIds.MaxHealth,
                    Operation = EAttributeModifierOp.Add,
                    Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = 10f },
                },
            ],
        };
        target.Tags.AddTag("state.combat");
        target.ApplyGameplayEffect(new GameplayEffectSpec(provider, targetAsc: target));
        target.ApplyGameplayEffect(new GameplayEffectSpec(dependent, targetAsc: target));
        Assert.That(target.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(15f));
        Assert.That(target.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(30f));

        target.Tags.RemoveTag("state.combat");
        target.OnTurnStart();

        Assert.That(target.ActiveEffects[0].IsSuspended, Is.True);
        Assert.That(target.ActiveEffects[1].IsSuspended, Is.True);
        Assert.That(target.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(10f));
        Assert.That(target.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(20f));
        Assert.That(target.Tags.HasTag("buff.provider"), Is.False);
    }

    [Test]
    public void Suspended_ge_does_not_grant_tags()
    {
        var target = GasTestHelper.CreateAscWithAttributes((AttributeIds.PhysicalAttack, 10f));
        var def = new GameplayEffectDefDto
        {
            Id = "ge.conditional_tag",
            DurationPolicy = EDurationPolicy.Infinite,
            OngoingRequiredTags = ["state.combat"],
            GrantedTags = ["buff.conditional"],
        };
        target.Tags.AddTag("state.combat");
        target.ApplyGameplayEffect(new GameplayEffectSpec(def, targetAsc: target));
        Assert.That(target.Tags.HasTag("buff.conditional"), Is.True);

        target.Tags.RemoveTag("state.combat");
        target.OnTurnStart();

        Assert.That(target.Tags.HasTag("buff.conditional"), Is.False);
    }
}