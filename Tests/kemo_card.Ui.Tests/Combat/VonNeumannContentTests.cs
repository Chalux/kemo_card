using System.Text.Json;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 冯·诺依曼（2026-09-23 出货内容）的元数据、接线与端到端效果。本轮为它新增的能力：
/// 槽位效果「暴风」+ 免疫特征（P1）、战斗条件 <c>ChainTierAtLeast</c>（P2）、
/// 连携"黄计入蓝"注入（P5）、敌人行动计数 + <c>oncePerWave</c>（P6）、
/// 领域内容通道 <c>SetDomain</c>（主动技）、敌人 <c>race</c> 字段（领域的目标筛选）。
/// 2026-09-24 改版：红属性 / Healer；P2 由"连携反击伤害"改为"连携达成时治疗全队"（<c>hookTargets: team</c>）。
/// </summary>
[TestFixture]
public sealed class VonNeumannContentTests
{
    private const string CharacterId = "von_neumann";
    private const string ActiveSkillId = "von_neumann_fate_reckoning";
    private const string DomainEffectId = "von_neumann_fate_domain";
    private const string DomainAllyBuff = "von_neumann_fate_domain_protect_ally";
    private const string DomainEnemyBuff = "von_neumann_fate_domain_protect_enemy";

    private const string BlueCard = "card.test_blue";
    private const string YellowCard = "card.test_yellow";

    private const string StormBuff = "test_storm";
    private const string StormDiscardEffect = "test_storm_discard";

    /// <summary>治疗用例的起手伤害：满血时治疗会被上限吃掉，必须先让账本掉一截。</summary>
    private const float LedgerDamage = 40f;

    #region 元数据与接线

    [Test]
    public void Character_definition_matches_the_design()
    {
        var definitions = BaseGameContent.Load();
        Assert.That(definitions.Characters.TryGetValue(CharacterId, out var character), Is.True, "冯·诺依曼未随内容出货");

        Assert.That(character!.Element, Is.EqualTo(EElement.Red), "2026-09-24 起改为红属性");
        Assert.That(character.Role, Is.EqualTo(ERole.Healer), "定位：Healer");
        Assert.That(character.Race, Is.EqualTo(ERace.Human | ERace.Academic));
        Assert.That(character.MaxEnergy, Is.EqualTo(8));
        Assert.That(character.InitialEnergy, Is.EqualTo(3));

        // 单档主动技：命结推算 CD10。
        Assert.That(character.ActiveSkillChain, Has.Count.EqualTo(1));
        Assert.That(character.ActiveSkillChain[0].SkillId, Is.EqualTo(ActiveSkillId));
        Assert.That(character.ActiveSkillChain[0].Cooldown, Is.EqualTo(10));

        // 潜能被动保留 0/10/30/50 四档（70/99 档已删除）。
        Assert.That(
            character.Passives.Select(passive => passive.RequiredPotential),
            Is.EqualTo(new[] { 0, 10, 30, 50 }));
        foreach (var passive in character.Passives)
            Assert.That(definitions.Buffs.ContainsKey(passive.BuffId), Is.True, $"被动 {passive.BuffId} 不存在");
    }

    [Test]
    public void Passive_wiring_matches_the_design()
    {
        BaseGameContent.RegisterBuiltinConditions();
        var definitions = BaseGameContent.Load();

        var immune = definitions.Buffs["von_neumann_passive_p1"];
        Assert.That(immune.Tags, Does.Contain(BuiltinBuffTags.TraitImmuneSlotStorm));

        // P2：结算后钩子 + 连携档位条件（新条件类型必须真的注册进 Combat 域）。
        // 目标选择器写在钩子引用上（效果定义自身的 params 不参与目标解析——chalux 充能同口径）；
        // hookTargets: "team" = 队伍共享账本，玩家侧治疗只能落在账本上（规格 §1.3）。
        var p2 = definitions.Buffs["von_neumann_passive_p2"];
        Assert.That(p2.Hooks.OnCardSettled, Has.Count.EqualTo(1));
        Assert.That(p2.Hooks.OnCardSettled[0].Params!["hookTargets"].ToString(), Is.EqualTo("team"));
        var mending = definitions.Effects["von_neumann_p2_fate_mending"];
        Assert.That(mending.Kind, Is.EqualTo(EEffectKind.Heal));
        Assert.That(mending.Conditions, Has.Count.EqualTo(1));
        Assert.That(mending.Conditions[0].Kind, Is.EqualTo(BuiltinCombatConditions.ChainTierAtLeast));
        Assert.That(ConditionDomains.Combat.Contains(BuiltinCombatConditions.ChainTierAtLeast), Is.True);

        // P3：阶层开始 + 波内每 10 回合。
        var p3 = definitions.Buffs["von_neumann_passive_p3"];
        Assert.That(p3.Hooks.OnWaveStart, Has.Count.EqualTo(4));
        Assert.That(p3.Hooks.OnTurnStart, Has.Count.EqualTo(4));
        foreach (var hook in p3.Hooks.OnTurnStart)
        {
            Assert.That(hook.Params, Is.Not.Null);
            Assert.That(((JsonElement)hook.Params!["turnInterval"]).GetInt32(), Is.EqualTo(10));
        }

        // P4：红·人类·学术（`·` = 或）+ 机械族在场时翻倍（队伍人数门闩）。
        var p4 = definitions.Buffs["von_neumann_passive_p4"];
        Assert.That(p4.ApplyScope, Is.EqualTo(EBuffApplyScope.AllAllies));
        var p4Condition = CombatTestHelper.IdentityParams(p4);
        Assert.That(p4Condition, Is.Not.Null, "P4 持有者条件走 IdentityMatch");
        Assert.That(CombatTestHelper.BoolParam(p4Condition, "matchAll"), Is.False, "描述里的 `·` = 或（跨维度默认取或）");
        Assert.That(CombatTestHelper.EnumNames(p4Condition, "elementAny"), Is.EqualTo(new[] { "Red" }), "2026-09-24 起由蓝改红");
        Assert.That(CombatTestHelper.EnumNames(p4Condition, "raceAny"), Is.EqualTo(new[] { "Human", "Academic" }));
        Assert.That(CombatTestHelper.IntParam(p4Condition, "partyMinCount"), Is.Zero, "基础档不筛队伍人数");

        var doubled = definitions.Buffs["von_neumann_passive_p4_double"];
        var doubledCondition = CombatTestHelper.IdentityParams(doubled);
        Assert.That(CombatTestHelper.IntParam(doubledCondition, "partyMinCount"), Is.EqualTo(1));
        Assert.That(CombatTestHelper.EnumNames(doubledCondition, "partyRaceAny"), Is.EqualTo(new[] { "Machine" }));
    }

    #endregion

    #region P1：暴风

    [Test]
    public void Storm_blows_away_the_nearest_neighbour_slots()
    {
        var storm = StormRegistry();
        using var sim = HandSimulation(storm, characters: 1);
        var character = sim.PlayerTeam.Characters[0];
        sim.Buffs.ApplyToSlot(sim, 0, slotIndex: 2, StormBuff);

        sim.Buffs.FireSlotCardPlayed(sim, 0, character.HandSlots[2]);

        Assert.That(character.HandSlots[1].IsEmpty, Is.True, "暴风 I = 1 个相邻槽（先左后右）");
        Assert.That(character.HandSlots[3].IsEmpty, Is.False, "只吹散 1 个槽，右侧不受影响");
        Assert.That(character.HandSlots[0].IsEmpty, Is.False);
        Assert.That(character.HandSlots[4].IsEmpty, Is.False);
        Assert.That(character.Asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(50f), "暴风不动血量");
    }

    [Test]
    public void Storm_neighbour_selection_is_left_first_then_right_by_distance()
    {
        Assert.That(BuffRuntime.StormNeighborSlots(slotIndex: 2, spread: 1, slotCount: 5), Is.EqualTo(new[] { 1 }));
        Assert.That(BuffRuntime.StormNeighborSlots(slotIndex: 2, spread: 2, slotCount: 5), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(BuffRuntime.StormNeighborSlots(slotIndex: 2, spread: 3, slotCount: 5), Is.EqualTo(new[] { 1, 3, 0 }));
        Assert.That(BuffRuntime.StormNeighborSlots(slotIndex: 0, spread: 2, slotCount: 5), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(BuffRuntime.StormNeighborSlots(slotIndex: 4, spread: 1, slotCount: 5), Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void Storm_never_discards_marked_cards()
    {
        var storm = StormRegistry();
        using var sim = HandSimulation(storm, characters: 1);
        var character = sim.PlayerTeam.Characters[0];

        // 槽 1 先入队（已标记）：暴风不得借弃牌回滚确认态（规格 §2.2"其它通道"）。
        var marked = sim.TryApply(new PlayCardCommand(0, 1, [Enemy(0)]));
        Assert.That(marked.Success, Is.True, marked.Error);

        sim.Buffs.ApplyToSlot(sim, 0, slotIndex: 2, StormBuff);
        sim.Buffs.FireSlotCardPlayed(sim, 0, character.HandSlots[2]);

        Assert.That(character.HandSlots[1].IsEmpty, Is.False, "已标记的牌保留");
        Assert.That(character.HandSlots[1].IsMarked, Is.True, "标记也不被清掉");
    }

    [Test]
    public void Passive_one_blocks_the_storm_entirely()
    {
        var definitions = BaseGameContent.Load();
        var registry = RegistryWith(
            definitions,
            cards: [ElementCard(BlueCard, EElement.Blue)],
            buffs: Pick(definitions.Buffs, "von_neumann_passive_p1"),
            effects: StormEffects(),
            extraBuffs: StormBuffDefinition());

        using var sim = HandSimulation(registry, characters: 1);
        var character = sim.PlayerTeam.Characters[0];
        sim.Buffs.Apply(sim, Player(0), "von_neumann_passive_p1");
        sim.Buffs.ApplyToSlot(sim, 0, slotIndex: 2, StormBuff);

        sim.Buffs.FireSlotCardPlayed(sim, 0, character.HandSlots[2]);

        for (var slot = 0; slot < CombatConstants.HandSlotCount; slot++)
            Assert.That(character.HandSlots[slot].IsEmpty, Is.False, $"免疫暴风：槽 {slot} 的手牌必须保留");
    }

    #endregion

    #region P2：连携档位治疗

    [Test]
    public void Passive_two_heals_the_party_when_the_settled_card_reaches_two_chain()
    {
        BaseGameContent.RegisterBuiltinConditions();
        var definitions = BaseGameContent.Load();
        var registry = RegistryWith(
            definitions,
            cards: [ElementCard(BlueCard, EElement.Blue), ElementCard(YellowCard, EElement.Yellow)],
            buffs: Pick(definitions.Buffs, "von_neumann_passive_p2"),
            effects: Pick(definitions.Effects, "von_neumann_p2_fate_mending"));

        // 2 名不同角色各打一张蓝卡 → 蓝属性 2 连携档 → 持有者那张结算时触发治疗。
        var twoChain = LedgerHealed(registry, [BlueCard, BlueCard], withPassive: true)
            - LedgerHealed(registry, [BlueCard, BlueCard], withPassive: false);
        Assert.That(twoChain, Is.EqualTo(6f).Within(0.01f), "6 + 100% 治疗量 0");

        // 只有自己一张蓝卡 → 1 名人头，不满足 2 连携档。
        var lone = LedgerHealed(registry, [BlueCard], withPassive: true)
            - LedgerHealed(registry, [BlueCard], withPassive: false);
        Assert.That(lone, Is.Zero, "不足 2 连携档时不触发");

        // 蓝只有自己 1 人、黄有 2 人：条件按"当前结算卡（蓝）"判定 → 不触发。
        // 若错判成"任意属性达到 2 档"，这条会因为黄属性人头而误治疗。
        var ownElementOnly = LedgerHealed(registry, [BlueCard, YellowCard, YellowCard], withPassive: true)
            - LedgerHealed(registry, [BlueCard, YellowCard, YellowCard], withPassive: false);
        Assert.That(ownElementOnly, Is.Zero, "条件只看当前结算卡的属性");

        // 反过来：自己打黄卡且黄有 2 人 → 触发（确实读的是"这张牌"的人头）。
        var yellowTwoChain = LedgerHealed(registry, [YellowCard, YellowCard], withPassive: true)
            - LedgerHealed(registry, [YellowCard, YellowCard], withPassive: false);
        Assert.That(yellowTwoChain, Is.EqualTo(6f).Within(0.01f));
    }

    #endregion

    #region P3：技能进度

    [Test]
    public void Passive_three_grants_skill_progress_at_wave_start_and_every_tenth_turn()
    {
        BaseGameContent.RegisterBuiltinConditions();
        var definitions = BaseGameContent.Load();
        using var sim = SkillProgressSimulation(RegistryWith(
            definitions,
            buffs: Pick(definitions.Buffs, "von_neumann_passive_p3"),
            effects: Pick(definitions.Effects, "gain_skill_counter")));

        sim.Buffs.Apply(sim, Player(0), "von_neumann_passive_p3");
        sim.Buffs.FireWaveStart(sim);

        // 自身 +1，红 +2，人类 +2，学术 +2（可叠加；2026-09-24 起元素维度由蓝改红）。
        Assert.That(Counter(sim, 0), Is.EqualTo(7), "冯·诺依曼（红·人类·学术）：自身 +1 + 红 +2 + 人类 +2 + 学术 +2");
        Assert.That(Counter(sim, 1), Is.EqualTo(2), "红·动物 → 只有红 +2");
        Assert.That(Counter(sim, 2), Is.EqualTo(2), "蓝·人类 → 只有人类 +2（蓝不再计入）");
        Assert.That(Counter(sim, 3), Is.Zero, "绿·动物：四项全不命中");

        // 阶层内每 10 回合：TurnsIntoWave = 10 时才再次触发。
        for (var i = 0; i < 10; i++)
            sim.IncrementTurnsIntoWave();
        sim.Buffs.FireTurnStart(sim);
        Assert.That(Counter(sim, 0), Is.EqualTo(14), "10 回合档再 +7");
        Assert.That(Counter(sim, 1), Is.EqualTo(4), "红·动物同样再 +2");

        // 第 11 回合不再触发（只认 10 的整数倍）。
        sim.IncrementTurnsIntoWave();
        sim.Buffs.FireTurnStart(sim);
        Assert.That(Counter(sim, 0), Is.EqualTo(14));
    }

    #endregion

    #region P4：队伍增益与机械族翻倍

    [Test]
    public void Passive_four_buffs_red_human_or_academic_allies()
    {
        BaseGameContent.RegisterBuiltinConditions();
        var definitions = BaseGameContent.Load();
        var registry = RegistryWith(
            definitions,
            buffs: Pick(definitions.Buffs, "von_neumann_passive_p4", "von_neumann_passive_p4_double"),
            effects: Pick(definitions.Effects, "von_neumann_p4_double_layer"));
        var attrs = Attributes();
        // 描述 `·` = 或：红 / 人类 / 学术 任一命中即生效；蓝·动物三项都不命中。
        using var sim = NewSimulation(registry, [
            CharacterBattleInstance.CreateForTests("c0", attrs, element: EElement.Red, race: ERace.Human | ERace.Academic),
            CharacterBattleInstance.CreateForTests("c1", attrs, element: EElement.Blue, race: ERace.Human),
            CharacterBattleInstance.CreateForTests("c2", attrs, element: EElement.Red, race: ERace.Animal),
            CharacterBattleInstance.CreateForTests("c3", attrs, element: EElement.Blue, race: ERace.Animal),
        ], [new EnemyUnit("e0", "slime", 500)]);

        sim.Buffs.Apply(sim, Player(0), "von_neumann_passive_p4");

        var allThree = sim.PlayerTeam.Characters[0];
        Assert.That(allThree.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(33f), "红·人类·学术：三项全中");
        Assert.That(allThree.Asc.GetCurrentValue(AttributeIds.HealPower), Is.EqualTo(3f));
        Assert.That(allThree.Asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(70f));

        var humanOnly = sim.PlayerTeam.Characters[1];
        Assert.That(humanOnly.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(33f), "蓝·人类：命中人类");
        Assert.That(humanOnly.Asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(70f));

        var redOnly = sim.PlayerTeam.Characters[2];
        Assert.That(redOnly.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(33f), "红·动物：命中红");

        var none = sim.PlayerTeam.Characters[3];
        Assert.That(none.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(30f), "蓝·动物：三项都不命中");
        Assert.That(none.Asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(50f));
    }

    /// <summary>
    /// 翻倍层 <c>von_neumann_passive_p4_double</c> 由 P4 载体的 <c>onApply</c> 钩子在投放时自动挂上；
    /// 它自身带 <c>partyMinCount: 1 + partyRaceAny: [Machine]</c> 条件，队伍里没有机械族时处于休眠，
    /// 因此这里只手动投放 P4 一次即可观察"有/无机械族"两种结果。
    /// </summary>
    [Test]
    public void Passive_four_doubles_while_the_party_holds_a_machine_character()
    {
        BaseGameContent.RegisterBuiltinConditions();
        var definitions = BaseGameContent.Load();

        using var withMachine = PartySimulation(
            RegistryWith(
                definitions,
                buffs: Pick(definitions.Buffs, "von_neumann_passive_p4", "von_neumann_passive_p4_double"),
                effects: Pick(definitions.Effects, "von_neumann_p4_double_layer")),
            machineAlly: true);
        withMachine.Buffs.Apply(withMachine, Player(0), "von_neumann_passive_p4");

        var boosted = withMachine.PlayerTeam.Characters[0];
        Assert.That(boosted.Buffs.Find("von_neumann_passive_p4_double"), Is.Not.Null, "钩子应自动投放翻倍层");
        Assert.That(boosted.Buffs.Find("von_neumann_passive_p4_double")!.IsDormant, Is.False, "有机械族时翻倍层必须醒来");
        Assert.That(boosted.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(36f), "3 + 3");
        Assert.That(boosted.Asc.GetCurrentValue(AttributeIds.HealPower), Is.EqualTo(6f), "3 + 3");
        Assert.That(boosted.Asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(90f), "20 + 20");

        using var withoutMachine = PartySimulation(
            RegistryWith(
                definitions,
                buffs: Pick(definitions.Buffs, "von_neumann_passive_p4", "von_neumann_passive_p4_double"),
                effects: Pick(definitions.Effects, "von_neumann_p4_double_layer")),
            machineAlly: false);
        withoutMachine.Buffs.Apply(withoutMachine, Player(0), "von_neumann_passive_p4");

        var plain = withoutMachine.PlayerTeam.Characters[0];
        Assert.That(plain.Buffs.Find("von_neumann_passive_p4_double"), Is.Not.Null, "翻倍层仍然挂着");
        Assert.That(plain.Buffs.Find("von_neumann_passive_p4_double")!.IsDormant, Is.True, "没有机械族时休眠");
        Assert.That(plain.Asc.GetCurrentValue(AttributeIds.MagicAttack), Is.EqualTo(33f), "不翻倍");
        Assert.That(plain.Asc.GetCurrentValue(AttributeIds.MaxHealth), Is.EqualTo(70f));
    }

    #endregion

    #region P6：行动计数（引擎机制；70/99 档被动已删除，本用例保留为引擎回归）

    [Test]
    public void Enemy_action_count_above_one_skips_the_enemy_phase()
    {
        BaseGameContent.RegisterBuiltinConditions();
        var registry = BaseGameContent.BuildRegistry();

        using var delayed = EnemyPhaseSimulation(registry, actionCount: 2);
        delayed.AdvancePhase();
        var skipped = delayed.EnemyTeam.Enemies[0];
        Assert.That(skipped.IntentSkillId, Is.Null, "行动计数 2：本回合不行动");
        Assert.That(skipped.ActionCount, Is.EqualTo(1), "只递减计数（下个回合照常行动）");

        using var acting = EnemyPhaseSimulation(registry, actionCount: 1);
        acting.AdvancePhase();
        Assert.That(acting.EnemyTeam.Enemies[0].IntentSkillId, Is.Not.Null, "计数 1：正常行动");
    }

    #endregion

    #region 主动技：领域

    [Test]
    public void Active_skill_opens_a_two_turn_domain_that_protects_matching_targets()
    {
        BaseGameContent.RegisterBuiltinConditions();
        using var sim = DomainSimulation(BaseGameContent.BuildRegistry());

        var cast = sim.TryApply(new CastActiveSkillCommand(0, []));
        Assert.That(cast.Success, Is.True, cast.Error);

        Assert.That(sim.PlayerTeam.ActiveDomain, Is.Not.Null, "领域必须真的展开");
        Assert.That(sim.PlayerTeam.ActiveDomain!.GameplayEffectId, Is.EqualTo(DomainEffectId));

        // 红·人类·学术角色：全伤害 -50%（2026-09-24 起元素维度由蓝改红）。
        var matching = sim.PlayerTeam.Characters[0];
        var allyBuff = matching.Buffs.Find(DomainAllyBuff);
        Assert.That(allyBuff, Is.Not.Null);
        Assert.That(allyBuff!.IsDormant, Is.False);
        Assert.That(matching.Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.EqualTo(-0.5f));

        // 非命中角色：buff 挂上了但休眠，不吃减伤。
        var wrongRace = sim.PlayerTeam.Characters[1];
        Assert.That(wrongRace.Buffs.Find(DomainAllyBuff)!.IsDormant, Is.True);
        Assert.That(wrongRace.Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.Zero);

        // 敌人同样按"红·人类·学术"筛选（EnemyDto.race 是本轮新增的字段）。
        var matchingEnemy = sim.EnemyTeam.Enemies[0];
        Assert.That(matchingEnemy.Buffs.Find(DomainEnemyBuff)!.IsDormant, Is.False);
        Assert.That(matchingEnemy.Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.EqualTo(-0.5f));

        var wrongEnemy = sim.EnemyTeam.Enemies[1];
        Assert.That(wrongEnemy.Buffs.Find(DomainEnemyBuff)!.IsDormant, Is.True);
        Assert.That(wrongEnemy.Asc.GetCurrentValue(AttributeIds.DamageTakenScale), Is.Zero);

        // 展开 2 回合：第 1 次回合结束还在，第 2 次收起。
        sim.DomainManager.FireTurnEndHooks();
        Assert.That(sim.PlayerTeam.ActiveDomain, Is.Not.Null, "第 1 回合结束仍展开");

        sim.DomainManager.FireTurnEndHooks();
        Assert.That(sim.PlayerTeam.ActiveDomain, Is.Null, "2 回合后自动收起");
    }

    [Test]
    public void Spawned_enemies_carry_their_content_element_and_race()
    {
        var enemy = new EnemyDto
        {
            Id = "test.blue_academic",
            MaxHp = 30,
            Element = EElement.Blue,
            Race = ERace.Human | ERace.Academic,
        };
        var registry = CombatTestHelper.CreateFullRegistry(
            enemies: new Dictionary<string, EnemyDto>(StringComparer.Ordinal) { [enemy.Id] = enemy });

        var wave = new BattleWaveDto
        {
            EnemySpawns = [new EnemySpawnDto { EnemyId = enemy.Id, Count = 1 }],
        };
        var units = CombatSimulationFactory.SpawnWaveEnemies(wave, registry, out var error);

        Assert.That(error, Is.Null);
        Assert.That(units, Has.Count.EqualTo(1));
        Assert.That(units![0].Element, Is.EqualTo(EElement.Blue));
        Assert.That(units[0].Race, Is.EqualTo(ERace.Human | ERace.Academic));
    }

    #endregion

    #region 装配

    private static CombatTargetRef Player(int index) => new(ECombatSide.Player, index);

    private static CombatTargetRef Enemy(int index) => new(ECombatSide.Enemy, index);

    private static EnemyUnit enemy(CombatSimulation simulation) => simulation.EnemyTeam.Enemies[0];

    private static int Counter(CombatSimulation simulation, int characterIndex) =>
        simulation.PlayerTeam.Characters[characterIndex].SkillCounter;

    private static Dictionary<string, T> Pick<T>(IReadOnlyDictionary<string, T> source, params string[] ids) =>
        ids.ToDictionary(id => id, id => source[id], StringComparer.Ordinal);

    private static Dictionary<string, EffectDto> StormEffects() => new(StringComparer.Ordinal)
    {
        [StormDiscardEffect] = new EffectDto { Id = StormDiscardEffect, Kind = EEffectKind.DiscardSlot },
    };

    private static Dictionary<string, BuffDto> StormBuffDefinition() => new(StringComparer.Ordinal)
    {
        [StormBuff] = new BuffDto
        {
            Id = StormBuff,
            DurationType = EBuffDurationType.Permanent,
            Tags = [BuiltinBuffTags.SlotStorm],
            Hooks = new BuffEffectHooksDto
            {
                OnSlotCardPlayed =
                [
                    new EffectRefDto
                    {
                        EffectId = StormDiscardEffect,
                        Params = new Dictionary<string, object> { ["adjacentSlots"] = 1 },
                    },
                ],
            },
        },
    };

    /// <summary>合成卡：不带 skillRefs，因此不会被内容校验剔除（本用例只关心它的元素）。</summary>
    private static CardDto ElementCard(string id, EElement element) => new()
    {
        Id = id,
        DisplayNameId = id,
        Element = (int)element,
        CardType = ECardType.Physics,
        CostType = ECostType.Energy,
        Cost = 0,
        TargetSide = ETargetSide.Enemy,
        TargetScope = ETargetScope.Single,
        TargetCount = 1,
        Priority = 1,
    };

    private static IReadOnlyDictionary<string, float> Attributes(
        float maxHealth = 50f,
        float physicalAttack = 10f,
        float magicAttack = 30f)
    {
        return new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [AttributeIds.MaxHealth] = maxHealth,
            [AttributeIds.PhysicalAttack] = physicalAttack,
            [AttributeIds.MagicAttack] = magicAttack,
            [AttributeIds.MaxEnergy] = 10f,
            [AttributeIds.InitialEnergy] = 10f,
        };
    }

    private static IReadOnlyDictionary<string, float> EnemyAttributes(int maxHealth) =>
        new Dictionary<string, float>(StringComparer.Ordinal) { [AttributeIds.MaxHealth] = maxHealth };

    /// <summary>用出货内容的指定定义 + 合成卡搭一个 registry（未列出的定义一律不注册）。</summary>
    private static GameDefinitionRegistry RegistryWith(
        ModDefinitionsBundle definitions,
        CardDto[]? cards = null,
        IReadOnlyDictionary<string, BuffDto>? buffs = null,
        IReadOnlyDictionary<string, EffectDto>? effects = null,
        IReadOnlyDictionary<string, GameplayEffectDefDto>? gameplayEffects = null,
        IReadOnlyDictionary<string, BuffDto>? extraBuffs = null,
        IReadOnlyDictionary<string, EffectDto>? extraEffects = null)
    {
        var cardDict = (cards ?? []).ToDictionary(card => card.Id, StringComparer.Ordinal);

        var mergedBuffs = new Dictionary<string, BuffDto>(StringComparer.Ordinal);
        foreach (var (id, buff) in buffs ?? new Dictionary<string, BuffDto>(StringComparer.Ordinal))
            mergedBuffs[id] = buff;
        foreach (var (id, buff) in extraBuffs ?? new Dictionary<string, BuffDto>(StringComparer.Ordinal))
            mergedBuffs[id] = buff;

        var mergedEffects = new Dictionary<string, EffectDto>(StringComparer.Ordinal);
        foreach (var (id, effect) in effects ?? new Dictionary<string, EffectDto>(StringComparer.Ordinal))
            mergedEffects[id] = effect;
        foreach (var (id, effect) in extraEffects ?? new Dictionary<string, EffectDto>(StringComparer.Ordinal))
            mergedEffects[id] = effect;

        return CombatTestHelper.CreateFullRegistry(
            cards: cardDict,
            buffs: mergedBuffs,
            effects: mergedEffects,
            gameplayEffects: gameplayEffects);
    }

    /// <summary>只有暴风 buff / 效果 + 一张合成卡的 registry（P1 之外的暴风用例都用它）。</summary>
    private static GameDefinitionRegistry StormRegistry() => CombatTestHelper.CreateFullRegistry(
        cards: new Dictionary<string, CardDto>(StringComparer.Ordinal) { [BlueCard] = ElementCard(BlueCard, EElement.Blue) },
        buffs: StormBuffDefinition(),
        effects: StormEffects());

    private static CombatSimulation HandSimulation(GameDefinitionRegistry registry, int characters)
    {
        var attrs = Attributes();
        var party = Enumerable.Range(0, characters)
            .Select(index => CharacterBattleInstance.CreateForTests(
                $"c{index}",
                attrs,
                drawPile: Enumerable.Range(0, CombatConstants.HandSlotCount)
                    .Select(slot => new CardRuntimeEntry(BlueCard, $"rt-{index}-{slot}")),
                element: EElement.Blue,
                race: ERace.Human | ERace.Academic))
            .ToArray();
        foreach (var character in party)
        {
            character.DrawCards(CombatConstants.HandSlotCount);
            character.RefillAvailableEnergy();
        }

        return NewSimulation(registry, party, [new EnemyUnit("e0", "slime", 500)]);
    }

    /// <summary>P3 用的四人队（筛选项：自身 / 红 / 人类 / 学术）：红·人类·学术、红·动物、蓝·人类、绿·动物。</summary>
    private static CombatSimulation SkillProgressSimulation(GameDefinitionRegistry registry)
    {
        var attrs = Attributes();
        var party = new[]
        {
            CharacterBattleInstance.CreateForTests(
                "c0", attrs, skillCounterCap: 30, element: EElement.Red, race: ERace.Human | ERace.Academic),
            CharacterBattleInstance.CreateForTests(
                "c1", attrs, skillCounterCap: 30, element: EElement.Red, race: ERace.Animal),
            CharacterBattleInstance.CreateForTests(
                "c2", attrs, skillCounterCap: 30, element: EElement.Blue, race: ERace.Human),
            CharacterBattleInstance.CreateForTests(
                "c3", attrs, skillCounterCap: 30, element: EElement.Green, race: ERace.Animal),
        };

        return NewSimulation(registry, party, [new EnemyUnit("e0", "slime", 500)]);
    }

    /// <summary>
    /// P4 用的四人队（P4 条件 = 红属性·人类·学术）：
    /// 槽 0 = 红·人类·学术（命中）、槽 1 = 蓝·人类·学术（元素不命中）、槽 2 = 红·动物（种族不命中）；
    /// <paramref name="machineAlly"/> 打开时槽 3 是机械族（用于翻倍层的队伍人数门闩）。
    /// </summary>
    private static CombatSimulation PartySimulation(GameDefinitionRegistry registry, bool machineAlly = false)
    {
        var attrs = Attributes();
        var party = new[]
        {
            CharacterBattleInstance.CreateForTests("c0", attrs, element: EElement.Red, race: ERace.Human | ERace.Academic),
            CharacterBattleInstance.CreateForTests("c1", attrs, element: EElement.Blue, race: ERace.Human | ERace.Academic),
            CharacterBattleInstance.CreateForTests("c2", attrs, element: EElement.Red, race: ERace.Animal),
            CharacterBattleInstance.CreateForTests(
                "c3",
                attrs,
                element: EElement.Red,
                race: machineAlly ? ERace.Machine : ERace.Animal),
        };

        return NewSimulation(registry, party, [new EnemyUnit("e0", "slime", 500)]);
    }

    /// <summary>每人一张指定卡（<paramref name="cardByCharacter"/> 按槽序），可选择先标记入队。</summary>
    private static CombatSimulation QueuedCards(
        GameDefinitionRegistry registry,
        IReadOnlyList<string> cardByCharacter,
        bool mark = true,
        int skillCounterCap = 0)
    {
        var attrs = Attributes();
        var party = new CharacterBattleInstance[cardByCharacter.Count];
        for (var i = 0; i < cardByCharacter.Count; i++)
        {
            party[i] = CharacterBattleInstance.CreateForTests(
                $"c{i}",
                attrs,
                drawPile: [new CardRuntimeEntry(cardByCharacter[i], $"rt-{i}")],
                skillCounterCap: skillCounterCap,
                element: EElement.Blue,
                race: ERace.Human | ERace.Academic);
            party[i].DrawCards(1);
            party[i].RefillAvailableEnergy();
        }

        var sim = NewSimulation(registry, party, [new EnemyUnit("e0", "slime", 500)]);
        if (!mark)
            return sim;

        for (var i = 0; i < party.Length; i++)
        {
            var slot = FindSlot(party[i], cardByCharacter[i]);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0), $"槽 {i} 手上没有 {cardByCharacter[i]}");
            var marked = sim.TryApply(new PlayCardCommand(i, slot, [Enemy(0)]));
            Assert.That(marked.Success, Is.True, marked.Error);
        }

        return sim;
    }

    /// <summary>
    /// 先把队伍账本打掉一截（满血时治疗会被上限吃掉），跑完结算后返回账本**净回复量**。
    /// 与"未挂被动"的对照组相减，差即被动本身的治疗贡献。
    /// </summary>
    private static float LedgerHealed(
        GameDefinitionRegistry registry,
        IReadOnlyList<string> cardByCharacter,
        bool withPassive)
    {
        using var sim = QueuedCards(registry, cardByCharacter);
        if (withPassive)
            sim.Buffs.Apply(sim, Player(0), "von_neumann_passive_p2");

        sim.PlayerTeam.ApplySharedDamage(LedgerDamage);
        var before = sim.PlayerTeam.Asc.GetCurrentValue(AttributeIds.Health);
        sim.TransitionTo(ECombatPhase.CardExecution);
        sim.AdvancePhase();
        return sim.PlayerTeam.Asc.GetCurrentValue(AttributeIds.Health) - before;
    }

    private static CombatSimulation EnemyPhaseSimulation(GameDefinitionRegistry registry, int actionCount)
    {
        var attrs = Attributes();
        var party = new[]
        {
            CharacterBattleInstance.CreateForTests("c0", attrs, element: EElement.Blue, race: ERace.Human | ERace.Academic),
        };
        var sim = NewSimulation(
            registry,
            party,
            [new EnemyUnit("e0", "slime", 500)],
            initialPhase: ECombatPhase.Enemy);
        sim.EnemyTeam.Enemies[0].ActionCount = actionCount;
        return sim;
    }

    /// <summary>领域用例：施法者本身是红·人类·学术（命中），队友蓝·动物（种族与元素都不命中）；敌人一命中一不命中。</summary>
    private static CombatSimulation DomainSimulation(GameDefinitionRegistry registry)
    {
        var attrs = Attributes();
        var party = new[]
        {
            CharacterBattleInstance.CreateForTests(
                CharacterId,
                attrs,
                activeSkillChain: [new ActiveSkillChainEntryDto { SkillId = ActiveSkillId, Cooldown = 10 }],
                element: EElement.Red,
                race: ERace.Human | ERace.Academic),
            CharacterBattleInstance.CreateForTests("c1", attrs, element: EElement.Blue, race: ERace.Animal),
        };
        party[0].GainSkillCounter(10);

        var enemies = new[]
        {
            new EnemyUnit("e0", "slime", EnemyAttributes(500), EElement.Red, ERace.Human | ERace.Academic),
            new EnemyUnit("e1", "slime", EnemyAttributes(500), EElement.Blue, ERace.Animal),
        };

        return NewSimulation(registry, party, enemies);
    }

    private static CombatSimulation NewSimulation(
        GameDefinitionRegistry registry,
        CharacterBattleInstance[] characters,
        EnemyUnit[] enemies,
        ECombatPhase initialPhase = ECombatPhase.Player) =>
        new(
            new PlayerTeamState(characters, sharedMaxHp: 400),
            new EnemyTeamState(enemies),
            new CombatRuleEngine([]),
            registry,
            initialPhase: initialPhase,
            runSeed: 20260923);

    private static int FindSlot(CharacterBattleInstance character, string cardId)
    {
        for (var i = 0; i < character.HandSlots.Count; i++)
        {
            if (string.Equals(character.HandSlots[i].CardId, cardId, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static int FindEmptySlot(CharacterBattleInstance character)
    {
        for (var i = 0; i < character.HandSlots.Count; i++)
        {
            if (character.HandSlots[i].IsEmpty)
                return i;
        }

        return -1;
    }

    #endregion
}