using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Events;
using KemoCard.Ui.Tests.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

/// <summary>
/// Run 阶段变化必须广播 <c>RunPhaseChanged</c>：常驻界面（<c>RunMainWin</c>）靠它重新取数。
/// </summary>
/// <remarks>
/// <para>根因：此前 <c>RunMod.Phase</c> 是裸自动属性，事件表里的 <c>RunPhaseChanged</c>
/// 全仓无人发送、也无人订阅，界面只在 <c>OnOpen</c> 刷一次视图。于是从调试面板开战后，
/// 阶段标签仍停在开战前的值、右上角充能球面板（<c>OrbPanel</c>）也不出现——
/// 「战斗已经跑起来了，界面却毫无变化」，看上去就是进不去战斗。</para>
/// <para>本类只依赖 Run 层与事件总线（不碰 Godot 节点），因此可直接覆盖。</para>
/// </remarks>
[TestFixture]
public sealed class RunPhaseChangeNotificationTests
{
    [Test]
    public void Phase_change_broadcasts_previous_and_current()
    {
        var mod = new RunMod();
        var payloads = new List<RunPhaseChangedPayload>();
        mod.OnRunPhaseChanged((payload, _) => payloads.Add(payload), caller: this);

        mod.Phase = ERunPhase.Reward;
        mod.Phase = ERunPhase.Battle;

        Assert.That(payloads, Has.Count.EqualTo(2));
        Assert.That(payloads[0].PreviousPhase, Is.EqualTo(ERunPhase.Event));
        Assert.That(payloads[0].CurrentPhase, Is.EqualTo(ERunPhase.Reward));
        Assert.That(payloads[1].PreviousPhase, Is.EqualTo(ERunPhase.Reward));
        Assert.That(payloads[1].CurrentPhase, Is.EqualTo(ERunPhase.Battle));
    }

    /// <summary>
    /// 调试面板开战前会先把阶段显式拨到 <c>Reward</c>（<c>StartBattle</c> 只接受该阶段），
    /// 重复赋同一个值不应产生多余事件，否则界面会被无谓地重刷。
    /// </summary>
    [Test]
    public void Assigning_the_same_phase_does_not_broadcast()
    {
        var mod = new RunMod();
        var count = 0;
        mod.OnRunPhaseChanged((_, _) => count++, caller: this);

        mod.Phase = ERunPhase.Event;
        mod.Phase = ERunPhase.Event;

        Assert.That(count, Is.Zero);
    }

    [Test]
    public void StartBattle_broadcasts_phase_change_to_battle()
    {
        var (controller, registry, rng) = BuildReadyRun();
        var phaseAtEvent = (ERunPhase?)null;
        controller.State.OnRunPhaseChanged(
            (payload, _) => phaseAtEvent = payload.CurrentPhase,
            caller: this);

        using var simulation = controller.StartBattle(registry, rng, runSeed: 1);

        Assert.That(phaseAtEvent, Is.EqualTo(ERunPhase.Battle),
            "开战必须广播阶段变化，否则常驻的 Run 主界面不会显示战斗态（相位 / 充能球面板）");
        Assert.That(controller.State.Phase, Is.EqualTo(ERunPhase.Battle));
    }

    [Test]
    public void EndBattle_broadcasts_phase_change()
    {
        var (controller, registry, rng) = BuildReadyRun();
        var phases = new List<ERunPhase>();
        controller.State.OnRunPhaseChanged((payload, _) => phases.Add(payload.CurrentPhase), caller: this);

        using (controller.StartBattle(registry, rng, runSeed: 1))
        {
            controller.EndBattle(won: true);
        }

        Assert.That(phases, Does.Contain(ERunPhase.Battle));
        Assert.That(phases[^1], Is.EqualTo(controller.State.Phase), "最后一次广播必须与最终阶段一致");
    }

    /// <summary>
    /// 界面经 <c>Binder.Add(listener.Off)</c> 登记解绑动作，因此 <c>Off()</c> 必须真的停止派发。
    /// </summary>
    [Test]
    public void Off_stops_delivery()
    {
        var mod = new RunMod();
        var count = 0;
        var listener = mod.OnRunPhaseChanged((_, _) => count++, caller: this);

        mod.Phase = ERunPhase.Reward;
        Assert.That(count, Is.EqualTo(1));

        listener.Off();
        mod.Phase = ERunPhase.Battle;

        Assert.That(count, Is.EqualTo(1), "Off 之后不得再收到事件");
        Assert.That(listener.IsActive, Is.False);
    }

    private static (RunController Controller, GameDefinitionRegistry Registry, HostRng Rng) BuildReadyRun()
    {
        var registry = RunTestHelper.CreateRegistryWithHpCard();
        var mod = new RunMod { Phase = ERunPhase.Reward, IsMultiplayer = false };
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            var character = RunTestHelper.CreateCharacterWithHp(i);
            mod.AddToCharacterPool(character);
            mod.PlayerStates[i].SetActiveCharacter(character);
        }

        mod.AddPlayerController(new PlayerController("local", "Player", isOwner: true));
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            mod.AssignSlotInternal(i, "local");
        }

        return (new RunController(mod), registry, new HostRng(1, "battle"));
    }
}
