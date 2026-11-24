using System.Reflection;
using Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Marble;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Ui;
using KemoCard.Mod.Run.Ui.CombatUi;

namespace KemoCard.Ui.Headless;

public partial class UiFrameworkHeadlessTests
{
    /// <summary>
    /// 战斗界面场景接线：右栏常驻缩略盘面 + 队列顺序 / 属性、放大预览开关、Esc 关闭、
    /// 状态同步后的队列刷新。使用游戏加载的英文翻译检查最终显示内容。
    /// </summary>
    private async Task CombatMarblePreviewAsync()
    {
        var definitions = new GameDefinitionRegistry();
        var attributes = new Dictionary<string, float>
        {
            [AttributeIds.MaxHealth] = 20,
            [AttributeIds.MaxEnergy] = 5,
            [AttributeIds.InitialEnergy] = 5,
        };
        var party = Enumerable.Range(0, 3)
            .Select(index => CharacterBattleInstance.CreateForTests($"owner{index}", attributes))
            .ToArray();
        using var run = new RunController(new RunMod());
        var simulation = new CombatSimulation(
            new PlayerTeamState(party, 50),
            new EnemyTeamState([new EnemyUnit("e0", "enemy", 20)]),
            new CombatRuleEngine([]),
            definitions,
            initialPhase: ECombatPhase.Player);
        simulation.Marble.BeginTurn(1, 3);
        simulation.Marble.EnqueueBall(new MarbleBallSpec(7, 12f));

        var current = typeof(RunRuntime).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = current.GetValue(null);
        typeof(RunController)
            .GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(run, simulation);

        var window = GD.Load<PackedScene>("res://Src/mod/run/Ui/Combat/CombatWin.tscn")
            .Instantiate<CombatWin>();
        var viewport = new SubViewport { Size = new Vector2I(1280, 720) };
        AddChild(viewport);
        var previousLocale = TranslationServer.GetLocale();
        try
        {
            TranslationServer.SetLocale("en");
            current.SetValue(null, run);
            viewport.AddChild(window);
            var lifecycle = (IUILifecycleInvoker)window;
            lifecycle.InvokeInitEvent();
            lifecycle.InvokeOpen();
            await NextFramesAsync();

            var panel = window.GetNode<MarblePanelCmp>("FitScale/Root/Middle/RightRail/Marble");
            var stats = panel.GetNode<Label>("Margin/VBox/Stats");
            var queue = panel.GetNode<Label>("Margin/VBox/QueueScroll/Queue");
            var board = panel.GetNode<MarbleBoardCmp>("Margin/VBox/Board");
            Require(panel.GetNode<Label>("Margin/VBox/Header/Title").Text == "Marble Board",
                "thumbnail title key was not resolved");
            Require(stats.Text == "Count 0 · Queue 3 · Live 0 / 20", $"marble stats mismatch: {stats.Text}");
            Require(
                queue.Text == "1. Points 1 · Radius 8\n2. Points 1 · Radius 8\n3. Points 7 · Radius 12",
                $"queue order / attributes mismatch: {queue.Text}");
            Require(board.Runtime == simulation.Marble && board.Size.Y >= 100, "thumbnail board was not bound or laid out");
            Require(panel.GetNode<Button>("Margin/VBox/Header/BtnToggle").Text == "Enlarge",
                "thumbnail toggle text was not localized");

            var toggle = panel.GetNode<Button>("Margin/VBox/Header/BtnToggle");
            toggle.EmitSignal(BaseButton.SignalName.Pressed);
            await NextFramesAsync();
            var preview = window.GetNode<Control>("MarblePreview");
            var large = window.GetNode<MarblePanelCmp>("MarblePreview/Center/Panel");
            Require(preview.Visible, "thumbnail toggle did not open the large preview");
            var endTurn = window.GetNode<Button>("FitScale/Root/BottomBar/Actions/BtnConfirm");
            Require(endTurn.GetFocusModeWithOverride() == Control.FocusModeEnum.None,
                "keyboard navigation must stay inside the large preview");
            Require(large.GetNode<Label>("Margin/VBox/Header/Title").Text == "Marble Board Preview",
                "large preview title key was not resolved");
            Require(large.GetNode<Label>("Margin/VBox/QueueScroll/Queue").Text == queue.Text,
                "large preview must list the same FIFO queue");
            Require(large.GetNode<Label>("Margin/VBox/QueueScroll/Queue").MaxLinesVisible == -1,
                "large preview must not clip the queue");
            var previewBoard = large.GetNode<MarbleBoardCmp>("Margin/VBox/Board");
            var queuedBeforePreviewClick = simulation.Marble.Queue.Count;
            previewBoard._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = true, Position = previewBoard.Size / 2f,
            });
            Require(simulation.Marble.Queue.Count == queuedBeforePreviewClick &&
                simulation.Marble.ActiveBalls.Count == 0,
                "clicking the read-only preview must not launch a marble");

            var close = large.GetNode<Button>("Margin/VBox/Header/BtnToggle");
            close.EmitSignal(BaseButton.SignalName.Pressed);
            await NextFramesAsync();
            Require(!preview.Visible, "large preview toggle did not close the overlay");

            simulation.Marble.EnqueueBall(new MarbleBallSpec(4, 9f));
            typeof(CombatWin)
                .GetMethod("SyncFromState", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, null);
            await NextFramesAsync();
            Require(queue.Text.Split('\n').Length == 4, "state sync did not append the queued ball");

            toggle.EmitSignal(BaseButton.SignalName.Pressed);
            await NextFramesAsync();
            Require(preview.Visible, "preview did not reopen");
            for (var i = 0; i < 50; i++)
                simulation.Marble.EnqueueBall(new MarbleBallSpec(i, 8));
            large.Bind(simulation.Marble);
            await NextFramesAsync();
            Require(large.GetNode<ScrollContainer>("Margin/VBox/QueueScroll").GetVScrollBar().Visible,
                "long queues must scroll inside the preview");
            Require(viewport.GetVisibleRect().Encloses(close.GetGlobalRect()), "long queues pushed the close button offscreen");
            viewport.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true }, true);
            await NextFramesAsync();
            Require(!preview.Visible, "Escape must close the large preview before other cancel handling");
        }
        finally
        {
            viewport.GuiCancelDrag();
            viewport.QueueFree();
            await NextFramesAsync();
            current.SetValue(null, previous);
            TranslationServer.SetLocale(previousLocale);
        }
    }

    /// <summary>
    /// 弹珠阶段接线：结束回合进入 Marble 后自动展开主盘 / 收起手牌，点击盘面按指针方向发射，
    /// 两颗球打完自动换回下一玩家回合；提前结束按钮常驻但只在合规时可用；敌方标记可点击切换。
    /// </summary>
    private async Task CombatMarblePhaseAsync()
    {
        var definitions = new GameDefinitionRegistry();
        var attributes = new Dictionary<string, float>
        {
            [AttributeIds.MaxHealth] = 20,
            [AttributeIds.MaxEnergy] = 5,
            [AttributeIds.InitialEnergy] = 5,
        };
        var party = Enumerable.Range(0, 3)
            .Select(index => CharacterBattleInstance.CreateForTests($"owner{index}", attributes))
            .ToArray();
        using var run = new RunController(new RunMod());
        var simulation = new CombatSimulation(
            new PlayerTeamState(party, 50),
            new EnemyTeamState([new EnemyUnit("e0", "enemy", 20), new EnemyUnit("e1", "enemy", 20)]),
            new CombatRuleEngine([]),
            definitions,
            initialPhase: ECombatPhase.Player);

        // 玩家阶段开局即发放基础 2 颗弹珠（CombatSimulation 构造时 BeginTurn）。
        Require(simulation.Marble.Queue.Count == 2, "player phase did not seed the base marble queue");

        var current = typeof(RunRuntime).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = current.GetValue(null);
        typeof(RunController)
            .GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(run, simulation);

        var window = GD.Load<PackedScene>("res://Src/mod/run/Ui/Combat/CombatWin.tscn")
            .Instantiate<CombatWin>();
        var viewport = new SubViewport { Size = new Vector2I(1280, 720) };
        AddChild(viewport);
        var previousLocale = TranslationServer.GetLocale();
        try
        {
            TranslationServer.SetLocale("en");
            current.SetValue(null, run);
            viewport.AddChild(window);
            var lifecycle = (IUILifecycleInvoker)window;
            lifecycle.InvokeInitEvent();
            lifecycle.InvokeOpen();
            await NextFramesAsync();

            var hand = window.GetNode<Control>("FitScale/Root/BottomBar/Hand");
            var main = window.GetNode<MarblePanelCmp>("FitScale/Root/Middle/Stage/MarbleMain");
            var endTurn = window.GetNode<Button>("FitScale/Root/BottomBar/Actions/BtnConfirm");
            var pause = window.GetNode<Button>("FitScale/Root/Middle/RightRail/BtnPause");
            Require(!main.Visible && hand.Visible, "marble main board must stay hidden during card play");
            Require(!main.GetNode<Button>("Margin/VBox/Header/BtnToggle").Visible,
                "main board must not offer the enlarge toggle");

            // 1) 点击结束回合：进入弹珠阶段，主盘展开、手牌收起，敌人 / 技能 / 道具 / 暂停仍在。
            void Click(Control control)
            {
                var point = control.GetGlobalTransformWithCanvas() * (control.Size / 2f);
                viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
                foreach (var down in new[] { true, false })
                    viewport.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 }, true);
            }
            Click(endTurn);
            await NextFramesAsync();
            Require(simulation.Phase == ECombatPhase.Marble, "end turn did not enter the marble phase");
            Require(main.Visible && !hand.Visible, "marble phase did not expand the main board / collapse the hand");
            var mainBoard = main.Board;
            Require(mainBoard is not null && mainBoard.Runtime == simulation.Marble && mainBoard.Interactive,
                "main board was not bound interactively");
            Require(window.GetEnemyUnit(0) is { Visible: true } && window.GetEnemyUnit(1) is { Visible: true },
                "enemies must stay visible during the marble phase");
            Require(!pause.Disabled, "pause entry must stay available during the marble phase");
            Require(endTurn.Text == "End Marbles", $"early end button was not localized: {endTurn.Text}");
            Require(endTurn.Disabled, "early end must be disabled before the clear / duration condition is met");
            Require(simulation.MarbleMarkedEnemyIndex == 0, "marble phase did not default the allocation mark");

            // 2) 点击敌方切换本轮标记。
            Click(window.GetEnemyUnit(1)!);
            await NextFramesAsync();
            Require(simulation.MarbleMarkedEnemyIndex == 1, "clicking an enemy did not switch the marble mark");

            // 3) 点击盘面发射第一颗：队列 -1、场上出现 1 颗活跃球。
            var board = mainBoard!;
            Click(board);
            Require(simulation.Marble.Queue.Count == 1 && simulation.Marble.ActiveBalls.Count == 1,
                "clicking the main board did not launch the first marble");

            var speed = window.GetNode<Button>("FitScale/Root/BottomBar/Actions/BtnPlayConfirm");
            Click(speed);
            var step = simulation.Marble.StepIndex;
            window._Process(1d / MarblePhysicsConfig.StepsPerSecond);
            Require(simulation.Marble.StepIndex == step + 2, "2× speed must run two fixed physics steps per frame");
            DriveMarble(window, () => simulation.Marble.ActiveBalls.Count > 0);
            Require(simulation.Phase == ECombatPhase.Marble && simulation.Marble.Queue.Count == 1,
                "the first marble did not leave the board while the second stayed queued");

            // 4) 第二颗手动发射；两颗都离盘后自动结算并轮换到下一玩家回合。
            Click(board);
            Require(simulation.Marble.Queue.Count == 0 && simulation.Marble.ActiveBalls.Count == 1,
                "second marble was not launched");

            DriveMarble(window, () => simulation.Phase == ECombatPhase.Marble);
            Require(simulation.Phase == ECombatPhase.Player && simulation.TurnNumber == 2,
                $"two launched marbles did not advance the turn: {simulation.Phase} turn {simulation.TurnNumber}");
            Require(simulation.Marble.Queue.Count == 2, "the next player turn did not seed a fresh marble queue");
            Require(!main.Visible && hand.Visible, "leaving the marble phase did not restore the card-play layout");
        }
        finally
        {
            viewport.GuiCancelDrag();
            viewport.QueueFree();
            await NextFramesAsync();
            current.SetValue(null, previous);
            TranslationServer.SetLocale(previousLocale);
        }
    }

    /// <summary>按固定 1/60s 步长驱动战斗界面的弹珠累加器，直到 <paramref name="keepGoing"/> 变 false 或步数耗尽。</summary>
    private static void DriveMarble(CombatWin window, Func<bool> keepGoing)
    {
        for (var i = 0; i < 1800 && keepGoing(); i++)
            window._Process(1.0 / MarblePhysicsConfig.StepsPerSecond);
        var sim = RunRuntime.Current!.Simulation!;
        Require(!keepGoing(), $"marble stepping timed out at step {sim.Marble.StepIndex}, position {sim.Marble.ActiveBalls.FirstOrDefault()?.Position}, paused {RunUiController.IsPauseMenuOpen()}, visible {window.IsVisibleInTree()}");
    }
}
