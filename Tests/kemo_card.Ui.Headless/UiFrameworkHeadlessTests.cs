using System.Reflection;
using Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.UI.Def;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run.Ui.CombatUi;
using KemoCard.Frame.Content;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Run.Ui.CombatUi.Presentation;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Adventure;

namespace KemoCard.Ui.Headless;

/// <summary>通过真实 Godot 节点和默认状态处理器验证 UI 契约；正常构建不包含本类。</summary>
public partial class UiFrameworkHeadlessTests : Node
{
    private readonly UIRuntimeRegistry _registry = new();
    private UIManager _manager = null!;
    private Control _stage = null!;
    private int _id;
    private int _failed;

    public override void _Ready() => _ = RunAllAsync();

    #region 执行与夹具

    private async Task RunAllAsync()
    {
        _stage = new Control();
        AddChild(_stage);
        _manager = new UIManager();
        AddChild(_manager);
        _manager.Init(new UIManagerInitOpt
        {
            Registry = _registry, StageRoot = _stage,
            Layers = [EUILayer.Win, EUILayer.Dlg, EUILayer.Pop],
            TopLayers = [EUILayer.Debug], KnownOwnerModIds = ["tests", "other", "remove"],
        });
        (string Name, Func<Task> Run)[] cases =
        [
            (nameof(WindowDefaults), () => Run(WindowDefaults)),
            (nameof(HideBelowAndBackAsync), HideBelowAndBackAsync),
            (nameof(CrossLayerVisibility), () => Run(CrossLayerVisibility)),
            (nameof(VisibilityCallbackCanCloseWindows), () => Run(VisibilityCallbackCanCloseWindows)),
            (nameof(TopLayerQueries), () => Run(TopLayerQueries)),
            (nameof(DialogsCoexist), () => Run(DialogsCoexist)),
            (nameof(MaskCacheAsync), MaskCacheAsync),
            ("close waits for window animation", () => Run(() => CloseWaitsForBothAnimations(false))),
            ("close waits for mask animation", () => Run(() => CloseWaitsForBothAnimations(true))),
            (nameof(ReopenCancelsOldClose), () => Run(ReopenCancelsOldClose)),
            (nameof(CloseAnimationFailureStillCloses), () => Run(CloseAnimationFailureStillCloses)),
            (nameof(StalePreloadFailure), () => Run(StalePreloadFailure)),
            (nameof(CloseCachedPreload), () => Run(CloseCachedPreload)),
            (nameof(ThrowingCallbacksAdvanceQueue), () => Run(ThrowingCallbacksAdvanceQueue)),
            (nameof(CallbackReentryPreservesRequest), () => Run(CallbackReentryPreservesRequest)),
            (nameof(ExitFailureStillUnbinds), () => Run(ExitFailureStillUnbinds)),
            (nameof(MaskExitFailureStillUnbinds), () => Run(MaskExitFailureStillUnbinds)),
            (nameof(FitExitFailureStillUnbinds), () => Run(FitExitFailureStillUnbinds)),
            (nameof(UnregisterOwnerAdvancesQueue), () => Run(UnregisterOwnerAdvancesQueue)),
            (nameof(CachedPreloadGetsFreshDeadline), () => Run(CachedPreloadGetsFreshDeadline)),
            (nameof(TimeoutCallbackReentry), () => Run(TimeoutCallbackReentry)),
            (nameof(ColdSceneLifecycle), () => Run(ColdSceneLifecycle)),
            (nameof(ColdMaskLifecycleAsync), ColdMaskLifecycleAsync),
            (nameof(FailedResourceFactory), () => Run(FailedResourceFactory)),
            (nameof(BusinessHookFailure), () => Run(BusinessHookFailure)),
            (nameof(CloseFromOpenBefore), () => Run(CloseFromOpenBefore)),
            (nameof(CustomParentMaskCache), () => Run(CustomParentMaskCache)),
            (nameof(DestroyDuringCloseAsync), DestroyDuringCloseAsync),
            (nameof(CharacterSpriteFitsAndPlaysAsync), CharacterSpriteFitsAndPlaysAsync),
            (nameof(AllyActionsSurviveStateSyncAsync), AllyActionsSurviveStateSyncAsync),
            (nameof(EnemyIntentColorsAndSharedDamageAsync), EnemyIntentColorsAndSharedDamageAsync),
            (nameof(SkillButtonsChargesAndShieldsAsync), SkillButtonsChargesAndShieldsAsync),
            (nameof(EnemyWaterBubbleAsync), EnemyWaterBubbleAsync),
            (nameof(ItemsAndBranchMapAsync), ItemsAndBranchMapAsync),
            (nameof(VisibleRewardsOnMapAsync), VisibleRewardsOnMapAsync),
            (nameof(MapWiringFollowsLayoutAsync), MapWiringFollowsLayoutAsync),
        ];
        foreach (var test in cases)
        {
            try
            {
                await test.Run();
                GD.Print($"UI_HEADLESS_PASS {test.Name}");
            }
            catch (Exception ex)
            {
                _failed++;
                GD.PrintErr($"UI_HEADLESS_FAIL {test.Name}: {ex}");
            }
            finally { Cleanup(); }
        }
        GD.Print($"UI_HEADLESS_SUMMARY total={cases.Length} failed={_failed}");
        GetTree().Quit(_failed == 0 ? 0 : 1);
    }

    private static Task Run(Action action) { action(); return Task.CompletedTask; }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Opened(Task<UIVo?> task, UIVo vo) =>
        Require(task.IsCompletedSuccessfully && task.Result == vo, $"{vo.Id}: open task not successful");
    private static void Rejected(Task<UIVo?> task) =>
        Require(task.IsCompletedSuccessfully && task.Result == null, "request should complete with null");

    private (UIVo Vo, TestWindow Win) Seed(EUIType type = EUIType.Win, string owner = "tests")
    {
        string id = $"headless.{++_id}";
        Register(id, type, owner);
        var vo = _manager.VoRegistry.GetOrCreate(id, type, owner, null);
        var win = new TestWindow { TestId = id, TestType = type, UIVo = vo };
        vo.Runtime.UI = win;
        vo.Lifecycle.OpenTime = 1;
        vo.Lifecycle.DestroyTime = -1;
        vo.StateMachine.SetInitialState(EUIState.Cache);
        return (vo, win);
    }
    private void Register(string id, EUIType type = EUIType.Win, string owner = "tests") =>
        _registry.Register(new UIRuntimeEntry
        {
            Id = id, OwnerModId = owner, Dir = "Tests/kemo_card.Ui.Headless", Type = type,
            BaseOpenOpt = DefaultUIOpenOpt.ForType(type),
        });
    private static TestMask AttachMask(UIVo vo)
    {
        var mask = new TestMask { UIVo = vo };
        vo.Runtime.Mask = mask;
        return mask;
    }
    private void Cleanup()
    {
        foreach (var vo in _manager.VoRegistry.Map.Values.ToArray())
        {
            if (vo.UI is TestWindow win) win.ThrowOnExit = false;
            if (vo.Mask is TestMask mask) mask.ThrowOnExit = false;
            vo.StateMachine.TransitionTo(EUIState.Destroy, vo);
        }
        _manager.OpenNext();
    }
    private async Task NextFramesAsync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    #endregion

    #region 默认参数、显示与叠放

    private async Task SkillButtonsChargesAndShieldsAsync()
    {
        var actor = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/Combat/ActorInfoCmp.tscn").Instantiate<ActorInfoCmp>();
        var member = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/Combat/PartyMemberCmp.tscn").Instantiate<PartyMemberCmp>();
        var ally = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/Combat/AllyUnitCmp.tscn").Instantiate<AllyUnitCmp>();
        _stage.AddChild(actor);
        _stage.AddChild(member);
        _stage.AddChild(ally);
        try
        {
            var character = CharacterBattleInstance.CreateForTests("test-charge", new Dictionary<string, float>
            {
                [AttributeIds.MaxHealth] = 200, [AttributeIds.Shield] = 25,
            }, activeSkillChain: new[] { 2, 3, 4, 5 }.Select((cooldown, index) =>
                new ActiveSkillChainEntryDto { SkillId = $"charge-{index}", Cooldown = cooldown }).ToArray());
            var actorButton = actor.GetNode<Button>("Margin/VBox/Resources/SkillRow/Release/BtnRelease");
            var memberButton = member.GetNode<Button>("Margin/VBox/Header/Release/BtnRelease");
            var actorDots = actor.GetNode<Label>("Margin/VBox/Resources/SkillRow/Release/Charges");
            var memberDots = member.GetNode<Label>("Margin/VBox/Header/Release/Charges");
            void Sync()
            {
                actor.Bind(character, null, ECombatActionMark.None);
                member.Bind(0, character, null, true, true, ECombatActionMark.None);
                ally.Bind(0, character, null);
                actor.SetSkillRelease(CombatActiveSkillTips.CanRelease(character));
                member.SetSkillRelease(CombatActiveSkillTips.CanRelease(character));
            }
            Sync();
            Require(actorButton.Visible && memberButton.Visible && actorButton.Disabled && memberButton.Disabled,
                "unready skill buttons must remain visible and disabled");
            Require(actorDots.Text == "○ ○ ○ ○" && memberDots.Text == actorDots.Text, "empty charge dots incorrect");
            character.GainSkillCounter(2);
            Sync();
            await NextFramesAsync();
            Require(!actorButton.Disabled && !memberButton.Disabled && actorDots.Text == "● ○ ○ ○"
                && memberDots.Text == actorDots.Text, "first charge did not enable skill buttons");
            Require(actorDots.GlobalPosition.Y + actorDots.Size.Y <= actorButton.GlobalPosition.Y + 0.1f
                && memberDots.GlobalPosition.Y + memberDots.Size.Y <= memberButton.GlobalPosition.Y + 0.1f,
                "charge dots must be above the skill button");
            character.GainSkillCounter(7);
            Sync();
            Require(actorDots.Text == "● ● ● ○", "charge count must use cumulative tier thresholds");
            actor.SetSkillRelease(false);
            member.SetSkillRelease(false);
            Require(actorButton.Visible && memberButton.Visible && actorButton.Disabled && memberButton.Disabled,
                "input lock hid skill buttons");
            character.PaySkillCounter(9);
            Sync();
            Require(actorButton.Disabled && actorDots.Text == "○ ○ ○ ○", "casting did not refresh charges");
            using var sim = new CombatSimulation(new PlayerTeamState([character], 200),
                new EnemyTeamState([new EnemyUnit("test-enemy", "enemy", 400)]), new CombatRuleEngine([]), new GameDefinitionRegistry());
            Require(actor.GetNode<Label>("Margin/VBox/Grid/ShieldValue").Text == "25"
                && member.GetNode<Label>("Margin/VBox/Grid/ShieldValue").Text == "25" && ally.GetNode<Label>("Shield").Visible,
                "shield not shown in character panels and battlefield");
            DamagePipeline.Settle(sim, new(ECombatSide.Enemy, 0), new(ECombatSide.Player, 0), 10);
            Sync();
            Require(sim.PlayerTeam.SharedHp == 200 && actor.GetNode<Label>("Margin/VBox/Grid/ShieldValue").Text == "15"
                && member.GetNode<Label>("Margin/VBox/Grid/ShieldValue").Text == "15" && ally.GetNode<Label>("Shield").Text.Contains("15"),
                "partial absorption did not refresh shield values");
            DamagePipeline.Settle(sim, new(ECombatSide.Enemy, 0), new(ECombatSide.Player, 0), 20);
            Sync();
            Require(sim.PlayerTeam.SharedHp == 195 && actor.GetNode<Label>("Margin/VBox/Grid/ShieldValue").Text == "0"
                && !ally.GetNode<Label>("Shield").Visible, "depleted shield must show zero and hide battlefield badge");
            var noSkill = CharacterBattleInstance.CreateForTests("no-skill", new Dictionary<string, float>());
            actor.Bind(noSkill, null, ECombatActionMark.None);
            actor.SetSkillRelease(false);
            member.Bind(0, noSkill, null, false, false, ECombatActionMark.None);
            member.SetSkillRelease(false);
            Require(actorButton.Visible && memberButton.Visible && actorButton.Disabled && memberButton.Disabled
                && !actorDots.Visible && !memberDots.Visible, "no-skill characters must retain disabled buttons without charge dots");
        }
        finally
        {
            actor.QueueFree();
            member.QueueFree();
            ally.QueueFree();
            await NextFramesAsync();
        }
    }

    private async Task EnemyIntentColorsAndSharedDamageAsync()
    {
        var enemy = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/Combat/EnemyUnitCmp.tscn").Instantiate<EnemyUnitCmp>();
        var hp = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/Combat/HpBarCmp.tscn").Instantiate<HpBarCmp>();
        var animator = new CombatAnimator();
        _stage.AddChild(enemy);
        _stage.AddChild(hp);
        _stage.AddChild(animator);
        var translation = new Translation { Locale = TranslationServer.GetLocale() };
        translation.AddMessage("UI_COMBAT_INTENT_SKILL", "Intent: {0} (count {1})");
        translation.AddMessage("test.intent.name", "Strike");
        translation.AddMessage("test.intent.desc", "Deal 25 physical damage.");
        TranslationServer.AddTranslation(translation);
        try
        {
            var unit = new EnemyUnit("test-enemy", "test.enemy", 400);
            enemy.Bind(0, unit, new EnemyDto { Id = "test.enemy", DisplayNameId = "Bear" });
            Require(enemy.GetNode<Label>("Name").Text == "Bear", "enemy name contains life bar numbers");
            var sharedBar = hp.GetNode<ProgressBar>("Bar");
            var sharedStyle = sharedBar.GetThemeStylebox("fill");
            var sharedBackground = sharedBar.GetThemeStylebox("background");
            var enemyBar = enemy.GetNode<ProgressBar>("Hp/Bar");
            var colors = new[] { Colors.White, new Color("56c779"), new Color("f2a33a"), new Color("b878e0"), new Color("e85a5a") };
            for (var count = 1; count <= 6; count++)
            {
                enemy.SetLifeBarColor(0, count);
                var fill = (StyleBoxFlat)enemyBar.GetThemeStylebox("fill");
                Require(fill.BgColor == colors[Math.Min(count - 1, 4)], "life bar palette does not match remaining bars");
                var underlay = enemyBar.GetThemeStylebox("background");
                Require(count == 1 ? underlay is StyleBoxEmpty
                    : underlay is StyleBoxFlat next && next.BgColor == colors[Math.Min(count - 2, 4)],
                    "next life bar underlay has the wrong color");
                enemy.SetLifeBarColor(count - 1, count);
                Require(((StyleBoxFlat)enemyBar.GetThemeStylebox("fill")).BgColor == Colors.White
                    && enemyBar.GetThemeStylebox("background") is StyleBoxEmpty,
                    "last life bar must be white over a transparent underlay");
            }
            Require(sharedBar.GetThemeStylebox("fill") == sharedStyle
                && sharedBar.GetThemeStylebox("background") == sharedBackground, "enemy colors changed the shared HP theme");
            animator.Bind(new CombatProbeView(enemy, hp, _stage));
            enemy.SetLifeBarColor(0, 2);
            enemy.HpBar!.SetValue(200, 400);
            Require(enemyBar.Value == 200 && ((StyleBoxFlat)enemyBar.GetThemeStylebox("background")).BgColor == Colors.White,
                "partially depleted green bar lost its white underlay");
            enemy.HpBar.SetValue(0, 400);
            Require(((StyleBoxFlat)enemyBar.GetThemeStylebox("background")).BgColor == Colors.White,
                "depleted bar awaiting transition lost the next bar underlay");
            await animator.PlayAsync(new EnemyLifeBarChangedEvent(0, 1, 2, 600, 600));
            Require(((StyleBoxFlat)enemyBar.GetThemeStylebox("fill")).BgColor == Colors.White
                && enemyBar.GetThemeStylebox("background") is StyleBoxEmpty && enemyBar.Value == 600,
                "life bar transition did not update both foreground and underlay");
            await animator.PlayAsync(new EnemyIntentChangedEvent(0, "test.intent.name", "test.intent.desc", 2, false));
            var label = enemy.GetNode<Label>("Intent");
            Require(label.Text.Contains("Strike") && label.Text.Contains("2") && !label.Text.Contains("UI_COMBAT_INTENT_SKILL")
                && label.TooltipText == "Deal 25 physical damage.",
                $"intent name/count/description not visible: {label.Text} / {label.TooltipText}");
            var character = CharacterBattleInstance.CreateForTests("test-player", new CharacterAttributes(200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
            using var sim = new CombatSimulation(new PlayerTeamState([character], 200),
                new EnemyTeamState([unit]), new CombatRuleEngine([]), new GameDefinitionRegistry());
            hp.SetValue(sim.PlayerTeam.SharedHp, sim.PlayerTeam.MaxHp);
            DamagePipeline.Settle(sim, new(ECombatSide.Enemy, 0), new(ECombatSide.Player, 0), 25);
            foreach (var evt in sim.Presentation.Drain().OfType<DamageDealtEvent>())
                await animator.PlayAsync(evt);
            Require(sim.PlayerTeam.SharedHp == 175 && hp.Current == 175 && sharedBar.Value == 175
                && hp.GetNode<Label>("Value").Text == "175 / 200", "enemy damage did not reach shared HP display");
        }
        finally
        {
            TranslationServer.RemoveTranslation(translation);
            enemy.QueueFree();
            hp.QueueFree();
            animator.QueueFree();
            await NextFramesAsync();
        }
    }

    private async Task ItemsAndBranchMapAsync()
    {
        var banner = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/Items/RunItemBannerCmp.tscn")
            .Instantiate<KemoCard.Mod.Run.Ui.Items.RunItemBannerCmp>();
        var map = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/RunAdventureCmp.tscn").Instantiate<KemoCard.Mod.Run.Ui.RunAdventureCmp>();
        _stage.AddChild(banner);
        _stage.AddChild(map);
        try
        {
            var registry = new GameDefinitionRegistry();
            var potion = new ItemDto { Id = "potion", DisplayNameId = "Potion", UseSkillRefs = [new() { SkillId = "use" }] };
            var relic = new ItemDto { Id = "relic", DisplayNameId = "Relic", Kind = EItemKind.Relic };
            registry.Rebuild([new ModContentBundle("test.mod", ModDefinitionsBundle.Empty with
            {
                Items = new Dictionary<string, ItemDto> { [potion.Id] = potion, [relic.Id] = relic },
                Skills = new Dictionary<string, SkillDto> { ["use"] = new() { Id = "use" } },
                Events = new Dictionary<string, EventDto>
                {
                    ["event"] = new() { Id = "event", EventKind = EEventKind.Data, Options = [new() {
                        OptionId = "gold", LabelId = "test.option", Conditions = [new() { Kind = "GoldAtLeast", Params = new() { ["amount"] = 100 } }] }] },
                },
                Stories = new Dictionary<string, StoryDto>
                {
                    ["story"] = new() { Id = "story", RingEventPools = [new() { Ring = 1, Events = [new() { EventId = "event", Weight = 1 }] }] },
                },
            })], out var report);
            Require(!report.HasIssues, "UI probe definitions did not validate");
            var controller = new KemoCard.Mod.Run.RunController(new KemoCard.Mod.Run.RunMod { StoryId = "story", SharedGold = 0 });
            var service = new KemoCard.Mod.Run.Adventure.RunAdventureService(controller, registry);
            banner.Size = new Vector2(1000, 64);
            map.Size = new Vector2(1000, 500);
            map.Position = new Vector2(0, 80);
            banner.Bind(controller.State.Items, registry.Store, true);
            await NextFramesAsync();
            var slots = banner.GetNode<Container>("Margin/Row/Potions").GetChildren().OfType<KemoCard.Mod.Run.Ui.Items.RunItemSlotCmp>().ToArray();
            Require(slots.Length == 3 && slots.All(slot => slot.GetNode<Label>("Dot").Visible && !slot.GetNode<Button>("Item").Visible),
                "three empty potion slots must retain fixed dots");
            controller.State.Items.TryAdd(potion);
            controller.State.Items.TryAdd(relic);
            banner.Bind(controller.State.Items, registry.Store, true);
            var requested = -1;
            banner.PotionRequested = slot => requested = slot;
            slots[0].GetNode<Button>("Item").EmitSignal(BaseButton.SignalName.Pressed);
            Require(requested == 0 && slots[0].GetNode<Button>("Item").Visible && slots[0].GetNode<Label>("Dot").Visible,
                "potion overlay must not hide the underlying dot");
            await NextFramesAsync();
            var relicNode = banner.GetNode<Container>("Margin/Row/RelicScroll/Relics").GetChildren().OfType<KemoCard.Mod.Run.Ui.Items.RunItemSlotCmp>().Single();
            Require(relicNode.GetGlobalRect().Position.X > slots[2].GetGlobalRect().End.X, "relics must be right of potions");
            banner.SetEnabled(false);
            Require(slots[0].GetNode<Button>("Item").Disabled, "combat playback must disable potion input");
            controller.State.Items.RemovePotion(0);
            banner.Bind(controller.State.Items, registry.Store, true);
            Require(!slots[0].GetNode<Button>("Item").Visible && slots[0].GetNode<Label>("Dot").Visible,
                "consumed potion must reveal the fixed empty slot dot");

            map.Bind(service);
            var visibleIndex = controller.State.Map!.Nodes.FindIndex(node => node.Branch == 0 && node.Step == 2);
            controller.State.Map.Nodes[visibleIndex] = controller.State.Map.Nodes[visibleIndex] with
            { VisibleReward = new KemoCard.Mod.Run.Adventure.RunVisibleReward { Kind = KemoCard.Mod.Run.Adventure.EVisibleEventKind.Gold } };
            map.Bind(service);
            await NextFramesAsync();
            Button Node(int b, int s) => map.GetNode<Button>($"Map/Routes/Lanes/Branch{b}/Node{s}");
            Button Select(int b) => map.GetNode<Button>($"Map/Routes/Lanes/Branch{b}/Select");
            Require(!Select(0).Disabled && !Select(1).Disabled && Node(0, 0).Disabled && Node(0, 1).Disabled
                && map.GetNode<Button>("Map/Routes/Boss").Disabled, "map must require an explicit route selection first");
            Require(Node(0, 2).Text.Contains("150"), "visible reward must be written on the route before selection");
            Select(0).EmitSignal(BaseButton.SignalName.Pressed);
            map.Bind(service);
            Require(Select(0).Disabled && Select(1).Disabled && !Node(0, 0).Disabled && !Node(0, 1).Disabled
                && !Node(0, 2).Disabled && Node(1, 0).Disabled, "selected route must allow every node in any order and lock the other route");
            Node(0, 2).EmitSignal(BaseButton.SignalName.Pressed);
            map.Bind(service);
            Require(!map.GetNode<Button>("Leave").Visible, "available visible reward must be claimed rather than silently skipped");
            var claimRow = map.GetNode<Container>("Scroll/Options").GetChildren().OfType<KemoCard.Mod.Run.Ui.Items.RunOptionCmp>().Single();
            claimRow.GetNode<Button>("Button").EmitSignal(BaseButton.SignalName.Pressed);
            map.Bind(service);
            Require(controller.State.SharedGold == 150, "visible gold reward must be granted exactly once");
            Require(Node(0, 2).Disabled && Node(0, 2).Text.Contains("✓") && !Node(0, 0).Disabled,
                "last displayed event can be completed first but cannot be repeated");
            controller.State.SharedGold = 0;
            Node(0, 0).EmitSignal(BaseButton.SignalName.Pressed);
            map.Bind(service);
            var row = map.GetNode<Container>("Scroll/Options").GetChildren().OfType<KemoCard.Mod.Run.Ui.Items.RunOptionCmp>().Single();
            Require(row.GetNode<Button>("Button").Disabled && row.GetNode<Label>("Details").Visible
                && row.GetNode<Label>("Details").Text.Contains("100"), "unmet event option must be grey with visible requirements");
            controller.State.SharedGold = 100;
            map.Bind(service);
            row = map.GetNode<Container>("Scroll/Options").GetChildren().OfType<KemoCard.Mod.Run.Ui.Items.RunOptionCmp>().Single();
            Require(!row.GetNode<Button>("Button").Disabled, "event option did not unlock when requirements became satisfied");
            row.GetNode<Button>("Button").EmitSignal(BaseButton.SignalName.Pressed);
            map.Bind(service);
            Require(Node(0, 0).Text.Contains("✓") && !Node(0, 1).Disabled && Node(1, 0).Disabled,
                "event completion must disable that event and preserve access to the shop");
            Require(!map.GetNode<Button>("Map/Routes/Boss").Disabled, "both events should unlock the boss without a shop visit");
            Node(0, 1).EmitSignal(BaseButton.SignalName.Pressed);
            map.Bind(service);
            Require(map.GetNode<Button>("Leave").Visible && map.GetNode<Container>("Scroll/Options").GetChildCount() == 0,
                "empty shop framework must have a working leave action");
            map.GetNode<Button>("Leave").EmitSignal(BaseButton.SignalName.Pressed);
            map.Bind(service);
            Require(!Node(0, 1).Disabled && !Node(0, 1).Text.Contains("✓"), "shop must remain available after leaving");
            Node(0, 1).EmitSignal(BaseButton.SignalName.Pressed);
            map.Bind(service);
            Require(map.GetNode<Button>("Leave").Visible && map.GetNode<Button>("Map/Routes/Boss").Disabled,
                "shop must be repeatable and must be left before starting the boss");
            map.GetNode<Button>("Leave").EmitSignal(BaseButton.SignalName.Pressed);
            map.Bind(service);
            Require(!map.GetNode<Button>("Map/Routes/Boss").Disabled, "completing the route must unlock its single boss");
        }
        finally
        {
            banner.QueueFree(); map.QueueFree(); await NextFramesAsync();
        }
    }

    private async Task VisibleRewardsOnMapAsync()
    {
        var registry = new GameDefinitionRegistry();
        var cards = Enum.GetValues<ERole>().Where(role => role != ERole.None).SelectMany(role =>
            Enumerable.Range(0, 6).Select(index => new CardDto { Id = $"{role}{index}", DisplayNameId = $"{role}{index}", Role = role })).ToDictionary(card => card.Id);
        registry.Rebuild([new ModContentBundle("test.mod", ModDefinitionsBundle.Empty with {
            Cards = cards, Items = Enumerable.Range(0, 4).ToDictionary(index => $"relic{index}", index =>
                new ItemDto { Id = $"relic{index}", DisplayNameId = $"relic{index}", Kind = EItemKind.Relic }),
        })], out var report);
        Require(!report.HasIssues, "visible reward UI definitions failed validation");
        foreach (var kind in new[] { EVisibleEventKind.RolePack, EVisibleEventKind.AllRolesPack, EVisibleEventKind.RelicChoice })
        {
            var map = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/RunAdventureCmp.tscn").Instantiate<KemoCard.Mod.Run.Ui.RunAdventureCmp>();
            _stage.AddChild(map);
            try
            {
                var nodes = Enumerable.Range(0, 2).SelectMany(branch => Enumerable.Range(0, 3).Select(step => new RunMapNode {
                    Branch = branch, Step = step, Kind = step == 0 ? ERunNodeKind.Event : step == 1 ? ERunNodeKind.Shop : ERunNodeKind.VisibleEvent,
                    VisibleReward = step == 2 ? new() { Kind = kind, Role = ERole.Warrior } : null,
                })).ToList();
                var controller = new RunController(new RunMod { Map = new() { Ring = 1, Nodes = nodes } });
                var service = new RunAdventureService(controller, registry);
                map.Size = new Vector2(1000, 500);
                map.Bind(service);
                await NextFramesAsync();
                var visible = map.GetNode<Button>("Map/Routes/Lanes/Branch0/Node2");
                var label = visible.Text;
                if (kind == EVisibleEventKind.RolePack)
                    Require(label.Contains("5") && label.Contains(KemoCard.Mod.Global.Ui.CharacterIdentityLabels.Role(ERole.Warrior, KemoCard.Fixed.Godot.Localization.Tr)),
                        "single-role pack must reveal its role and five-card count on the route");
                else if (kind == EVisibleEventKind.AllRolesPack)
                    Require(label == KemoCard.Fixed.Godot.Localization.Tr("UI_VISIBLE_ALL_PACK"), "all-role pack must be explicit on the route");
                else Require(label == KemoCard.Fixed.Godot.Localization.Tr("UI_VISIBLE_RELIC"), "relic choice must be explicit on the route");
                map.GetNode<Button>("Map/Routes/Lanes/Branch0/Select").EmitSignal(BaseButton.SignalName.Pressed);
                map.Bind(service);
                visible.EmitSignal(BaseButton.SignalName.Pressed);
                map.Bind(service);
                var options = map.GetNode<Container>("Scroll/Options").GetChildren().OfType<KemoCard.Mod.Run.Ui.Items.RunOptionCmp>().ToArray();
                Require(options.Length == (kind == EVisibleEventKind.RelicChoice ? 3 : 1), "reward options did not match pack claim or three relic candidates");
                Require(options.All(option => !option.GetNode<Button>("Button").Disabled), "valid reward options should be enabled");
                if (kind != EVisibleEventKind.RelicChoice)
                    Require(options[0].GetNode<Label>("Details").Text.Split('\n').Length == (kind == EVisibleEventKind.RolePack ? 5 : 12),
                        "card pack must preview every awarded card");
                options[0].GetNode<Button>("Button").EmitSignal(BaseButton.SignalName.Pressed);
                map.Bind(service);
                Require(visible.Disabled && visible.Text.Contains("✓"), "claimed visible reward must become single-use");
                Require(kind == EVisibleEventKind.RelicChoice ? controller.State.Items.Relics.Count == 1 :
                    controller.State.CardCollection.Count == (kind == EVisibleEventKind.RolePack ? 5 : 12), "reward did not grant exactly the intended contents");
            }
            finally { map.QueueFree(); await NextFramesAsync(); }
        }
    }

    /// <summary>连线层画在按钮下层，几何完全取自媒体容器布局，每次重排后都必须重绘。</summary>
    private async Task MapWiringFollowsLayoutAsync()
    {
        var registry = new GameDefinitionRegistry();
        registry.Rebuild([new ModContentBundle("test.mod", ModDefinitionsBundle.Empty with
        {
            Items = new Dictionary<string, ItemDto> { ["potion"] = new() { Id = "potion", DisplayNameId = "Potion", UseSkillRefs = [new() { SkillId = "use" }] } },
            Skills = new Dictionary<string, SkillDto> { ["use"] = new() { Id = "use" } },
            Events = new Dictionary<string, EventDto>
            {
                ["event"] = new() { Id = "event", EventKind = EEventKind.Data, Options = [new() {
                    OptionId = "gold", LabelId = "test.option", Conditions = [new() { Kind = "GoldAtLeast", Params = new() { ["amount"] = 100 } }] }] },
            },
            Stories = new Dictionary<string, StoryDto>
            {
                ["story"] = new() { Id = "story", RingEventPools = [new() { Ring = 1, Events = [new() { EventId = "event", Weight = 1 }] }] },
            },
        })], out var report);
        Require(!report.HasIssues, "map wiring definitions failed validation");
        var map = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/RunAdventureCmp.tscn").Instantiate<KemoCard.Mod.Run.Ui.RunAdventureCmp>();
        _stage.AddChild(map);
        try
        {
            var controller = new RunController(new RunMod { StoryId = "story" });
            var service = new RunAdventureService(controller, registry);
            map.Size = new Vector2(1000, 500);
            map.Bind(service);
            await NextFramesAsync();
            var lines = map.GetNode<KemoCard.Mod.Run.Ui.RunMapLinesCmp>("Map/Lines");
            var surface = map.GetNode<Control>("Map");
            var select = map.GetNode<Button>("Map/Routes/Lanes/Branch0/Select");
            var inverse = lines.GetGlobalTransform().AffineInverse();
            Rect2 LocalRect(Control control)
            {
                var topLeft = inverse * control.GlobalPosition;
                return new Rect2(topLeft, inverse * (control.GetGlobalTransform() * control.Size) - topLeft);
            }
            Require(lines.RedrawRequests > 0, "map wiring never requested a redraw");
            var frame = lines.ComputeWiring(0, surface).Frame;
            Require(frame.Encloses(LocalRect(map.GetNode<Button>("Map/Routes/Lanes/Branch0/Node0")))
                && frame.Encloses(LocalRect(map.GetNode<Button>("Map/Routes/Lanes/Branch0/Node2"))),
                "route frame must enclose the route's own node buttons");
            Require(frame.Position.X >= LocalRect(select).End.X - 0.5f,
                "route frame must start after the select button instead of covering it");

            // 节点文字变长会撑开最小宽度、让分支容器重排，而连线层自身尺寸不变；
            // 此时必须重新请求重绘，否则连线会停在重排前的旧坐标上。
            var before = lines.RedrawRequests;
            var nodes = controller.State.Map!.Nodes;
            var index = nodes.FindIndex(node => node.Branch == 0 && node.Step == 0);
            nodes[index] = nodes[index] with
            {
                Kind = ERunNodeKind.VisibleEvent,
                VisibleReward = new RunVisibleReward { Kind = EVisibleEventKind.AllRolesPack },
            };
            map.Bind(service);
            await NextFramesAsync();
            Require(lines.RedrawRequests > before, "container re-sort did not request a map wiring redraw");
            var resorted = lines.ComputeWiring(0, surface).Frame;
            Require(resorted.Encloses(LocalRect(map.GetNode<Button>("Map/Routes/Lanes/Branch0/Node0")))
                && resorted.Encloses(LocalRect(map.GetNode<Button>("Map/Routes/Lanes/Branch0/Node2")))
                && resorted.Position.X >= LocalRect(select).End.X - 0.5f,
                "route frame did not follow the re-sorted buttons");
        }
        finally { map.QueueFree(); await NextFramesAsync(); }
    }

    private async Task EnemyWaterBubbleAsync()
    {
        var enemy = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/Combat/EnemyUnitCmp.tscn").Instantiate<EnemyUnitCmp>();
        var ally = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/Combat/AllyUnitCmp.tscn").Instantiate<AllyUnitCmp>();
        var hp = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/Combat/HpBarCmp.tscn").Instantiate<HpBarCmp>();
        var fx = new Control { Position = new Vector2(37, 22), Scale = new Vector2(0.7f, 0.7f) };
        var animator = new CombatAnimator();
        _stage.AddChild(enemy);
        _stage.AddChild(ally);
        _stage.AddChild(hp);
        _stage.AddChild(fx);
        _stage.AddChild(animator);
        try
        {
            enemy.Position = new Vector2(600, 50);
            ally.Position = new Vector2(70, 260);
            ally.Scale = new Vector2(0.8f, 0.8f);
            hp.SetValue(200, 200);
            animator.Bind(new CombatProbeView(enemy, hp, fx, ally));
            await NextFramesAsync();
            var source = enemy.GetNode<Control>("Presenter");
            var target = ally.GetNode<Control>("Presenter");
            var from = source.GetGlobalTransform() * (source.Size / 2f);
            var to = target.GetGlobalTransform() * (target.Size / 2f);
            var hit = new EnemyAttackHitEvent(0, new(ECombatSide.Player, 0));
            var flight = animator.PlayAsync(hit);
            var bubble = fx.GetChildren().OfType<WaterBubbleCmp>().Single();
            Require(!flight.IsCompleted && bubble.GlobalPosition.DistanceTo(from) < 0.1f,
                "water bubble did not start at the enemy visual center under a scaled FX layer");
            await ToSignal(GetTree().CreateTimer(0.1f), SceneTreeTimer.SignalName.Timeout);
            Require(bubble.GlobalPosition.DistanceTo(from) > 10f && bubble.GlobalPosition.DistanceTo(to) > 10f
                && hp.Current == 200, "bubble did not travel before the health change");
            await ToSignal(GetTree().CreateTimer(0.26f), SceneTreeTimer.SignalName.Timeout);
            Require(bubble.GlobalPosition.DistanceTo(to) < 0.1f && !flight.IsCompleted,
                "bubble did not burst at the actual scaled character center");
            await flight;
            await animator.PlayAsync(new DamageDealtEvent(new(ECombatSide.Enemy, 0), hit.Target, 25,
                EDamageKind.Physical, EElement.None, null, 175, 200, true));
            await NextFramesAsync();
            Require(hp.Current == 175 && !fx.GetChildren().OfType<WaterBubbleCmp>().Any(),
                "health animation or projectile cleanup did not follow the hit");

            // 完全吸收只有命中事件，仍能播完，且不改血条。
            await animator.PlayAsync(hit);
            await NextFramesAsync();
            Require(hp.Current == 175 && !fx.GetChildren().OfType<WaterBubbleCmp>().Any(),
                "absorbed attack changed health or left a bubble behind");
            await animator.PlayAsync(new EnemyAttackHitEvent(9, hit.Target));
            await animator.PlayAsync(new EnemyAttackHitEvent(0, new(ECombatSide.Player, 9)));
            Require(!fx.GetChildren().OfType<WaterBubbleCmp>().Any(), "missing combat unit created a stray projectile");

            var interrupted = animator.PlayAsync(hit);
            fx.QueueFree();
            await NextFramesAsync();
            Require(interrupted.IsCompletedSuccessfully, "removing the FX layer stalled combat playback");
        }
        finally
        {
            enemy.QueueFree();
            ally.QueueFree();
            hp.QueueFree();
            if (GodotObject.IsInstanceValid(fx))
                fx.QueueFree();
            animator.QueueFree();
            await NextFramesAsync();
        }
    }

    private sealed class CombatProbeView(EnemyUnitCmp enemy, HpBarCmp hp, Control stage, AllyUnitCmp? ally = null) : ICombatStageView
    {
        public int ControlledSlot => 0;
        public HpBarCmp? SharedHpBar => hp;
        public Control FxLayer => stage;
        public Vector2 GraveyardGlobalCenter => Vector2.Zero;
        public ActorInfoCmp? ActorInfo => null;
        public OrbQueueCmp? OrbQueue => null;
        public AllyUnitCmp? GetAllyUnit(int slotIndex) => slotIndex == 0 ? ally : null;
        public EnemyUnitCmp? GetEnemyUnit(int enemyIndex) => enemyIndex == 0 ? enemy : null;
        public HandSlotCmp? GetHandSlot(int characterIndex, int slotIndex) => null;
        public BuffListCmp? GetBuffList(BuffHolderRef holder) => null;
        public DamageNumberCmp? SpawnDamageNumber() => null;
        public Task ShowPhaseBannerAsync(string text, float duration) => Task.CompletedTask;
        public void RebuildEnemies() { }
        public void AddSummonedEnemy(int enemyIndex) { }
        public void RefreshEnemy(int enemyIndex) { }
        public void RefreshBuffs(BuffHolderRef holder) { }
        public void RefreshHandSlot(int characterIndex, int slotIndex) { }
        public void RefreshOrbs() { }
        public void PaintOrb(int index, string orbTypeId, int queueCount, int producerIndex) { }
    }

    private async Task AllyActionsSurviveStateSyncAsync()
    {
        var scene = ResourceLoader.Load<PackedScene>("res://Src/mod/run/Ui/Combat/AllyUnitCmp.tscn");
        var ally = scene.Instantiate<AllyUnitCmp>();
        _stage.AddChild(ally);
        try
        {
            var character = CharacterBattleInstance.CreateForTests("chalux", new Dictionary<string, float>());
            var definition = new CharacterDto
            {
                Id = "chalux",
                Presentation = new CharacterPresentationDto { Path = "Characters/chalux/animations.tres" },
            };
            ally.Bind(0, character, definition);
            var sprite = ally.GetNode<AnimatedSprite2D>("Presenter/AnimSprite");
            Require(ally.HasSpritePresentation && sprite.Animation == "idle", "battle binding did not play idle");
            sprite.Frame = 12;
            var attack = ally.AttackPulseAsync(0.16f);
            Require(attack.IsCompletedSuccessfully && sprite.Animation == "idle" && sprite.Frame == 12,
                "missing attack interrupted idle or used the portrait pulse");
            ally.Bind(0, character, definition);
            Require(sprite.Animation == "idle" && sprite.Frame == 12, "state sync restarted idle");
            await attack;
            Require(ally.OffsetTransformPosition == Vector2.Zero && ally.Scale == Vector2.One,
                "sprite attack moved or scaled the unit");
            await ally.PlayOnceAsync("defeat", returnToIdle: false);
            ally.Bind(0, character, definition);
            Require(sprite.Animation == "idle" && sprite.IsPlaying(), "missing defeat interrupted idle");
        }
        finally
        {
            ally.QueueFree();
            await NextFramesAsync();
        }
    }

    private async Task CharacterSpriteFitsAndPlaysAsync()
    {
        var scene = ResourceLoader.Load<PackedScene>("res://Src/mod/global/Ui/Comp/CharacterPresenter.tscn");
        var presenter = scene.Instantiate<CharacterPresenter>();
        _stage.AddChild(presenter);
        try
        {
            presenter.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
            presenter.Size = new Vector2(104, 126);
            var character = new CharacterDto
            {
                Id = "chalux",
                Presentation = new CharacterPresentationDto
                {
                    Path = "Characters/chalux/animations.tres", DefaultAnim = "idle",
                    Anims = ["idle"],
                },
            };
            presenter.Bind(character);
            await NextFramesAsync();
            var sprite = presenter.GetNode<AnimatedSprite2D>("AnimSprite");
            Require(presenter.HasPresentation && sprite.IsPlaying(), "character idle did not load/play");
            Require(sprite.TextureFilter == CanvasItem.TextureFilterEnum.Nearest, "pixel art must use nearest filtering");
            var frames = sprite.SpriteFrames;
            Require(frames.GetFrameCount("idle") == 32 && frames.GetAnimationLoop("idle")
                && frames.GetAnimationSpeed("idle") == 10, "idle resource timing or frame count changed");
            Require(sprite.Position.IsEqualApprox(presenter.Size * 0.5f), "combat sprite not centered");
            Require(Mathf.IsEqualApprox(sprite.Scale.X, sprite.Scale.Y), "sprite aspect ratio changed");
            var textureSize = frames.GetFrameTexture("idle", 0).GetSize();
            Require(sprite.Offset.IsEqualApprox(new Vector2(-32, 0)), "asset-specific body alignment was not applied");
            var bodyAnchor = sprite.Position + (new Vector2(264, 280) - textureSize * 0.5f + sprite.Offset) * sprite.Scale;
            Require(bodyAnchor.IsEqualApprox(presenter.Size * 0.5f), "body anchor is not centered in combat slot");
            var rendered = textureSize * sprite.Scale;
            Require(rendered.X <= presenter.Size.X + 0.01f && rendered.Y <= presenter.Size.Y + 0.01f,
                "combat sprite overflows its slot");
            var previousProgress = sprite.Frame + sprite.FrameProgress;
            await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
            Require(sprite.Frame + sprite.FrameProgress > previousProgress, "idle frame clock did not advance");
            presenter.Size = new Vector2(304, 520);
            await NextFramesAsync();
            rendered = textureSize * sprite.Scale;
            Require(rendered.X <= 304.01f && rendered.Y <= 520.01f
                && sprite.Position.IsEqualApprox(new Vector2(152, 260)), "details resize did not refit sprite");
            bodyAnchor = sprite.Position + (new Vector2(264, 280) - textureSize * 0.5f + sprite.Offset) * sprite.Scale;
            Require(bodyAnchor.IsEqualApprox(presenter.Size * 0.5f), "body anchor moved when resized");
            presenter.Play("missing-animation");
            Require(sprite.Animation == "idle", "unavailable actions must fall back to idle");
            _stage.RemoveChild(presenter);
            _stage.AddChild(presenter);
            presenter.Size = new Vector2(80, 96);
            await NextFramesAsync();
            rendered = textureSize * sprite.Scale;
            Require(rendered.X <= 80.01f && rendered.Y <= 96.01f
                && sprite.Position.IsEqualApprox(new Vector2(40, 48)), "cached presenter did not refit");
            for (var index = 0; index < frames.GetFrameCount("idle"); index++)
            {
                sprite.Frame = index;
                Require(sprite.Offset.IsEqualApprox(new Vector2(-32, 0)), "alignment changed between idle frames");
            }
            Require(presenter.ListAnims().SequenceEqual(new[] { "idle" }), "animation selection is not idle-only");
            foreach (var name in new[] { "enter", "attack", "buff", "defeat" })
            {
                Require(!presenter.HasAnimation(name) && presenter.GetAnimationDuration(name) == 0,
                    $"removed animation {name} remains available");
                sprite.Frame = 12;
                await presenter.PlayOnceAsync(name);
                Require(sprite.Animation == "idle" && sprite.Frame == 12 && sprite.IsPlaying(),
                    $"missing {name} restarted or stopped idle");
            }
            var plainFrames = (SpriteFrames)frames.Duplicate();
            plainFrames.RemoveMeta("presentation_offset");
            var plainPath = "res://Resource/Assets/Characters/chalux/.headless-plain.tres";
            Require(ResourceSaver.Save(plainFrames, plainPath) == Error.Ok, "plain fixture could not be saved");
            try
            {
                presenter.Bind(new CharacterDto
                {
                    Id = "alignment-fixture",
                    Presentation = new CharacterPresentationDto
                    {
                        Path = "Characters/chalux/.headless-plain.tres",
                    },
                });
                Require(presenter.HasPresentation && sprite.Offset == Vector2.Zero,
                    "alignment leaked to a resource without an offset");
            }
            finally { DirAccess.RemoveAbsolute(plainPath); }
        }
        finally
        {
            presenter.QueueFree();
            await NextFramesAsync();
        }
    }

    private void WindowDefaults()
    {
        var s = Seed();
        Opened(_manager.OpenAsync(s.Vo.Id), s.Vo);
        Require(s.Vo.OpenOpt.EffectiveHideBelow && s.Vo.OpenOpt.EffectiveAlign == EUIAlign.Full, "layer baseline overwrote window defaults");
        Require(_manager.NavStack.Contains(s.Vo.Id), "window missing from navigation stack");
    }

    private async Task HideBelowAndBackAsync()
    {
        var lower = Seed();
        var upper = Seed();
        Opened(_manager.OpenAsync(lower.Vo.Id), lower.Vo);
        Opened(_manager.OpenAsync(upper.Vo.Id), upper.Vo);
        Require(!lower.Win.Visible && lower.Vo.Runtime.HideBool.Value, "same-layer lower window remained visible");
        Require(lower.Win.VisibleUpdates > 0, "visibility lifecycle hook not invoked");
        Require(await _manager.BackAsync() == lower.Vo, "BackAsync did not return lower window");
        Require(lower.Win.Visible && lower.Win.IsVisibleInTree(), "lower window did not restore");
        _manager.Close(lower.Vo.Id);
        Opened(_manager.OpenAsync(lower.Vo.Id), lower.Vo);
        Require(lower.Win.Visible, "cached hidden window remained hidden after reopen");
    }

    private void CrossLayerVisibility()
    {
        var lower = Seed();
        var upper = Seed(EUIType.Dlg);
        _ = _manager.OpenAsync(lower.Vo.Id);
        _ = _manager.OpenAsync(upper.Vo.Id, openOpt: new UIOpenOpt { HideBelow = true });
        Require(!lower.Win.IsVisibleInTree(), "lower layer not hidden");
        _manager.Close(upper.Vo.Id);
        Require(lower.Win.IsVisibleInTree(), "lower layer not restored");
    }

    private void TopLayerQueries()
    {
        var lower = Seed();
        var debug = Seed(EUIType.Dlg);
        _ = _manager.OpenAsync(lower.Vo.Id);
        _ = _manager.OpenAsync(debug.Vo.Id, openOpt: new UIOpenOpt { Layer = EUILayer.Debug });
        Require(!_manager.IsUITop(lower.Vo.Id) && _manager.IsUITop(debug.Vo.Id), "TopLayers ignored by top query");
        _ = _manager.OpenAsync(debug.Vo.Id, openOpt: new UIOpenOpt { Layer = EUILayer.Debug, NoCover = true });
        Require(_manager.IsUITop(lower.Vo.Id), "NoCover top window should allow lower top query");
    }

    private void VisibilityCallbackCanCloseWindows()
    {
        var windows = Enumerable.Range(0, 4).Select(_ => Seed(EUIType.Dlg)).ToArray();
        foreach (var window in windows) Opened(_manager.OpenAsync(window.Vo.Id), window.Vo);
        windows[2].Win.VisibilityAction = () =>
        {
            windows[2].Win.VisibilityAction = null;
            foreach (var window in windows) _manager.Close(window.Vo.Id);
        };
        windows[3].Vo.OpenOpt.HideBelow = true;
        _manager.LayerManager.UpdateLayers();
        Require(_manager.GetLayer(EUILayer.Dlg)!.UISort.Count == 0, "visibility callback did not close all dialogs");
    }

    private void DialogsCoexist()
    {
        var first = Seed(EUIType.Dlg);
        var second = Seed(EUIType.Dlg);
        Opened(_manager.OpenAsync(first.Vo.Id), first.Vo);
        Opened(_manager.OpenAsync(second.Vo.Id), second.Vo);
        Require(first.Vo.IsOpen && second.Vo.IsOpen && first.Win.Visible, "new dialog replaced previous dialog");
        Require(_manager.GetLayer(EUILayer.Dlg)!.UISort.Count == 2, "dialog layer should contain two instances");
        _manager.Close(second.Vo.Id);
        Require(first.Vo.IsOpen, "closing upper dialog also closed lower");
        Opened(_manager.OpenAsync(first.Vo.Id), first.Vo);
        Require(_manager.GetWin(first.Vo.Id) == first.Win && first.Win.BindingCount == 1, "same id should reuse node with one subscription");
        first.Win.ClickButton.EmitSignal(BaseButton.SignalName.Pressed);
        Require(first.Win.Clicks == 1, "reopen duplicated click handler");
    }

    #endregion

    #region 缓存和动画

    private async Task MaskCacheAsync()
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        _ = _manager.OpenAsync(s.Vo.Id);
        _manager.Close(s.Vo.Id);
        await NextFramesAsync();
        Require(s.Vo.StateMachine.CurrentState == EUIState.Cache && GodotObject.IsInstanceValid(mask), "cached mask was freed");
        Require(mask.GetParent() == null && s.Win.GetParent() == null, "cached nodes still attached");
        Opened(_manager.OpenAsync(s.Vo.Id), s.Vo);
        Require(s.Vo.Mask == mask && mask.GetParent() == s.Win.GetParent(), "cached mask was not reattached");
    }

    private void CloseWaitsForBothAnimations(bool delayMask)
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        s.Win.DelayClose = !delayMask;
        mask.DelayClose = delayMask;
        _ = _manager.OpenAsync(s.Vo.Id);
        _manager.Close(s.Vo.Id);
        Require(s.Vo.StateMachine.CurrentState == EUIState.Close, "cache entered before both animations completed");
        if (delayMask) mask.CloseAnimations[0]();
        else s.Win.CloseAnimations[0]();
        Require(s.Vo.StateMachine.CurrentState == EUIState.Cache, "close did not complete after both animations");
        Require(mask.GetParent() == null && s.Win.GetParent() == null, "mask left attached after close");
    }

    private void ReopenCancelsOldClose()
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        s.Win.DelayClose = mask.DelayClose = true;
        _ = _manager.OpenAsync(s.Vo.Id);
        _manager.Close(s.Vo.Id);
        var oldWindowDone = s.Win.CloseAnimations[0];
        var oldMaskDone = mask.CloseAnimations[0];
        Opened(_manager.OpenAsync(s.Vo.Id), s.Vo);
        oldWindowDone();
        oldMaskDone();
        Require(s.Vo.IsOpen && GodotObject.IsInstanceValid(mask) && mask.Closed == 0, "old close callback affected reopened UI");
    }

    private void CloseAnimationFailureStillCloses()
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        mask.DelayClose = true;
        Opened(_manager.OpenAsync(s.Vo.Id), s.Vo);
        s.Win.ThrowOnCloseAnimation = true;
        _manager.Close(s.Vo.Id);
        Require(s.Vo.StateMachine.CurrentState == EUIState.Cache, "throwing close animation left UI stuck in Close");
        mask.CloseAnimations[0]();
        Require(s.Vo.StateMachine.CurrentState == EUIState.Cache && mask.Closed == 0, "cancelled mask animation still invoked close callback");
    }

    private async Task DestroyDuringCloseAsync()
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        s.Win.DelayClose = mask.DelayClose = true;
        _ = _manager.OpenAsync(s.Vo.Id);
        _manager.Close(s.Vo.Id);
        var oldWindowDone = s.Win.CloseAnimations[0];
        var oldMaskDone = mask.CloseAnimations[0];
        s.Vo.StateMachine.TransitionTo(EUIState.Destroy, s.Vo);
        await NextFramesAsync();
        oldWindowDone();
        oldMaskDone();
        Require(!GodotObject.IsInstanceValid(s.Win) && !GodotObject.IsInstanceValid(mask), "destroy did not release both nodes");
    }

    #endregion

    #region 请求边界与异常

    private void StalePreloadFailure()
    {
        var s = Seed();
        s.Win.DelayPreload = true;
        var old = _manager.OpenAsync(s.Vo.Id);
        var latest = _manager.OpenAsync(s.Vo.Id);
        Rejected(old);
        s.Win.Preloads[1].Done();
        s.Win.Preloads[0].Fail();
        s.Win.Preloads[1].Fail();
        Opened(latest, s.Vo);
        Require(s.Vo.IsOpen && _manager.GetUIVo(s.Vo.Id) == s.Vo, "stale/duplicate failure destroyed successful request");
    }

    private void CloseCachedPreload()
    {
        var s = Seed();
        _ = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { CacheTime = -1 });
        _manager.Close(s.Vo.Id);
        s.Win.DelayPreload = true;
        var pending = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { CacheTime = -1 });
        _manager.Close(s.Vo.Id);
        Rejected(pending);
        Require(s.Vo.StateMachine.CurrentState == EUIState.Cache, "cached request should remain reusable");
        s.Win.Preloads[0].Done();
        Require(!s.Vo.IsOpen, "closed preload completion reopened window");
    }

    private void ThrowingCallbacksAdvanceQueue()
    {
        var first = Seed();
        var next = Seed();
        first.Win.DelayPreload = true;
        var failed = _manager.OpenAsync(first.Vo.Id, openOpt: new UIOpenOpt { OnFail = () => throw new InvalidOperationException("expected OnFail") });
        var queued = _manager.OpenAsync(next.Vo.Id, openOpt: new UIOpenOpt { OnOpen = _ => throw new InvalidOperationException("expected OnOpen") });
        first.Win.Preloads[0].Fail();
        Rejected(failed);
        Opened(queued, next.Vo);
        Require(_manager.OpenCoordinator.CurrentOpening == null, "callback exception stalled queue");
    }

    private void CallbackReentryPreservesRequest()
    {
        var s = Seed();
        s.Win.DelayPreload = true;
        Task<UIVo?>? latest = null;
        var first = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { OnOpen = _ => latest = _manager.OpenAsync(s.Vo.Id) });
        s.Win.Preloads[0].Done();
        Opened(first, s.Vo);
        Require(latest != null && !latest.IsCompleted && s.Vo.OpenTaskSource != null, "old callback cleared reentrant request");
        s.Win.Preloads[1].Done();
        Opened(latest!, s.Vo);
    }

    private void CloseFromOpenBefore()
    {
        var s = Seed();
        var task = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { OnOpenBefore = _ => _manager.Close(s.Vo.Id) });
        Rejected(task);
        Require(!s.Vo.IsOpen && s.Win.BindingCount == 0, "open continued after OnOpenBefore closed UI");
    }

    private void BusinessHookFailure()
    {
        var s = Seed();
        s.Win.ThrowOnOpen = true;
        Rejected(_manager.OpenAsync(s.Vo.Id));
        Require(_manager.GetUIVo(s.Vo.Id) == null, "failed OnOpen retained VO");
    }

    #endregion

    #region 离场与卸载

    private void ExitFailureStillUnbinds()
    {
        var s = Seed();
        _ = _manager.OpenAsync(s.Vo.Id);
        s.Win.ThrowOnExit = true;
        s.Win.GetParent().RemoveChild(s.Win);
        s.Win.ClickButton.EmitSignal(BaseButton.SignalName.Pressed);
        Require(s.Win.BindingCount == 0 && s.Win.Clicks == 0, "exit failure bypassed native signal unbind");
        s.Win.ThrowOnExit = false;
    }

    private void MaskExitFailureStillUnbinds()
    {
        var s = Seed();
        var mask = AttachMask(s.Vo);
        _ = _manager.OpenAsync(s.Vo.Id);
        mask.AddBinding();
        mask.ThrowOnExit = true;
        mask.GetParent().RemoveChild(mask);
        Require(mask.BindingCount == 0, "mask exit failure bypassed unbind");
        mask.ThrowOnExit = false;
    }

    private void FitExitFailureStillUnbinds()
    {
        var fit = new TestFitScaleBox();
        fit.AddChild(new Control());
        _stage.AddChild(fit);
        var field = typeof(FitScaleBox).GetField("_binder", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var binder = (BindingScope)field.GetValue(fit)!;
        Require(binder.Count > 0, "fit fixture has no subscription");
        _stage.RemoveChild(fit);
        Require(binder.Count == 0, "fit exit failure bypassed unbind");
        fit.QueueFree();
    }

    private void UnregisterOwnerAdvancesQueue()
    {
        var current = Seed(owner: "remove");
        var sameOwner = Seed(owner: "remove");
        var other = Seed(owner: "other");
        current.Win.DelayPreload = true;
        var oldTask = _manager.OpenAsync(current.Vo.Id);
        var sameTask = _manager.OpenAsync(sameOwner.Vo.Id);
        var nextTask = _manager.OpenAsync(other.Vo.Id);
        _manager.UnregisterOwner("remove");
        Rejected(oldTask);
        Rejected(sameTask);
        Opened(nextTask, other.Vo);
        Require(_manager.GetUIVo(current.Vo.Id) == null && _manager.GetUIVo(sameOwner.Vo.Id) == null, "unregistered owner retained VO");
        Rejected(_manager.OpenAsync(current.Vo.Id));
    }

    private void CachedPreloadGetsFreshDeadline()
    {
        var s = Seed();
        s.Win.DelayPreload = true;
        s.Vo.Lifecycle.LoadTime = 1;
        var task = _manager.OpenAsync(s.Vo.Id);
        _manager.OpenCoordinator.CheckLoadTimeout();
        Require(!task.IsCompleted && s.Vo.StateMachine.CurrentState == EUIState.PreLoad, "cached preload used historical timeout");
        s.Win.Preloads[0].Done();
        Opened(task, s.Vo);
    }

    private void TimeoutCallbackReentry()
    {
        var s = Seed();
        var next = Seed();
        s.Win.DelayPreload = next.Win.DelayPreload = true;
        Task<UIVo?>? nextTask = null;
        var failed = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { OnFail = () => nextTask = _manager.OpenAsync(next.Vo.Id) });
        s.Vo.Lifecycle.LoadTime = 1;
        _manager.OpenCoordinator.CheckLoadTimeout();
        Rejected(failed);
        Require(_manager.OpenCoordinator.CurrentOpening == next.Vo, "timeout erased request started by failure callback");
        next.Win.Preloads[0].Done();
        Opened(nextTask!, next.Vo);
    }

    #endregion

    #region 冷加载与自定义父节点

    private void ColdSceneLifecycle()
    {
        Register("headless.window");
        var task = _manager.OpenAsync("headless.window");
        var vo = _manager.GetUIVo("headless.window")!;
        Opened(task, vo);
        var win = (TestWindow)vo.UI!;
        Require(win.Creates == 1, "cold scene did not create exactly once");
        _manager.Close(vo.Id);
        Opened(_manager.OpenAsync(vo.Id), vo);
        Require(vo.UI == win && win.Creates == 1 && win.BindingCount == 1, "cold scene cache lifecycle duplicated create/bind");
    }

    private async Task ColdMaskLifecycleAsync()
    {
        Register("headless.window");
        Register("headless.mask", EUIType.Dlg);
        var opt = new UIOpenOpt { Mask = new UIMaskOpt { Runtime = "headless.mask" } };
        var task = _manager.OpenAsync("headless.window", openOpt: opt);
        var vo = _manager.GetUIVo("headless.window")!;
        Opened(task, vo);
        var mask = vo.Mask;
        Require(mask is TestMask && mask.UIVo == vo, "cold mask not created/bound");
        _manager.Close(vo.Id);
        await NextFramesAsync();
        Opened(_manager.OpenAsync(vo.Id, openOpt: opt), vo);
        Require(vo.Mask == mask && GodotObject.IsInstanceValid(mask), "cold mask cache reopen failed");
    }

    private void FailedResourceFactory()
    {
        Register("headless.window");
        Rejected(_manager.OpenAsync("headless.window", openOpt: new UIOpenOpt
        {
            PreLoadResList = _ => throw new InvalidOperationException("expected preload resource factory failure"),
        }));
        Require(_manager.GetUIVo("headless.window") == null, "failed resource factory left VO loading");
        Rejected(_manager.OpenAsync("unregistered", openOpt: new UIOpenOpt { OnFail = () => throw new InvalidOperationException("expected rejected callback failure") }));
    }

    private void CustomParentMaskCache()
    {
        var s = Seed(EUIType.Pge);
        var mask = AttachMask(s.Vo);
        var first = new Control();
        var second = new Control();
        _stage.AddChild(first);
        _stage.AddChild(second);
        _ = _manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { Parent = first });
        _manager.Close(s.Vo.Id);
        Require(mask.GetParent() == null && s.Win.GetParent() == null, "custom parent retained cached mask");
        Opened(_manager.OpenAsync(s.Vo.Id, openOpt: new UIOpenOpt { Parent = second }), s.Vo);
        Require(s.Win.GetParent() == second && mask.GetParent() == second, "cached page did not reparent both nodes");
        first.QueueFree();
        // second owns the active fixture and is released after its UI in Cleanup.
        second.CallDeferred(Node.MethodName.QueueFree);
    }

    #endregion
}
