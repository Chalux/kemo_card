using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Commands;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Adventure;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunAdventureTests
{
    #region 库存与指令

    [TestCase("Potion")]
    [TestCase("Consumable")]
    public void Old_and_current_item_kinds_load_as_consumables(string kind)
    {
        var json = JsonSerializer.Serialize(new { kind });
        var item = JsonSerializer.Deserialize<ItemDto>(json, ContentDefinitionJson.Options)!;
        Assert.That(item.Kind, Is.EqualTo(EItemKind.Consumable));
        Assert.That(JsonSerializer.Serialize(item, ContentDefinitionJson.Options), Does.Contain("\"Consumable\""));
    }

    [Test]
    public void Inventory_has_five_nonstacking_slots_unique_relics_and_checked_replacement()
    {
        var inventory = new CombatItemInventory();
        var potion = new ItemDto { Id = "potion" };
        var relic = new ItemDto { Id = "relic", Kind = EItemKind.Relic };
        Assert.That(inventory.TryAdd(relic), Is.True);
        Assert.That(inventory.TryAdd(relic), Is.False);
        for (var i = 0; i < 5; i++) Assert.That(inventory.TryAdd(potion), Is.True);
        Assert.That(inventory.TryAdd(potion), Is.False);
        Assert.That(inventory.TryAdd(potion, -1), Is.False);
        Assert.That(inventory.TryAdd(potion, 5), Is.False);
        Assert.That(inventory.TryAdd(new ItemDto { Id = "other" }, 1), Is.True);
        Assert.That(inventory.Consumables, Is.EqualTo(new[] { "potion", "other", "potion", "potion", "potion" }));
        Assert.That(inventory.RemoveConsumable(1, "potion"), Is.False);
        Assert.That(inventory.RemoveConsumable(1, "other"), Is.True);
        Assert.That(inventory.Consumables[1], Is.Null);
        Assert.That(inventory.TryAdd(potion, -1), Is.False, "invalid replacement must not index a free slot");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Consumable_heals_shared_hp_without_energy_action_or_active_skill_cost(bool duringMarbleFlight)
    {
        var registry = Definitions();
        var character = CharacterBattleInstance.CreateForTests("c", new Dictionary<string, float>
        { [AttributeIds.MaxHealth] = 100, [AttributeIds.MaxEnergy] = 5, [AttributeIds.InitialEnergy] = 5 });
        using var sim = new CombatSimulation(new PlayerTeamState([character], 100), new EnemyTeamState([new EnemyUnit("e", "enemy", 100)]),
            new CombatRuleEngine([]), registry, initialPhase: ECombatPhase.Player);
        sim.Items.TryAdd(registry.Store.Items["potion"], 4);
        DamagePipeline.Settle(sim, new(ECombatSide.Enemy, 0), new(ECombatSide.Player, 0), 40);
        var energy = character.CurrentEnergy;
        if (duringMarbleFlight)
        {
            Assert.That(sim.TryApply(new EndTurnCommand()).Success, Is.True);
            Assert.That(sim.TryApply(new LaunchMarbleCommand(1.35f)).Success, Is.True);
            Assert.That(sim.CanUseItemInCurrentPhase(new ItemDto { Id = "restricted", CardPhaseOnly = true }), Is.False);
        }
        var command = new UseConsumableCommand(4, "potion", 0, []);
        Assert.That(sim.TryApply(command).Success, Is.True);
        Assert.That(sim.PlayerTeam.SharedHp, Is.EqualTo(80));
        Assert.That(character.CurrentEnergy, Is.EqualTo(energy));
        Assert.That(character.SkillCounter, Is.Zero);
        Assert.That(sim.Items.Consumables[4], Is.Null);
        Assert.That(sim.TryApply(command).Success, Is.False);
    }

    [Test]
    public void Invalid_phase_identity_and_targets_never_consume_potions()
    {
        var registry = Definitions();
        var character = CharacterBattleInstance.CreateForTests("c", new Dictionary<string, float> { [AttributeIds.MaxHealth] = 100 });
        using var sim = new CombatSimulation(new PlayerTeamState([character], 100), new EnemyTeamState([new EnemyUnit("e", "enemy", 100)]),
            new CombatRuleEngine([]), registry, initialPhase: ECombatPhase.Player);
        sim.Items.TryAdd(registry.Store.Items["potion"]);
        Assert.That(sim.TryApply(new UseConsumableCommand(0, "wrong", 0, [])).Success, Is.False);
        Assert.That(sim.TryApply(new UseConsumableCommand(0, "potion", 0, [new(ECombatSide.Enemy, 0)])).Success, Is.False);
        Assert.That(sim.Items.Consumables[0], Is.EqualTo("potion"));
        using var enemyPhase = new CombatSimulation(new PlayerTeamState([character], 100), new EnemyTeamState([new EnemyUnit("e", "enemy", 100)]),
            new CombatRuleEngine([]), registry, initialPhase: ECombatPhase.Enemy);
        enemyPhase.Items.TryAdd(registry.Store.Items["potion"]);
        Assert.That(enemyPhase.TryApply(new UseConsumableCommand(0, "potion", 0, [])).Success, Is.False);
        Assert.That(enemyPhase.Items.Consumables[0], Is.EqualTo("potion"));
    }

    #endregion

    #region 路线与事件

    [Test]
    public void Map_is_seeded_equal_length_and_events_are_unique_across_branches_and_rings()
    {
        var definitions = Definitions();
        var first = new RunController(new RunMod { StoryId = "story", RunSeed = 27 });
        var second = new RunController(new RunMod { StoryId = "story", RunSeed = 27 });
        var service = new RunAdventureService(first, definitions);
        service.EnsureMap();
        new RunAdventureService(second, definitions).EnsureMap();
        Assert.That(JsonSerializer.Serialize(first.State.Map), Is.EqualTo(JsonSerializer.Serialize(second.State.Map)));
        Assert.That(first.State.Map!.Nodes.GroupBy(node => node.Branch).Select(group => group.Count()), Is.EqualTo(new[] { 3, 3 }));
        foreach (var branch in first.State.Map.Nodes.GroupBy(node => node.Branch))
            Assert.That(branch.Select(node => node.Kind), Is.EqualTo(new[] { ERunNodeKind.Event, ERunNodeKind.Shop, ERunNodeKind.VisibleEvent }));
        var reserved = first.State.ReservedEventIds.ToHashSet();
        Assert.That(reserved.Count, Is.EqualTo(2));
        first.State.CurrentRing = 2;
        service.EnsureMap();
        Assert.That(first.State.Map.Nodes.Where(node => node.EventId is not null).All(node => !reserved.Contains(node.EventId!)), Is.True);
        Assert.That(first.State.ReservedEventIds.Count, Is.EqualTo(4));
    }

    [Test]
    public void Route_requires_selection_then_allows_free_order_and_repeat_shop_but_single_use_events()
    {
        var controller = new RunController(new RunMod { StoryId = "empty" });
        var service = new RunAdventureService(controller, Definitions());
        service.EnsureMap();
        Assert.That(service.OpenNode(0, 2), Is.False);
        Assert.That(service.CanStartBoss, Is.False);
        Assert.That(service.SelectBranch(-1), Is.False);
        Assert.That(service.SelectBranch(2), Is.False);
        Assert.That(service.SelectBranch(1), Is.True);
        Assert.That(service.SelectBranch(0), Is.False);
        foreach (var step in new[] { 2, 1, 1, 0 })
        {
            Assert.That(service.OpenNode(1, step), Is.True);
            Assert.That(service.OpenNode(0, step), Is.False);
            Assert.That(service.OpenNode(1, 1), Is.False, "an open node must be resolved or left first");
            Assert.That(service.CanStartBoss, Is.False);
            Assert.That(service.CanClaimVisibleReward() ? service.ClaimVisibleReward() : service.LeaveNode(), Is.True);
        }
        Assert.That(service.OpenNode(1, 0), Is.False);
        Assert.That(service.OpenNode(1, 2), Is.False);
        Assert.That(service.CanStartBoss, Is.True);
        Assert.That(service.OpenNode(1, 1), Is.True);
        Assert.That(service.CanStartBoss, Is.False);
        Assert.That(service.LeaveNode(), Is.True);
        Assert.That(service.CanStartBoss, Is.True);
        Assert.That(controller.State.Map!.Nodes.Where(node => node.Branch == 0).All(node => !node.Completed), Is.True);
    }

    [Test]
    public void Boss_requires_two_events_but_does_not_require_visiting_shop()
    {
        var service = new RunAdventureService(new RunController(new RunMod { StoryId = "empty" }), Definitions());
        service.EnsureMap(); service.SelectBranch(0);
        service.OpenNode(0, 2);
        if (service.CanClaimVisibleReward()) service.ClaimVisibleReward(); else service.LeaveNode();
        Assert.That(service.CanStartBoss, Is.False);
        service.OpenNode(0, 0); service.LeaveNode();
        Assert.That(service.CanStartBoss, Is.True);
        Assert.That(service.State.Map!.Nodes.Single(node => node.Branch == 0 && node.Kind == ERunNodeKind.Shop).Completed, Is.False);
    }

    [Test]
    public void StartBoss_starts_from_init_phase()
    {
        var registry = Definitions();
        var controller = new RunController(new RunMod { StoryId = "story", Phase = ERunPhase.Init });
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            var character = new CharacterInstance(new CharacterDto { Id = $"hero{i}", Cards = ["hp"] });
            controller.State.AddToCharacterPool(character);
            controller.State.PlayerStates[i].SetActiveCharacter(character);
        }

        controller.State.Map = new RunMapState
        {
            Ring = 1,
            Position = 0,
            SelectedBranch = 0,
            NodeOpen = false,
            Nodes =
            [
                new RunMapNode { Branch = 0, Step = 0, Kind = ERunNodeKind.Event, Completed = true },
                new RunMapNode { Branch = 0, Step = 1, Kind = ERunNodeKind.Shop },
                new RunMapNode { Branch = 0, Step = 2, Kind = ERunNodeKind.Event, Completed = true },
            ],
        };
        var service = new RunAdventureService(controller, registry);
        Assert.That(service.CanStartBoss, Is.True);

        Assert.That(service.StartBoss(out var error), Is.True, error);
        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.Battle));
        controller.AbandonRun();
    }

    [Test]
    public void StartBoss_failure_keeps_the_previous_phase()
    {
        var controller = new RunController(new RunMod { StoryId = "story", Phase = ERunPhase.Init });
        controller.State.Map = new RunMapState
        {
            Ring = 1,
            Position = 0,
            SelectedBranch = 0,
            NodeOpen = false,
            Nodes =
            [
                new RunMapNode { Branch = 0, Step = 0, Kind = ERunNodeKind.Event, Completed = true },
                new RunMapNode { Branch = 0, Step = 1, Kind = ERunNodeKind.Shop },
                new RunMapNode { Branch = 0, Step = 2, Kind = ERunNodeKind.Event, Completed = true },
            ],
        };
        var service = new RunAdventureService(controller, Definitions());
        Assert.That(service.CanStartBoss, Is.True);

        Assert.That(service.StartBoss(out var error), Is.False);
        Assert.That(error, Does.Contain("上阵角色"));
        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.Init),
            "开战失败必须还原进入探索前的阶段");
    }

    [Test]
    public void Conditions_are_explained_and_checked_again_at_submission()
    {
        var controller = new RunController(new RunMod { StoryId = "story", SharedGold = 20 });
        var service = new RunAdventureService(controller, Definitions());
        service.EnsureMap();
        service.SelectBranch(0);
        service.OpenNode(0, 0);
        var option = service.CurrentEvent!.Options.Single();
        var requirement = service.Requirements(option).Single();
        Assert.That(requirement.Passed, Is.False);
        Assert.That(requirement.TextKey, Is.EqualTo("UI_EVENT_NEED_GOLD"));
        Assert.That(requirement.Args, Is.EqualTo(new object[] { 50, 20 }));
        Assert.That(service.ChooseOption("take"), Is.False);
        controller.State.SharedGold = 50;
        Assert.That(service.ChooseOption("take"), Is.True);
        Assert.That(controller.State.SharedGold, Is.EqualTo(60));
        Assert.That(controller.State.Map!.Nodes.Single(node => node.Branch == 0 && node.Step == 0).Completed, Is.True);
        Assert.That(service.ChooseOption("take"), Is.False);
    }

    [Test]
    public void Legacy_event_with_only_relic_options_can_be_left_without_granting_a_reward()
    {
        var controller = new RunController(new RunMod { StoryId = "story" });
        var service = new RunAdventureService(controller, Definitions());
        service.EnsureMap();
        service.SelectBranch(0);
        service.OpenNode(0, 0);
        var options = service.CurrentEvent!.Options;
        options.Clear();
        options.Add(new() { OptionId = "claim", ItemRewards = ["relic"] });
        options.Add(new() { OptionId = "show", Conditions = [new() { Kind = "HasItem", Params = new() { ["itemId"] = "relic" } }] });

        Assert.That(options.Any(service.IsVisibleOption), Is.False);
        Assert.That(service.ChooseOption("claim"), Is.False);
        Assert.That(service.GrantItem("relic"), Is.False);
        Assert.That(service.State.Items.Relics, Is.Empty);
        Assert.That(service.LeaveNode(), Is.True);
        Assert.That(service.OpenNode(0, 0), Is.False);
    }

    [Test]
    public void Event_with_only_unexecutable_options_can_be_left()
    {
        var controller = new RunController(new RunMod { StoryId = "story", SharedGold = 50 });
        var service = new RunAdventureService(controller, Definitions());
        service.EnsureMap();
        service.SelectBranch(0);
        service.OpenNode(0, 0);
        var options = service.CurrentEvent!.Options;
        options.Clear();
        // 分页选项当前不可执行（NextPageIndex 尚未实现）；它仍然可见，旧逻辑会同时禁用按钮并拒绝离开。
        options.Add(new() { OptionId = "page", LabelId = "page", NextPageIndex = 0 });

        Assert.That(service.IsVisibleOption(options[0]), Is.True);
        Assert.That(service.CanChoose(options[0]), Is.False);
        Assert.That(service.CanLeaveCurrentEvent, Is.True);
        Assert.That(service.LeaveNode(), Is.True);
        Assert.That(controller.State.Map!.CurrentNode!.Completed, Is.True);
    }

    [Test]
    public void Event_with_an_unmet_requirement_can_be_left()
    {
        var controller = new RunController(new RunMod { StoryId = "story" });
        var service = new RunAdventureService(controller, Definitions());
        service.EnsureMap();
        service.SelectBranch(0);
        service.OpenNode(0, 0);

        Assert.That(service.CurrentEvent, Is.Not.Null);
        Assert.That(service.CanChoose(service.CurrentEvent!.Options.Single()), Is.False, "金币不足");
        Assert.That(service.CanLeaveCurrentEvent, Is.True);
        Assert.That(service.LeaveNode(), Is.True);
    }

    [Test]
    public void Failed_event_inventory_grant_is_atomic_and_declining_new_potion_completes_event()
    {
        var controller = new RunController(new RunMod { StoryId = "story", SharedGold = 50 });
        var service = new RunAdventureService(controller, Definitions());
        service.EnsureMap(); service.SelectBranch(0); service.OpenNode(0, 0);
        for (var i = 0; i < 5; i++) controller.State.Items.TryAdd(service.Store.Items["potion"]);
        Assert.That(service.ChooseOption("take"), Is.False);
        Assert.That(controller.State.SharedGold, Is.EqualTo(50));
        Assert.That(controller.State.Map!.Position, Is.Zero);
        Assert.That(service.ChooseOption("take", declineConsumables: true), Is.True);
        Assert.That(controller.State.SharedGold, Is.EqualTo(60));
        Assert.That(controller.State.Items.Consumables.All(id => id == "potion"), Is.True);
        Assert.That(controller.State.Map.Nodes.Single(node => node.Branch == 0 && node.Step == 0).Completed, Is.True);
    }

    [Test]
    public void Event_rewarding_two_consumables_into_one_free_slot_can_complete_with_one_replacement()
    {
        var controller = new RunController(new RunMod { StoryId = "story", SharedGold = 50 });
        var service = new RunAdventureService(controller, Definitions());
        service.EnsureMap(); service.SelectBranch(0); service.OpenNode(0, 0);
        for (var i = 0; i < 4; i++)
            controller.State.Items.TryAdd(service.Store.Items["potion"]);

        var option = service.CurrentEvent!.Options.Single();
        option.ItemRewards.Clear();
        option.ItemRewards.Add("potion");
        option.ItemRewards.Add("potion");

        Assert.That(service.CanChoose(option), Is.True, "容量不足不能禁用选项，否则事件节点无法离开");
        Assert.That(service.NeedsConsumableReplacement(option), Is.True);
        Assert.That(service.ChooseOption(option.OptionId), Is.False, "直接提交必须原子失败");
        Assert.That(
            controller.State.Items.Consumables.Count(id => id is null),
            Is.EqualTo(1),
            "失败的提交不得改变库存");

        Assert.That(service.ChooseOption(option.OptionId, 0), Is.True, "替换一个槽后两件奖励都放得下");
        Assert.That(controller.State.Items.Consumables.All(id => id == "potion"), Is.True);
        Assert.That(controller.State.Map!.CurrentNode!.Completed, Is.True);
    }

    [Test]
    public void Event_rewarding_two_consumables_into_a_full_inventory_supports_multiple_replacements()
    {
        var controller = new RunController(new RunMod { StoryId = "story", SharedGold = 50 });
        var service = new RunAdventureService(controller, Definitions());
        service.EnsureMap(); service.SelectBranch(0); service.OpenNode(0, 0);
        for (var i = 0; i < 5; i++)
            controller.State.Items.TryAdd(service.Store.Items["potion"]);

        var option = service.CurrentEvent!.Options.Single();
        option.ItemRewards.Clear();
        option.ItemRewards.Add("potion");
        option.ItemRewards.Add("potion");

        Assert.That(service.ChooseOption(option.OptionId, 0), Is.False, "满槽时一个替换位仍不够");
        Assert.That(service.ChooseOption(option.OptionId, new[] { 0, 1 }), Is.True);
        Assert.That(controller.State.Items.Consumables.All(id => id == "potion"), Is.True);
        Assert.That(controller.State.Map!.CurrentNode!.Completed, Is.True);
    }

    #endregion

    #region 商店与快照

    [Test]
    public void Purchases_check_inventory_before_payment_and_persist_sold_out_state()
    {
        var controller = new RunController(new RunMod { StoryId = "story", SharedGold = 50 });
        var service = new RunAdventureService(controller, Definitions());
        service.EnsureMap(); service.SelectBranch(0); service.OpenNode(0, 0); service.ChooseOption("take"); service.OpenNode(0, 1);
        for (var i = 0; i < 4; i++) controller.State.Items.TryAdd(service.Store.Items["potion"]);
        var before = controller.State.SharedGold;
        Assert.That(service.Buy(0), Is.False);
        Assert.That(controller.State.SharedGold, Is.EqualTo(before));
        Assert.That(service.Buy(0, 4), Is.True);
        Assert.That(controller.State.SharedGold, Is.EqualTo(before - 10));
        Assert.That(service.Buy(0, 4), Is.False);
        var restored = new RunMod(); restored.RestoreFrom(controller.State.ToDto());
        Assert.That(restored.Map!.CurrentNode!.Offers[0].Sold, Is.True);
        Assert.That(restored.Items.Consumables, Is.EqualTo(controller.State.Items.Consumables));
        Assert.That(service.Buy(1), Is.False);
        Assert.That(controller.State.Items.Relics, Is.Empty);
        Assert.That(service.LeaveNode(), Is.True);
        Assert.That(service.OpenNode(0, 1), Is.True);
        Assert.That(service.Buy(0, 4), Is.False, "reentry must not refresh sold goods");
        restored.RestoreFrom(controller.State.ToDto());
        var restoredService = new RunAdventureService(new RunController(restored), Definitions());
        Assert.That(restoredService.LeaveNode(), Is.True);
        Assert.That(restoredService.OpenNode(0, 1), Is.True);
        Assert.That(restoredService.Buy(0, 1), Is.False);
        Assert.That(restoredService.OpenNode(1, 0), Is.False);
        Assert.That(restoredService.LeaveNode(), Is.True);
        Assert.That(restoredService.OpenNode(0, 0), Is.False, "completed event must remain single-use after restoring a save");
        restored.Map!.Nodes.Single(node => node.Branch == 0 && node.Kind == ERunNodeKind.Shop).Completed = true;
        Assert.That(restoredService.OpenNode(0, 1), Is.True, "shops marked completed by an older save must still allow reentry");
    }

    [Test]
    public void Snapshots_are_independent_and_old_saves_default_to_empty_items()
    {
        var controller = new RunController(new RunMod { StoryId = "story" });
        var service = new RunAdventureService(controller, Definitions()); service.EnsureMap();
        controller.State.Items.TryAdd(service.Store.Items["potion"]);
        var snapshot = controller.State.ToDto();
        service.SelectBranch(1); service.OpenNode(1, 0); controller.State.Items.RemoveConsumable(0);
        Assert.That(snapshot.Map!.SelectedBranch, Is.EqualTo(-1));
        Assert.That(snapshot.Consumables[0], Is.EqualTo("potion"));
        var normalized = new RunDto { SchemaVersion = 2 }.Normalize();
        Assert.That(normalized.SchemaVersion, Is.EqualTo(2));
        var old = new RunMod(); old.RestoreFrom(normalized);
        Assert.That(old.Items.Consumables, Is.EqualTo(new string?[5]));
        Assert.That(old.Items.Relics, Is.Empty);
    }

    [Test]
    public void Defeat_phase_event_observes_restored_inventory()
    {
        var registry = Definitions();
        var controller = new RunController(new RunMod { StoryId = "story", Phase = ERunPhase.Event });
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            var character = new CharacterInstance(new CharacterDto { Id = $"hero{i}", Cards = ["hp"] });
            controller.State.AddToCharacterPool(character);
            controller.State.PlayerStates[i].SetActiveCharacter(character);
        }

        controller.State.Items.TryAdd(registry.Store.Items["potion"]);
        string? potionWhenEvent = null;

        using (controller.StartBattle(registry, new HostRng(1, "battle"), 1, "battle"))
        {
            Assert.That(controller.State.Items.RemoveConsumable(0, "potion"), Is.True);
            Assert.That(controller.State.Items.Consumables[0], Is.Null);

            var listener = controller.State.OnRunPhaseChanged((payload, _) =>
            {
                if (payload.CurrentPhase == ERunPhase.Event)
                {
                    potionWhenEvent = controller.State.Items.Consumables[0];
                }
            }, this);

            controller.EndBattle(won: false);
            listener.Off();
        }

        Assert.That(potionWhenEvent, Is.EqualTo("potion"),
            "战败回滚的 RunPhaseChanged 必须能读到已经恢复的背包，否则 RunMainWin 会渲染战斗中状态");
        Assert.That(controller.State.Items.Consumables[0], Is.EqualTo("potion"));
    }

    [Test]
    public void Relics_run_after_passives_and_defeat_restores_items_and_boss_route()
    {
        var registry = Definitions();
        var controller = new RunController(new RunMod { StoryId = "story", Phase = ERunPhase.Event });
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            var character = new CharacterInstance(new CharacterDto { Id = $"hero{i}", Cards = ["hp"] });
            controller.State.AddToCharacterPool(character);
            controller.State.PlayerStates[i].SetActiveCharacter(character);
        }
        controller.State.Items.TryAdd(registry.Store.Items["potion"]);
        controller.State.Items.TryAdd(registry.Store.Items["relic"]);
        controller.State.Map = new RunMapState
        {
            Ring = 1, Position = 0, SelectedBranch = 1, Nodes = [
            new RunMapNode { Branch = 1, Step = 0, Kind = ERunNodeKind.Event, Completed = true },
            new RunMapNode { Branch = 1, Step = 1, Kind = ERunNodeKind.Shop },
            new RunMapNode { Branch = 1, Step = 2, Kind = ERunNodeKind.Event, Completed = true },
        ]
        };
        var sim = controller.StartBattle(registry, new HostRng(1, "battle"), 1, "battle");
        Assert.That(sim.PlayerTeam.Characters.All(character => character.Asc.GetCurrentValue(AttributeIds.Shield) == 7), Is.True);
        Assert.That(sim.TryApply(new UseConsumableCommand(0, "potion", 0, [])).Success, Is.True);
        Assert.That(controller.State.Items.Consumables[0], Is.Null);
        controller.EndBattle(false);
        Assert.That(controller.State.Items.Consumables[0], Is.EqualTo("potion"));
        Assert.That(controller.State.Items.Relics, Is.EqualTo(new[] { "relic" }));
        Assert.That(controller.State.Map!.Position, Is.Zero);
        Assert.That(controller.State.Map.SelectedBranch, Is.EqualTo(1));
        Assert.That(new RunAdventureService(controller, registry).CanStartBoss, Is.True);
    }

    #endregion

    internal static GameDefinitionRegistry Definitions()
    {
        var events = Enumerable.Range(0, 8).Select(i => new EventDto
        {
            Id = $"event{i}", EventKind = EEventKind.Data,
            Options = [new EventOptionDto { OptionId = "take", LabelId = "take", ItemRewards = ["potion"],
                Conditions = [new ConditionRefDto { Kind = "GoldAtLeast", Params = new() { ["amount"] = 50 } }],
                EffectRefs = [new EffectRefDto { EffectId = "gold" }] }],
        }).ToDictionary(evt => evt.Id);
        var bundle = ModDefinitionsBundle.Empty with
        {
            Events = events,
            Items = new Dictionary<string, ItemDto>
            {
                ["potion"] = new()
                {
                    Id = "potion", ShopPrice = 10, TargetSpec = new() { Side = ETargetSide.Ally, Scope = ETargetScope.Team },
                    UseSkillRefs = [new SkillRefDto { SkillId = "heal" }]
                },
                ["relic"] = new()
                {
                    Id = "relic", Kind = EItemKind.Relic, ShopPrice = 10,
                    BattleStartSkillRefs = [new SkillRefDto { SkillId = "shield", Params = new() { ["amount"] = 7 } }]
                },
            },
            Skills = new Dictionary<string, SkillDto>
            {
                ["heal"] = new() { Id = "heal", TargetOverride = new() { Side = ETargetSide.Ally, Scope = ETargetScope.Team }, EffectRefs = [new() { EffectId = "heal" }] },
                ["shield"] = new() { Id = "shield", TargetOverride = new() { Side = ETargetSide.Ally, Scope = ETargetScope.All }, ActionRefs = [new() { ActionId = "shield" }] },
            },
            Effects = new Dictionary<string, EffectDto>
            {
                ["heal"] = new() { Id = "heal", Kind = EEffectKind.Heal, Params = new() { ["amount"] = 20 } },
                ["gold"] = new() { Id = "gold", Kind = EEffectKind.GainResource, Params = new() { ["resource"] = "Gold", ["amount"] = 10 } },
            },
            SkillActions = new Dictionary<string, SkillActionDto>
            { ["shield"] = new() { Id = "shield", Kind = ESkillActionKind.GainShield, Params = new() { ["amount"] = 1 } } },
            Cards = new Dictionary<string, CardDto> { ["hp"] = new() { Id = "hp", Stats = new() { HpCap = 25 } } },
            Enemies = new Dictionary<string, EnemyDto> { ["enemy"] = new() { Id = "enemy", MaxHp = 100 } },
            Battles = new Dictionary<string, BattleDto> { ["battle"] = new() { Id = "battle", Waves = [new() { EnemySpawns = [new() { EnemyId = "enemy", Count = 1 }] }] } },
            Stories = new Dictionary<string, StoryDto>
            {
                ["story"] = new()
                {
                    Id = "story", ShopItemIds = ["potion", "relic"], RingEventPools = Enumerable.Range(1, 2).Select(ring =>
                new RingEventPoolDto { Ring = ring, Events = events.Keys.Select(id => new WeightedEventRefDto { EventId = id, Weight = 1 }).ToList() }).ToList()
                }
            },
        };
        var registry = new GameDefinitionRegistry();
        registry.Rebuild([new ModContentBundle("test.mod", bundle)], out var report);
        Assert.That(report.RemovedValidationErrors, Is.Empty);
        return registry;
    }
}