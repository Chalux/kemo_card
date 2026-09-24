using KemoCard.Frame.Content;
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
/// 冯·诺依曼的四张专属卡（2026-09-24 出货）：红属性治疗套件。
/// 同时覆盖本轮新增的三个引擎能力：<c>stackRule: Add</c> 的**独立计时叠层**、
/// 伤害执行的 <c>attackAttribute</c>（按回复量缩放）、GE 的 <c>lifestealScale</c>（按伤害量回血）。
/// </summary>
[TestFixture]
public sealed class VonNeumannExclusiveCardContentTests
{
    private const string DirectDeduction = "von_neumann_direct_deduction";
    private const string ProbabilityCollapse = "von_neumann_probability_collapse";
    private const string MergeSort = "von_neumann_merge_sort";
    private const string MergeFusion = "von_neumann_merge_fusion";

    private const string AmpBuff = "von_neumann_prob_collapse_amp";
    private const string RegenBuff = "von_neumann_prob_collapse_regen";
    private const string MergeAmpBuff = "von_neumann_merge_sort_amp";

    /// <summary>队伍共享血量的初始值（<see cref="PlayerTeamState"/> 的 sharedMaxHp）。</summary>
    private const int PartyHp = 100;

    /// <summary>治疗/吸血用例的起手伤害：满血时治疗会被上限吃掉。</summary>
    private const float LedgerDamage = 60f;

    /// <summary>测试角色的回复量（HealPower）：所有治疗的 <c>amount + 100% 回复量</c> 都基于它。</summary>
    private const float BaseHealPower = 10f;

    #region 元数据与接线

    [TestCase(DirectDeduction, 2, 11, "Healing", 20, 20, 2, "Self", "Team")]
    [TestCase(ProbabilityCollapse, 1, 50, "Support", 0, 30, 1, "Self", "Self")]
    [TestCase(MergeSort, 3, 19, "Healing", 30, 20, 2, "Self", "Team")]
    [TestCase(MergeFusion, 2, 7, "Magical", 6, 30, 1, "Enemy", "Single")]
    public void Exclusive_cards_match_the_design(
        string cardId,
        int cost,
        int priority,
        string cardType,
        int baseValue,
        int maxHealth,
        int healPower,
        string side,
        string scope)
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Cards.TryGetValue(cardId, out var card), Is.True, $"{cardId} 未随内容出货");

        Assert.That(card!.Element, Is.EqualTo((int)EElement.Red), "四张卡都是红属性");
        Assert.That(card.CostType, Is.EqualTo(ECostType.Energy));
        Assert.That(card.Cost, Is.EqualTo(cost));
        Assert.That(card.Priority, Is.EqualTo(priority));
        Assert.That(card.CardType.ToString(), Is.EqualTo(cardType));
        Assert.That(card.BaseValue, Is.EqualTo(baseValue));
        Assert.That(card.Role, Is.EqualTo(ERole.Healer), "卡牌定位跟随角色定位");
        Assert.That(card.TargetSide.ToString(), Is.EqualTo(side));
        Assert.That(card.TargetScope.ToString(), Is.EqualTo(scope));
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("MaxHealth"), Is.EqualTo(maxHealth));
        Assert.That(card.Stats?.Attributes.GetValueOrDefault("HealPower"), Is.EqualTo(healPower));
        Assert.That(card.Rarity, Is.EqualTo(ERarity.Exclusive));
        Assert.That(card.IsExclusive, Is.True, "专属卡必须标记 isExclusive");
        Assert.That(card.SkillRefs, Is.Not.Empty);
    }

    [Test]
    public void Heal_cards_declare_the_team_scope_required_by_the_spec()
    {
        var definitions = BaseGameContent.Load();

        foreach (var cardId in new[] { DirectDeduction, MergeSort })
        {
            var card = definitions.Cards[cardId];
            Assert.That(
                card.TargetSide is ETargetSide.Self or ETargetSide.Ally && card.TargetScope is ETargetScope.Team,
                Is.True,
                $"{cardId} 是玩家侧治疗卡：规格 §1.3 要求 targetSide ∈ {{Self, Ally}} 且 targetScope = Team");
        }
    }

    [Test]
    public void Cards_are_in_von_neumann_deck_and_their_payloads_resolve()
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Characters.TryGetValue("von_neumann", out var character), Is.True);

        foreach (var cardId in new[] { DirectDeduction, ProbabilityCollapse, MergeSort, MergeFusion })
        {
            Assert.That(character!.Cards, Does.Contain(cardId), $"冯·诺依曼初始卡组缺少 {cardId}");

            var card = definitions.Cards[cardId];
            foreach (var skillRef in card.SkillRefs)
            {
                Assert.That(definitions.Skills.TryGetValue(skillRef.SkillId, out var skill), Is.True,
                    $"{cardId} 的技能 {skillRef.SkillId} 不存在");
                Assert.That(skill!.DescId, Is.Not.Empty, $"{cardId} 的技能缺少描述键");

                foreach (var actionRef in skill.ActionRefs)
                    Assert.That(definitions.SkillActions.ContainsKey(actionRef.ActionId), Is.True,
                        $"{cardId} → {skill.Id} 的动作 {actionRef.ActionId} 不存在");

                foreach (var effectRef in skill.EffectRefs)
                    AssertEffectTreeResolves(effectRef, definitions, $"{cardId} → {skill.Id}");
            }
        }
    }

    private static void AssertEffectTreeResolves(
        EffectRefDto effectRef,
        ModDefinitionsBundle definitions,
        string path)
    {
        Assert.That(definitions.Effects.TryGetValue(effectRef.EffectId, out var effect), Is.True,
            $"{path} 的效果 {effectRef.EffectId} 不存在");

        if (effect!.Kind == EEffectKind.ApplyBuff)
        {
            var buffId = effect.Params?["buffId"]?.ToString();
            Assert.That(buffId, Is.Not.Null.And.Not.Empty, $"{effectRef.EffectId} 缺少 buffId");
            Assert.That(definitions.Buffs.ContainsKey(buffId!), Is.True, $"悬空 buffId '{buffId}'");
        }

        foreach (var child in effect.EffectRefs)
            AssertEffectTreeResolves(child, definitions, $"{path} → {effectRef.EffectId}");
    }

    #endregion

    #region 直辑结命（回合治疗）

    [Test]
    public void Direct_deduction_heals_the_party_for_amount_plus_heal_power()
    {
        using var sim = BuildPlayerPhase(DirectDeduction);
        sim.PlayerTeam.ApplySharedDamage(LedgerDamage);

        MarkAndExecute(sim, DirectDeduction, [Player(0)]);

        Assert.That(
            Ledger(sim),
            Is.EqualTo(PartyHp - LedgerDamage + 20f + BaseHealPower).Within(0.01f),
            "20 + 100% 回复量");
    }

    #endregion

    #region 概率坍缩（自身增幅 + 队伍回血）

    [Test]
    public void Probability_collapse_amps_heal_power_and_regenerates_every_turn()
    {
        using var sim = BuildPlayerPhase(ProbabilityCollapse);
        var caster = sim.PlayerTeam.Characters[0];

        MarkAndExecute(sim, ProbabilityCollapse, [Player(0)]);

        var amp = caster.Buffs.Find(AmpBuff);
        Assert.That(amp, Is.Not.Null, "自身必须拿到【回复量 +6】");
        Assert.That(amp!.RemainingTurns, Is.EqualTo(3));
        Assert.That(amp.Stacks, Is.EqualTo(1));
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.HealPower), Is.EqualTo(BaseHealPower + 6f));

        var regen = caster.Buffs.Find(RegenBuff);
        Assert.That(regen, Is.Not.Null, "己方回血挂在施法者身上（血量本就在队伍共享账本）");
        Assert.That(regen!.RemainingTurns, Is.EqualTo(3));

        // 回合开始的回响：治疗同样吃源侧回复量（1 + 100% 回复量 16）。
        sim.PlayerTeam.ApplySharedDamage(LedgerDamage);
        var before = Ledger(sim);
        sim.Buffs.FireTurnStart(sim);
        Assert.That(Ledger(sim), Is.EqualTo(before + 1f + (BaseHealPower + 6f)).Within(0.01f));
    }

    [Test]
    public void Regen_does_not_stack_and_reapplying_only_refreshes_the_duration()
    {
        using var sim = BuildPlayerPhase(ProbabilityCollapse);
        var caster = sim.PlayerTeam.Characters[0];

        sim.Buffs.Apply(sim, Player(0), RegenBuff);
        sim.Buffs.FireTurnEnd(sim);
        var regen = caster.Buffs.Find(RegenBuff)!;
        Assert.That(regen.RemainingTurns, Is.EqualTo(2), "回合结束 tick 掉 1");

        sim.Buffs.Apply(sim, Player(0), RegenBuff);

        Assert.That(regen.Stacks, Is.EqualTo(1), "回复效果不可叠加");
        Assert.That(regen.RemainingTurns, Is.EqualTo(3), "重复施放重置持续时间");
    }

    /// <summary>
    /// 回复量增幅可叠加且**各层独立计时**：先挂的层先到期，层数随之减少，属性修正按剩余层数结算。
    /// </summary>
    [Test]
    public void Heal_power_amp_stacks_and_each_stack_keeps_its_own_timer()
    {
        using var sim = BuildPlayerPhase(ProbabilityCollapse);
        var caster = sim.PlayerTeam.Characters[0];

        sim.Buffs.Apply(sim, Player(0), AmpBuff);
        sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireTurnEnd(sim);

        var amp = caster.Buffs.Find(AmpBuff)!;
        Assert.That(amp.RemainingTurns, Is.EqualTo(1), "第一层只剩 1 回合");

        sim.Buffs.Apply(sim, Player(0), AmpBuff);

        Assert.That(amp.Stacks, Is.EqualTo(2), "第二层独立叠上来");
        Assert.That(amp.RemainingTurns, Is.EqualTo(3), "显示取最长的那层");
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.HealPower), Is.EqualTo(BaseHealPower + 12f), "两层共 +12");

        sim.Buffs.FireTurnEnd(sim);

        Assert.That(amp.Stacks, Is.EqualTo(1), "先挂的层先到期");
        Assert.That(amp.RemainingTurns, Is.EqualTo(2));
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.HealPower), Is.EqualTo(BaseHealPower + 6f), "只剩第二层");
    }

    #endregion

    #region 确定的归并排序（治疗 + 自身增幅）

    [Test]
    public void Merge_sort_heals_first_then_amps_its_own_heal_power()
    {
        using var sim = BuildPlayerPhase(MergeSort);
        var caster = sim.PlayerTeam.Characters[0];
        sim.PlayerTeam.ApplySharedDamage(LedgerDamage);

        MarkAndExecute(sim, MergeSort, [Player(0)]);

        // ChainEffects 顺序：先治疗（此时回复量还是 10）再挂增幅。
        Assert.That(
            Ledger(sim),
            Is.EqualTo(PartyHp - LedgerDamage + 30f + BaseHealPower).Within(0.01f),
            "30 + 100% 回复量（未吃本次增幅）");

        var amp = caster.Buffs.Find(MergeAmpBuff);
        Assert.That(amp, Is.Not.Null);
        Assert.That(amp!.RemainingTurns, Is.EqualTo(2));
        Assert.That(caster.Asc.GetCurrentValue(AttributeIds.HealPower), Is.EqualTo(BaseHealPower + 6f));
    }

    #endregion

    #region 和谐的步进融合（按回复量缩放伤害 + 吸血）

    [Test]
    public void Merge_fusion_scales_damage_off_heal_power_and_heals_the_party_by_ten_percent()
    {
        using var sim = BuildPlayerPhase(
            MergeFusion,
            enemyExtraAttributes: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [AttributeIds.MagicDefense] = 2f,
            });
        var enemy = sim.EnemyTeam.Enemies[0];
        sim.PlayerTeam.ApplySharedDamage(LedgerDamage);

        var enemyHealthBefore = enemy.Asc.GetCurrentValue(AttributeIds.Health);
        var ledgerBefore = Ledger(sim);

        MarkAndExecute(sim, MergeFusion, [Enemy(0)]);

        // 卡牌：6 + 100% 回复量 10 − 目标魔防 2 = 14（普攻另行结算）。
        const float expectedCardDamage = 6f + BaseHealPower - 2f;
        var normalAttack = sim.NormalAttacks.LastResult;
        Assert.That(normalAttack, Is.Not.Null, "卡牌结算完成后应执行普通攻击");
        Assert.That(
            enemyHealthBefore - enemy.Asc.GetCurrentValue(AttributeIds.Health),
            Is.EqualTo(expectedCardDamage + normalAttack!.TotalDamage).Within(0.01f));

        Assert.That(
            Ledger(sim),
            Is.EqualTo(ledgerBefore + expectedCardDamage * 0.1f).Within(0.01f),
            "吸血：为己方回复 10% 伤害量");
    }

    #endregion

    #region 装配

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static float Ledger(CombatSimulation sim) =>
        sim.PlayerTeam.Asc.GetCurrentValue(AttributeIds.Health);

    private static CombatSimulation BuildPlayerPhase(
        string handCardId,
        int enemyCount = 1,
        int enemyHp = 300,
        IReadOnlyDictionary<string, float>? enemyExtraAttributes = null)
    {
        var registry = BaseGameContent.BuildRegistry();
        var characters = Enumerable.Range(0, CombatConstants.SlotCount)
            .Select(index => CharacterBattleInstance.CreateForTests(
                "von_neumann",
                DefaultAttributes(),
                drawPile: [new CardRuntimeEntry(handCardId, $"rt-{handCardId}-{index}")],
                element: EElement.Red,
                race: ERace.Human | ERace.Academic))
            .ToArray();

        foreach (var character in characters)
        {
            character.DrawCards(1);
            character.RefillAvailableEnergy();
        }

        var enemyAttributes = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = enemyHp,
        };
        foreach (var (key, value) in enemyExtraAttributes ?? new Dictionary<string, float>(StringComparer.Ordinal))
            enemyAttributes[key] = value;

        var enemies = Enumerable.Range(0, enemyCount)
            .Select(index => new EnemyUnit($"e{index}", "slime", enemyAttributes))
            .ToArray();

        return new CombatSimulation(
            new PlayerTeamState(characters, sharedMaxHp: PartyHp),
            new EnemyTeamState(enemies),
            new CombatRuleEngine([]),
            registry,
            initialPhase: ECombatPhase.Player,
            runSeed: 20260924);
    }

    private static Dictionary<string, float> DefaultAttributes() => new(StringComparer.Ordinal)
    {
        [AttributeIds.MaxHealth] = 50f,
        [AttributeIds.PhysicalAttack] = 10f,
        [AttributeIds.MagicAttack] = 30f,
        [AttributeIds.HealPower] = BaseHealPower,
        [AttributeIds.MaxEnergy] = 10f,
        [AttributeIds.InitialEnergy] = 10f,
    };

    /// <summary>标记入队 → 切结算阶段执行（与其它专属卡用例同约定）。</summary>
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