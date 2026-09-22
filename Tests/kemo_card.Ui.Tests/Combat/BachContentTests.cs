using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 巴赫（2026-09-21 出货内容）的元数据与六条被动。本轮为它新增的能力：
/// 球伤害倍率（<c>OrbDamageScale</c> / <c>GreenOrbDamageScale</c>）、队伍人数条件（<c>partyMinCount</c>）、
/// <c>onOrbTriggered</c> + <c>oncePerTurn</c>、<c>onCardExecutionEnd</c> + <c>GainOrbPerPlayedCard</c>、
/// 中毒免疫（<c>trait.immune_poison</c>）。
/// </summary>
[TestFixture]
public sealed class BachContentTests
{
    private const string GreenOrb = "orb.test_green";
    private const string BlueOrb = "orb.test_blue";

    /// <summary>出货内容里的绿属性球 id（Bach 被动发的就是它）。</summary>
    private const string ShippedGreenOrb = "green";

    private const string GreenDeckCard = "card.test_elemental";
    private const string RedDeckCard = "card.test_red";

    #region 元数据与接线

    [Test]
    public void Character_definition_matches_the_design()
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Characters.TryGetValue("bach", out var bach), Is.True, "巴赫未随内容出货");

        Assert.That(bach!.Element, Is.EqualTo(EElement.Green));
        Assert.That(bach.Role, Is.EqualTo(ERole.Elementist));
        Assert.That(bach.Race, Is.EqualTo(ERace.Human | ERace.God));
        Assert.That(bach.MaxEnergy, Is.EqualTo(8));
        Assert.That(bach.InitialEnergy, Is.EqualTo(3));

        // 两档蓄力：天国神启 CD6 → 充能II CD4。
        Assert.That(bach.ActiveSkillChain, Has.Count.EqualTo(2));
        Assert.That(bach.ActiveSkillChain[0].SkillId, Is.EqualTo("bach_heavenly_revelation"));
        Assert.That(bach.ActiveSkillChain[0].Cooldown, Is.EqualTo(6));
        Assert.That(bach.ActiveSkillChain[1].SkillId, Is.EqualTo("bach_charge_two"));
        Assert.That(bach.ActiveSkillChain[1].Cooldown, Is.EqualTo(4));

        Assert.That(
            bach.Passives.Select(passive => passive.RequiredPotential),
            Is.EqualTo(new[] { 0, 10, 30, 50, 70, 99 }));
        foreach (var passive in bach.Passives)
            Assert.That(definitions.Buffs.ContainsKey(passive.BuffId), Is.True, $"被动 {passive.BuffId} 不存在");
    }

    [Test]
    public void Active_skill_tiers_grant_the_declared_orbs()
    {
        var definitions = BaseGameContent.Load();
        var tier1 = definitions.Skills["bach_heavenly_revelation"];
        var tier2 = definitions.Skills["bach_charge_two"];

        Assert.That(tier1.ActionRefs, Has.Count.EqualTo(4), "天国神启：四色球各 1");
        Assert.That(tier2.ActionRefs, Has.Count.EqualTo(7), "充能II：六种球各 1 + 自身增幅");

        foreach (var actionRef in tier1.ActionRefs.Concat(tier2.ActionRefs))
            Assert.That(definitions.SkillActions.ContainsKey(actionRef.ActionId), Is.True, $"{actionRef.ActionId} 不存在");
    }

    #endregion

    #region 球伤害倍率

    [Test]
    public void Orb_damage_scale_boosts_every_orb()
    {
        using var sim = Build();
        Assert.That(GreenOrbDamage(sim), Is.EqualTo(30f).Within(0.001f), "基线：3 × 10");

        sim.Buffs.Apply(sim, Player(0), "bach_orb_amp");

        Assert.That(GreenOrbDamage(sim), Is.EqualTo(37.5f).Within(0.001f), "× (1 + 0.25)");
    }

    [Test]
    public void Green_orb_scale_only_affects_green_orbs()
    {
        using var sim = Build();
        sim.Buffs.Apply(sim, Player(0), "bach_passive_p4");

        Assert.That(GreenOrbDamage(sim), Is.EqualTo(45f).Within(0.001f), "绿球 × (1 + 0.5)");
        Assert.That(BlueOrbDamage(sim), Is.EqualTo(30f).Within(0.001f), "蓝球不受绿球专属倍率影响");
    }

    [Test]
    public void Passive_four_sleeps_until_two_green_characters_are_deployed()
    {
        // 只有 1 名绿属性角色：partyMinCount 2 不满足 → buff 休眠、不加成。
        using var sim = Build(greenCharacters: 1);
        sim.Buffs.Apply(sim, Player(0), "bach_passive_p4");

        var buff = sim.PlayerTeam.Characters[0].Buffs.Find("bach_passive_p4");
        Assert.That(buff, Is.Not.Null);
        Assert.That(buff!.IsDormant, Is.True, "人数门闩不满足时必须休眠");
        Assert.That(GreenOrbDamage(sim), Is.EqualTo(30f).Within(0.001f));
    }

    #endregion

    #region 触发后钩子（P2）

    [Test]
    public void Passive_two_grants_two_green_orbs_once_per_turn()
    {
        using var sim = Build();
        sim.Buffs.Apply(sim, Player(0), "bach_passive_p2");
        var caster = sim.PlayerTeam.Characters[0];

        sim.Buffs.FireOrbTriggered(sim, [0]);
        Assert.That(caster.Buffs.Find("bach_passive_p2"), Is.Not.Null);
        Assert.That(sim.Orbs.Queue.CountOf(ShippedGreenOrb), Is.EqualTo(2), "触发后获得 2 个绿球");

        sim.Buffs.FireOrbTriggered(sim, [0]);
        Assert.That(sim.Orbs.Queue.CountOf(ShippedGreenOrb), Is.EqualTo(2), "同回合第 2 次不再给（每回合仅 1 次）");

        sim.Buffs.FireTurnStart(sim);
        sim.Buffs.FireOrbTriggered(sim, [0]);
        Assert.That(sim.Orbs.Queue.CountOf(ShippedGreenOrb), Is.EqualTo(4), "新回合重新可用");
    }

    #endregion

    #region 卡牌执行结束钩子（P5）

    [Test]
    public void Passive_five_grants_played_cards_minus_one_orbs()
    {
        using var sim = Build();
        var registry = sim.Definitions;
        sim.Buffs.Apply(sim, Player(0), "bach_passive_p5");

        for (var i = 0; i < 3; i++)
            sim.RecordPlayedCard(YellowCard(sim), 0);

        sim.Buffs.FireCardExecutionEnd(sim);

        Assert.That(sim.Orbs.Queue.CountOf(ShippedGreenOrb), Is.EqualTo(2), "X=3 → X-1=2 个绿球");
        Assert.That(registry.Store.Cards.ContainsKey(YellowCard(sim)), Is.True);
    }

    [Test]
    public void Passive_five_grants_nothing_with_a_single_card()
    {
        using var sim = Build();
        sim.Buffs.Apply(sim, Player(0), "bach_passive_p5");

        sim.RecordPlayedCard(YellowCard(sim), 0);
        sim.Buffs.FireCardExecutionEnd(sim);

        Assert.That(sim.Orbs.Queue.CountOf(ShippedGreenOrb), Is.Zero, "X=1 → 0 个（不外溢为负数）");
    }

    #endregion

    #region 中毒免疫（P1）

    [Test]
    public void Passive_one_blocks_poison_effects()
    {
        var definitions = BaseGameContent.Load();
        var registry = CombatTestHelper.CreateFullRegistry(
            buffs: new Dictionary<string, BuffDto>(StringComparer.Ordinal)
            {
                ["bach_passive_p1"] = definitions.Buffs["bach_passive_p1"],
            },
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto>(StringComparer.Ordinal)
            {
                ["ge.test_poison"] = new()
                {
                    Id = "ge.test_poison",
                    DurationPolicy = EDurationPolicy.HasDuration,
                    DurationTurns = 3,
                    GrantedTags = [CombatConstants.PoisonTag],
                },
            });

        using var withPassive = Build(registry: registry);
        withPassive.Buffs.Apply(withPassive, Player(0), "bach_passive_p1");
        ApplyPoison(withPassive, registry);
        Assert.That(
            withPassive.PlayerTeam.Characters[0].Asc.Tags.HasTag(CombatConstants.PoisonTag),
            Is.False,
            "trait.immune_poison 必须抵消中毒标签");

        using var withoutPassive = Build(registry: registry);
        ApplyPoison(withoutPassive, registry);
        Assert.That(
            withoutPassive.PlayerTeam.Characters[0].Asc.Tags.HasTag(CombatConstants.PoisonTag),
            Is.True,
            "没有被动时中毒照常生效");
    }

    [Test]
    public void Cantata_and_hymn_match_the_design()
    {
        var definitions = BaseGameContent.Load();

        AssertExclusiveCard(definitions, "bach_cantata", cost: 3, priority: 10, maxHealth: 30, magicAttack: 1);
        AssertExclusiveCard(definitions, "bach_hymn", cost: 2, priority: 50, maxHealth: 20, magicAttack: 2);

        var trio = definitions.Cards["bach_trio"];
        Assert.That(trio.Element, Is.EqualTo((int)EElement.Green));
        Assert.That(trio.Cost, Is.EqualTo(3));
        Assert.That(trio.CardType, Is.EqualTo(ECardType.Magical));
        Assert.That(trio.Priority, Is.EqualTo(1));
        Assert.That(trio.Rarity, Is.EqualTo(ERarity.Exclusive));
        Assert.That(trio.Role, Is.EqualTo(ERole.Elementist));
        Assert.That(trio.IsExclusive, Is.True);
        Assert.That(trio.Stats?.Attributes.GetValueOrDefault("MaxHealth"), Is.EqualTo(30));
        Assert.That(trio.Stats?.Attributes.GetValueOrDefault("MagicAttack"), Is.EqualTo(1));
    }

    [Test]
    public void Trio_scales_by_green_orbs_triggered_this_turn()
    {
        // 魔攻 30、目标无魔防：伤害 = 6 + 30 × attackScale（1 + min(3, 本回合已触发绿球数)）。
        Assert.That(TrioDamage(greenOrbsTriggered: 0), Is.EqualTo(6f + 30f).Within(0.001f), "无球：100% 魔攻");
        Assert.That(TrioDamage(greenOrbsTriggered: 1), Is.EqualTo(6f + 60f).Within(0.001f), "+100%");
        Assert.That(TrioDamage(greenOrbsTriggered: 2), Is.EqualTo(6f + 90f).Within(0.001f), "+200%");
        Assert.That(TrioDamage(greenOrbsTriggered: 5), Is.EqualTo(6f + 120f).Within(0.001f), "封顶 +300%");
    }

    [Test]
    public void Trio_counts_orbs_triggered_through_the_real_trigger_path()
    {
        using var sim = Build();

        sim.Orbs.Grant(sim, GreenOrb, producerIndex: 0, count: 3);
        sim.Orbs.TriggerManual(sim);

        Assert.That(sim.CountOrbsTriggeredThisTurn(elementMask: 4), Is.EqualTo(3), "一次触发结算了 3 个绿球");
        Assert.That(ExecuteTrio(sim), Is.EqualTo(6f + 120f).Within(0.001f), "3 球 → +300% 封顶");
    }

    [Test]
    public void Trio_ignores_other_elements_orbs()
    {
        // 只数绿球：触发 3 个蓝球不该提升伤害。
        using var sim = Build();
        sim.Orbs.Grant(sim, BlueOrb, producerIndex: 0, count: 3);
        sim.Orbs.TriggerManual(sim);

        Assert.That(sim.CountOrbsTriggeredThisTurn(elementMask: 4), Is.Zero, "绿球计数为 0");
        Assert.That(ExecuteTrio(sim), Is.EqualTo(36f).Within(0.001f), "仍是 100% 魔攻");
    }

    [Test]
    public void Cantata_grants_orbs_by_green_deck_count()
    {
        // 分档：绿卡 0-3 → 2 个；4-6 → 3 个；≥7 → 4 个。每副卡组额外塞 1 张红卡验证元素掩码生效。
        Assert.That(CantataOrbs(greenDeckCards: 0), Is.EqualTo(2));
        Assert.That(CantataOrbs(greenDeckCards: 3), Is.EqualTo(2), "1 张红卡不计入绿卡数");
        Assert.That(CantataOrbs(greenDeckCards: 4), Is.EqualTo(3));
        Assert.That(CantataOrbs(greenDeckCards: 7), Is.EqualTo(4));
    }

    [Test]
    public void Fugue_attaches_charge_one_to_every_hand_slot()
    {
        using var sim = Build();
        var caster = sim.PlayerTeam.Characters[0];

        sim.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "bach_fugue_attach" },
            sim,
            Player(0),
            [Player(0)]);

        for (var index = 0; index < caster.HandSlots.Count; index++)
        {
            var charge = caster.HandSlots[index].Buffs.Find("bach_fugue_charge");
            Assert.That(charge, Is.Not.Null, $"槽位 {index + 1} 应挂上充能");
            Assert.That(charge!.RemainingTurns, Is.EqualTo(2));
            Assert.That(charge.ChargeCounter, Is.EqualTo(1), "充能I = 计数 1");
        }

        // 打出该槽的一张牌 → 触发载荷（获得 1 个绿球），计数重置。
        var target = caster.HandSlots[0];
        var before = sim.Orbs.Queue.CountOf(ShippedGreenOrb);
        sim.Buffs.FireSlotCardPlayed(sim, 0, target);

        Assert.That(
            sim.Orbs.Queue.CountOf(ShippedGreenOrb),
            Is.EqualTo(before + 1),
            "充能 I 触发时获得 1 个绿属性球");
    }

    [Test]
    public void Fugue_card_matches_the_design()
    {
        var definitions = BaseGameContent.Load();
        var card = definitions.Cards["bach_fugue"];

        Assert.That(card.Element, Is.EqualTo((int)EElement.Green));
        Assert.That(card.Cost, Is.EqualTo(5));
        Assert.That(card.CardType, Is.EqualTo(ECardType.Support));
        Assert.That(card.Priority, Is.EqualTo(10));
        Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("MaxHealth"), Is.EqualTo(40));
        Assert.That(card.IsExclusive, Is.True);
    }

    [Test]
    public void New_charge_overwrites_the_old_one_and_resets_progress()
    {
        using var sim = Build();
        var caster = sim.PlayerTeam.Characters[0];
        var slot = caster.HandSlots[0];

        // 先挂一个"充能II"（计数 2：充能I 是 1）。
        sim.Buffs.ApplyToSlot(
            sim,
            0,
            0,
            "chalux_charge_glacial",
            new Dictionary<string, object>(StringComparer.Ordinal) { ["charge"] = 2 });
        Assert.That(slot.Buffs.Find("chalux_charge_glacial")!.ChargeCounter, Is.EqualTo(2));

        // 打出该槽的牌 → 进度 2 → 1（尚未触发）。
        sim.Buffs.FireSlotCardPlayed(sim, 0, slot);
        Assert.That(slot.Buffs.Find("chalux_charge_glacial")!.ChargeCounter, Is.EqualTo(1), "已积累进度");

        // 新的充能（赋格的充能I）覆盖：旧的一律移除、新的进度从头开始。
        sim.Buffs.ApplyToSlot(sim, 0, 0, "bach_fugue_charge");
        Assert.That(slot.Buffs.Find("chalux_charge_glacial"), Is.Null, "旧充能被覆盖");
        Assert.That(slot.Buffs.Find("bach_fugue_charge")!.ChargeCounter, Is.EqualTo(1), "进度重置");

        // 即使新触发的与老的完全一样（同 id），也要覆盖并重置：不能再叠出第二个实例。
        sim.Buffs.FireSlotCardPlayed(sim, 0, slot);
        Assert.That(slot.Buffs.Find("bach_fugue_charge")!.ChargeCounter, Is.EqualTo(1), "触发后重置计数");
        sim.Buffs.ApplyToSlot(sim, 0, 0, "bach_fugue_charge");
        Assert.That(
            slot.Buffs.All.Count(instance => instance.Def.Id == "bach_fugue_charge"),
            Is.EqualTo(1),
            "同 id 的充能也只保留 1 个实例");
    }

    [Test]
    public void Hymn_grants_two_turns_of_magic_attack()
    {
        using var sim = Build();
        var caster = sim.PlayerTeam.Characters[0];

        sim.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "bach_hymn_magic_up" },
            sim,
            Player(0),
            [Player(0)]);

        var buff = caster.Buffs.Find("bach_hymn_magic_up");
        Assert.That(buff, Is.Not.Null);
        Assert.That(buff!.RemainingTurns, Is.EqualTo(2));
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(6f).Within(0.001f));
    }

    #endregion

    #region 装配

    private static void AssertExclusiveCard(
        KemoCard.Frame.Content.ModDefinitionsBundle definitions,
        string cardId,
        int cost,
        int priority,
        int maxHealth,
        int magicAttack)
    {
        Assert.That(definitions.Cards.TryGetValue(cardId, out var card), Is.True, $"{cardId} 未随内容出货");
        Assert.That(card!.Element, Is.EqualTo((int)EElement.Green));
        Assert.That(card.CostType, Is.EqualTo(ECostType.Energy));
        Assert.That(card.Cost, Is.EqualTo(cost));
        Assert.That(card.CardType, Is.EqualTo(ECardType.Support));
        Assert.That(card.Priority, Is.EqualTo(priority));
        Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
        Assert.That(card.Role, Is.EqualTo(ERole.Elementist));
        Assert.That(card.IsExclusive, Is.True);
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("MaxHealth"), Is.EqualTo(maxHealth));
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("MagicAttack"), Is.EqualTo(magicAttack));
    }

    /// <summary>执行一次康塔塔的效果，返回入队的绿球数。</summary>
    private static int CantataOrbs(int greenDeckCards)
    {
        using var sim = Build(greenDeckCards: greenDeckCards);
        sim.EffectExecutor.ExecuteEffectRef(
            new EffectRefDto { EffectId = "bach_cantata_green_orbs" },
            sim,
            Player(0),
            [Player(0)]);

        return sim.Orbs.Queue.CountOf(ShippedGreenOrb);
    }

    /// <summary>先登记 N 个已触发的绿球，再打出三重奏，返回对敌人造成的伤害。</summary>
    private static float TrioDamage(int greenOrbsTriggered)
    {
        using var sim = Build();
        if (greenOrbsTriggered > 0)
        {
            sim.RecordOrbsTriggered(new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [ShippedGreenOrb] = greenOrbsTriggered,
            });
        }

        return ExecuteTrio(sim);
    }

    /// <summary>用魔攻 30 的角色执行三重奏的伤害动作，返回敌人掉血量。</summary>
    private static float ExecuteTrio(CombatSimulation sim)
    {
        sim.PlayerTeam.Characters[0].Asc.SetBaseValue(AttributeIds.MagicAttack, 30f);
        var before = sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health);
        sim.EffectExecutor.ExecuteSkillActionRef(
            new SkillActionRefDto { ActionId = "bach_trio_strike" },
            sim,
            Player(0),
            [Enemy(0)]);

        return before - sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health);
    }

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    /// <summary>3 个绿球触发一次造成的总伤害（手动触发下限就是 3）。</summary>
    private static float GreenOrbDamage(CombatSimulation simulation) => OrbDamage(simulation, GreenOrb);

    private static float BlueOrbDamage(CombatSimulation simulation) => OrbDamage(simulation, BlueOrb);

    private static float OrbDamage(CombatSimulation simulation, string orbTypeId)
    {
        var before = simulation.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health);
        simulation.Orbs.Grant(simulation, orbTypeId, producerIndex: 0, count: 3);
        simulation.Orbs.TriggerManual(simulation);
        return before - simulation.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health);
    }

    private static string YellowCard(CombatSimulation simulation)
    {
        foreach (var cardId in simulation.Definitions.Store.Cards.Keys)
        {
            if (simulation.Definitions.Store.Cards[cardId].Element != 0)
                return cardId;
        }

        return "card.test";
    }

    private static void ApplyPoison(
        CombatSimulation simulation,
        KemoCard.Frame.Content.GameDefinitionRegistry registry)
    {
        var target = Player(0);
        new KemoCard.Mod.Combat.Effects.GameplayEffectApplicator(registry)
            .ApplyToTargets(simulation, target, [target], "ge.test_poison");
    }

    private static CombatSimulation Build(
        int greenCharacters = 4,
        int greenDeckCards = 0,
        KemoCard.Frame.Content.GameDefinitionRegistry? registry = null)
    {
        registry ??= CreateRegistry();

        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests(
                "bach",
                new Dictionary<string, float>(StringComparer.Ordinal)
                {
                    [AttributeIds.MaxHealth] = 50f,
                    [AttributeIds.PhysicalAttack] = 10f,
                    [AttributeIds.MaxEnergy] = 10f,
                    [AttributeIds.InitialEnergy] = 10f,
                },
                drawPile: index == 0 ? Deck(greenDeckCards) : null,
                element: index < greenCharacters ? EElement.Green : EElement.Red,
                race: index % 2 == 0 ? ERace.Human : ERace.God))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 200),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: 1000)]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260921);
    }

    /// <summary>
    /// 合成战场：出货的 buff / 效果 / 球全量带入（否则被动 buff 会因悬空引用被内容校验剔除），
    /// 再补两个可控的测试球与一张绿卡。
    /// </summary>
    private static KemoCard.Frame.Content.GameDefinitionRegistry CreateRegistry()
    {
        BaseGameContent.RegisterBuiltinConditions();
        var shipped = BaseGameContent.Load();

        var orbs = new Dictionary<string, OrbTypeDto>(shipped.OrbTypes, StringComparer.Ordinal)
        {
            [GreenOrb] = Orb(GreenOrb, EElement.Green),
            [BlueOrb] = Orb(BlueOrb, EElement.Blue),
        };

        return CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>(StringComparer.Ordinal)
            {
                [GreenDeckCard] = ElementCard(GreenDeckCard, EElement.Green),
                [RedDeckCard] = ElementCard(RedDeckCard, EElement.Red),
            },
            buffs: shipped.Buffs,
            effects: shipped.Effects,
            skillActions: shipped.SkillActions,
            gameplayEffects: shipped.GameplayEffects,
            attributes: shipped.Attributes,
            orbs: orbs);
    }

    /// <summary>合成卡：不带 skillRefs，因此不会被内容校验剔除（本用例只关心它的元素）。</summary>
    private static CardDto ElementCard(string id, EElement element) => new()
    {
        Id = id,
        DisplayNameId = id,
        Element = (int)element,
        CardType = ECardType.Physics,
        TargetSide = ETargetSide.Enemy,
        TargetScope = ETargetScope.Single,
        TargetCount = 1,
        Priority = 1,
    };

    /// <summary>卡组：N 张绿卡 + 1 张红卡（红卡用来证明元素掩码确实在筛属性）。</summary>
    private static IReadOnlyList<CardRuntimeEntry> Deck(int greenCards)
    {
        var entries = new List<CardRuntimeEntry>();
        for (var i = 0; i < greenCards; i++)
            entries.Add(new CardRuntimeEntry(GreenDeckCard, $"rt-green-{i}"));
        entries.Add(new CardRuntimeEntry(RedDeckCard, "rt-red-0"));
        return entries;
    }

    private static OrbTypeDto Orb(string id, EElement element) => new()
    {
        Id = id,
        DisplayNameId = id,
        DamageKind = EDamageKind.Elemental,
        Element = element,
        PerOrbAmount = 10f,
        AttackBonusScale = 0f,
        AttackSource = EOrbAttackSource.Higher,
    };

    #endregion
}
