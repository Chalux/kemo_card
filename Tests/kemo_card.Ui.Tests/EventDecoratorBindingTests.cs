using System;
using KemoCard.Frame.Mvc;
using KemoCard.Frame.UI;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 装饰器事件订阅（<see cref="EventSubscribeAttribute"/> + <see cref="EventDispatcher.BindDecorated"/>）
/// 与 UI 订阅账本的联动。
/// </summary>
[TestFixture]
public sealed class EventDecoratorBindingTests
{
    private enum ETestEvent
    {
        [EventPayload(typeof(ValuePayload))]
        Value,

        ValueAlias = Value,

        [EventPayload(typeof(ValuePayload))]
        ValueWithoutPayload,

        [EventPayload(typeof(OtherPayload))]
        Other,

        MissingContract,
    }

    private enum EUintEvent : uint
    {
        [EventPayload(typeof(ValuePayload))]
        High = 0x80000000u,
    }

    private readonly struct ValuePayload(int value)
    {
        public int Value { get; } = value;
    }

    private readonly struct OtherPayload(int value)
    {
        public int Value { get; } = value;
    }

    private sealed class CapturingEventDispatcherLogger : IEventDispatcherLogger
    {
        public int ErrorCount => Messages.Count;

        public List<string> Messages { get; } = [];

        public void LogError(string message) => Messages.Add(message);
    }

    private sealed class DecoratedTarget
    {
        public int PayloadCalls { get; private set; }
        public int ValueSeen { get; private set; }
        public int ListenerCalls { get; private set; }
        public int ParameterlessCalls { get; private set; }
        public int TwoArgumentCalls { get; private set; }
        public IEventListener<ValuePayload>? LastListener { get; private set; }

        [EventSubscribe(ETestEvent.Value)]
        private void OnValue(ValuePayload payload)
        {
            PayloadCalls++;
            ValueSeen = payload.Value;
        }

        [EventSubscribe(ETestEvent.ValueWithoutPayload)]
        private void OnValueWithoutPayload()
        {
            ParameterlessCalls++;
        }

        [EventSubscribe(ETestEvent.Value)]
        private void OnValueListener(IEventListener<ValuePayload> listener)
        {
            ListenerCalls++;
            LastListener = listener;
        }

        [EventSubscribe(ETestEvent.Value)]
        private void OnValueTwoArguments(ValuePayload payload, IEventListener<ValuePayload> listener)
        {
            TwoArgumentCalls++;
            ValueSeen = payload.Value + (listener.IsActive ? 0 : 1000);
        }
    }

    private sealed class OnceTarget
    {
        public int Calls { get; private set; }

        [EventSubscribe(ETestEvent.Value, Once = true)]
        private void OnValue(ValuePayload payload) => Calls++;
    }

    private sealed class OnceAndPersistentTarget
    {
        public int OnceCalls { get; private set; }
        public int PersistentCalls { get; private set; }

        [EventSubscribe(ETestEvent.Value, Once = true)]
        private void OnOnce(ValuePayload payload) => OnceCalls++;

        [EventSubscribe(ETestEvent.Value)]
        private void OnPersistent(ValuePayload payload) => PersistentCalls++;
    }

    private sealed class MultiEventTarget
    {
        public int Calls { get; private set; }

        [EventSubscribe(ETestEvent.Value)]
        [EventSubscribe(ETestEvent.Other)]
        private void OnAny() => Calls++;
    }

    private sealed class InvalidTarget
    {
        public int ValidCalls { get; private set; }

        [EventSubscribe(ETestEvent.Value)]
        private void WrongParameterType(int value)
        {
        }

        [EventSubscribe(ETestEvent.MissingContract)]
        private void MissingPayloadContract()
        {
        }

        [EventSubscribe(null!)]
        private void NullEventValue()
        {
        }

        [EventSubscribe(ETestEvent.ValueWithoutPayload)]
        private void Valid() => ValidCalls++;
    }

    private class HiddenBaseTarget
    {
        public int BaseCalls { get; private set; }

        [EventSubscribe(ETestEvent.Value)]
        protected void OnValue(ValuePayload payload) => BaseCalls++;
    }

    private class HiddenDerivedTarget : HiddenBaseTarget
    {
        public int DerivedCalls { get; private set; }

        [EventSubscribe(ETestEvent.Value)]
        protected new void OnValue(ValuePayload payload) => DerivedCalls++;
    }

    private class OverrideBaseTarget
    {
        public int Calls { get; protected set; }

        [EventSubscribe(ETestEvent.Value)]
        protected virtual void OnValue(ValuePayload payload) => Calls++;
    }

    private sealed class OverrideDerivedTarget : OverrideBaseTarget
    {
        [EventSubscribe(ETestEvent.Value)]
        protected override void OnValue(ValuePayload payload) => Calls++;
    }

    private class OverrideMiddleTarget : OverrideBaseTarget
    {
        [EventSubscribe(ETestEvent.Value)]
        protected override void OnValue(ValuePayload payload) => Calls++;
    }

    private sealed class OverrideLeafTarget : OverrideMiddleTarget
    {
        [EventSubscribe(ETestEvent.Value)]
        protected override void OnValue(ValuePayload payload) => Calls++;
    }

    private class OnceBaseTarget
    {
        public int Calls { get; protected set; }

        [EventSubscribe(ETestEvent.Value, Once = true)]
        protected virtual void OnValue(ValuePayload payload) => Calls++;
    }

    private sealed class OnceDerivedTarget : OnceBaseTarget
    {
        [EventSubscribe(ETestEvent.Value)]
        protected override void OnValue(ValuePayload payload) => Calls++;
    }

    private sealed class AliasTarget
    {
        public int Calls { get; private set; }

        [EventSubscribe(ETestEvent.ValueAlias)]
        private void OnValue(ValuePayload payload) => Calls++;
    }

    private sealed class UintHighTarget
    {
        public int Calls { get; private set; }

        [EventSubscribe(EUintEvent.High)]
        private void OnHigh(ValuePayload payload) => Calls++;
    }

    private sealed class ScopedTarget
    {
        public int OwnerCalls { get; private set; }
        public int GlobalCalls { get; private set; }

        [EventSubscribe(ETestEvent.Value)]
        private void OnOwner(ValuePayload payload) => OwnerCalls++;

        [EventSubscribe(ETestEvent.Value, Bus = EventBusScope.Global)]
        private void OnGlobal(ValuePayload payload) => GlobalCalls++;
    }

    private sealed record EqualKeyTarget(string Key)
    {
        public int Calls { get; private set; }

        [EventSubscribe(ETestEvent.Value)]
        private void OnValue(ValuePayload payload) => Calls++;
    }

    private static EventKey<ValuePayload> ValueKey => new((int)ETestEvent.Value);

    private static EventKey<ValuePayload> ValueWithoutPayloadKey => new((int)ETestEvent.ValueWithoutPayload);

    private static EventKey<OtherPayload> OtherKey => new((int)ETestEvent.Other);

    [SetUp]
    public void SetUp()
    {
        EventDecoratorBinder.ClearCache();
        EventDispatcher.Configure(NullEventDispatcherLogger.Instance);
        EventDispatcher.FailureMode = EventDispatchFailureMode.LogAndContinue;
    }

    [TearDown]
    public void TearDown()
    {
        EventDecoratorBinder.ClearCache();
        EventDispatcher.Configure(NullEventDispatcherLogger.Instance);
        EventDispatcher.FailureMode = EventDispatchFailureMode.LogAndContinue;
    }

    [Test]
    public void BindDecorated_supports_all_documented_signatures()
    {
        var bus = new EventDispatcher();
        var target = new DecoratedTarget();

        using var binding = bus.BindDecorated(target);

        Assert.That(binding.Count, Is.EqualTo(4), "三个 Value 方法 + 一个 ValueWithoutPayload 方法");

        bus.Send(ValueKey, new ValuePayload(42));
        bus.Send(ValueWithoutPayloadKey, new ValuePayload(7));

        Assert.Multiple(() =>
        {
            Assert.That(target.PayloadCalls, Is.EqualTo(1));
            Assert.That(target.ListenerCalls, Is.EqualTo(1));
            Assert.That(target.TwoArgumentCalls, Is.EqualTo(1));
            Assert.That(target.ParameterlessCalls, Is.EqualTo(1));
            Assert.That(target.ValueSeen, Is.EqualTo(42));
            Assert.That(target.LastListener, Is.Not.Null);
        });
    }

    [Test]
    public void Once_decorator_unsubscribes_after_first_dispatch()
    {
        var bus = new EventDispatcher();
        var target = new OnceTarget();

        using var binding = bus.BindDecorated(target);

        bus.Send(ValueKey, new ValuePayload(1));
        bus.Send(ValueKey, new ValuePayload(2));

        Assert.That(target.Calls, Is.EqualTo(1));
    }

    [Test]
    public void Once_listener_completion_does_not_tear_down_sibling_handlers()
    {
        var bus = new EventDispatcher();
        var target = new OnceAndPersistentTarget();

        using var binding = bus.BindDecorated(target);

        bus.Send(ValueKey, new ValuePayload(1));
        bus.Send(ValueKey, new ValuePayload(2));

        Assert.Multiple(() =>
        {
            Assert.That(target.OnceCalls, Is.EqualTo(1));
            Assert.That(target.PersistentCalls, Is.EqualTo(2), "Once 触发后的清理不得拆掉同一绑定的持久监听器");
        });
    }

    [Test]
    public void BindDecorated_rearms_a_fired_once_listener()
    {
        var bus = new EventDispatcher();
        var target = new OnceAndPersistentTarget();

        var first = bus.BindDecorated(target);
        bus.Send(ValueKey, new ValuePayload(1));

        var second = bus.BindDecorated(target);
        Assert.That(second, Is.Not.SameAs(first), "Once 已触发后不得复用陈旧句柄");
        bus.Send(ValueKey, new ValuePayload(2));

        Assert.Multiple(() =>
        {
            Assert.That(target.OnceCalls, Is.EqualTo(2), "重新绑定必须重装 Once 监听器");
            Assert.That(target.PersistentCalls, Is.EqualTo(2));
        });
        second.Off();
    }

    [Test]
    public void Multiple_attributes_on_one_method_bind_each_event()
    {
        var bus = new EventDispatcher();
        var target = new MultiEventTarget();

        using var binding = bus.BindDecorated(target);

        bus.Send(ValueKey, new ValuePayload(1));
        bus.Send(OtherKey, new OtherPayload(2));

        Assert.That(target.Calls, Is.EqualTo(2));
    }

    [Test]
    public void BindDecorated_is_idempotent_until_off()
    {
        var bus = new EventDispatcher();
        var target = new DecoratedTarget();

        var first = bus.BindDecorated(target);
        var second = bus.BindDecorated(target);

        Assert.That(second, Is.SameAs(first), "同一 target + caller 的重复绑定应复用同一句柄");
        bus.Send(ValueKey, new ValuePayload(1));
        Assert.That(target.PayloadCalls, Is.EqualTo(1), "重复绑定不得让 handler 执行两次");

        first.Off();
        var third = bus.BindDecorated(target);
        Assert.That(third, Is.Not.SameAs(first), "Off 之后重新绑定必须创建新句柄");
        bus.Send(ValueKey, new ValuePayload(2));
        Assert.That(target.PayloadCalls, Is.EqualTo(2));
        third.Off();
    }

    [Test]
    public void Hidden_new_method_shadows_base_decoration()
    {
        var bus = new EventDispatcher();
        var target = new HiddenDerivedTarget();

        using var binding = bus.BindDecorated(target);
        bus.Send(ValueKey, new ValuePayload(1));

        Assert.Multiple(() =>
        {
            Assert.That(binding.Count, Is.EqualTo(1), "new 隐藏的基类实现不得重复注册");
            Assert.That(target.DerivedCalls, Is.EqualTo(1));
            Assert.That(target.BaseCalls, Is.Zero);
        });
    }

    [Test]
    public void Virtual_override_does_not_log_a_false_hidden_warning()
    {
        var logger = new CapturingEventDispatcherLogger();
        EventDispatcher.Configure(logger);
        var bus = new EventDispatcher();
        var target = new OverrideDerivedTarget();

        using var binding = bus.BindDecorated(target);
        bus.Send(ValueKey, new ValuePayload(1));

        Assert.Multiple(() =>
        {
            Assert.That(binding.Count, Is.EqualTo(1), "override 只绑定一次");
            Assert.That(target.Calls, Is.EqualTo(1));
            Assert.That(logger.ErrorCount, Is.Zero, "正常 override 不应产生 new 隐藏告警");
        });
    }

    [Test]
    public void Three_level_override_does_not_log_a_false_hidden_warning()
    {
        var logger = new CapturingEventDispatcherLogger();
        EventDispatcher.Configure(logger);
        var bus = new EventDispatcher();
        var target = new OverrideLeafTarget();

        using var binding = bus.BindDecorated(target);
        bus.Send(ValueKey, new ValuePayload(1));

        Assert.Multiple(() =>
        {
            Assert.That(binding.Count, Is.EqualTo(1));
            Assert.That(target.Calls, Is.EqualTo(1));
            Assert.That(logger.ErrorCount, Is.Zero, "多级 override 不应误报 new 隐藏");
        });
    }

    [Test]
    public void Override_once_flag_takes_the_most_derived_attribute()
    {
        var bus = new EventDispatcher();
        var target = new OnceDerivedTarget();

        using var binding = bus.BindDecorated(target);
        Assert.That(binding.Count, Is.EqualTo(1), "同一方法同一事件只注册一次");

        bus.Send(ValueKey, new ValuePayload(1));
        bus.Send(ValueKey, new ValuePayload(2));

        Assert.That(target.Calls, Is.EqualTo(2), "派生标注未写 Once，应保持持久订阅");
    }

    [Test]
    public void Enum_alias_resolves_to_the_member_with_payload_contract()
    {
        var bus = new EventDispatcher();
        var target = new AliasTarget();

        using var binding = bus.BindDecorated(target);
        bus.Send(ValueKey, new ValuePayload(1));

        Assert.Multiple(() =>
        {
            Assert.That(binding.Count, Is.EqualTo(1));
            Assert.That(target.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Uint_high_value_uses_the_generators_unchecked_id()
    {
        var bus = new EventDispatcher();
        var target = new UintHighTarget();
        var key = new EventKey<ValuePayload>(unchecked((int)0x80000000u));

        using var binding = bus.BindDecorated(target);
        bus.Send(key, new ValuePayload(1));

        Assert.Multiple(() =>
        {
            Assert.That(binding.Count, Is.EqualTo(1));
            Assert.That(target.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Equal_value_targets_get_independent_bindings()
    {
        var firstTarget = new EqualKeyTarget("same");
        var secondTarget = new EqualKeyTarget("same");
        Assert.That(firstTarget, Is.EqualTo(secondTarget), "record 值相等，但二者是不同订阅方");

        var bus = new EventDispatcher();
        var first = bus.BindDecorated(firstTarget);
        var second = bus.BindDecorated(secondTarget);

        Assert.That(second, Is.Not.SameAs(first), "装饰器索引必须按引用身份而非值相等区分 target");
        bus.Send(ValueKey, new ValuePayload(1));

        Assert.Multiple(() =>
        {
            Assert.That(firstTarget.Calls, Is.EqualTo(1));
            Assert.That(secondTarget.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Custom_bus_binding_defaults_to_owner_scope()
    {
        var scope = new BindingScope();
        var bus = new EventDispatcher();
        var target = new ScopedTarget();

        var binding = scope.BindDecorated(bus, target);
        Assert.That(binding.Count, Is.EqualTo(1), "缺省只绑定 Owner 作用域");

        bus.Send(ValueKey, new ValuePayload(1));
        Assert.Multiple(() =>
        {
            Assert.That(target.OwnerCalls, Is.EqualTo(1));
            Assert.That(target.GlobalCalls, Is.Zero, "Global 标注不得被默认注册到自定义总线");
        });

        scope.UnbindAll();
    }

    [Test]
    public void Scope_filter_splits_owner_and_global_handlers()
    {
        var ownerBus = new EventDispatcher();
        var globalBus = new EventDispatcher();
        var target = new ScopedTarget();

        using var ownerBinding = ownerBus.BindDecorated(target, scopeFilter: EventBusScope.Owner);
        using var globalBinding = globalBus.BindDecorated(target, scopeFilter: EventBusScope.Global);

        Assert.Multiple(() =>
        {
            Assert.That(ownerBinding.Count, Is.EqualTo(1));
            Assert.That(globalBinding.Count, Is.EqualTo(1));
        });

        ownerBus.Send(ValueKey, new ValuePayload(1));
        globalBus.Send(ValueKey, new ValuePayload(2));

        Assert.Multiple(() =>
        {
            Assert.That(target.OwnerCalls, Is.EqualTo(1));
            Assert.That(target.GlobalCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void BindDecorated_with_no_decorated_methods_does_not_leave_an_index_entry()
    {
        var bus = new EventDispatcher();

        using var binding = bus.BindDecorated(new object());

        Assert.Multiple(() =>
        {
            Assert.That(binding.Count, Is.Zero);
            Assert.That(bus.DecoratedBindingCount, Is.Zero, "空绑定不得进入总线索引并强引用节点");
        });
    }

    [Test]
    public void Decorated_binding_index_is_released_on_off()
    {
        var bus = new EventDispatcher();
        var binding = bus.BindDecorated(new DecoratedTarget());
        Assert.That(bus.DecoratedBindingCount, Is.EqualTo(1));

        binding.Off();

        Assert.That(bus.DecoratedBindingCount, Is.Zero);
    }

    [Test]
    public void Cached_binding_is_recreated_after_out_of_band_listener_removal()
    {
        var bus = new EventDispatcher();
        var target = new DecoratedTarget();
        var first = bus.BindDecorated(target);

        bus.OffId((int)ETestEvent.Value);
        Assert.That(bus.DecoratedBindingCount, Is.EqualTo(1),
            "同一绑定里仍有其他事件的活跃监听器，不能整体拆掉");

        bus.Send(ValueWithoutPayloadKey, new ValuePayload(1));
        Assert.That(target.ParameterlessCalls, Is.EqualTo(1), "OffId 不得误杀同一 UI 的其他装饰器事件");

        var second = bus.BindDecorated(target);

        Assert.That(second, Is.Not.SameAs(first), "监听器被 OffId 移除后不得复用陈旧句柄");
        bus.Send(ValueKey, new ValuePayload(1));
        Assert.That(target.PayloadCalls, Is.EqualTo(1), "重新绑定必须恢复被移除的监听器");
        second.Off();
    }

    [Test]
    public void Two_binding_scopes_get_independent_handles()
    {
        var bus = new EventDispatcher();
        var target = new DecoratedTarget();
        var scopeA = new BindingScope();
        var scopeB = new BindingScope();

        var first = scopeA.BindDecorated(bus, target);
        var second = scopeB.BindDecorated(bus, target);

        Assert.That(second, Is.Not.SameAs(first), "不同 BindingScope 不得共享可被互相关闭的句柄");

        scopeA.UnbindAll();
        bus.Send(ValueKey, new ValuePayload(1));
        Assert.That(target.PayloadCalls, Is.EqualTo(1), "拆掉 scope A 后 scope B 仍须存活");

        scopeB.UnbindAll();
        bus.Send(ValueKey, new ValuePayload(2));
        Assert.That(target.PayloadCalls, Is.EqualTo(1));
    }

    [Test]
    public void Off_removes_only_decorated_listeners_with_same_caller()
    {
        var bus = new EventDispatcher();
        var target = new DecoratedTarget();
        var manualCalls = 0;

        bus.On(ValueKey, (_, _) => manualCalls++, target);
        var binding = bus.BindDecorated(target);

        binding.Off();
        binding.Off(); // 幂等

        bus.Send(ValueKey, new ValuePayload(1));

        Assert.Multiple(() =>
        {
            Assert.That(binding.IsActive, Is.False);
            Assert.That(manualCalls, Is.EqualTo(1), "手动注册的同 caller 监听器不应被装饰器句柄误删");
            Assert.That(target.PayloadCalls, Is.Zero);
            Assert.That(target.ListenerCalls, Is.Zero);
            Assert.That(target.TwoArgumentCalls, Is.Zero);
        });
    }

    [Test]
    public void BindDecorated_defaults_caller_to_target_so_OffCaller_cleans_everything()
    {
        var bus = new EventDispatcher();
        var target = new DecoratedTarget();

        var binding = bus.BindDecorated(target);
        bus.OffCaller(target);

        bus.Send(ValueKey, new ValuePayload(1));

        Assert.Multiple(() =>
        {
            Assert.That(binding.IsActive, Is.False, "OffCaller 必须同时让装饰器句柄失效");
            Assert.That(target.PayloadCalls, Is.Zero);
            Assert.That(target.ListenerCalls, Is.Zero);
            Assert.That(target.TwoArgumentCalls, Is.Zero);
        });
    }

    [Test]
    public void Invalid_methods_are_logged_and_skipped_without_blocking_valid_ones()
    {
        var logger = new CapturingEventDispatcherLogger();
        EventDispatcher.Configure(logger);

        var bus = new EventDispatcher();
        var target = new InvalidTarget();

        using var binding = bus.BindDecorated(target);

        bus.Send(ValueKey, new ValuePayload(1));
        bus.Send(ValueWithoutPayloadKey, new ValuePayload(2));

        Assert.Multiple(() =>
        {
            Assert.That(binding.Count, Is.EqualTo(1), "只有签名合法的 Valid 被绑定");
            Assert.That(target.ValidCalls, Is.EqualTo(1));
            Assert.That(logger.ErrorCount, Is.EqualTo(3), "参数错误 + 缺 [EventPayload] + 构造器异常各一条");
            Assert.That(logger.Messages, Has.Some.Contains("[EventPayload]"));
            Assert.That(logger.Messages, Has.Some.Contains("扫描失败"));
        });
    }

    [Test]
    public void BindingScope_tracks_and_unbinds_decorated_events()
    {
        var bus = new EventDispatcher();
        var target = new DecoratedTarget();
        var scope = new BindingScope();

        var binding = scope.BindDecorated(bus, target);

        Assert.Multiple(() =>
        {
            Assert.That(binding.Count, Is.EqualTo(4));
            Assert.That(scope.Count, Is.EqualTo(1), "一次批量绑定只占一个账本条目");
        });

        bus.Send(ValueKey, new ValuePayload(1));
        Assert.That(target.PayloadCalls, Is.EqualTo(1));

        scope.UnbindAll();
        bus.Send(ValueKey, new ValuePayload(2));

        Assert.Multiple(() =>
        {
            Assert.That(scope.Count, Is.Zero);
            Assert.That(target.PayloadCalls, Is.EqualTo(1), "离场后不再收到事件");
        });

        // 账本可重复使用：缓存重开时重新绑定。
        scope.BindDecorated(bus, target);
        bus.Send(ValueKey, new ValuePayload(3));

        Assert.That(target.PayloadCalls, Is.EqualTo(2));
    }

    [Test]
    public void BindingScope_with_no_decorated_methods_does_not_consume_a_ledger_entry()
    {
        var scope = new BindingScope();

        var binding = scope.BindDecorated(new EventDispatcher(), new object());

        Assert.Multiple(() =>
        {
            Assert.That(binding.Count, Is.Zero);
            Assert.That(scope.Count, Is.Zero);
        });
    }

    [Test]
    public void BindDecorated_rejects_null_target()
    {
        var bus = new EventDispatcher();

        Assert.Throws<ArgumentNullException>(() => bus.BindDecorated(null!));
    }
}