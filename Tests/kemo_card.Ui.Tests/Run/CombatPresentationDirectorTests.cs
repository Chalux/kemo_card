using KemoCard.Mod.Combat.Presentation;
using KemoCard.Mod.Run.Ui.CombatUi.Presentation;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

/// <summary>Run 规格 §14.4：表现队列顺序播放、播放中追加、播完回调、动画异常隔离。</summary>
[TestFixture]
public sealed class CombatPresentationDirectorTests
{
    private sealed class RecordingPlayer : ICombatEventPlayer
    {
        public List<CombatPresentationEvent> Played { get; } = [];
        public Func<CombatPresentationEvent, Task>? OnPlay { get; set; }

        public async Task PlayAsync(CombatPresentationEvent presentationEvent)
        {
            Played.Add(presentationEvent);
            if (OnPlay is not null)
                await OnPlay(presentationEvent);
        }
    }

    [Test]
    public async Task Plays_events_in_order_and_raises_drained()
    {
        var director = new CombatPresentationDirector();
        var player = new RecordingPlayer();
        var drained = 0;
        director.Drained += () => drained++;
        director.Enqueue([new BattleStartedEvent(), new WaveStartedEvent(0), new WaveStartedEvent(1)]);

        await director.PlayAsync(player);

        Assert.That(player.Played, Has.Count.EqualTo(3));
        Assert.That(player.Played[0], Is.TypeOf<BattleStartedEvent>());
        Assert.That(player.Played[1], Is.EqualTo(new WaveStartedEvent(0)));
        Assert.That(player.Played[2], Is.EqualTo(new WaveStartedEvent(1)));
        Assert.That(drained, Is.EqualTo(1));
        Assert.That(director.IsPlaying, Is.False);
        Assert.That(director.PendingCount, Is.Zero);
    }

    [Test]
    public async Task Events_enqueued_during_playback_are_appended_to_the_same_run()
    {
        var director = new CombatPresentationDirector();
        var player = new RecordingPlayer();
        player.OnPlay = evt =>
        {
            if (evt is BattleStartedEvent)
            {
                Assert.That(director.IsPlaying, Is.True);
                director.Enqueue([new WaveStartedEvent(7)]);
            }

            return Task.CompletedTask;
        };
        director.Enqueue([new BattleStartedEvent()]);

        await director.PlayAsync(player);

        Assert.That(player.Played, Has.Count.EqualTo(2));
        Assert.That(player.Played[1], Is.EqualTo(new WaveStartedEvent(7)));
    }

    [Test]
    public async Task Second_play_call_while_playing_returns_immediately()
    {
        var director = new CombatPresentationDirector();
        var gate = new TaskCompletionSource();
        var player = new RecordingPlayer { OnPlay = _ => gate.Task };
        director.Enqueue([new BattleStartedEvent()]);

        var first = director.PlayAsync(player);
        Assert.That(director.IsPlaying, Is.True);

        var second = director.PlayAsync(player);
        Assert.That(second.IsCompleted, Is.True, "并发调用不开第二个循环");

        gate.SetResult();
        await first;
        Assert.That(player.Played, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Player_exception_does_not_stop_the_queue()
    {
        var director = new CombatPresentationDirector();
        var player = new RecordingPlayer
        {
            OnPlay = evt => evt is BattleStartedEvent
                ? throw new InvalidOperationException("boom")
                : Task.CompletedTask,
        };
        director.Enqueue([new BattleStartedEvent(), new WaveStartedEvent(1)]);

        await director.PlayAsync(player);

        Assert.That(player.Played, Has.Count.EqualTo(2), "异常后继续播放下一条");
        Assert.That(director.IsPlaying, Is.False);
    }

    [Test]
    public void Clear_drops_pending_events()
    {
        var director = new CombatPresentationDirector();
        director.Enqueue([new BattleStartedEvent()]);

        director.Clear();

        Assert.That(director.PendingCount, Is.Zero);
    }
}