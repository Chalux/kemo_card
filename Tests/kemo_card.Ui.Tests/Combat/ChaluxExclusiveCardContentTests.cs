using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// chalux 的四张专属卡（出货内容）：元数据、接线完整性与端到端效果
/// （单体/群体蓝伤、自身增益、蓝属性球、槽位充能）。
/// </summary>
[TestFixture]
public sealed class ChaluxExclusiveCardContentTests
{
    private const string OrcaCard = "chalux_orca_ice_rush";
    private const string PathCard = "chalux_resplendent_path";
    private const string SwordCard = "chalux_brilliant_sword";
    private const string ResolveCard = "chalux_absolute_resolve";

    private const string PathFrostBuff = "chalux_path_frost";
    private const string SwordFrostBuff = "chalux_sword_frost";
    private const string VoidChargeBuff = "chalux_charge_void";
    private const string ActiveFrostBuff = "chalux_active_frost";

    #region 元数据与接线

    [TestCase(OrcaCard, 3, "Physics", 4, 20, 3)]
    [TestCase(PathCard, 4, "Physics", 9, 40, 0)]
    [TestCase(SwordCard, 2, "Support", 44, 30, 2)]
    [TestCase(ResolveCard, 2, "Support", 50, 20, 3)]
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

        Assert.That(card!.Element, Is.EqualTo((int)EElement.Blue), "四张卡都是蓝属性");
        Assert.That(card.CostType, Is.EqualTo(ECostType.Energy));
        Assert.That(card.Cost, Is.EqualTo(cost));
        Assert.That(card.CardType.ToString(), Is.EqualTo(cardType));
        Assert.That(card.Priority, Is.EqualTo(priority));
        Assert.That(card.Rarity, Is.EqualTo(ERarity.Epic));
        Assert.That(card.IsExclusive, Is.True, "专属卡必须标记 isExclusive");
        Assert.That(card.CardGroupId, Is.Null, "无升级链");
        Assert.That(card.UpgradeTier, Is.Zero);
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("MaxHealth"), Is.EqualTo(maxHealth));
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("PhysicalAttack"), Is.EqualTo(physicalAttack));
        Assert.That(card.SkillRefs, Is.Not.Empty);
    }

    [Test]
    public void Exclusive_cards_are_in_chalux_initial_deck_and_their_skill_chains_resolve()
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Characters.TryGetValue("chalux", out var chalux), Is.True);

        foreach (var cardId in new[] { OrcaCard, PathCard, SwordCard, ResolveCard })
        {
            Assert.That(chalux!.Cards, Does.Contain(cardId), $"chalux 初始卡组缺少 {cardId}");

            Assert.That(definitions.Cards.TryGetValue(cardId, out var card), Is.True);
            foreach (var skillRef in card!.SkillRefs)
            {
                Assert.That(definitions.Skills.TryGetValue(skillRef.SkillId, out var skill), Is.True,
                    $"{cardId} 的技能 {skillRef.SkillId} 不存在");
                Assert.That(skill!.DescId, Is.Not.Empty, $"{cardId} 的技能缺少描述键（卡牌描述即由技能描述拼接）");

                foreach (var actionRef in skill.ActionRefs)
                {
                    Assert.That(definitions.SkillActions.ContainsKey(actionRef.ActionId), Is.True,
                        $"{cardId} → {skill.Id} 的动作 {actionRef.ActionId} 不存在");
                }
            }
        }
    }

    [Test]
    public void Support_cards_do_not_count_toward_chain_heads()
    {
        var definitions = BaseGameContent.Load();

        Assert.That(ChainCalculator.AppliesToCard(definitions.Cards[OrcaCard]), Is.True);
        Assert.That(ChainCalculator.AppliesToCard(definitions.Cards[PathCard]), Is.True);
        Assert.That(ChainCalculator.AppliesToCard(definitions.Cards[SwordCard]), Is.False, "纯增益卡不堆连携人头");
        Assert.That(ChainCalculator.AppliesToCard(definitions.Cards[ResolveCard]), Is.False);
    }

    #endregion

    #region 端到端

    [Test]
    public void Orca_ice_rush_deals_single_target_blue_damage()
    {
        using var sim = BuildPlayerPhase(OrcaCard);
        var enemy = sim.EnemyTeam.Enemies[0];
        var before = enemy.Asc.GetCurrentValue(AttributeIds.Health);

        MarkAndExecute(sim, OrcaCard, [Enemy(0)]);

        // 卡牌：12 + 100% 施法者物攻 10 − 目标物防 0 = 22；
        // 随后本回合普通攻击（第 1 回合归槽位 1 = 角色 0，物攻 10 − 物防 0）= 10。
        var normalAttack = sim.NormalAttacks.LastResult;
        Assert.That(normalAttack, Is.Not.Null, "卡牌结算完成后应执行普通攻击");
        Assert.That(normalAttack!.Kind, Is.EqualTo(EDamageKind.Physical));
        Assert.That(enemy.Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(before - 22f - 10f).Within(0.001f));
    }

    [Test]
    public void Resplendent_path_hits_all_enemies_and_buffs_self()
    {
        using var sim = BuildPlayerPhase(PathCard, enemyCount: 2);
        var caster = sim.PlayerTeam.Characters[0];
        var attackBefore = caster.Asc.GetCurrentValue(AttributeIds.PhysicalAttack);
        var first = sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health);
        var second = sim.EnemyTeam.Enemies[1].Asc.GetCurrentValue(AttributeIds.Health);

        MarkAndExecute(sim, PathCard, [Enemy(0), Enemy(1)]);

        // 卡牌 22/敌人；自身 +6 物攻在普攻之前落地 → 普攻 = 10 + 6 = 16/敌人（普攻也是敌方全体）。
        var normalAttack = sim.NormalAttacks.LastResult;
        Assert.That(normalAttack, Is.Not.Null);
        Assert.That(normalAttack!.TargetCount, Is.EqualTo(2), "普攻目标是敌方全体");
        Assert.That(normalAttack.TotalDamage, Is.EqualTo(32f).Within(0.001f), "16 × 2 个敌人");
        Assert.That(sim.EnemyTeam.Enemies[0].Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(first - 22f - 16f).Within(0.001f));
        Assert.That(sim.EnemyTeam.Enemies[1].Asc.GetCurrentValue(AttributeIds.Health), Is.EqualTo(second - 22f - 16f).Within(0.001f));

        var buff = caster.Buffs.Find(PathFrostBuff);
        Assert.That(buff, Is.Not.Null, "群体伤害同时必须把增益落在施法者自己身上（不是敌人）");
        Assert.That(buff!.RemainingTurns, Is.EqualTo(2), "2 回合");
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(attackBefore + 6f).Within(0.001f));
    }

    [Test]
    public void Brilliant_sword_buffs_self_and_grants_two_blue_orbs()
    {
        using var sim = BuildPlayerPhase(SwordCard);
        var caster = sim.PlayerTeam.Characters[0];
        var attackBefore = caster.Asc.GetCurrentValue(AttributeIds.PhysicalAttack);

        MarkAndExecute(sim, SwordCard, [Player(0)]);

        var buff = caster.Buffs.Find(SwordFrostBuff);
        Assert.That(buff, Is.Not.Null);
        Assert.That(buff!.RemainingTurns, Is.EqualTo(2));
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(attackBefore + 9f).Within(0.001f));

        Assert.That(sim.Orbs.Queue.Count, Is.EqualTo(2), "获得 2 个蓝属性球");
        Assert.That(sim.Orbs.Queue.CountOf(BuiltinOrbTypes.Blue), Is.EqualTo(2));
        Assert.That(
            sim.Orbs.Queue.Orbs.Select(orb => orb.ProducerIndex),
            Is.All.EqualTo(0),
            "产球者 = 打出卡牌的角色");
    }

    [Test]
    public void Absolute_resolve_attaches_charge_two_to_second_hand_slot()
    {
        using var sim = BuildPlayerPhase(ResolveCard);
        var caster = sim.PlayerTeam.Characters[0];

        MarkAndExecute(sim, ResolveCard, [Player(0)]);

        var slot = caster.HandSlots[1];
        var charge = slot.Buffs.Find(VoidChargeBuff);
        Assert.That(charge, Is.Not.Null, "2 号手牌槽（索引 1）获得充能 buff");
        Assert.That(charge!.ChargeCounter, Is.EqualTo(2), "充能 II");
        Assert.That(charge.RemainingTurns, Is.EqualTo(5), "持续 5 回合");
        Assert.That(caster.Buffs.Find(ActiveFrostBuff), Is.Null, "未触发前不给增益");

        // 第 1 张牌：计数 2 → 1，不触发。
        sim.Buffs.FireSlotCardPlayed(sim, 0, slot);
        Assert.That(caster.Buffs.Find(ActiveFrostBuff), Is.Null, "充能 II 需要两张牌");
        Assert.That(charge.ChargeCounter, Is.EqualTo(1));

        // 第 2 张牌：归零 → 载荷（3 回合 / 物攻 +6）并重置计数。
        var attackBefore = caster.Asc.GetCurrentValue(AttributeIds.PhysicalAttack);
        sim.Buffs.FireSlotCardPlayed(sim, 0, slot);

        var payload = caster.Buffs.Find(ActiveFrostBuff);
        Assert.That(payload, Is.Not.Null, "载荷复用超限增幅（物攻 +6 / 3 回合）");
        Assert.That(payload!.RemainingTurns, Is.EqualTo(3));
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(attackBefore + 6f).Within(0.001f));
        Assert.That(charge.ChargeCounter, Is.EqualTo(2), "触发后重置计数，可持续触发");
    }

    #endregion

    #region 装配

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static CombatSimulation BuildPlayerPhase(string handCardId, int enemyCount = 1, int enemyHp = 300)
    {
        var registry = BaseGameContent.BuildRegistry();
        var attrs = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = 50f,
            [AttributeIds.PhysicalAttack] = 10f,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };

        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests(
                "chalux",
                attrs,
                drawPile: [new CardRuntimeEntry(handCardId, $"rt-{handCardId}-{index}")]))
            .ToArray();
        foreach (var character in characters)
        {
            character.DrawCards(1);
            character.RefillAvailableEnergy();
        }

        var enemies = Enumerable.Range(0, enemyCount)
            .Select(index => new EnemyUnit($"e{index}", "slime", maxHp: enemyHp))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: 100),
            new EnemyTeamState(enemies),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260919);
    }

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
