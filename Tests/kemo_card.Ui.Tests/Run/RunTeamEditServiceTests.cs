using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Events;
using KemoCard.Mod.Run.Potential;
using KemoCard.Mod.Run.Team;
using KemoCard.Ui.Tests.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

/// <summary>
/// 队伍编辑的语义层：槽位/角色池视图、上阵（含自动迁移）与下阵、卡组增删与校验、
/// 以及"战斗中禁止编辑"的门闩。界面（RunTeamEditDlg / RunCharacterDeckDlg）只是它的绑定层。
/// </summary>
[TestFixture]
public sealed class RunTeamEditServiceTests
{
    private const string StoryId = "story.test";

    #region 门闩

    [Test]
    public void Editing_is_allowed_outside_battle_and_blocked_in_battle()
    {
        var (service, controller, _) = Build();

        Assert.That(service.CanEdit, Is.True, "事件/奖励/环间都允许编辑");
        Assert.That(service.EditBlockReasonKey, Is.Null);

        controller.State.Phase = ERunPhase.Battle;
        Assert.That(service.CanEdit, Is.False);
        Assert.That(service.EditBlockReasonKey, Is.EqualTo("UI_TEAM_EDIT_BLOCKED"));

        controller.State.Phase = ERunPhase.BattleEnd;
        Assert.That(service.CanEdit, Is.False);

        controller.State.Phase = ERunPhase.Finished;
        Assert.That(service.CanEdit, Is.False);

        controller.State.Phase = ERunPhase.Reward;
        Assert.That(service.CanEdit, Is.True);
    }

    [Test]
    public void Writes_are_rejected_during_battle()
    {
        var (service, controller, _) = Build();
        var instanceId = service.GetPoolEntries()[0].InstanceId;
        controller.State.Phase = ERunPhase.Battle;

        Assert.That(service.AssignToSlot(0, instanceId).Ok, Is.False);
        Assert.That(service.UnassignSlot(0).Ok, Is.False);
        Assert.That(service.CreateDeck(instanceId).Ok, Is.False);
        Assert.That(service.AddCard(instanceId, 0, "card.hp").Ok, Is.False);
    }

    #endregion

    #region 上阵 / 下阵 / 自动迁移

    [Test]
    public void Slots_and_pool_reflect_assignment_state()
    {
        var (service, _, _) = Build(poolSize: 2);

        var slots = service.GetSlots();
        Assert.That(slots, Has.Count.EqualTo(RunConstants.SlotCount));
        Assert.That(slots[0].InstanceId, Is.Null, "初始全部空槽");

        var pool = service.GetPoolEntries();
        Assert.That(pool, Has.Count.EqualTo(2));
        Assert.That(pool[0].InstanceId, Is.EqualTo("inst-0"));
        Assert.That(pool[0].AssignedSlotIndex, Is.Null);
        Assert.That(pool[0].DeckSize, Is.EqualTo(1), "入池即带初始卡组");

        Assert.That(service.AssignToSlot(2, "inst-1").Ok, Is.True);

        Assert.That(service.GetSlots()[2].InstanceId, Is.EqualTo("inst-1"));
        Assert.That(service.GetPoolEntries()[1].AssignedSlotIndex, Is.EqualTo(2));
        Assert.That(service.IsDirty, Is.True, "写操作要标记待保存");
    }

    [Test]
    public void Assigning_a_character_already_in_another_slot_migrates_it()
    {
        var (service, _, _) = Build(poolSize: 1);

        Assert.That(service.AssignToSlot(0, "inst-0").Ok, Is.True);
        Assert.That(service.AssignToSlot(3, "inst-0").Ok, Is.True, "同一实例迁到别的槽");

        var slots = service.GetSlots();
        Assert.That(slots[0].InstanceId, Is.Null, "原槽自动清空（同一实例不占多槽）");
        Assert.That(slots[3].InstanceId, Is.EqualTo("inst-0"));
    }

    [Test]
    public void Assigning_to_the_same_slot_is_a_no_op_success()
    {
        var (service, _, _) = Build(poolSize: 1);
        service.AssignToSlot(1, "inst-0");

        var again = service.AssignToSlot(1, "inst-0");

        Assert.That(again.Ok, Is.True);
        Assert.That(again.MessageKey, Is.EqualTo("UI_TEAM_ALREADY_DEPLOYED"));
        Assert.That(service.GetSlots()[1].InstanceId, Is.EqualTo("inst-0"));
    }

    [Test]
    public void Assign_rejects_unknown_character_and_invalid_slot()
    {
        var (service, _, _) = Build(poolSize: 1);

        Assert.That(service.AssignToSlot(0, "inst.missing").Ok, Is.False);
        Assert.That(service.AssignToSlot(-1, "inst-0").Ok, Is.False);
        Assert.That(service.AssignToSlot(RunConstants.SlotCount, "inst-0").Ok, Is.False);
    }

    [Test]
    public void Unassign_clears_the_slot_and_reports_empty_slot()
    {
        var (service, _, _) = Build(poolSize: 1);
        service.AssignToSlot(0, "inst-0");

        Assert.That(service.UnassignSlot(0).Ok, Is.True);
        Assert.That(service.GetSlots()[0].InstanceId, Is.Null);
        Assert.That(service.UnassignSlot(0).MessageKey, Is.EqualTo("UI_TEAM_SLOT_ALREADY_EMPTY"));
    }

    #endregion

    #region 被动视图

    [Test]
    public void Passives_view_reports_thresholds_unlock_state_and_desc()
    {
        var registry = CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>
            {
                [CombatSimulationTestBuilder.PartyHpCardId] = new()
                {
                    Id = CombatSimulationTestBuilder.PartyHpCardId,
                    Stats = new CardStatBlockDto { HpCap = 10 },
                },
            },
            buffs: new Dictionary<string, BuffDto>(StringComparer.Ordinal)
            {
                ["buff.p1"] = new() { Id = "buff.p1", DescId = "buff.p1.desc" },
                ["buff.p2"] = new() { Id = "buff.p2", DescId = "buff.p2.desc" },
            });

        var controller = new RunController(new RunMod { MaxRing = 5 });
        controller.CreateRun(StoryId, new HostRng(20260921, "test"), [], isMultiplayer: false);
        controller.AddToCharacterPool(new CharacterInstance(
            new CharacterDto
            {
                Id = "hero_passive",
                Cards = [CombatSimulationTestBuilder.PartyHpCardId],
                Passives =
                [
                    new PassiveRefDto { BuffId = "buff.p1", RequiredPotential = 0 },
                    new PassiveRefDto { BuffId = "buff.p2", RequiredPotential = 10 },
                ],
            },
            "inst-passive"));

        var service = new RunTeamEditService(controller, registry);

        var passives = service.GetPassives("inst-passive");
        Assert.That(passives, Has.Count.EqualTo(2), "展示顺序 = 定义声明顺序");
        Assert.That(passives[0].RequiredPotential, Is.EqualTo(0));
        Assert.That(passives[0].Unlocked, Is.True, "门槛 0 默认解锁");
        Assert.That(passives[0].DescriptionId, Is.EqualTo("buff.p1.desc"));
        Assert.That(passives[1].Unlocked, Is.False);
        Assert.That(passives[1].DescriptionId, Is.EqualTo("buff.p2.desc"));

        // 上阵 + 把潜能分配到该槽位 → 达到门槛自动解锁：解锁状态与战斗挂载共用 PotentialService 判定。
        Assert.That(controller.SetActiveCharacter(0, 0), Is.True);
        var potential = new PotentialService(controller.State);
        potential.Grant(10);
        Assert.That(potential.TryAllocate(0, 10).Success, Is.True);

        Assert.That(service.GetPassives("inst-passive")[1].Unlocked, Is.True, "槽位已分配达到门槛后自动解锁");
        Assert.That(service.GetPassives("inst.missing"), Is.Empty, "未知实例返回空列表");
    }

    #endregion

    #region 潜能划拨

    [Test]
    public void Potential_allocation_moves_pool_to_slot_and_back()
    {
        var (service, controller, _) = Build(poolSize: 1);
        controller.Potential.Grant(30);

        Assert.That(service.AvailablePotential, Is.EqualTo(30));
        Assert.That(service.SlotAllocatedPotential(1), Is.EqualTo(0));

        var allocated = service.AllocatePotential(1, 20);
        Assert.That(allocated.Ok, Is.True, allocated.MessageKey);
        Assert.That(service.AvailablePotential, Is.EqualTo(10), "分配从团队池划走");
        Assert.That(service.SlotAllocatedPotential(1), Is.EqualTo(20));
        Assert.That(service.IsDirty, Is.True, "划拨要置脏，关闭界面时统一保存");

        var deducted = service.DeductPotential(1, 5);
        Assert.That(deducted.Ok, Is.True, deducted.MessageKey);
        Assert.That(service.AvailablePotential, Is.EqualTo(15), "扣除退回团队池");
        Assert.That(service.SlotAllocatedPotential(1), Is.EqualTo(15));
    }

    [Test]
    public void Potential_allocation_reports_reason_keys()
    {
        var (service, controller, _) = Build(poolSize: 1);
        controller.Potential.Grant(10);

        Assert.That(service.AllocatePotential(0, 0).MessageKey, Is.EqualTo("UI_TEAM_POTENTIAL_INVALID"));
        Assert.That(service.AllocatePotential(0, 11).MessageKey, Is.EqualTo("UI_TEAM_POTENTIAL_POOL_SHORT"));
        Assert.That(service.DeductPotential(0, 1).MessageKey, Is.EqualTo("UI_TEAM_POTENTIAL_SLOT_SHORT"));
        Assert.That(service.AllocatePotential(99, 5).MessageKey, Is.EqualTo("UI_TEAM_SLOT_INVALID"));

        controller.State.Phase = ERunPhase.Battle;
        Assert.That(service.AllocatePotential(0, 5).MessageKey, Is.EqualTo("UI_TEAM_EDIT_BLOCKED"));
        Assert.That(service.DeductPotential(0, 5).MessageKey, Is.EqualTo("UI_TEAM_EDIT_BLOCKED"));
    }

    #endregion

    #region 卡组

    [Test]
    public void Deck_view_exposes_current_cards_buildable_pool_and_limits()
    {
        var (service, _, _) = Build(poolSize: 1, extraCollectionCards: ["card.extra"]);

        var deck = service.GetDeck("inst-0", 0);

        Assert.That(deck, Is.Not.Null);
        Assert.That(deck!.CardIds, Is.EquivalentTo(new[] { CombatSimulationTestBuilder.PartyHpCardId }));
        Assert.That(deck.BuildableCardIds, Does.Contain(CombatSimulationTestBuilder.PartyHpCardId));
        Assert.That(deck.BuildableCardIds, Does.Contain("card.extra"), "收藏里的卡可入卡组");
        Assert.That(deck.MaxCards, Is.EqualTo(CombatConstants.MaxCardsPerDeck));
        Assert.That(deck.InvalidCardIds, Is.Empty);
        Assert.That(deck.IsLocked, Is.False);
    }

    [Test]
    public void Add_and_remove_card_follow_deck_rules()
    {
        var (service, _, _) = Build(poolSize: 1, extraCollectionCards: ["card.extra"]);

        Assert.That(service.AddCard("inst-0", 0, "card.extra").Ok, Is.True);
        Assert.That(service.GetDeck("inst-0", 0)!.CardIds, Does.Contain("card.extra"));

        var duplicate = service.AddCard("inst-0", 0, "card.extra");
        Assert.That(duplicate.Ok, Is.False);
        Assert.That(duplicate.MessageKey, Is.EqualTo("UI_TEAM_DECK_DUPLICATE"));

        var notBuildable = service.AddCard("inst-0", 0, "card.unknown");
        Assert.That(notBuildable.Ok, Is.False);
        Assert.That(notBuildable.MessageKey, Is.EqualTo("UI_TEAM_DECK_NOT_BUILDABLE"));

        Assert.That(service.RemoveCard("inst-0", 0, "card.extra").Ok, Is.True);
        Assert.That(service.GetDeck("inst-0", 0)!.CardIds, Does.Not.Contain("card.extra"));
    }

    [Test]
    public void Deck_cannot_be_emptied_and_missing_card_is_reported()
    {
        var (service, _, _) = Build(poolSize: 1);
        var onlyCard = service.GetDeck("inst-0", 0)!.CardIds[0];

        var empty = service.RemoveCard("inst-0", 0, onlyCard);
        Assert.That(empty.Ok, Is.False);
        Assert.That(empty.MessageKey, Is.EqualTo("UI_TEAM_DECK_EMPTY"));

        var missing = service.RemoveCard("inst-0", 0, "card.not_in_deck");
        Assert.That(missing.Ok, Is.False);
        Assert.That(missing.MessageKey, Is.EqualTo("UI_TEAM_DECK_CARD_MISSING"));
    }

    [Test]
    public void Deck_edit_is_rejected_when_deck_is_locked()
    {
        var (service, controller, _) = Build(poolSize: 1);
        controller.State.CharacterPool[0].SetDeckLocked(true);

        var result = service.AddCard("inst-0", 0, "card.hp");

        Assert.That(result.Ok, Is.False);
        Assert.That(result.MessageKey, Is.EqualTo("UI_TEAM_DECK_LOCKED"));
    }

    [Test]
    public void Creating_and_switching_decks_respects_the_limit()
    {
        var (service, _, _) = Build(poolSize: 1);

        Assert.That(service.CreateDeck("inst-0").Ok, Is.True);
        var decks = service.GetDecks("inst-0");
        Assert.That(decks, Has.Count.EqualTo(2), "新建第二套卡组");
        Assert.That(decks[1].IsCurrent, Is.True, "新建后切为当前卡组");

        Assert.That(service.SetCurrentDeck("inst-0", 0).Ok, Is.True);
        Assert.That(service.GetDecks("inst-0")[0].IsCurrent, Is.True);

        // 顶到上限后拒绝新建。
        while (service.GetDecks("inst-0").Count < CombatConstants.MaxDecksPerCharacter)
        {
            Assert.That(service.CreateDeck("inst-0").Ok, Is.True);
        }

        var overflow = service.CreateDeck("inst-0");
        Assert.That(overflow.Ok, Is.False);
        Assert.That(overflow.MessageKey, Is.EqualTo("UI_TEAM_DECK_LIMIT"));
    }

    [Test]
    public void Battle_locks_decks_and_leaves_the_lock_on_battle_end()
    {
        var (service, controller, registry) = Build(poolSize: RunConstants.SlotCount);
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            Assert.That(service.AssignToSlot(i, $"inst-{i}").Ok, Is.True);
        }

        controller.State.Phase = ERunPhase.Reward;
        controller.StartBattle(registry, new HostRng(20260921, "battle"), runSeed: 7, RunTestHelper.TestBattleId);

        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.Battle));
        Assert.That(service.CanEdit, Is.False);
        foreach (var character in controller.State.CharacterPool)
        {
            Assert.That(character.IsDeckLocked, Is.True, "战斗中卡组加锁");
        }

        controller.EndBattle(won: true);

        foreach (var character in controller.State.CharacterPool)
        {
            Assert.That(character.IsDeckLocked, Is.False, "战斗结束解锁");
        }
    }

    #endregion

    #region "可加入卡组"列表

    /// <summary>
    /// 卡组不允许重复，因此"可加入卡组"列表必须把**已在当前卡组内**的牌标出来
    /// （界面据此加遮罩 + "已在卡组内"提示并停掉点击）。
    /// </summary>
    [Test]
    public void Pool_cards_mark_the_ones_already_in_the_deck()
    {
        var (service, _, _) = Build(poolSize: 1, extraCollectionCards: ["card.extra_a", "card.extra_b"]);
        var instanceId = service.GetPoolEntries()[0].InstanceId;

        var pool = service.GetPoolCards(instanceId, deckIndex: 0);

        Assert.That(
            pool.Select(entry => entry.CardId),
            Is.EquivalentTo(new[] { CombatSimulationTestBuilder.PartyHpCardId, "card.extra_a", "card.extra_b" }),
            "列表 = 可构筑卡牌（收藏 ∪ 角色专属）");
        Assert.That(pool.Select(entry => entry.CardId), Is.Ordered, "按 id 排序，界面顺序稳定");
        Assert.That(
            pool.Single(entry => entry.CardId == CombatSimulationTestBuilder.PartyHpCardId).InDeck,
            Is.True,
            "初始卡组里就有的牌必须带标记");
        Assert.That(pool.Count(entry => entry.InDeck), Is.EqualTo(1));

        Assert.That(service.AddCard(instanceId, 0, "card.extra_a").Ok, Is.True);
        Assert.That(
            service.GetPoolCards(instanceId, 0).Single(entry => entry.CardId == "card.extra_a").InDeck,
            Is.True,
            "加入后立刻带上标记");

        Assert.That(service.RemoveCard(instanceId, 0, "card.extra_a").Ok, Is.True);
        Assert.That(
            service.GetPoolCards(instanceId, 0).Single(entry => entry.CardId == "card.extra_a").InDeck,
            Is.False,
            "移出后标记消失");
    }

    [Test]
    public void Pool_card_marks_are_per_deck()
    {
        var (service, _, _) = Build(poolSize: 1, extraCollectionCards: ["card.extra_a"]);
        var instanceId = service.GetPoolEntries()[0].InstanceId;
        Assert.That(service.AddCard(instanceId, 0, "card.extra_a").Ok, Is.True);

        Assert.That(service.CreateDeck(instanceId).Ok, Is.True);

        var first = service.GetPoolCards(instanceId, 0);
        var second = service.GetPoolCards(instanceId, 1);

        Assert.That(first.Single(entry => entry.CardId == "card.extra_a").InDeck, Is.True);
        Assert.That(
            second.Single(entry => entry.CardId == "card.extra_a").InDeck,
            Is.False,
            "标记按卡组各自计算（新卡组没这张牌）");
    }

    [Test]
    public void Pool_cards_are_empty_for_unknown_character_or_deck()
    {
        var (service, _, _) = Build(poolSize: 1);
        var instanceId = service.GetPoolEntries()[0].InstanceId;

        Assert.That(service.GetPoolCards("inst.missing", 0), Is.Empty);
        Assert.That(service.GetPoolCards(instanceId, deckIndex: 9), Is.Empty);
    }

    #endregion

    #region 事件广播（Run 功能内部总线）

    [Test]
    public void Assignment_and_unassignment_broadcast_on_the_run_bus()
    {
        var (service, controller, _) = Build(poolSize: 2);
        var captured = new List<RunCharacterAssignedPayload>();
        var listener = controller.State.OnRunCharacterAssigned((payload, _) => captured.Add(payload));

        Assert.That(service.AssignToSlot(2, "inst-1").Ok, Is.True);
        Assert.That(captured, Has.Count.EqualTo(1));
        Assert.That(captured[0].SlotIndex, Is.EqualTo(2));
        Assert.That(captured[0].CurrentInstanceId, Is.EqualTo("inst-1"));
        Assert.That(captured[0].PreviousInstanceId, Is.Null);

        // 自动迁移：源槽清空 + 新槽上阵，各广播一次。
        Assert.That(service.AssignToSlot(0, "inst-1").Ok, Is.True);
        Assert.That(captured, Has.Count.EqualTo(3));
        Assert.That(captured[1].SlotIndex, Is.EqualTo(2));
        Assert.That(captured[1].CurrentInstanceId, Is.Null, "原槽清空");
        Assert.That(captured[1].PreviousInstanceId, Is.EqualTo("inst-1"));
        Assert.That(captured[2].SlotIndex, Is.EqualTo(0));
        Assert.That(captured[2].CurrentInstanceId, Is.EqualTo("inst-1"));

        Assert.That(service.UnassignSlot(0).Ok, Is.True);
        Assert.That(captured, Has.Count.EqualTo(4));
        Assert.That(captured[3].CurrentInstanceId, Is.Null);

        listener.Off();
    }

    [Test]
    public void Failed_writes_broadcast_nothing()
    {
        var (service, controller, _) = Build(poolSize: 1);
        var hits = 0;
        var assigned = controller.State.OnRunCharacterAssigned((_, _) => hits++);
        var deck = controller.State.OnRunDeckChanged((_, _) => hits++);

        Assert.That(service.AssignToSlot(0, "no-such-instance").Ok, Is.False);
        Assert.That(service.AssignToSlot(99, "inst-0").Ok, Is.False);
        Assert.That(service.AddCard("inst-0", 0, "not-in-collection").Ok, Is.False);

        Assert.That(hits, Is.Zero, "失败的写操作不应广播");

        assigned.Off();
        deck.Off();
    }

    [Test]
    public void Deck_edits_broadcast_on_the_run_bus()
    {
        var (service, controller, _) = Build(poolSize: 1, extraCollectionCards: ["card.hp2"]);
        var captured = new List<RunDeckChangedPayload>();
        var listener = controller.State.OnRunDeckChanged((payload, _) => captured.Add(payload));

        Assert.That(service.CreateDeck("inst-0").Ok, Is.True);
        Assert.That(captured, Has.Count.EqualTo(1));
        Assert.That(captured[0].InstanceId, Is.EqualTo("inst-0"));
        Assert.That(captured[0].DeckIndex, Is.EqualTo(1), "新建后切到新卡组");

        Assert.That(service.SetCurrentDeck("inst-0", 0).Ok, Is.True);
        Assert.That(captured[1].DeckIndex, Is.EqualTo(0));

        Assert.That(service.AddCard("inst-0", 0, "card.hp2").Ok, Is.True);
        Assert.That(captured[2].DeckIndex, Is.EqualTo(0));
        Assert.That(captured, Has.Count.EqualTo(3));

        Assert.That(service.RemoveCard("inst-0", 0, "card.hp2").Ok, Is.True);
        Assert.That(captured, Has.Count.EqualTo(4));

        listener.Off();
        Assert.That(controller.State.InternalBus.Has(RunModEventTable.RunDeckChanged), Is.False, "Off 后不再订阅");
    }

    [Test]
    public void Debug_service_deck_edits_also_broadcast()
    {
        var (_, controller, registry) = Build(poolSize: 1, extraCollectionCards: ["card.hp2"]);
        Assert.That(controller.SetActiveCharacter(0, 0), Is.True);

        var debug = new KemoCard.Mod.Run.Debug.RunDebugService(
            controller,
            registry,
            new NullContentEffectScriptHost());
        var captured = new List<RunDeckChangedPayload>();
        var listener = controller.State.OnRunDeckChanged((payload, _) => captured.Add(payload));

        var result = debug.AddCardToDeck(0, "card.hp2");

        Assert.That(result.Ok, Is.True, result.Message);
        Assert.That(captured, Has.Count.EqualTo(1), "调试面板改卡组同样走总线");
        Assert.That(captured[0].InstanceId, Is.EqualTo("inst-0"));

        listener.Off();
    }

    #endregion

    #region 装配

    private static (RunTeamEditService Service, RunController Controller, GameDefinitionRegistry Registry) Build(
        int poolSize = 2,
        IReadOnlyList<string>? extraCollectionCards = null)
    {
        var registry = RunTestHelper.CreateRegistryWithHpCard();
        var controller = new RunController(new RunMod { MaxRing = 5 });
        controller.CreateRun(StoryId, new HostRng(20260921, "test"), [], isMultiplayer: false);

        for (var i = 0; i < poolSize; i++)
        {
            controller.AddToCharacterPool(RunTestHelper.CreateCharacterWithHp(i));
        }

        foreach (var cardId in extraCollectionCards ?? [])
        {
            if (registry.Store.TryGetCard(cardId, out _))
            {
                controller.AddCard(cardId);
                continue;
            }

            // 收藏里额外加入一张仅用于构筑测试的卡。
            CombatTestHelper.RebuildInto(
                registry,
                cards: new Dictionary<string, CardDto>
                {
                    [CombatSimulationTestBuilder.PartyHpCardId] = new()
                    {
                        Id = CombatSimulationTestBuilder.PartyHpCardId,
                        Stats = new CardStatBlockDto { HpCap = 10 },
                    },
                    [cardId] = new() { Id = cardId, Stats = new CardStatBlockDto { HpCap = 1 } },
                });
            controller.AddCard(cardId);
        }

        return (new RunTeamEditService(controller, registry), controller, registry);
    }

    #endregion
}