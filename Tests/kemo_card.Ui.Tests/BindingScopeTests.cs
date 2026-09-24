using KemoCard.Frame.UI;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// <see cref="BindingScope"/> 是界面订阅生命周期的唯一账本。
/// 本类刻意不引用 Godot 类型，因此可在此直接覆盖。
/// </summary>
[TestFixture]
public sealed class BindingScopeTests
{
    [Test]
    public void Bind_subscribes_immediately_and_tracks_count()
    {
        var scope = new BindingScope();
        var subscribed = false;

        scope.Bind(() => subscribed = true, () => { });

        Assert.That(subscribed, Is.True, "Bind 必须立即订阅");
        Assert.That(scope.Count, Is.EqualTo(1));
        Assert.That(scope.HasBindings, Is.True);
    }

    [Test]
    public void Add_only_tracks_without_subscribing()
    {
        var scope = new BindingScope();
        var subscribed = false;

        scope.Add(() => subscribed = true);

        Assert.That(subscribed, Is.False, "Add 只登记，不订阅");
        Assert.That(scope.Count, Is.EqualTo(1));
    }

    [Test]
    public void UnbindAll_invokes_every_unbinder_and_clears()
    {
        var scope = new BindingScope();
        var unbound = new List<int>();
        scope.Add(() => unbound.Add(1));
        scope.Add(() => unbound.Add(2));
        scope.Add(() => unbound.Add(3));

        scope.UnbindAll();

        Assert.That(unbound, Is.EqualTo(new[] { 3, 2, 1 }), "必须逆序解绑（后建立的依赖先拆）");
        Assert.That(scope.Count, Is.Zero);
        Assert.That(scope.HasBindings, Is.False);
    }

    [Test]
    public void UnbindAll_is_idempotent()
    {
        var scope = new BindingScope();
        var count = 0;
        scope.Add(() => count++);

        scope.UnbindAll();
        Assert.DoesNotThrow(() => scope.UnbindAll());
        scope.UnbindAll();

        Assert.That(count, Is.EqualTo(1), "重复解绑不得重复执行同一解绑动作");
    }

    /// <summary>
    /// 缓存重开的必要条件：关闭走缓存时节点只是 RemoveChild，重开会再次 InitEvent 重新订阅。
    /// 若解绑后禁止登记，重开必然失败。
    /// </summary>
    [Test]
    public void Scope_is_reusable_after_UnbindAll()
    {
        var scope = new BindingScope();
        var unbindCount = 0;
        scope.Add(() => unbindCount++);
        scope.UnbindAll();
        Assert.That(scope.Count, Is.Zero);

        Assert.DoesNotThrow(() => scope.Add(() => unbindCount++));
        Assert.That(scope.Count, Is.EqualTo(1));

        scope.UnbindAll();
        Assert.That(unbindCount, Is.EqualTo(2));
    }

    [Test]
    public void A_throwing_unbinder_does_not_block_the_others()
    {
        var scope = new BindingScope();
        var unbound = new List<int>();
        scope.Add(() => unbound.Add(1));
        scope.Add(() => throw new InvalidOperationException("boom"));
        scope.Add(() => unbound.Add(3));

        Assert.DoesNotThrow(() => scope.UnbindAll());

        Assert.That(unbound, Is.EqualTo(new[] { 3, 1 }), "坏解绑不得阻断其余清理");
        Assert.That(scope.Count, Is.Zero, "账本仍须清空，否则重开会重复解绑");
    }

    /// <summary>模拟「关闭 → 重开」循环，断言订阅数不随开关次数增长（D1/D2 的守护）。</summary>
    [Test]
    public void Repeated_close_open_cycles_do_not_accumulate_subscriptions()
    {
        var scope = new BindingScope();
        var handlerInvocations = 0;
        var listeners = new List<Action>();

        void Subscribe() => listeners.Add(() => handlerInvocations++);
        void Unsubscribe() { }

        for (var cycle = 0; cycle < 5; cycle++)
        {
            scope.Bind(Subscribe, Unsubscribe);
            Assert.That(listeners, Has.Count.EqualTo(1), $"第 {cycle + 1} 轮重开时监听器数量必须回到 1");

            scope.UnbindAll();
            listeners.Clear();
        }

        Assert.That(handlerInvocations, Is.Zero);
    }

    [Test]
    public void Bind_rejects_null_arguments()
    {
        var scope = new BindingScope();

        Assert.Throws<ArgumentNullException>(() => scope.Bind(null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => scope.Bind(() => { }, null!));
        Assert.Throws<ArgumentNullException>(() => scope.Add(null!));
    }

    /// <summary>
    /// 框架契约（<c>UIOpenStateHandler</c>）：**界面已经打开时被再次 Open 不经过 <c>_ExitTree</c>**，
    /// 旧订阅仍然活着，因此每次 <c>InitEvent</c> 之前必须先清账（<c>InvokeResetBindings</c> → <c>UnbindAll</c>）。
    /// </summary>
    /// <remarks>
    /// 2026-09-24 实测缺陷：从 Run 退回主菜单会重新打开仍处于打开态的 <c>MenuWin</c>，
    /// 不清账直接 <c>InitEvent</c>，Godot 报 5 条 <c>Signal 'pressed' is already connected</c>，
    /// 账本里还多出 5 条永远解不掉的条目（进程退出时再报 5 条
    /// <c>Attempt to disconnect a nonexistent connection</c>）。本用例固定「先解绑、再登记」这一调用约定。
    /// </remarks>
    [Test]
    public void Reopening_without_leaving_the_tree_stays_flat_when_bindings_are_reset_first()
    {
        var scope = new BindingScope();
        var listeners = new List<Action>();

        for (var reopen = 0; reopen < 3; reopen++)
        {
            scope.UnbindAll();                     // ← 框架在 InitEvent 之前做的事
            listeners.Clear();

            scope.Bind(() => listeners.Add(() => { }), () => { });
            scope.Bind(() => listeners.Add(() => { }), () => { });

            Assert.That(listeners, Has.Count.EqualTo(2), $"第 {reopen + 1} 次重开：每个信号只登记一次");
            Assert.That(scope.Count, Is.EqualTo(2), "账本条目数必须与真实订阅数一致");
        }

        // 反证：少了这一步，同一信号会被登记两次，账本随之翻倍（就是上面那条 ERROR 的成因）。
        var withoutReset = new BindingScope();
        withoutReset.Bind(() => { }, () => { });
        withoutReset.Bind(() => { }, () => { });
        Assert.That(withoutReset.Count, Is.EqualTo(2), "未清账时账本累积——故框架必须显式清账");
    }
}