using KemoCard.Frame.Condition;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// BuffInstance 运行时：叠层、修正聚合、条件休眠、驱散 tag 规则、时长 tick、
/// 槽位 buff（伤害/充能）与连携批量定档的端到端行为。
/// </summary>
[TestFixture]
public sealed class BuffRuntimeTests
{
    private static readonly Dictionary<string, float> BaseAttrs = new(StringComparer.Ordinal)
    {
        [AttributeIds.MaxHealth] = 50,
        [AttributeIds.PhysicalAttack] = 10,
    };

    /// <summary>
    /// 条件域是进程级静态状态：显式注册内置条件，避免 IdentityMatch 等条件求值依赖其它 fixture
    /// 的执行顺序（注册表为空时条件恒不通过）。
    /// </summary>
    [OneTimeSetUp]
    public void RegisterCombatConditions()
    {
        ConditionDomains.Combat.Clear();
        BuiltinCombatConditions.RegisterAll(ConditionDomains.Combat);
    }

    #region 构造辅助

    private static CombatSimulation BuildSim(
        Action<GameDefinitionRegistry>? extend = null,
        CharacterBattleInstance[]? characters = null,
        int enemyHp = 100)
    {
        var registry = new GameDefinitionRegistry();
        extend?.Invoke(registry);
        var team = new PlayerTeamState(
            characters ?? [CharacterBattleInstance.CreateForTests("c0", BaseAttrs)],
            sharedMaxHp: 200);
        var enemyTeam = new EnemyTeamState([new EnemyUnit("e0", "slime", maxHp: enemyHp)]);
        return new CombatSimulation(team, enemyTeam, new CombatRuleEngine([]), registry);
    }

    private static BuffDto StatBuff(string id = "buff.stat", int addAttack = 6, int maxStacks = 1) => new()
    {
        Id = id,
        MaxStacks = maxStacks,
        StackRule = EBuffStackRule.Add,
        DurationType = EBuffDurationType.Permanent,
        Modifiers =
        [
            new AttributeModifierDefDto
            {
                AttributeId = AttributeIds.PhysicalAttack,
                Operation = EAttributeModifierOp.Add,
                Magnitude = new MagnitudeDefDto { Kind = EMagnitudeKind.Scalar, Scalar = addAttack },
            },
        ],
    };

    #endregion

    #region 投放与叠层

    [Test]
    public void Apply_with_unknown_buff_definition_fails_softly_without_mounting()
    {
        using var sim = BuildSim();
        var buff = StatBuff();

        // registry 里没有该 buff 定义 → 投放失败（软失败），容器保持为空；
        // 正路径（注册定义后挂载改属性）见 Apply_with_registered_definition_modifies_attribute。
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), buff.Id);

        Assert.That(sim.PlayerTeam.Characters[0].Buffs.All, Is.Empty);
    }

    [Test]
    public void Apply_with_registered_definition_modifies_attribute()
    {
        var buff = StatBuff();
        using var sim = BuildSim(registry => CombatTestHelper.RebuildInto(
            registry,
            buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }));
        var before = sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalAttack);

        var result = sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), buff.Id);

        Assert.That(result.Success, Is.True);
        Assert.That(
            sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalAttack),
            Is.EqualTo(before + 6));
    }

    [Test]
    public void Add_stack_multiplies_modifier_and_caps_at_max()
    {
        var buff = StatBuff(maxStacks: 3);
        using var sim = BuildSim(registry => CombatTestHelper.RebuildInto(
            registry,
            buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }));
        var target = new CombatTargetRef(ECombatSide.Player, 0);
        var before = sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalAttack);

        sim.Buffs.Apply(sim, target, buff.Id);
        sim.Buffs.Apply(sim, target, buff.Id);
        sim.Buffs.Apply(sim, target, buff.Id);
        sim.Buffs.Apply(sim, target, buff.Id);

        var instance = sim.PlayerTeam.Characters[0].Buffs.Find(buff.Id);
        Assert.That(instance!.Stacks, Is.EqualTo(3), "Add 叠层封顶 MaxStacks");
        Assert.That(
            sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalAttack),
            Is.EqualTo(before + 18),
            "修正幅度随层数翻倍");
    }

    [Test]
    public void AllAllies_scope_expands_to_every_character()
    {
        var teamBuff = new BuffDto
        {
            Id = "buff.team",
            DurationType = EBuffDurationType.Permanent,
            ApplyScope = EBuffApplyScope.AllAllies,
            Modifiers = StatBuff().Modifiers,
        };
        var characters = Enumerable.Range(0, 4)
            .Select(i => CharacterBattleInstance.CreateForTests($"c{i}", BaseAttrs))
            .ToArray();
        using var sim = BuildSim(
            registry => CombatTestHelper.RebuildInto(
                registry,
                buffs: new Dictionary<string, BuffDto> { [teamBuff.Id] = teamBuff }),
            characters);

        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), teamBuff.Id);

        foreach (var character in sim.PlayerTeam.Characters)
        {
            Assert.That(character.Buffs.Find(teamBuff.Id), Is.Not.Null, "团队 buff 展开到每个队友");
        }
    }

    #endregion

    #region 条件休眠

    [Test]
    public void Condition_unmet_buff_is_dormant_and_contributes_nothing()
    {
        var buff = new BuffDto
        {
            Id = "buff.conditional",
            DurationType = EBuffDurationType.Permanent,
            ApplyScope = EBuffApplyScope.AllAllies,
            Conditions = [IdentityMatch(elementAny: [EElement.Blue], raceAny: [ERace.Animal])],
            Modifiers = StatBuff().Modifiers,
        };
        var blueHuman = CharacterBattleInstance.CreateForTests("blue", BaseAttrs, element: EElement.Blue);
        var redHuman = CharacterBattleInstance.CreateForTests("red", BaseAttrs, element: EElement.Red);
        var characters = new[] { blueHuman, redHuman };
        using var sim = BuildSim(
            registry => CombatTestHelper.RebuildInto(
                registry,
                buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }),
            characters);

        var attackBeforeBlue = blueHuman.Asc.GetCurrentValue(AttributeIds.PhysicalAttack);
        var attackBeforeRed = redHuman.Asc.GetCurrentValue(AttributeIds.PhysicalAttack);
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), buff.Id);

        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(buff.Id)!.IsDormant, Is.False, "蓝属性命中条件 → 激活");
        Assert.That(sim.PlayerTeam.Characters[1].Buffs.Find(buff.Id)!.IsDormant, Is.True, "红属性不命中 → 休眠");
        Assert.That(blueHuman.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(attackBeforeBlue + 6));
        Assert.That(redHuman.Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(attackBeforeRed), "休眠 buff 不参与聚合");
    }

    /// <summary>描述约定（2026-09-25）：元素/种族维度**默认取"或"**（`·` = 或）。</summary>
    [Test]
    public void Condition_dimensions_default_to_or()
    {
        var buff = ConditionalAttackBuff(
            "buff.or",
            IdentityMatch(elementAny: [EElement.Blue], raceAny: [ERace.Animal]));
        var blueHuman = CharacterBattleInstance.CreateForTests("blue_human", BaseAttrs, element: EElement.Blue, race: ERace.Human);
        var redAnimal = CharacterBattleInstance.CreateForTests("red_animal", BaseAttrs, element: EElement.Red, race: ERace.Animal);
        var blueAnimal = CharacterBattleInstance.CreateForTests("blue_animal", BaseAttrs, element: EElement.Blue, race: ERace.Animal);
        var redHuman = CharacterBattleInstance.CreateForTests("red_human", BaseAttrs, element: EElement.Red, race: ERace.Human);
        using var sim = BuildSim(
            registry => CombatTestHelper.RebuildInto(
                registry,
                buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }),
            [blueHuman, redAnimal, blueAnimal, redHuman]);

        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), buff.Id);

        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(buff.Id)!.IsDormant, Is.False, "蓝·人类：命中元素");
        Assert.That(sim.PlayerTeam.Characters[1].Buffs.Find(buff.Id)!.IsDormant, Is.False, "红·动物：命中种族");
        Assert.That(sim.PlayerTeam.Characters[2].Buffs.Find(buff.Id)!.IsDormant, Is.False, "蓝·动物：两项都命中");
        Assert.That(sim.PlayerTeam.Characters[3].Buffs.Find(buff.Id)!.IsDormant, Is.True, "红·人类：两项都不命中");
    }

    /// <summary>描述里显式写「且」时才用 `matchAll: true`：每个已配置维度都必须命中。</summary>
    [Test]
    public void Condition_match_all_requires_every_configured_dimension()
    {
        var buff = ConditionalAttackBuff(
            "buff.match_all",
            IdentityMatch(elementAny: [EElement.Blue], raceAny: [ERace.Animal], matchAll: true));
        var blueAnimal = CharacterBattleInstance.CreateForTests("blue_animal", BaseAttrs, element: EElement.Blue, race: ERace.Animal);
        var blueHuman = CharacterBattleInstance.CreateForTests("blue_human", BaseAttrs, element: EElement.Blue, race: ERace.Human);
        var redAnimal = CharacterBattleInstance.CreateForTests("red_animal", BaseAttrs, element: EElement.Red, race: ERace.Animal);
        using var sim = BuildSim(
            registry => CombatTestHelper.RebuildInto(
                registry,
                buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }),
            [blueAnimal, blueHuman, redAnimal]);

        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), buff.Id);

        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(buff.Id)!.IsDormant, Is.False, "蓝·动物：两项都命中");
        Assert.That(sim.PlayerTeam.Characters[1].Buffs.Find(buff.Id)!.IsDormant, Is.True, "蓝·人类：缺动物 → 休眠");
        Assert.That(sim.PlayerTeam.Characters[2].Buffs.Find(buff.Id)!.IsDormant, Is.True, "红·动物：缺蓝 → 休眠");
    }

    /// <summary>`raceAll` = 显式「人类且学术」：列表内取且，只带其一不命中。</summary>
    [Test]
    public void Condition_race_all_requires_every_listed_race()
    {
        var buff = ConditionalAttackBuff(
            "buff.race_all",
            IdentityMatch(raceAll: [ERace.Human, ERace.Academic]));
        var both = CharacterBattleInstance.CreateForTests("both", BaseAttrs, race: ERace.Human | ERace.Academic);
        var humanOnly = CharacterBattleInstance.CreateForTests("human", BaseAttrs, race: ERace.Human);
        var academicOnly = CharacterBattleInstance.CreateForTests("academic", BaseAttrs, race: ERace.Academic);
        using var sim = BuildSim(
            registry => CombatTestHelper.RebuildInto(
                registry,
                buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }),
            [both, humanOnly, academicOnly]);

        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), buff.Id);

        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(buff.Id)!.IsDormant, Is.False, "人类·学术：两个都带");
        Assert.That(sim.PlayerTeam.Characters[1].Buffs.Find(buff.Id)!.IsDormant, Is.True, "只带人类 → 休眠");
        Assert.That(sim.PlayerTeam.Characters[2].Buffs.Find(buff.Id)!.IsDormant, Is.True, "只带学术 → 休眠");
    }

    /// <summary>
    /// `PartyCountScaled` 的人数筛选同样遵守描述约定：两个维度默认取"或"，
    /// `matchAll: true` 时只数两项都命中的角色。
    /// </summary>
    [Test]
    public void Party_count_scaled_defaults_to_or_and_match_all_counts_intersection()
    {
        var orBuff = PartyCountBuff("buff.count_or", matchAll: false);
        var andBuff = PartyCountBuff("buff.count_and", matchAll: true);
        var blueHuman = CharacterBattleInstance.CreateForTests("blue_human", BaseAttrs, element: EElement.Blue, race: ERace.Human);
        var redAnimal = CharacterBattleInstance.CreateForTests("red_animal", BaseAttrs, element: EElement.Red, race: ERace.Animal);
        var blueAnimal = CharacterBattleInstance.CreateForTests("blue_animal", BaseAttrs, element: EElement.Blue, race: ERace.Animal);
        var redHuman = CharacterBattleInstance.CreateForTests("red_human", BaseAttrs, element: EElement.Red, race: ERace.Human);
        using var sim = BuildSim(
            registry => CombatTestHelper.RebuildInto(
                registry,
                buffs: new Dictionary<string, BuffDto> { [orBuff.Id] = orBuff, [andBuff.Id] = andBuff }),
            [blueHuman, redAnimal, blueAnimal, redHuman]);

        var attackBefore = blueHuman.Asc.GetCurrentValue(AttributeIds.PhysicalAttack);
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), orBuff.Id);
        Assert.That(
            blueHuman.Asc.GetCurrentValue(AttributeIds.PhysicalAttack),
            Is.EqualTo(attackBefore + 9),
            "默认取或：蓝·人类 + 红·动物 + 蓝·动物 = 3 人 × 3");

        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), andBuff.Id);
        Assert.That(
            blueHuman.Asc.GetCurrentValue(AttributeIds.PhysicalAttack),
            Is.EqualTo(attackBefore + 12),
            "matchAll 取且：只有蓝·动物命中 = 1 人 × 3（叠加在 9 之上）");
    }

    private static BuffDto ConditionalAttackBuff(string id, ConditionRefDto condition) => new()
    {
        Id = id,
        DurationType = EBuffDurationType.Permanent,
        ApplyScope = EBuffApplyScope.AllAllies,
        Conditions = [condition],
        Modifiers = StatBuff().Modifiers,
    };

    /// <summary>通用身份条件（IdentityMatch）的测试构造器：参数即内容 JSON 的同名字段。</summary>
    private static ConditionRefDto IdentityMatch(
        IReadOnlyList<EElement>? elementAny = null,
        IReadOnlyList<ERace>? raceAny = null,
        IReadOnlyList<ERace>? raceAll = null,
        bool matchAll = false,
        int partyMinCount = 0,
        IReadOnlyList<EElement>? partyElementAny = null,
        IReadOnlyList<ERace>? partyRaceAny = null)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        if (elementAny is { Count: > 0 })
            parameters["elementAny"] = elementAny.Select(value => value.ToString()).ToArray();
        if (raceAny is { Count: > 0 })
            parameters["raceAny"] = raceAny.Select(value => value.ToString()).ToArray();
        if (raceAll is { Count: > 0 })
            parameters["raceAll"] = raceAll.Select(value => value.ToString()).ToArray();
        if (matchAll)
            parameters["matchAll"] = true;
        if (partyMinCount > 0)
            parameters["partyMinCount"] = partyMinCount;
        if (partyElementAny is { Count: > 0 })
            parameters["partyElementAny"] = partyElementAny.Select(value => value.ToString()).ToArray();
        if (partyRaceAny is { Count: > 0 })
            parameters["partyRaceAny"] = partyRaceAny.Select(value => value.ToString()).ToArray();

        return new ConditionRefDto { Kind = "IdentityMatch", Params = parameters };
    }

    private static BuffDto PartyCountBuff(string id, bool matchAll) => new()
    {
        Id = id,
        DurationType = EBuffDurationType.Permanent,
        Modifiers =
        [
            new AttributeModifierDefDto
            {
                AttributeId = AttributeIds.PhysicalAttack,
                Operation = EAttributeModifierOp.Add,
                Magnitude = new MagnitudeDefDto
                {
                    Kind = EMagnitudeKind.PartyCountScaled,
                    PerCount = 3,
                    CountElementAny = [EElement.Blue],
                    CountRaceAny = [ERace.Animal],
                    MatchAll = matchAll,
                },
            },
        ],
    };

    #endregion

    #region 驱散 tag 规则

    [Test]
    public void Dispel_skips_undispellable_tag_and_removes_others()
    {
        var plain = StatBuff(id: "buff.plain");
        var locked = new BuffDto
        {
            Id = "buff.locked",
            DurationType = EBuffDurationType.Permanent,
            Tags = [BuiltinBuffTags.Undispellable],
        };
        using var sim = BuildSim(registry => CombatTestHelper.RebuildInto(
            registry,
            buffs: new Dictionary<string, BuffDto>
            {
                [plain.Id] = plain,
                [locked.Id] = locked,
            }));
        var target = new CombatTargetRef(ECombatSide.Player, 0);
        sim.Buffs.Apply(sim, target, plain.Id);
        sim.Buffs.Apply(sim, target, locked.Id);

        var removedPlain = sim.Buffs.Dispel(sim, target, buffId: plain.Id);
        var removedLocked = sim.Buffs.Dispel(sim, target, buffId: locked.Id);

        Assert.That(removedPlain, Is.EqualTo(1), "无不可驱散 tag 的 buff 可被驱散");
        Assert.That(removedLocked, Is.EqualTo(0), "带不可驱散 tag 的 buff 被跳过");
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(locked.Id), Is.Not.Null);
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(plain.Id), Is.Null);
    }

    [Test]
    public void Legacy_dispellable_false_normalizes_to_undispellable_tag()
    {
        var dto = new BuffDto { Id = "b", LegacyDispellable = false };
        Assert.That(dto.EffectiveTags, Does.Contain(BuiltinBuffTags.Undispellable));

        var fresh = new BuffDto { Id = "b", LegacyDispellable = true };
        Assert.That(fresh.EffectiveTags, Does.Not.Contain(BuiltinBuffTags.Undispellable));
    }

    #endregion

    #region 时长 tick

    [Test]
    public void Turns_buff_expires_after_n_turn_ends()
    {
        var buff = new BuffDto
        {
            Id = "buff.timed",
            DurationType = EBuffDurationType.Turns,
            Duration = 2,
            Modifiers = StatBuff().Modifiers,
        };
        using var sim = BuildSim(registry => CombatTestHelper.RebuildInto(
            registry,
            buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }));
        var target = new CombatTargetRef(ECombatSide.Player, 0);
        sim.Buffs.Apply(sim, target, buff.Id);
        var before = sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalAttack);

        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(buff.Id), Is.Not.Null, "第 1 回合结束仍在");
        Assert.That(sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalAttack), Is.EqualTo(before));

        sim.Buffs.FireTurnEnd(sim);
        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(buff.Id), Is.Null, "第 2 回合结束到期移除");
        Assert.That(
            sim.PlayerTeam.Characters[0].Asc.GetCurrentValue(AttributeIds.PhysicalAttack),
            Is.EqualTo(before - 6),
            "移除后修正撤销");
    }

    /// <summary>
    /// P0 回归（2026-09-19）：onTurnStart 钩子往<b>同一容器</b>追加 buff。
    /// 分发必须走快照——活列表枚举中修改会抛 InvalidOperationException（.NET 不允许）。
    /// </summary>
    [Test]
    public void TurnStart_hook_appending_to_same_container_completes_without_throwing()
    {
        var grow = new EffectDto
        {
            Id = "effect.grow",
            Kind = EEffectKind.ApplyBuff,
            Params = new Dictionary<string, object> { ["buffId"] = "buff.seed" },
        };
        var spawner = new BuffDto
        {
            Id = "buff.spawner",
            DurationType = EBuffDurationType.Permanent,
            Hooks = new BuffEffectHooksDto
            {
                OnTurnStart = [new EffectRefDto { EffectId = grow.Id }],
            },
        };
        var seed = StatBuff("buff.seed");
        using var sim = BuildSim(registry => CombatTestHelper.RebuildInto(
            registry,
            buffs: new Dictionary<string, BuffDto> { [spawner.Id] = spawner, [seed.Id] = seed },
            effects: new Dictionary<string, EffectDto> { [grow.Id] = grow }));
        var target = new CombatTargetRef(ECombatSide.Player, 0);
        sim.Buffs.Apply(sim, target, spawner.Id);

        Assert.DoesNotThrow(() => sim.Buffs.FireTurnStart(sim));

        var container = sim.PlayerTeam.Characters[0].Buffs;
        Assert.That(container.Find(spawner.Id), Is.Not.Null, "钩子触发后源 buff 仍在");
        Assert.That(container.Find(seed.Id), Is.Not.Null, "钩子挂载的新 buff 已就位");
    }

    /// <summary>
    /// P0 回归（2026-09-19）：同回合到期的两个 buff，前者的 onRemove 驱散了后者。
    /// 后者在到期补发时已不在容器：必须跳过（否则 onRemove 双触发）；
    /// 用 onRemove 挂载计数 marker buff 观察触发次数。
    /// </summary>
    [Test]
    public void TurnEnd_expiry_skips_instance_already_dispersed_by_earlier_onRemove()
    {
        var cleanse = new EffectDto
        {
            Id = "effect.cleanse",
            Kind = EEffectKind.RemoveBuff,
            Params = new Dictionary<string, object> { ["withTags"] = new List<string> { "test.dispel" } },
        };
        var mark = new EffectDto
        {
            Id = "effect.mark",
            Kind = EEffectKind.ApplyBuff,
            Params = new Dictionary<string, object> { ["buffId"] = "buff.echo" },
        };
        var doomed = new BuffDto
        {
            Id = "buff.doomed",
            DurationType = EBuffDurationType.Turns,
            Duration = 1,
            Hooks = new BuffEffectHooksDto
            {
                OnRemove = [new EffectRefDto { EffectId = cleanse.Id }],
            },
        };
        var bleed = new BuffDto
        {
            Id = "buff.bleed",
            DurationType = EBuffDurationType.Turns,
            Duration = 1,
            Tags = ["test.dispel"],
            Hooks = new BuffEffectHooksDto
            {
                OnRemove = [new EffectRefDto { EffectId = mark.Id }],
            },
        };
        var echo = StatBuff("buff.echo", maxStacks: 5);
        using var sim = BuildSim(registry => CombatTestHelper.RebuildInto(
            registry,
            buffs: new Dictionary<string, BuffDto> { [doomed.Id] = doomed, [bleed.Id] = bleed, [echo.Id] = echo },
            effects: new Dictionary<string, EffectDto> { [cleanse.Id] = cleanse, [mark.Id] = mark }));
        var target = new CombatTargetRef(ECombatSide.Player, 0);
        sim.Buffs.Apply(sim, target, doomed.Id);
        sim.Buffs.Apply(sim, target, bleed.Id);

        Assert.DoesNotThrow(() => sim.Buffs.FireTurnEnd(sim));

        var container = sim.PlayerTeam.Characters[0].Buffs;
        Assert.That(container.Find(doomed.Id), Is.Null, "doomed 到期移除");
        Assert.That(container.Find(bleed.Id), Is.Null, "bleed 被 onRemove 驱散（先于到期补发）");
        var marker = container.Find(echo.Id);
        Assert.That(marker, Is.Not.Null, "bleed 的 onRemove 至少触发一次（marker 已挂载）");
        Assert.That(marker!.Stacks, Is.EqualTo(1), "onRemove 不得双触发：已驱散的实例跳过到期补发");
    }

    #endregion

    #region 槽位 buff：伤害与免疫

    private static GameDefinitionRegistry BuildSlotDamageRegistry()
    {
        var slotDamage = new BuffDto
        {
            Id = "buff.slot_damage",
            DurationType = EBuffDurationType.Permanent,
            Tags = [BuiltinBuffTags.SlotDamage],
            Hooks = new BuffEffectHooksDto
            {
                OnSlotCardPlayed =
                [
                    new EffectRefDto
                    {
                        EffectId = "effect.slot_damage",
                    },
                ],
            },
        };
        var immunity = new BuffDto
        {
            Id = "buff.immunity",
            DurationType = EBuffDurationType.Permanent,
            Tags = [BuiltinBuffTags.TraitImmuneSlotDamage],
        };
        return CombatTestHelper.CreateFullRegistry(
            buffs: new Dictionary<string, BuffDto>
            {
                [slotDamage.Id] = slotDamage,
                [immunity.Id] = immunity,
            },
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.slot_damage"] = new()
                {
                    Id = "effect.slot_damage",
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object> { ["amount"] = 5 },
                },
            });
    }

    [Test]
    public void Slot_damage_buff_deals_shared_hp_damage_on_play()
    {
        var registry = BuildSlotDamageRegistry();
        var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        CombatTestHelper.RebuildInto(
            sim.Definitions,
            buffs: registry.Store.Buffs.ToDictionary(pair => pair.Key, pair => pair.Value),
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.slot_damage"] = new()
                {
                    Id = "effect.slot_damage",
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object> { ["amount"] = 5 },
                },
            });

        var hpBefore = sim.PlayerTeam.SharedHp;
        sim.Buffs.ApplyToSlot(sim, 0, 0, "buff.slot_damage");

        sim.Buffs.FireSlotCardPlayed(sim, 0, sim.PlayerTeam.Characters[0].HandSlots[0]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hpBefore - 5), "槽位伤害结算到共享血量");
    }

    [Test]
    public void Slot_damage_is_immune_when_holder_has_trait_tag()
    {
        var registry = BuildSlotDamageRegistry();
        var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        CombatTestHelper.RebuildInto(
            sim.Definitions,
            buffs: registry.Store.Buffs.ToDictionary(pair => pair.Key, pair => pair.Value),
            effects: new Dictionary<string, EffectDto>
            {
                ["effect.slot_damage"] = new()
                {
                    Id = "effect.slot_damage",
                    Kind = EEffectKind.Damage,
                    Params = new Dictionary<string, object> { ["amount"] = 5 },
                },
            });

        var hpBefore = sim.PlayerTeam.SharedHp;
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), "buff.immunity");
        sim.Buffs.ApplyToSlot(sim, 0, 0, "buff.slot_damage");

        sim.Buffs.FireSlotCardPlayed(sim, 0, sim.PlayerTeam.Characters[0].HandSlots[0]);

        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(hpBefore), "持有免疫特征 tag 的角色不受槽位伤害");
    }

    #endregion

    #region 槽位 buff：充能

    [Test]
    public void Charge_buff_fires_payload_on_threshold_and_expires_after_duration()
    {
        var charge = new BuffDto
        {
            Id = "buff.charge",
            DurationType = EBuffDurationType.Turns,
            Duration = 3,
            Tags = [BuiltinBuffTags.SlotCharge],
            Hooks = new BuffEffectHooksDto
            {
                OnSlotCardPlayed =
                [
                    new EffectRefDto
                    {
                        EffectId = "effect.charge_burst",
                        Params = new Dictionary<string, object> { ["hookTargets"] = "randomEnemy" },
                    },
                ],
            },
        };
        using var sim = BuildSim(
            registry => CombatTestHelper.RebuildInto(
                registry,
                buffs: new Dictionary<string, BuffDto> { [charge.Id] = charge },
                effects: new Dictionary<string, EffectDto>
                {
                    ["effect.charge_burst"] = new()
                    {
                        Id = "effect.charge_burst",
                        Kind = EEffectKind.Damage,
                        Params = new Dictionary<string, object> { ["amount"] = 12 },
                    },
                }),
            enemyHp: 100);

        sim.Buffs.ApplyToSlot(sim, 0, 1, charge.Id);
        var slot = sim.PlayerTeam.Characters[0].HandSlots[1];
        var enemyHpBefore = sim.EnemyTeam.Enemies[0].CurrentHp;

        // 充能 I：打出一张即触发并重置。
        sim.Buffs.FireSlotCardPlayed(sim, 0, slot);
        Assert.That(sim.EnemyTeam.Enemies[0].CurrentHp, Is.EqualTo(enemyHpBefore - 12), "充能 I 触发载荷");

        sim.Buffs.FireSlotCardPlayed(sim, 0, slot);
        Assert.That(sim.EnemyTeam.Enemies[0].CurrentHp, Is.EqualTo(enemyHpBefore - 24), "触发后重置，可再次触发");

        // 3 回合到期移除，之后不再触发。
        sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireTurnEnd(sim);
        sim.Buffs.FireTurnEnd(sim);
        Assert.That(slot.Buffs.Find(charge.Id), Is.Null, "3 回合后到期移除");

        sim.Buffs.FireSlotCardPlayed(sim, 0, slot);
        Assert.That(sim.EnemyTeam.Enemies[0].CurrentHp, Is.EqualTo(enemyHpBefore - 24), "移除后不再触发");
    }

    #endregion

    #region 连携批量定档

    private static CardDto ElementCard(string id, EElement element, ECardType type = ECardType.Physics) => new()
    {
        Id = id,
        DisplayNameId = id,
        Element = (int)element,
        CardType = type,
        TargetSide = ETargetSide.Enemy,
        TargetScope = ETargetScope.Single,
        TargetCount = 1,
        Priority = 1,
    };

    [Test]
    public void Chain_counts_distinct_characters_per_element()
    {
        var blue = ElementCard("card.blue", EElement.Blue);
        var red = ElementCard("card.red", EElement.Red);
        var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        CombatTestHelper.RebuildInto(
            sim.Definitions,
            cards: new Dictionary<string, CardDto> { [blue.Id] = blue, [red.Id] = red });

        sim.CardQueue.Enqueue(new QueuedCardEntry(0, blue.Id, "rt1", 1, [], sim.AllocateQueueSequence()));
        sim.CardQueue.Enqueue(new QueuedCardEntry(1, blue.Id, "rt2", 1, [], sim.AllocateQueueSequence()));
        sim.CardQueue.Enqueue(new QueuedCardEntry(0, blue.Id, "rt3", 1, [], sim.AllocateQueueSequence()));
        sim.CardQueue.Enqueue(new QueuedCardEntry(2, red.Id, "rt4", 1, [], sim.AllocateQueueSequence()));

        var counts = ChainCalculator.CountDistinctCharacters(sim);

        Assert.That(counts.GetValueOrDefault(EElement.Blue), Is.EqualTo(2), "同一角色多张只计 1 人");
        Assert.That(counts.GetValueOrDefault(EElement.Red), Is.EqualTo(1));

        // 档位数值是可调平衡参数，断言对比常量而不是写死百分比；本用例只钉结构（1 人无增益 / ≥4 封顶 / 单调递增）。
        Assert.That(ChainCalculator.TierScale(0), Is.Zero);
        Assert.That(ChainCalculator.TierScale(1), Is.Zero, "1 人无增益");
        Assert.That(ChainCalculator.TierScale(2), Is.EqualTo(ChainCalculator.TwoChainScale));
        Assert.That(ChainCalculator.TierScale(3), Is.EqualTo(ChainCalculator.ThreeChainScale));
        Assert.That(ChainCalculator.TierScale(4), Is.EqualTo(ChainCalculator.FourChainScale));
        Assert.That(ChainCalculator.TierScale(9), Is.EqualTo(ChainCalculator.FourChainScale), "≥4 人封顶");
        Assert.That(ChainCalculator.TwoChainScale, Is.LessThan(ChainCalculator.ThreeChainScale));
        Assert.That(ChainCalculator.ThreeChainScale, Is.LessThan(ChainCalculator.FourChainScale));
    }

    [Test]
    public void Chain_element_inject_adds_red_counting_for_holder_cards()
    {
        var blueCard = ElementCard("card.blue2", EElement.Blue);
        var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        CombatTestHelper.RebuildInto(
            sim.Definitions,
            cards: new Dictionary<string, CardDto> { [blueCard.Id] = blueCard });

        // 手工把 StandardPlayerPhase 的角色换成带注入的角色不可行（team 不可变），
        // 改用注入的 buff 走正式 Apply 通道（2026-09-26 参数化，取代 trait tag）。
        var inject = new BuffDto
        {
            Id = "buff.inject",
            DurationType = EBuffDurationType.Permanent,
            ChainElementInject = [new ChainElementInjectDto { Add = [EElement.Red] }],
        };
        CombatTestHelper.RebuildInto(
            sim.Definitions,
            cards: new Dictionary<string, CardDto> { [blueCard.Id] = blueCard },
            buffs: new Dictionary<string, BuffDto> { [inject.Id] = inject });
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 1), inject.Id);

        sim.CardQueue.Enqueue(new QueuedCardEntry(0, blueCard.Id, "rt-a", 1, [], sim.AllocateQueueSequence()));
        sim.CardQueue.Enqueue(new QueuedCardEntry(1, blueCard.Id, "rt-b", 1, [], sim.AllocateQueueSequence()));

        var counts = ChainCalculator.CountDistinctCharacters(sim);

        Assert.That(counts.GetValueOrDefault(EElement.Blue), Is.EqualTo(2));
        Assert.That(counts.GetValueOrDefault(EElement.Red), Is.EqualTo(1), "被动2：持有者的蓝卡计入红属性统计（暂 1 人）");

        var bonusForHolder = ChainCalculator.BonusForCard(counts, blueCard, sim.PlayerTeam.Characters[1]);
        Assert.That(bonusForHolder, Is.EqualTo(ChainCalculator.TwoChainScale), "持有者的蓝卡取蓝/红中的最高档（蓝 2 人 = 二连档）");
    }

    [Test]
    public void Chain_element_inject_maps_from_element_to_added_element()
    {
        var yellowCard = ElementCard("card.yellow2", EElement.Yellow);
        var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        CombatTestHelper.RebuildInto(
            sim.Definitions,
            cards: new Dictionary<string, CardDto> { [yellowCard.Id] = yellowCard });

        // 参数化注入（2026-09-26）：含黄属性的卡额外计入蓝属性（取代 trait.chain_yellow_counts_blue）。
        var inject = new BuffDto
        {
            Id = "buff.inject_yellow_blue",
            DurationType = EBuffDurationType.Permanent,
            ChainElementInject = [new ChainElementInjectDto { From = [EElement.Yellow], Add = [EElement.Blue] }],
        };
        CombatTestHelper.RebuildInto(
            sim.Definitions,
            cards: new Dictionary<string, CardDto> { [yellowCard.Id] = yellowCard },
            buffs: new Dictionary<string, BuffDto> { [inject.Id] = inject });
        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 1), inject.Id);

        sim.CardQueue.Enqueue(new QueuedCardEntry(0, yellowCard.Id, "rt-a", 1, [], sim.AllocateQueueSequence()));
        sim.CardQueue.Enqueue(new QueuedCardEntry(1, yellowCard.Id, "rt-b", 1, [], sim.AllocateQueueSequence()));

        var counts = ChainCalculator.CountDistinctCharacters(sim);

        Assert.That(counts.GetValueOrDefault(EElement.Yellow), Is.EqualTo(2));
        Assert.That(counts.GetValueOrDefault(EElement.Blue), Is.EqualTo(1), "from=Yellow add=Blue：只有持有者的黄卡计入蓝");
    }

    /// <summary>
    /// IdentityMatch 的队伍人数门闩（2026-09-26 统一）：与主体维度取"且"，
    /// 只配人数时完全由人数决定。
    /// </summary>
    [Test]
    public void Identity_party_gate_requires_min_count_and_holder_match()
    {
        var buff = new BuffDto
        {
            Id = "buff.party_gate",
            DurationType = EBuffDurationType.Permanent,
            ApplyScope = EBuffApplyScope.AllAllies,
            Conditions =
            [
                IdentityMatch(
                    elementAny: [EElement.Blue],
                    partyMinCount: 2,
                    partyElementAny: [EElement.Blue]),
            ],
            Modifiers = StatBuff().Modifiers,
        };
        var blueA = CharacterBattleInstance.CreateForTests("blue_a", BaseAttrs, element: EElement.Blue);
        var blueB = CharacterBattleInstance.CreateForTests("blue_b", BaseAttrs, element: EElement.Blue);
        var red = CharacterBattleInstance.CreateForTests("red", BaseAttrs, element: EElement.Red);
        using var sim = BuildSim(
            registry => CombatTestHelper.RebuildInto(
                registry,
                buffs: new Dictionary<string, BuffDto> { [buff.Id] = buff }),
            [blueA, blueB, red]);

        sim.Buffs.Apply(sim, new CombatTargetRef(ECombatSide.Player, 0), buff.Id);

        Assert.That(sim.PlayerTeam.Characters[0].Buffs.Find(buff.Id)!.IsDormant, Is.False, "蓝主体 + 队伍 2 蓝 → 激活");
        Assert.That(sim.PlayerTeam.Characters[1].Buffs.Find(buff.Id)!.IsDormant, Is.False, "第二蓝同样激活");
        Assert.That(sim.PlayerTeam.Characters[2].Buffs.Find(buff.Id)!.IsDormant, Is.True, "红主体不命中（人数够也没用）");
    }

    /// <summary>
    /// 槽位容器没有 ASC 与持有者身份：经 <see cref="BuffRuntime.ApplyToSlot"/> 挂载的身份条件
    /// <b>不按所属角色求值</b>，一律休眠（与 <c>BuffContainer</c> 类注释口径一致）。
    /// </summary>
    [Test]
    public void Slot_buff_identity_condition_stays_dormant_even_when_owner_matches()
    {
        var slotBuff = new BuffDto
        {
            Id = "buff.slot_condition",
            DurationType = EBuffDurationType.Permanent,
            Tags = [BuiltinBuffTags.SlotCharge],
            Conditions = [IdentityMatch(elementAny: [EElement.Green])],
        };
        var green = CharacterBattleInstance.CreateForTests("green", BaseAttrs, element: EElement.Green);
        using var sim = BuildSim(
            registry => CombatTestHelper.RebuildInto(
                registry,
                buffs: new Dictionary<string, BuffDto> { [slotBuff.Id] = slotBuff }),
            [green]);

        var result = sim.Buffs.ApplyToSlot(sim, characterIndex: 0, slotIndex: 0, slotBuff.Id);

        Assert.That(result.Success, Is.True, result.Error);
        var instance = green.HandSlots[0].Buffs.Find(slotBuff.Id);
        Assert.That(instance, Is.Not.Null);
        Assert.That(instance!.IsDormant, Is.True, "槽位 buff 的身份条件不按所属角色求值 → 休眠");
    }

    /// <summary>
    /// 旧扁平 targetFilter（已删除）在运行期保守回退到 [来源]：绝不能静默命中全队。
    /// </summary>
    [Test]
    public void Legacy_flat_target_filter_falls_back_to_source_instead_of_hitting_everyone()
    {
        using var sim = BuildSim(characters:
        [
            CharacterBattleInstance.CreateForTests("blue_a", BaseAttrs, element: EElement.Blue),
            CharacterBattleInstance.CreateForTests("blue_b", BaseAttrs, element: EElement.Blue),
        ]);
        var source = new CombatTargetRef(ECombatSide.Player, 0);
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["targetFilter"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["elementAny"] = new[] { "Blue" },
            },
        };

        var targets = CombatTargetSelector.Resolve(sim, source, parameters);

        Assert.That(targets, Has.Count.EqualTo(1), "未知键 → 回退 [来源]，不是全队");
        Assert.That(targets[0], Is.EqualTo(source));
    }

    /// <summary>
    /// 新写法 <c>targetFilter.condition</c> 正常筛选：IdentityMatch 逐候选判定，excludeSelf 剔除来源。
    /// </summary>
    [Test]
    public void Target_filter_condition_filters_candidates_and_respects_exclude_self()
    {
        using var sim = BuildSim(characters:
        [
            CharacterBattleInstance.CreateForTests("blue_a", BaseAttrs, element: EElement.Blue),
            CharacterBattleInstance.CreateForTests("green", BaseAttrs, element: EElement.Green),
            CharacterBattleInstance.CreateForTests("blue_b", BaseAttrs, element: EElement.Blue),
        ]);
        var source = new CombatTargetRef(ECombatSide.Player, 0);
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["targetFilter"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["excludeSelf"] = true,
                ["condition"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["kind"] = "IdentityMatch",
                    ["params"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["elementAny"] = new[] { "Blue" },
                    },
                },
            },
        };

        var targets = CombatTargetSelector.Resolve(sim, source, parameters);

        Assert.That(
            targets,
            Is.EqualTo(new[] { new CombatTargetRef(ECombatSide.Player, 2) }),
            "只命中非来源的蓝属性候选");
    }

    [Test]
    public void Chain_does_not_apply_to_non_damage_heal_card_types()    {
        var curseCard = ElementCard("card.curse", EElement.Blue, ECardType.Curse);
        Assert.That(ChainCalculator.AppliesToCard(curseCard), Is.False);

        var healCard = ElementCard("card.heal", EElement.Blue, ECardType.Healing);
        Assert.That(ChainCalculator.AppliesToCard(healCard), Is.True);
    }

    /// <summary>
    /// 统计侧统计队列里的<b>所有</b>卡：控制/诅咒等非输出卡同样把它打出的角色计入人头；
    /// 只有加成侧（<see cref="ChainCalculator.AppliesToCard"/>）限定物/魔/治疗卡。
    /// </summary>
    [Test]
    public void Chain_counting_includes_non_output_cards_in_queue()
    {
        var blue = ElementCard("card.blue3", EElement.Blue);
        var curse = ElementCard("card.curse3", EElement.Blue, ECardType.Curse);
        var sim = CombatSimulationTestBuilder.StandardPlayerPhase();
        CombatTestHelper.RebuildInto(
            sim.Definitions,
            cards: new Dictionary<string, CardDto> { [blue.Id] = blue, [curse.Id] = curse });

        sim.CardQueue.Enqueue(new QueuedCardEntry(0, blue.Id, "rt-n0", 1, [], sim.AllocateQueueSequence()));
        sim.CardQueue.Enqueue(new QueuedCardEntry(1, curse.Id, "rt-n1", 1, [], sim.AllocateQueueSequence()));

        var counts = ChainCalculator.CountDistinctCharacters(sim);

        Assert.That(counts.GetValueOrDefault(EElement.Blue), Is.EqualTo(2),
            "诅咒卡也堆人头：2 人即二连档");
        Assert.That(
            ChainCalculator.BonusForCard(counts, curse, sim.PlayerTeam.Characters[1]),
            Is.Zero,
            "但非输出卡自身不吃连携加成");
        Assert.That(
            ChainCalculator.BonusForCard(counts, blue, sim.PlayerTeam.Characters[0]),
            Is.EqualTo(ChainCalculator.TwoChainScale),
            "同一档位下物/魔/治疗卡照常吃加成");
    }

    #endregion

    #region 伤害公式（DamageDealtScale + 连携加算）

    [Test]
    public void Damage_execution_multiplies_dealt_scale_and_chain_additively()
    {
        var source = new AbilitySystemComponent();
        source.SetBaseValue(AttributeIds.PhysicalAttack, 10f);
        source.SetBaseValue(AttributeIds.DamageDealtScale, 0.25f);
        var target = new AbilitySystemComponent();
        target.SetBaseValue(AttributeIds.MaxHealth, 100f);
        target.SetBaseValue(AttributeIds.Health, 100f);
        target.SetBaseValue(AttributeIds.PhysicalDefense, 2f);
        target.SetBaseValue(AttributeIds.DamageTakenScale, 0.5f);

        var spec = new GameplayEffectSpec(
            new GameplayEffectDefDto { Id = "ge.test", DurationPolicy = EDurationPolicy.Instant },
            sourceAsc: source,
            targetAsc: target,
            setByCaller: new Dictionary<string, float>
            {
                ["Amount"] = 12f,
                [DamageExecution.SetByCallerChainBonusScale] = 0.5f,
            });

        new DamageExecution().Execute(new ExecutionDefDto(), spec, target);

        // base = 12 + 10(物攻) − 2(物防) = 20；
        // ×(1 + 0.25 增伤 + 0.5 受伤增加) = 35 → ×(1 + 0.5 连携) = 52.5（规格「一律加算，连携除外」）。
        Assert.That(target.GetCurrentValue(AttributeIds.Health), Is.EqualTo(100f - 52.5f).Within(0.001f));
    }

    #endregion
}