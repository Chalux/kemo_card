using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Debug;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

/// <summary>
/// Run 调试面板的操作语义。逻辑层刻意不依赖 Godot，因此每条调试动作的
/// 「给了什么 / 改了什么 / 在哪一步失败」都能在这里直接断言。
/// </summary>
[TestFixture]
public sealed class RunDebugServiceTests
{
    private const string StoryId = "story.test";
    private const string HpCardId = "card.hp";
    private const string ExtraCardId = "card.extra";
    private const string EnemyId = "enemy.test";
    private const string BattleId = "battle.test";
    private const string ScriptEventId = "event.script";
    private const string DataEventId = "event.data";
    private const string PassiveBuffId = "buff.debug_passive";
    private const string OrbId = "blue";
    private const int Seed = 20260915;

    private static readonly string[] HeroIds = ["hero.a", "hero.b", "hero.c", "hero.d"];

    #region 卡牌

    [Test]
    public void ListCards_returns_definitions_sorted_by_id()
    {
        var (service, _, _) = Build();

        var cards = service.ListCards();

        Assert.That(cards.Select(c => c.Id), Is.EqualTo(new[] { ExtraCardId, HpCardId }));
    }

    [Test]
    public void GrantCard_adds_to_collection()
    {
        var (service, controller, _) = Build();

        var result = service.GrantCard(HpCardId);

        Assert.That(result.Ok, Is.True);
        Assert.That(controller.State.CanUseCard(HpCardId), Is.True);
    }

    [Test]
    public void GrantCard_rejects_unknown_card()
    {
        var (service, controller, _) = Build();

        var result = service.GrantCard("card.nope");

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("card.nope"));
        Assert.That(controller.State.CanUseCard("card.nope"), Is.False);
    }

    [Test]
    public void GrantCard_rejects_blank_id()
    {
        var (service, _, _) = Build();

        Assert.That(service.GrantCard("  ").Ok, Is.False);
    }

    [Test]
    public void RevokeCard_removes_from_collection()
    {
        var (service, controller, _) = Build();
        service.GrantCard(HpCardId);

        var result = service.RevokeCard(HpCardId);

        Assert.That(result.Ok, Is.True);
        Assert.That(controller.State.CanUseCard(HpCardId), Is.False);
    }

    [Test]
    public void AddCardToDeck_writes_card_into_slot_character_deck()
    {
        var (service, controller, _) = Build();
        service.GrantCharacter(HeroIds[0], deploySlot: 0);

        var result = service.AddCardToDeck(0, ExtraCardId);

        var character = controller.State.ActiveParty[0];
        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(character, Is.Not.Null);
        Assert.That(character!.Decks[character.CurrentDeckIndex].CardIds, Does.Contain(ExtraCardId));
        Assert.That(controller.State.CanUseCard(ExtraCardId), Is.True, "写入卡组必须同时进收藏，否则卡组校验会失败");
    }

    [Test]
    public void AddCardToDeck_rejects_slot_without_character()
    {
        var (service, _, _) = Build();

        var result = service.AddCardToDeck(0, ExtraCardId);

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("没有上阵角色"));
    }

    [Test]
    public void AddCardToDeck_is_idempotent_for_existing_card()
    {
        var (service, controller, _) = Build();
        service.GrantCharacter(HeroIds[0], deploySlot: 0);
        service.AddCardToDeck(0, ExtraCardId);

        var again = service.AddCardToDeck(0, ExtraCardId);

        var character = controller.State.ActiveParty[0]!;
        Assert.That(again.Ok, Is.True);
        Assert.That(
            character.Decks[character.CurrentDeckIndex].CardIds.Count(id => id == ExtraCardId),
            Is.EqualTo(1),
            "重复写入不得产生重复卡牌");
    }

    #endregion

    #region 角色

    [Test]
    public void GrantCharacter_adds_to_pool_without_deploying()
    {
        var (service, controller, _) = Build();

        var result = service.GrantCharacter(HeroIds[0]);

        Assert.That(result.Ok, Is.True);
        Assert.That(controller.State.CharacterPool, Has.Count.EqualTo(1));
        Assert.That(controller.State.ActiveParty[0], Is.Null, "未指定槽位时不应自动上阵");
    }

    [Test]
    public void GrantCharacter_deploys_when_slot_given()
    {
        var (service, controller, _) = Build();

        var result = service.GrantCharacter(HeroIds[1], deploySlot: 2);

        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(controller.State.ActiveParty[2], Is.Not.Null);
        Assert.That(controller.State.ActiveParty[2]!.DefinitionId, Is.EqualTo(HeroIds[1]));
    }

    [Test]
    public void GrantCharacter_rejects_unknown_character()
    {
        var (service, _, _) = Build();

        var result = service.GrantCharacter("hero.nope");

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("hero.nope"));
    }

    [Test]
    public void DeployCharacter_finds_pool_instance_by_definition_id()
    {
        var (service, controller, _) = Build();
        service.GrantCharacter(HeroIds[0]);
        service.GrantCharacter(HeroIds[1]);

        var result = service.DeployCharacter(HeroIds[1], 3);

        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(controller.State.ActiveParty[3]!.DefinitionId, Is.EqualTo(HeroIds[1]));
    }

    [Test]
    public void DeployCharacter_rejects_character_not_in_pool()
    {
        var (service, _, _) = Build();

        var result = service.DeployCharacter(HeroIds[0], 0);

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("请先「加入角色池」"));
    }

    [Test]
    public void FillPartyFromPool_fills_every_slot_when_pool_is_big_enough()
    {
        var (service, controller, _) = Build();
        foreach (var hero in HeroIds)
        {
            service.GrantCharacter(hero);
        }

        var result = service.FillPartyFromPool();

        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(controller.State.ValidateParty(), Is.True);
        Assert.That(
            controller.State.ActiveParty.Select(c => c!.InstanceId).Distinct().Count(),
            Is.EqualTo(RunConstants.SlotCount),
            "同一个角色实例不得占用多个槽位");
    }

    [Test]
    public void FillPartyFromPool_reports_failure_when_pool_is_empty()
    {
        var (service, controller, _) = Build();

        var result = service.FillPartyFromPool();

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("没有空闲角色"));
        Assert.That(controller.State.ValidateParty(), Is.False);
    }

    #endregion

    #region 战斗

    [Test]
    public void StartBattle_prepares_party_and_enters_battle()
    {
        var (service, controller, _) = Build();
        foreach (var hero in HeroIds)
        {
            service.GrantCharacter(hero);
        }

        var result = service.StartBattle(BattleId);

        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.Battle));
        Assert.That(result.Message, Does.Contain(BattleId));
    }

    [Test]
    public void StartBattle_rejects_unknown_battle()
    {
        var (service, controller, _) = Build();

        var result = service.StartBattle("battle.nope");

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("battle.nope"));
        Assert.That(controller.State.Phase, Is.Not.EqualTo(ERunPhase.Battle));
    }

    [Test]
    public void StartBattle_fails_when_there_are_not_enough_characters()
    {
        var (service, controller, _) = Build();
        service.GrantCharacter(HeroIds[0]);

        var result = service.StartBattle(BattleId);

        Assert.That(result.Ok, Is.False);
        Assert.That(controller.State.Phase, Is.Not.EqualTo(ERunPhase.Battle), "开战失败不得留下 Battle 阶段");
    }

    [Test]
    public void StartBattle_bypasses_phase_but_keeps_production_pipeline()
    {
        var (service, controller, _) = Build();
        foreach (var hero in HeroIds)
        {
            service.GrantCharacter(hero);
        }

        // 调试前阶段是 Event（由 Build 的 CreateRun 决定），StartBattle 必须自己补到 Reward。
        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.Event));

        var result = service.StartBattle(BattleId);

        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(result.Message, Does.Contain("1 波"), "必须报出内容里真实的波次，说明走的是内容定义");
    }

    [Test]
    public void EndBattle_resolves_battle_and_returns_to_ring_end()
    {
        var (service, controller, _) = Build();
        foreach (var hero in HeroIds)
        {
            service.GrantCharacter(hero);
        }

        service.StartBattle(BattleId);
        var result = service.EndBattle(won: true);

        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.RingEnd));
    }

    [Test]
    public void EndBattle_rejects_when_not_in_battle()
    {
        var (service, controller, _) = Build();

        var result = service.EndBattle(won: true);

        Assert.That(result.Ok, Is.False);
        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.Event));
    }

    #endregion

    #region 事件

    [Test]
    public void TriggerEvent_sets_phase_and_records_flag()
    {
        var (service, controller, _) = Build();

        var result = service.TriggerEvent(DataEventId);

        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.Event));
        foreach (var state in controller.State.PlayerStates)
        {
            Assert.That(
                state.GetEventFlag<string>("debug.lastTriggeredEventId"),
                Is.EqualTo(DataEventId),
                "每个槽位都要留下可随存档往返的事件标记");
        }

        Assert.That(result.Message, Does.Contain("事件运行时未实现"), "必须显式说明不会弹窗，避免误判为脚本没跑");
    }

    [Test]
    public void TriggerEvent_runs_script_when_declared()
    {
        var host = new RecordingScriptHost();
        var (service, _, _) = Build(host);

        var result = service.TriggerEvent(ScriptEventId);

        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(host.Calls, Is.EqualTo(1));
        Assert.That(host.LastModId, Is.EqualTo("test.mod"), "脚本按事件归属 mod 执行");
        Assert.That(host.LastPath, Is.EqualTo("effects/event.ts"));
        Assert.That(host.LastScriptEntry, Is.EqualTo("execute"));
        Assert.That(result.Message, Does.Contain("产出 1 条效果"));
    }

    [Test]
    public void TriggerEvent_reports_missing_script_without_failing()
    {
        var host = new RecordingScriptHost();
        var (service, _, _) = Build(host);

        var result = service.TriggerEvent(DataEventId);

        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(host.Calls, Is.Zero);
        Assert.That(result.Message, Does.Contain("未声明 ScriptPath"));
    }

    [Test]
    public void TriggerEvent_rejects_unknown_event()
    {
        var (service, _, _) = Build();

        var result = service.TriggerEvent("event.nope");

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("event.nope"));
    }

    #endregion

    #region 通用

    [Test]
    public void AddGold_increases_gold()
    {
        var (service, controller, _) = Build();

        var result = service.AddGold(250);

        Assert.That(result.Ok, Is.True);
        Assert.That(controller.GetGold(), Is.EqualTo(250));
    }

    [Test]
    public void AddGold_rejects_non_positive_amount()
    {
        var (service, controller, _) = Build();

        Assert.That(service.AddGold(0).Ok, Is.False);
        Assert.That(controller.GetGold(), Is.Zero);
    }

    [Test]
    public void SetRing_accepts_in_range_and_rejects_out_of_range()
    {
        var (service, controller, _) = Build();

        Assert.That(service.SetRing(3).Ok, Is.True);
        Assert.That(controller.State.CurrentRing, Is.EqualTo(3));

        Assert.That(service.SetRing(0).Ok, Is.False);
        Assert.That(service.SetRing(controller.State.MaxRing + 1).Ok, Is.False);
        Assert.That(controller.State.CurrentRing, Is.EqualTo(3), "越界时不得改动环数");
    }

    [Test]
    public void SetPhase_sets_phase()
    {
        var (service, controller, _) = Build();

        var result = service.SetPhase(ERunPhase.Reward);

        Assert.That(result.Ok, Is.True);
        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.Reward));
    }

    #endregion

    #region 潜能与检查

    [Test]
    public void GrantPotential_grants_exact_amount_to_slot_or_pool()
    {
        var (service, controller, _) = Build();

        var pool = service.GrantPotential(5);
        var slot = service.GrantPotential(7, slotIndex: 1);

        Assert.That(pool.Ok, Is.True, pool.Message);
        Assert.That(slot.Ok, Is.True, slot.Message);
        Assert.That(controller.Potential.TeamPool, Is.EqualTo(5), "按申请数额入团队池（不得按 20 取整）");
        Assert.That(controller.State.PlayerStates[1].PotentialDirectCredit, Is.EqualTo(7));
        Assert.That(service.GrantPotential(0).Ok, Is.False, "非正数额必须拒绝");
    }

    [Test]
    public void UnlockNextPassive_unlocks_then_RefundLatest_returns_whole_purchase()
    {
        var (service, controller, _) = Build();
        service.GrantCharacter(HeroIds[0], deploySlot: 0);
        service.GrantPotential(30);

        var unlock = service.UnlockNextPassive(0);
        Assert.That(unlock.Ok, Is.True, unlock.Message);
        Assert.That(controller.Potential.TeamPool, Is.EqualTo(20), "走正式消费管线扣款");

        var refund = service.RefundLatestPotential(0);
        Assert.That(refund.Ok, Is.True, refund.Message);
        Assert.That(controller.Potential.TeamPool, Is.EqualTo(30), "整笔回到团队池");
        Assert.That(controller.State.PlayerStates[0].PotentialSpent, Is.Empty);
        // 解锁已撤销：再次解锁应当再次扣款（而不是幂等空过）。
        Assert.That(service.UnlockNextPassive(0).Ok, Is.True);
    }

    [Test]
    public void RefundLatestPotential_reports_when_no_spend_entries()
    {
        var (service, _, _) = Build();

        var result = service.RefundLatestPotential(0);

        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("没有可返还"));
    }

    [Test]
    public void GrantCharacter_duplicate_converts_to_potential_instead_of_failing()
    {
        var (service, controller, _) = Build();
        service.GrantCharacter(HeroIds[0]);

        var result = service.GrantCharacter(HeroIds[0]);

        Assert.That(result.Ok, Is.True, "重复获得不是失败：已转化为潜能");
        Assert.That(result.Message, Does.Contain("重复"));
        Assert.That(controller.State.CharacterPool, Has.Count.EqualTo(1), "不入第二实例");
        Assert.That(controller.Potential.TeamPool, Is.EqualTo(20));
    }

    [Test]
    public void InspectBattle_reports_no_battle_before_start()
    {
        var (service, _, _) = Build();

        Assert.That(service.InspectBattle(), Does.Contain("没有进行中的战斗"));
    }

    [Test]
    public void InspectBattle_reports_battle_summary_after_start()
    {
        var (service, _, _) = Build();
        foreach (var hero in HeroIds)
        {
            service.GrantCharacter(hero);
        }
        service.StartBattle(BattleId);

        var text = service.InspectBattle();

        Assert.That(text, Does.Contain(BattleId));
        Assert.That(text, Does.Contain("波 1/1"), "报出真实波次进度");
        Assert.That(text, Does.Contain("回合"), "报出回合与阶段");
        Assert.That(text, Does.Contain("充能球 0/7"), "报出充能球队列状态");
    }

    #endregion

    #region 充能球

    [Test]
    public void GrantOrb_reports_failure_without_battle_or_unknown_type()
    {
        var (service, _, _) = Build();

        Assert.That(service.GrantOrb(OrbId).Ok, Is.False, "未开战时报明原因");
        Assert.That(service.GrantOrb(OrbId).Message, Does.Contain("没有进行中的战斗"));

        foreach (var hero in HeroIds)
        {
            service.GrantCharacter(hero);
        }
        service.StartBattle(BattleId);

        var unknown = service.GrantOrb("orb.missing");

        Assert.That(unknown.Ok, Is.False);
        Assert.That(unknown.Message, Does.Contain("orb.missing"));
    }

    [Test]
    public void GrantOrb_enqueues_into_shared_queue_and_TriggerOrbs_resolves_it()
    {
        var (service, _, _) = Build();
        foreach (var hero in HeroIds)
        {
            service.GrantCharacter(hero);
        }
        service.StartBattle(BattleId);

        Assert.That(service.TriggerOrbs().Ok, Is.False, "空队列不可触发");

        Assert.That(service.GrantOrb(OrbId, count: 2, producerSlot: 1).Ok, Is.True);
        Assert.That(service.TriggerOrbs().Ok, Is.False, "不足门槛不可触发");

        Assert.That(service.GrantOrb(OrbId, producerSlot: 2).Ok, Is.True);
        var triggered = service.TriggerOrbs();

        Assert.That(triggered.Ok, Is.True, triggered.Message);
        Assert.That(triggered.Message, Does.Contain($"{OrbId}×3"));
        Assert.That(service.InspectBattle(), Does.Contain("充能球 0/7"), "触发后队列清空");
    }

    #endregion

    #region 装配

    private static (RunDebugService Service, RunController Controller, GameDefinitionRegistry Registry) Build(
        IContentEffectScriptHost? scriptHost = null)
    {
        var registry = CreateRegistry();
        var controller = new RunController(new RunMod { MaxRing = 5 });
        controller.CreateRun(StoryId, new HostRng(Seed, "test"), [], isMultiplayer: false);

        return (new RunDebugService(controller, registry, scriptHost), controller, registry);
    }

    private static GameDefinitionRegistry CreateRegistry()
    {
        var characters = new Dictionary<string, CharacterDto>(StringComparer.Ordinal);
        foreach (var hero in HeroIds)
        {
            characters[hero] = new CharacterDto
            {
                Id = hero,
                Cards = [HpCardId],
                // 给首位角色一条被动，供潜能调试操作（解锁/返还）测试使用；成本 > 0 → 默认锁定。
                Passives = hero == HeroIds[0]
                    ? [new PassiveRefDto { BuffId = PassiveBuffId, RequiredPotential = 10 }]
                    : [],
            };
        }

        var definitions = ModDefinitionsBundle.Empty with
        {
            Buffs = new Dictionary<string, BuffDto>(StringComparer.Ordinal)
            {
                // hero.a 的被动目标 buff：不注册的话内容校验会把整个角色定义剔除。
                [PassiveBuffId] = new() { Id = PassiveBuffId, DurationType = EBuffDurationType.Permanent },
            },
            OrbTypes = new Dictionary<string, OrbTypeDto>(StringComparer.Ordinal)
            {
                // 调试面板的充能球操作需要内容里有球类型。
                [OrbId] = new()
                {
                    Id = OrbId,
                    DisplayNameId = "orb.blue.name",
                    DamageKind = EDamageKind.Elemental,
                    Element = EElement.Blue,
                    PerOrbAmount = 6,
                    AttackBonusScale = 1,
                    AttackSource = EOrbAttackSource.Higher,
                },
            },
            Cards = new Dictionary<string, CardDto>(StringComparer.Ordinal)
            {
                [HpCardId] = new() { Id = HpCardId, Stats = new CardStatBlockDto { HpCap = 10 } },
                [ExtraCardId] = new() { Id = ExtraCardId, Stats = new CardStatBlockDto { HpCap = 4 } },
            },
            Characters = characters,
            Enemies = new Dictionary<string, EnemyDto>(StringComparer.Ordinal)
            {
                [EnemyId] = new() { Id = EnemyId, MaxHp = 10 },
            },
            Battles = new Dictionary<string, BattleDto>(StringComparer.Ordinal)
            {
                [BattleId] = new()
                {
                    Id = BattleId,
                    Waves =
                    [
                        new BattleWaveDto
                        {
                            EnemySpawns = [new EnemySpawnDto { EnemyId = EnemyId, Count = 1 }],
                        },
                    ],
                },
            },
            Events = new Dictionary<string, EventDto>(StringComparer.Ordinal)
            {
                [ScriptEventId] = new()
                {
                    Id = ScriptEventId,
                    EventKind = EEventKind.Script,
                    ScriptPath = "effects/event.ts",
                },
                // Data 事件必须至少有一个选项，否则内容校验会直接把它剔除。
                [DataEventId] = new()
                {
                    Id = DataEventId,
                    EventKind = EEventKind.Data,
                    Pages = [new EventPageDto { TextId = "event.data.page0" }],
                    Options = [new EventOptionDto { OptionId = "opt.a", LabelId = "event.data.opt.a" }],
                },
            },
        };

        var registry = new GameDefinitionRegistry();
        registry.Rebuild([new ModContentBundle("test.mod", definitions)], out _);
        return registry;
    }

    private sealed class RecordingScriptHost : IContentEffectScriptHost
    {
        public int Calls { get; private set; }

        public string? LastModId { get; private set; }

        public string? LastPath { get; private set; }

        public string? LastScriptEntry { get; private set; }

        public bool TryExecute(
            string modId,
            string scriptPath,
            string scriptEntry,
            IReadOnlyDictionary<string, object>? context,
            out IReadOnlyList<Dictionary<string, object>> proposedEffects)
        {
            Calls++;
            LastModId = modId;
            LastPath = scriptPath;
            LastScriptEntry = scriptEntry;
            proposedEffects = [new Dictionary<string, object>(StringComparer.Ordinal) { ["kind"] = "Damage" }];
            return true;
        }
    }

    #endregion
}
