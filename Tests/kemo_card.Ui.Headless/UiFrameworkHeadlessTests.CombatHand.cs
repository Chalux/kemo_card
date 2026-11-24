using System.Reflection;
using Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Ui.CombatUi;

namespace KemoCard.Ui.Headless;

public partial class UiFrameworkHeadlessTests
{
    private async Task CombatHandDragAsync()
    {
        var definitions = new GameDefinitionRegistry();
        foreach (var side in new[] { ETargetSide.Enemy, ETargetSide.Self })
            definitions.Store.CardsMutable[side.ToString()] = new CardDto
            {
                Id = side.ToString(), TargetSide = side, TargetScope = ETargetScope.Single,
                CostType = ECostType.Energy, Cost = 1,
            };
        var attributes = new Dictionary<string, float>
        {
            [AttributeIds.MaxHealth] = 20, [AttributeIds.MaxEnergy] = 5, [AttributeIds.InitialEnergy] = 5,
        };
        int[] dealt = [2, 1, 7];
        var party = Enumerable.Range(0, dealt.Length).Select(i =>
        {
            var character = CharacterBattleInstance.CreateForTests($"owner{i}", attributes,
                Enumerable.Range(0, dealt[i] + 1).Select(j => new CardRuntimeEntry(j == 1 ? "Enemy" : "Self", $"{i}-{j}")));
            character.DrawCards(dealt[i]);
            character.RefillAvailableEnergy();
            return character;
        }).ToArray();
        using var run = new RunController(new RunMod());
        var simulation = new CombatSimulation(new PlayerTeamState(party, 50),
            new EnemyTeamState([new EnemyUnit("e0", "enemy", 20)]), new CombatRuleEngine([]), definitions,
            initialPhase: ECombatPhase.Player);
        var current = typeof(RunRuntime).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = current.GetValue(null);
        typeof(RunController).GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(run, simulation);
        var window = GD.Load<PackedScene>("res://Src/mod/run/Ui/Combat/CombatWin.tscn").Instantiate<CombatWin>();
        var viewport = new SubViewport { Size = new Vector2I(1280, 720) };
        AddChild(viewport);
        try
        {
            current.SetValue(null, run);
            viewport.AddChild(window);
            var lifecycle = (IUILifecycleInvoker)window;
            lifecycle.InvokeInitEvent();
            lifecycle.InvokeOpen();
            await NextFramesAsync();
            var left = window.GetHandSlot(0, 0)!;
            var middle = window.GetHandSlot(2, 1)!;
            var right = window.GetHandSlot(2, 6)!;
            var bottomBar = window.GetNode<Control>("FitScale/Root/BottomBar");
            var deckCount = bottomBar.GetNode<Label>("Deck/VBox/Count");
            var graveCount = bottomBar.GetNode<Label>("Grave/VBox/Count");
            Require(deckCount.Text == "3" && graveCount.Text == "0", "opening counters must include all three loadouts");
            Require(Mathf.IsEqualApprox(-left.Rotation, right.Rotation) && left.Rotation < middle.Rotation &&
                left.Position.Y > middle.Position.Y && middle.Position.Y < right.Position.Y, "10 cards should form a centered fan");
            viewport.Size = new Vector2I(1024, 640);
            await NextFramesAsync();

            static Vector2 Center(Control node) => node.GetGlobalTransformWithCanvas() * (node.Size * 0.5f);
            void Move(Vector2 point, Vector2 delta, bool held)
            {
                viewport.PushInput(new InputEventMouseMotion
                {
                    Position = point, GlobalPosition = point, Relative = delta, ButtonMask = held ? MouseButtonMask.Left : 0,
                }, true);
            }
            void Button(Vector2 point, bool down) => viewport.PushInput(new InputEventMouseButton
            {
                Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = down,
                ButtonMask = down ? MouseButtonMask.Left : 0,
            }, true);
            void DragFrom(HandSlotCmp slot, Vector2 end)
            {
                var from = Center(slot.Card!);
                Move(from, Vector2.Zero, false);
                Button(from, true);
                Move(from + Vector2.Up * 24, Vector2.Up * 24, true);
                Require(viewport.GuiIsDragging(), $"drag did not start on {slot.Name}");
                Move(end, end - from - Vector2.Up * 24, true);
            }

            var allyPoint = Center(window.GetAllyUnit(0)!);
            var enemyPoint = Center(window.GetEnemyUnit(0)!);
            DragFrom(right, allyPoint);
            Button(allyPoint, false);
            Require(!viewport.GuiIsDragging() && party[2].AvailableEnergy == 5 && right.Visible, "enemy card on ally must cancel");
            Require(deckCount.Text == "3" && graveCount.Text == "0", "switching payer must keep the team pile counts");
            DragFrom(right, enemyPoint);
            Button(enemyPoint, false);
            Require(party[2].AvailableEnergy == 4 && party[0].AvailableEnergy == 5 && party[2].HandSlots[6].IsEmpty,
                "release on enemy must play the owner's card once");

            var ui = (CombatUiState)typeof(CombatWin).GetField("_ui", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            while (ui.InputLocked)
                await NextFramesAsync();
            Require(graveCount.Text == "1", "settled card must enter the team graveyard");
            var auto = window.GetHandSlot(2, 5)!;
            var home = Center(auto.Card!);
            DragFrom(auto, home);
            Button(home, false);
            Require(party[2].AvailableEnergy == 4 && !party[2].HandSlots[5].IsEmpty, "drop back in hand must cancel");
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            var stage = window.GetAllyUnit(0)!.GetParent().GetParent<Control>();
            var field = stage.GetGlobalTransformWithCanvas() * new Vector2(stage.Size.X * 0.5f, 8f);
            DragFrom(auto, field);
            Require(Center(auto).DistanceTo(field) < 1f, "auto card must follow the pointer");
            viewport.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true }, true);
            Button(field, false);
            Require(!viewport.GuiIsDragging() && party[2].AvailableEnergy == 4, "Escape must cancel before release");
            await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
            Require(Center(auto.Card!).DistanceTo(home) < 1f, "cancelled card must return to its fan position");
            DragFrom(auto, field);
            Button(field, false);
            Require(party[2].AvailableEnergy == 3 && party[2].HandSlots[5].IsEmpty, "auto card must play over empty battlefield");
            while (ui.InputLocked)
                await NextFramesAsync();
        }
        finally
        {
            viewport.GuiCancelDrag();
            viewport.QueueFree();
            await NextFramesAsync();
            current.SetValue(null, previous);
        }
    }
}
