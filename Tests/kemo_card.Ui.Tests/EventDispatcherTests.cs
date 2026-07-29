using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KemoCard.Frame.Mvc;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class EventDispatcherTests
{
    private readonly struct IntPayload(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class CapturingEventDispatcherLogger : IEventDispatcherLogger
    {
        public int ErrorCount { get; private set; }

        public string? LastMessage { get; private set; }

        public void LogError(string message)
        {
            ErrorCount++;
            LastMessage = message;
        }
    }

    [SetUp]
    public void SetUp()
    {
        EventDispatcher.Configure(NullEventDispatcherLogger.Instance);
        EventDispatcher.FailureMode = EventDispatchFailureMode.LogAndContinue;
    }

    [Test]
    public void Send_invokes_registered_handler()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(1);
        var received = 0;

        bus.On(key, (p, _) => received = p.Value, this);
        bus.Send(key, new IntPayload(42));

        Assert.That(received, Is.EqualTo(42));
    }

    [Test]
    public void OffId_with_value_type_payload_does_not_throw()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(7);
        bus.On(key, static (_, _) => { }, this);

        Assert.DoesNotThrow(() => bus.OffId(7));
        Assert.That(bus.Has(key), Is.False);
    }

    [Test]
    public void Once_listener_is_removed_after_single_invoke()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(2);
        var count = 0;

        bus.Once(key, (_, _) => count++, this);
        bus.Send(key, new IntPayload(1));
        bus.Send(key, new IntPayload(1));

        Assert.That(count, Is.EqualTo(1));
        Assert.That(bus.Has(key), Is.False);
    }

    [Test]
    public void OffCaller_only_removes_that_callers_listeners()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(3);
        var callerA = new object();
        var callerB = new object();
        var hitsB = 0;

        bus.On(key, static (_, _) => { }, callerA);
        bus.On(key, (_, _) => hitsB++, callerB);

        bus.OffCaller(callerA);
        bus.Send(key, new IntPayload(1));

        Assert.That(hitsB, Is.EqualTo(1));
        Assert.That(bus.Has(key, caller: callerA), Is.False);
        Assert.That(bus.Has(key, caller: callerB), Is.True);
    }

    [Test]
    public void Off_without_args_removes_all_listeners()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(4);
        var callerA = new object();
        var callerB = new object();

        bus.On(key, static (_, _) => { }, callerA);
        bus.On(key, static (_, _) => { }, callerB);

        bus.Off(key);

        Assert.That(bus.Has(key), Is.False);
    }

    [Test]
    public void Off_with_handler_removes_across_callers()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(5);
        var callerA = new object();
        var callerB = new object();
        Action<IntPayload, IEventListener<IntPayload>> handler = static (_, _) => { };

        bus.On(key, handler, callerA);
        bus.On(key, handler, callerB);

        bus.Off(key, handler);

        Assert.That(bus.Has(key, handler), Is.False);
        Assert.That(bus.Has(key, handler, callerA), Is.False);
        Assert.That(bus.Has(key, handler, callerB), Is.False);
    }

    [Test]
    public void Has_with_wrong_payload_type_returns_false()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(6);
        bus.On(key, static (_, _) => { }, this);

        Assert.That(bus.Has<string>(new EventKey<string>(6)), Is.False);
    }

    [Test]
    public void Once_concurrent_send_invokes_at_most_once()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(8);
        var count = 0;
        var startGate = new ManualResetEventSlim(false);

        bus.Once(key, (_, _) => Interlocked.Increment(ref count), this);

        var tasks = new List<Task>();
        for (var t = 0; t < 16; t++)
        {
            tasks.Add(Task.Run(() =>
            {
                startGate.Wait();
                bus.Send(key, new IntPayload(1));
            }));
        }

        startGate.Set();
        Task.WaitAll(tasks.ToArray());

        Assert.That(count, Is.EqualTo(1));
        Assert.That(bus.Has(key), Is.False);
    }

    [Test]
    public void On_duplicate_registration_returns_same_listener()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(10);
        var count = 0;
        Action<IntPayload, IEventListener<IntPayload>> handler = (_, _) => count++;

        var first = bus.On(key, handler, this);
        var second = bus.On(key, handler, this);

        Assert.That(second, Is.SameAs(first));

        bus.Send(key, new IntPayload(1));

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void On_null_handler_throws()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(11);

        Assert.Throws<ArgumentNullException>(() => bus.On(key, null!, this));
    }

    [Test]
    public void Concurrent_on_and_send_do_not_throw()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(9);

        Assert.DoesNotThrowAsync(async () =>
        {
            var tasks = new List<Task>();
            for (var t = 0; t < 8; t++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (var i = 0; i < 500; i++)
                    {
                        var listener = bus.On(key, static (_, _) => { }, new object());
                        bus.Send(key, new IntPayload(i));
                        listener.Off();
                    }
                }));
            }
            await Task.WhenAll(tasks);
        });
    }

    [Test]
    public void Send_single_listener_struct_payload_invokes_without_error()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(12);
        var received = 0;

        bus.On(key, (p, _) => received = p.Value, this);
        bus.Send(key, new IntPayload(99));

        Assert.That(received, Is.EqualTo(99));
    }

    [Test]
    public void Send_multiple_listeners_struct_payload_invokes_all()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(13);
        var total = 0;

        bus.On(key, (p, _) => total += p.Value, this);
        bus.On(key, (p, _) => total += p.Value, new object());

        bus.Send(key, new IntPayload(5));

        Assert.That(total, Is.EqualTo(10));
    }

    [Test]
    public void OffCaller_null_removes_none_caller_listeners()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(14);
        var count = 0;

        bus.On(key, (_, _) => count++, caller: null);
        bus.OffCaller(null);

        Assert.That(bus.Has(key), Is.False);
        bus.Send(key, new IntPayload(1));
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void On_once_after_on_logs_and_keeps_persistent_listener()
    {
        var logger = new CapturingEventDispatcherLogger();
        EventDispatcher.Configure(logger);

        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(15);
        var count = 0;
        Action<IntPayload, IEventListener<IntPayload>> handler = (_, _) => count++;

        var first = bus.On(key, handler, this);
        bus.Once(key, handler, this);
        var second = bus.On(key, handler, this);

        Assert.That(second, Is.SameAs(first));
        Assert.That(logger.ErrorCount, Is.EqualTo(1));
        Assert.That(logger.LastMessage, Does.Contain("mismatched once flag"));

        bus.Send(key, new IntPayload(1));
        bus.Send(key, new IntPayload(1));

        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public void Has_ignores_inactive_once_listener_during_invoke()
    {
        var bus = new EventDispatcher();
        var key = new EventKey<IntPayload>(16);
        var hasDuringInvoke = true;

        bus.Once(key, (_, _) => hasDuringInvoke = bus.Has(key), this);
        bus.Send(key, new IntPayload(1));

        Assert.That(hasDuringInvoke, Is.False);
    }

    [Test]
    public void On_conflicting_payload_type_throws()
    {
        var bus = new EventDispatcher();
        var intKey = new EventKey<IntPayload>(20);
        var stringKey = new EventKey<string>(20);

        bus.On(intKey, static (_, _) => { }, this);

        Assert.Throws<InvalidOperationException>(() => bus.On(stringKey, static (_, _) => { }, this));
    }

    [Test]
    public void Send_throw_mode_propagates_listener_exception()
    {
        var previousMode = EventDispatcher.FailureMode;
        EventDispatcher.FailureMode = EventDispatchFailureMode.Throw;
        try
        {
            var bus = new EventDispatcher();
            var key = new EventKey<IntPayload>(21);
            bus.On(key, static (_, _) => throw new InvalidOperationException("boom"), this);

            Assert.Throws<InvalidOperationException>(() => bus.Send(key, new IntPayload(1)));
        }
        finally
        {
            EventDispatcher.FailureMode = previousMode;
        }
    }
}