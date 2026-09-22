using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 莱因哈特（2026-09-21 出货内容）的元数据、接线与端到端效果。
/// 六条被动全部由 buff 驱动，其中 P2 走 <c>onCardSettled</c> + <c>CardPlayedThisTurn</c> 条件、
/// P4 走 <c>PartyCountScaled</c> 取值、P6 走"随机手牌槽费用归零"。
/// </summary>
[TestFixture]
public sealed class ReinhardtContentTests
{
    private const string TreasureCard = "reinhardt_black_ship_treasure";
    private const string HowlingCard = "reinhardt_howling_onslaught";
    private const string VoyageCard = "reinhardt_azure_voyage";
    private const string BladeCard = "reinhardt_radiant_blade";

    private const string ExtraAttackBuff = "reinhardt_extra_normal_attack";
    private const string VulnerableBuff = "reinhardt_normal_attack_vulnerable";
    private const string FollowUpBuff = "reinhardt_follow_up_4";
    private const string FreeCostBuff = "reinhardt_free_cost";
    private const string AttackUpBuff = "reinhardt_p2_attack_up";
    private const string YellowCard = "card.test_yellow";
    private const string RedCard = "card.test_red";

    #region 元数据与接线

    [TestCase(TreasureCard, 2, "Support", 50, 20, 2)]
    [TestCase(HowlingCard, 3, "Weak", 30, 30, 1)]
    [TestCase(VoyageCard, 4, "Support", 50, 40, 0)]
    [TestCase(BladeCard, 2, "Physics", 4, 30, 1)]
    public void Exclusive_cards_match_the_design(
        string cardId,
        int cost,
        string cardType,
        int priority,
        int maxHealth,
        int physicalAttack)
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Cards.TryGetValue(cardId, out var card), Is.True, $"{cardId} 未随内容出货");

        Assert.That(card!.Element, Is.EqualTo((int)EElement.Yellow), "四张卡都是黄属性");
        Assert.That(card.CostType, Is.EqualTo(ECostType.Energy));
        Assert.That(card.Cost, Is.EqualTo(cost));
        Assert.That(card.CardType.ToString(), Is.EqualTo(cardType));
        Assert.That(card.Priority, Is.EqualTo(priority));
        Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
        Assert.That(card.Role, Is.EqualTo(ERole.SwordMan), "Role 与角色一致");
        Assert.That(card.IsExclusive, Is.True, "专属卡必须标记 isExclusive");
        Assert.That(card.CardGroupId, Is.Null, "无升级链");
        Assert.That(card.UpgradeTier, Is.Zero);
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("MaxHealth"), Is.EqualTo(maxHealth));
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("PhysicalAttack"), Is.EqualTo(physicalAttack));
        Assert.That(card.SkillRefs, Is.Not.Empty);
    }

    [Test]
    public void Cards_are_in_the_initial_deck_and_their_skill_chains_resolve()
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Characters.TryGetValue("reinhardt", out var reinhardt), Is.True);

        foreach (var cardId in new[] { TreasureCard, HowlingCard, VoyageCard, BladeCard })
        {
            Assert.That(reinhardt!.Cards, Does.Contain(cardId), $"莱因哈特初始卡组缺少 {cardId}");

            Assert.That(definitions.Cards.TryGetValue(cardId, out var card), Is.True);
            foreach (var skillRef in card!.SkillRefs)
            {
                Assert.That(definitions.Skills.TryGetValue(skillRef.SkillId, out var skill), Is.True,
                    $"{cardId} 的技能 {skillRef.SkillId} 不存在");
                Assert.That(skill!.DescId, Is.Not.Empty, $"{cardId} 的技能缺少描述键（卡牌描述即由技能描述拼接）");
            }
        }
    }

    [Test]
    public void Character_definition_matches_the_design()
    {
        var definitions = BaseGameContent.Load();
        var reinhardt = definitions.Characters["reinhardt"];

        Assert.That(reinhardt.Element, Is.EqualTo(EElement.Yellow));
        Assert.That(reinhardt.Role, Is.EqualTo(ERole.SwordMan));
        Assert.That(reinhardt.Race, Is.EqualTo(ERace.Animal));
        Assert.That(reinhardt.MaxEnergy, Is.EqualTo(8));
        Assert.That(reinhardt.InitialEnergy, Is.EqualTo(3));
        Assert.That(reinhardt.ActiveSkillChain, Has.Count.EqualTo(1), "单档主动技");
        Assert.That(reinhardt.ActiveSkillChain[0].SkillId, Is.EqualTo("reinhardt_yellow_tide_command"));
        Assert.That(reinhardt.ActiveSkillChain[0].Cooldown, Is.EqualTo(10));

        Assert.That(
            reinhardt.Passives.Select(passive => passive.RequiredPotential),
            Is.EqualTo(new[] { 0, 10, 30, 50, 70, 99 }));
        foreach (var passive in reinhardt.Passives)
            Assert.That(definitions.Buffs.ContainsKey(passive.BuffId), Is.True, $"被动 {passive.BuffId} 不存在");
    }

    #endregion

    #region 端到端：四张卡

    [Test]
    public void Treasure_grants_an_extra_normal_attack_that_fires_the_same_turn()
    {
        using var sim = BuildPlayerPhase(TreasureCard);

        MarkAndExecute(sim, TreasureCard, [Self()]);

        var caster = sim.PlayerTeam.Characters[0];
        Assert.That(caster.Buffs.Find(ExtraAttackBuff), Is.Not.Null);
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.NormalAttackCount), Is.EqualTo(1f));

        // 普攻在卡牌结算之后执行，因此本回合就打两次：2 × 物攻 10。
        var normalAttack = sim.NormalAttacks.LastResult;
        Assert.That(normalAttack, Is.Not.Null);
        Assert.That(normalAttack!.Executions, Is.EqualTo(2));
        Assert.That(enemy(sim).Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(500f - 20f).Within(0.001f));
    }

    [Test]
    public void Howling_marks_the_target_and_boosts_normal_attack_damage_only()
    {
        using var sim = BuildPlayerPhase(HowlingCard);

        MarkAndExecute(sim, HowlingCard, [Enemy(0)]);

        Assert.That(enemy(sim).Buffs.Find(VulnerableBuff), Is.Not.Null, "弱化必须挂在敌方目标上");
        // 卡牌本身无伤害；随后的普攻 10 × (1 + 0.25) = 12.5。
        Assert.That(enemy(sim).Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(500f - 12.5f).Within(0.001f));
    }

    [Test]
    public void Voyage_grants_four_turns_of_follow_up()
    {
        using var sim = BuildPlayerPhase(VoyageCard);

        MarkAndExecute(sim, VoyageCard, [Self()]);

        var buff = sim.PlayerTeam.Characters[0].Buffs.Find(FollowUpBuff);
        Assert.That(buff, Is.Not.Null);
        Assert.That(buff!.RemainingTurns, Is.EqualTo(4));
    }

    [Test]
    public void Radiant_blade_deals_three_plus_a_quarter_of_attack()
    {
        using var sim = BuildPlayerPhase(BladeCard);

        MarkAndExecute(sim, BladeCard, [Enemy(0)]);

        // 卡牌 3 + 25% × 物攻 10 − 物防 0 = 5.5；随后普攻 10（持有者自己就是归属角色，追打不重复出手）。
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find("reinhardt_follow_up_1"), Is.Not.Null);
        Assert.That(enemy(sim).Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(500f - 5.5f - 10f).Within(0.001f));
    }

    #endregion

    #region 被动

    [Test]
    public void Passive_four_scales_max_health_by_matching_team_members()
    {
        using var sim = BuildPlayerPhase(TreasureCard);

        sim.Buffs.Apply(sim, Player(0), "reinhardt_passive_p4");

        // 4 名黄属性·动物角色（含自己）→ +160，且队伍账本上限同步抬高。
        Assert.That(
            sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.MaxHealth),
            Is.EqualTo(210f).Within(0.001f));
        Assert.That(sim.PlayerTeam.Asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(360f).Within(0.001f));
    }

    [Test]
    public void Passive_five_boosts_normal_attack_damage_by_half()
    {
        using var sim = BuildPlayerPhase(TreasureCard);

        sim.Buffs.Apply(sim, Player(0), "reinhardt_passive_p5");
        sim.NormalAttacks.Execute(sim);

        Assert.That(enemy(sim).Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(500f - 15f).Within(0.001f));
    }

    [Test]
    public void Passive_one_blocks_sealing()
    {
        var definitions = BaseGameContent.Load();
        var registry = CombatTestHelper.CreateFullRegistry(
            buffs: new Dictionary<string, BuffDto>(StringComparer.Ordinal)
            {
                ["reinhardt_passive_p1"] = definitions.Buffs["reinhardt_passive_p1"],
            },
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto>(StringComparer.Ordinal)
            {
                ["ge.test_seal"] = SealGameplayEffect(),
            });

        using var withPassive = BuildMinimal(registry, applyPassive: true);
        ApplySeal(withPassive, registry, Player(0));
        Assert.That(withPassive.PlayerTeam.Characters[0].IsSealed, Is.False, "trait.immune_seal 必须抵消封印");

        using var withoutPassive = BuildMinimal(registry, applyPassive: false);
        ApplySeal(withoutPassive, registry, Player(0));
        Assert.That(withoutPassive.PlayerTeam.Characters[0].IsSealed, Is.True, "没有被动时封印照常生效");
    }

    [Test]
    public void Passive_two_wiring_is_complete()
    {
        BaseGameContent.RegisterBuiltinConditions();
        var definitions = BaseGameContent.Load();

        var passive = definitions.Buffs["reinhardt_passive_p2"];
        Assert.That(passive.Hooks.OnCardSettled, Has.Count.EqualTo(1), "P2 必须挂在 onCardSettled 上");

        var effect = definitions.Effects["reinhardt_p2_boost"];
        Assert.That(effect.Conditions, Has.Count.EqualTo(1));
        Assert.That(effect.Conditions[0].Kind, Is.EqualTo("CardPlayedThisTurn"));
        Assert.That(KemoCard.Frame.Condition.ConditionDomains.Combat.Contains("CardPlayedThisTurn"), Is.True);
    }

    [Test]
    public void Passive_two_needs_two_yellow_cards_this_turn()
    {
        // 条件域必须先注册（复刻 ModFactory.Bootstrap 的顺序），否则 CardPlayedThisTurn 解析不到。
        BaseGameContent.RegisterBuiltinConditions();

        var definitions = BaseGameContent.Load();
        var registry = CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>(StringComparer.Ordinal)
            {
                [YellowCard] = ElementCard(YellowCard, EElement.Yellow),
                [RedCard] = ElementCard(RedCard, EElement.Red),
            },
            buffs: new Dictionary<string, BuffDto>(StringComparer.Ordinal)
            {
                ["reinhardt_passive_p2"] = definitions.Buffs["reinhardt_passive_p2"],
                ["reinhardt_p2_attack_up"] = definitions.Buffs["reinhardt_p2_attack_up"],
            },
            effects: new Dictionary<string, EffectDto>(StringComparer.Ordinal)
            {
                ["reinhardt_p2_boost"] = definitions.Effects["reinhardt_p2_boost"],
            });

        Assert.That(registry.Store.Cards.ContainsKey(YellowCard), Is.True, "合成卡必须留下（无悬空引用）");

        using var oneCard = BuildMinimal(registry, applyPassive: true, passiveId: "reinhardt_passive_p2");
        oneCard.RecordPlayedCard(YellowCard, 0);
        oneCard.Buffs.FireCardSettled(oneCard, 0);
        Assert.That(oneCard.PlayerTeam.Characters[0].Buffs.Find(AttackUpBuff), Is.Null, "1 张不触发");

        using var wrongElement = BuildMinimal(registry, applyPassive: true, passiveId: "reinhardt_passive_p2");
        wrongElement.RecordPlayedCard(RedCard, 0);
        wrongElement.RecordPlayedCard(RedCard, 0);
        wrongElement.Buffs.FireCardSettled(wrongElement, 0);
        Assert.That(wrongElement.PlayerTeam.Characters[0].Buffs.Find(AttackUpBuff), Is.Null, "非黄卡不计入");

        using var twoCards = BuildMinimal(registry, applyPassive: true, passiveId: "reinhardt_passive_p2");
        twoCards.RecordPlayedCard(YellowCard, 0);
        twoCards.RecordPlayedCard(YellowCard, 0);
        twoCards.Buffs.FireCardSettled(twoCards, 0);
        Assert.That(twoCards.PlayerTeam.Characters[0].Buffs.Find(AttackUpBuff), Is.Not.Null, "2 张触发");
    }

    [Test]
    public void Passive_six_zeroes_the_cost_of_a_random_hand_slot()
    {
        using var sim = BuildPlayerPhase(TreasureCard);
        var caster = sim.PlayerTeam.Characters[0];

        sim.Buffs.Apply(sim, Player(0), "reinhardt_passive_p6");
        sim.Buffs.FireActiveSkillCast(sim, 0);

        var slot = caster.HandSlots[0];
        Assert.That(slot.Buffs.Find(FreeCostBuff), Is.Not.Null, "手牌槽应挂上零费 buff");
        Assert.That(slot.CardId, Is.EqualTo(TreasureCard));

        var card = BaseGameContent.Load().Cards[TreasureCard];
        Assert.That(CardCostCalculator.Compute(sim, 0, card, slot.RuntimeInstanceId), Is.Zero, "该槽当前费用为 0");
        Assert.That(CardCostCalculator.Compute(sim, 0, card), Is.EqualTo(card.Cost), "不带槽位标识时按原价");
    }

    #endregion

    #region 装配

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

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

    private static CombatTargetRef Self() => Player(0);

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    private static EnemyUnit enemy(CombatSimulation simulation) => simulation.EnemyTeam.Enemies[0];

    private static GameplayEffectDefDto SealGameplayEffect() => new()
    {
        Id = "ge.test_seal",
        DurationPolicy = EDurationPolicy.HasDuration,
        DurationTurns = 1,
        GrantedTags = [CombatConstants.SealedTag],
    };

    /// <summary>用内容里的 GE 通道施加封印（效果体系没有 ApplyGameplayEffect，只有技能动作有）。</summary>
    private static void ApplySeal(
        CombatSimulation simulation,
        KemoCard.Frame.Content.GameDefinitionRegistry registry,
        CombatTargetRef target) =>
        new KemoCard.Mod.Combat.Effects.GameplayEffectApplicator(registry)
            .ApplyToTargets(simulation, target, [target], "ge.test_seal");

    private static IReadOnlyDictionary<string, float> CharacterAttributes() =>
        new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.PhysicalAttack] = 10f,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };

    /// <summary>4 名"黄属性·动物"角色（含自身会命中 P4 的筛选），手上各抓一张指定卡。</summary>
    private static CombatSimulation BuildPlayerPhase(string handCardId, int enemyHp = 500)
    {
        var registry = BaseGameContent.BuildRegistry();
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests(
                "reinhardt",
                CharacterAttributes(),
                drawPile: [new CardRuntimeEntry(handCardId, $"rt-{handCardId}-{index}")],
                element: EElement.Yellow,
                race: ERace.Animal))
            .ToArray();
        foreach (var character in characters)
        {
            character.DrawCards(1);
            character.RefillAvailableEnergy();
        }

        return NewSimulation(registry, characters, enemyHp);
    }

    /// <summary>最小战场：只用于验证被动 buff 本身（不带手牌 / 能量）。</summary>
    private static CombatSimulation BuildMinimal(
        KemoCard.Frame.Content.GameDefinitionRegistry registry,
        bool applyPassive,
        string passiveId = "reinhardt_passive_p1")
    {
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests(
                "reinhardt",
                CharacterAttributes(),
                element: EElement.Yellow,
                race: ERace.Animal))
            .ToArray();

        var sim = NewSimulation(registry, characters, enemyHp: 500);
        if (applyPassive)
            sim.Buffs.Apply(sim, Player(0), passiveId);

        return sim;
    }

    private static CombatSimulation NewSimulation(
        KemoCard.Frame.Content.GameDefinitionRegistry registry,
        CharacterBattleInstance[] characters,
        int enemyHp) =>
        new(
            new PlayerTeamState(characters, sharedMaxHp: 200),
            new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: enemyHp)]),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260921);

    /// <summary>标记入队 → 切结算阶段执行（与 <c>SharedSettlementTests</c> 同约定）。</summary>
    private static void MarkAndExecute(CombatSimulation sim, string cardId, CombatTargetRef[] targets)
    {
        var slotIndex = FindSlot(sim.PlayerTeam.Characters[0], cardId);
        var marked = sim.TryApply(new PlayCardCommand(0, slotIndex, targets));
        Assert.That(marked.Success, Is.True, marked.Error);

        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
    }

    private static int FindSlot(CharacterBattleInstance character, string cardId)
    {
        for (var i = 0; i < character.HandSlots.Count; i++)
        {
            if (string.Equals(character.HandSlots[i].CardId, cardId, StringComparison.Ordinal))
                return i;
        }

        throw new InvalidOperationException($"角色手牌中没有 {cardId}。");
    }

    #endregion
}
