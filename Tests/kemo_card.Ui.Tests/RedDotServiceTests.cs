using KemoCard.Frame.Mvc;
using KemoCard.Frame.Notification;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class RedDotServiceTests
{
    [SetUp]
    public void SetUp()
    {
        RedDotService.Configure();
    }

    [TearDown]
    public void TearDown()
    {
        RedDotService.UnregisterNode("A");
        RedDotService.UnregisterNode("B");
        RedDotService.UnregisterNode("Menu");
        RedDotService.UnregisterNode("Menu/Codex");
        RedDotService.UnregisterNode("Menu/Settings");
    }

    [Test]
    public void ReRegister_tolerates_throwing_unsubscribe()
    {
        (Action subscribe, Action unsubscribe) throwing = (() => { }, () =>
            throw new InvalidOperationException("boom"));
        RedDotService.RegisterNode("A", () => false, throwing);

        Assert.DoesNotThrow(() => RedDotService.RegisterNode("A", () => true));
        Assert.That(RedDotService.IsActive("A"), Is.True, "旧触发器反订阅失败不得阻断重注册");
    }

    [Test]
    public void RegisterNode_with_checkFunc_evaluates_immediately()
    {
        RedDotService.RegisterNode("A", () => true);
        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void RegisterNode_checkFunc_returns_false()
    {
        RedDotService.RegisterNode("A", () => false);
        Assert.That(RedDotService.IsActive("A"), Is.False);
    }

    [Test]
    public void IsActive_unknown_id_returns_false()
    {
        Assert.That(RedDotService.IsActive("nonexistent"), Is.False);
    }

    [Test]
    public void RegisterParent_after_RegisterNode_propagates_state_upward()
    {
        RedDotService.RegisterNode("Menu/Codex", () => true);
        RedDotService.RegisterNode("Menu");
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        Assert.That(RedDotService.IsActive("Menu"), Is.True);
    }

    [Test]
    public void RegisterParent_before_RegisterNode_works()
    {
        RedDotService.RegisterNode("Menu");
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        RedDotService.RegisterNode("Menu/Codex", () => true);
        Assert.That(RedDotService.IsActive("Menu"), Is.True);
    }

    [Test]
    public void Parent_inactive_when_all_children_inactive()
    {
        RedDotService.RegisterNode("Menu/Codex", () => false);
        RedDotService.RegisterNode("Menu/Settings", () => false);
        RedDotService.RegisterNode("Menu");
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        RedDotService.RegisterParent("Menu/Settings", "Menu");
        Assert.That(RedDotService.IsActive("Menu"), Is.False);
    }

    [Test]
    public void ReRegister_same_id_replaces()
    {
        RedDotService.RegisterNode("A", () => false);
        Assert.That(RedDotService.IsActive("A"), Is.False);
        RedDotService.RegisterNode("A", () => true);
        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void FlushAll_evaluates_dirty_nodes()
    {
        bool checkReturn = false;
        RedDotService.RegisterNode("A", () => checkReturn);
        Assert.That(RedDotService.IsActive("A"), Is.False);
        checkReturn = true;
        RedDotService.Refresh("A");
        RedDotService.InternalFlushAll();
        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void Changed_event_fires_when_state_changes()
    {
        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);
        RedDotService.RegisterNode("A", () => false);
        var node = RedDotService.InternalGetNode("A");
        node!.LastEvaluated = true;
        RedDotService.MarkDirty(node);
        RedDotService.InternalFlushAll();

        Assert.That(events, Has.Count.EqualTo(1), "无父无子的节点只有一条自身变更事件");
        Assert.That(events[0].Id, Is.EqualTo("A"));
        Assert.That(events[0].Active, Is.True);
        Assert.That(events[0].SourceId, Is.EqualTo("A"));
        Assert.That(events[0].Scope, Is.EqualTo(RedDotChangeScope.Self));
    }

    [Test]
    public void Changed_event_does_not_fire_when_state_unchanged()
    {
        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);
        RedDotService.RegisterNode("A", () => false);
        RedDotService.Refresh("A");
        RedDotService.InternalFlushAll();
        Assert.That(events, Is.Empty);
    }

    [Test]
    public void Changed_event_notifies_ancestors_and_descendants()
    {
        RedDotService.RegisterNode("Root");
        RedDotService.RegisterNode("Root/Parent");
        RedDotService.RegisterNode("Root/Parent/Child", () => false);
        RedDotService.RegisterParent("Root/Parent", "Root");
        RedDotService.RegisterParent("Root/Parent/Child", "Root/Parent");

        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);

        var child = RedDotService.InternalGetNode("Root/Parent/Child")!;
        child.LastEvaluated = true;
        RedDotService.MarkDirty(child);
        RedDotService.InternalFlushAll();

        Assert.Multiple(() =>
        {
            Assert.That(Contains(events, "Root/Parent/Child", RedDotChangeScope.Self, "Root/Parent/Child", true), Is.True);
            Assert.That(Contains(events, "Root/Parent", RedDotChangeScope.Self, "Root/Parent", true), Is.True);
            Assert.That(Contains(events, "Root", RedDotChangeScope.Self, "Root", true), Is.True);

            Assert.That(Contains(events, "Root/Parent", RedDotChangeScope.Ancestor, "Root/Parent/Child", true), Is.True);
            Assert.That(Contains(events, "Root", RedDotChangeScope.Ancestor, "Root/Parent/Child", true), Is.True);

            Assert.That(Contains(events, "Root/Parent/Child", RedDotChangeScope.Descendant, "Root/Parent", true), Is.True);
            Assert.That(Contains(events, "Root/Parent", RedDotChangeScope.Descendant, "Root", true), Is.True);
            Assert.That(Contains(events, "Root/Parent/Child", RedDotChangeScope.Descendant, "Root", true), Is.True);
        });
    }

    [Test]
    public void Changed_event_notifies_parent_even_when_parent_state_is_unchanged()
    {
        RedDotService.RegisterNode("P");
        RedDotService.RegisterNode("P/A", () => true);
        RedDotService.RegisterNode("P/B", () => false);
        RedDotService.RegisterParent("P/A", "P");
        RedDotService.RegisterParent("P/B", "P");

        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);

        var b = RedDotService.InternalGetNode("P/B")!;
        b.LastEvaluated = true;
        RedDotService.MarkDirty(b);
        RedDotService.InternalFlushAll();

        Assert.Multiple(() =>
        {
            Assert.That(Contains(events, "P/B", RedDotChangeScope.Self, "P/B", true), Is.True);
            Assert.That(Contains(events, "P", RedDotChangeScope.Ancestor, "P/B", true), Is.True,
                "父级自身状态不变，也必须收到子级变化通知");
            Assert.That(events.Exists(e => e.Id == "P" && e.Scope == RedDotChangeScope.Self), Is.False,
                "父级状态未变化，不应发自身变更事件");
        });
    }

    [Test]
    public void Changed_event_notifies_descendants_when_parent_state_changes()
    {
        RedDotService.RegisterNode("P");
        RedDotService.RegisterNode("P/C", () => true);
        RedDotService.RegisterParent("P/C", "P");

        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);

        RedDotService.SetOverride("P", RedDotOverride.ForceInactive);
        RedDotService.InternalFlushAll();

        Assert.Multiple(() =>
        {
            Assert.That(Contains(events, "P", RedDotChangeScope.Self, "P", false), Is.True);
            Assert.That(Contains(events, "P/C", RedDotChangeScope.Descendant, "P", true), Is.True,
                "父级状态变化时，子级状态不变也要收到通知");
            Assert.That(events.Exists(e => e.Id == "P/C" && e.Scope == RedDotChangeScope.Self), Is.False);
        });
    }

    [Test]
    public void RegisterNode_publishes_when_an_already_watched_id_becomes_active()
    {
        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);

        RedDotService.RegisterNode("A", () => true);

        Assert.That(Contains(events, "A", RedDotChangeScope.Self, "A", true), Is.True,
            "watch 先于注册时，注册即激活必须发事件");
    }

    [Test]
    public void UnregisterNode_publishes_inactive_before_removal()
    {
        RedDotService.RegisterNode("A", () => true);
        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);

        RedDotService.UnregisterNode("A");

        Assert.That(events.Exists(e => e.Id == "A" && e.Scope == RedDotChangeScope.Self && !e.Active), Is.True,
            "删除仍激活的节点必须让 watcher 收到 inactive");
        Assert.That(RedDotService.IsActive("A"), Is.False);
    }

    [Test]
    public void UnregisterNode_tolerates_reentrant_and_throwing_unsubscribe()
    {
        var reentered = false;
        (Action subscribe, Action unsubscribe) reentrant = (() => { }, () =>
        {
            if (!reentered)
            {
                reentered = true;
                RedDotService.UnregisterNode("A");
            }
        }
        );
        RedDotService.RegisterNode("A", () => true, reentrant);
        Assert.DoesNotThrow(() => RedDotService.UnregisterNode("A"));
        Assert.That(RedDotService.InternalGetNode("A"), Is.Null);

        (Action subscribe, Action unsubscribe) throwing = (() => { }, () =>
            throw new InvalidOperationException("boom"));
        RedDotService.RegisterNode("A", () => true, throwing);
        Assert.DoesNotThrow(() => RedDotService.UnregisterNode("A"), "单个反订阅抛异常不得阻断清理");
        Assert.That(RedDotService.InternalGetNode("A"), Is.Null);
    }

    [Test]
    public void UnregisterNode_clears_pending_parent_requests()
    {
        RedDotService.RegisterParent("A", "P");
        RedDotService.RegisterNode("A", () => true);
        RedDotService.UnregisterNode("A");

        RedDotService.RegisterNode("A", () => true);
        RedDotService.RegisterNode("P");

        Assert.That(RedDotService.InternalGetNode("A")!.Parent, Is.Null,
            "已反注册节点的旧挂起父关系不得粘到同 id 新节点上");
    }

    [Test]
    public void Parent_unregister_keeps_child_reregistered_by_handler()
    {
        RedDotService.RegisterNode("P");
        RedDotService.RegisterNode("P/C", () => true);
        RedDotService.RegisterParent("P/C", "P");

        var listener = RedDotService.Events.On(RedDotEvents.Changed, (payload, _) =>
        {
            if (payload.Id == "P" && payload.Scope == RedDotChangeScope.Self && !payload.Active)
            {
                RedDotService.RegisterNode("P/C", () => true);
            }
        }, caller: null);

        try
        {
            RedDotService.UnregisterNode("P");

            var child = RedDotService.InternalGetNode("P/C");
            Assert.Multiple(() =>
            {
                Assert.That(child, Is.Not.Null, "handler 重注册的子节点不得被父级级联删除");
                Assert.That(child!.Parent, Is.Null, "父节点已删除，新子节点必须断开旧父链");
                Assert.That(RedDotService.IsActive("P/C"), Is.True);
            });
        }
        finally
        {
            listener.Off();
        }
    }

    [Test]
    public void Parent_unregister_rejects_new_child_links_during_broadcast()
    {
        RedDotService.RegisterNode("P");
        RedDotService.RegisterNode("C");

        var listener = RedDotService.Events.On(RedDotEvents.Changed, (payload, _) =>
        {
            if (payload.Id == "P" && payload.Scope == RedDotChangeScope.Self && !payload.Active)
            {
                RedDotService.RegisterParent("C", "P");
            }
        }, caller: null);

        try
        {
            RedDotService.UnregisterNode("P");

            Assert.That(RedDotService.InternalGetNode("C")!.Parent, Is.Null,
                "正在删除的父节点不得在广播期间接收新子节点");
        }
        finally
        {
            listener.Off();
        }
    }

    [Test]
    public void RegisterParent_before_parent_registers_links_when_parent_arrives()
    {
        RedDotService.RegisterNode("Menu/Codex", () => true);
        RedDotService.RegisterParent("Menu/Codex", "Menu");

        Assert.That(RedDotService.InternalGetNode("Menu/Codex")!.Parent, Is.Null);

        RedDotService.RegisterNode("Menu");

        Assert.That(RedDotService.InternalGetNode("Menu/Codex")!.Parent,
            Is.SameAs(RedDotService.InternalGetNode("Menu")));
        Assert.That(RedDotService.IsActive("Menu"), Is.True, "父节点后注册也必须补上聚合关系");
    }

    [Test]
    public void RegisterParent_before_both_nodes_survives_child_registering_first()
    {
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        RedDotService.RegisterNode("Menu/Codex", () => true);
        RedDotService.RegisterNode("Menu");

        Assert.That(RedDotService.InternalGetNode("Menu/Codex")!.Parent,
            Is.SameAs(RedDotService.InternalGetNode("Menu")));
        Assert.That(RedDotService.IsActive("Menu"), Is.True);
    }

    [Test]
    public void Later_explicit_reparent_wins_over_stale_pending_relation()
    {
        RedDotService.RegisterNode("badge", () => true);
        RedDotService.RegisterParent("badge", "menuTab");   // menuTab 未注册，进入挂起
        RedDotService.RegisterNode("toolbar");
        RedDotService.RegisterParent("badge", "toolbar");   // 显式改挂，应立即生效

        RedDotService.RegisterNode("menuTab");              // 旧挂起不得把 badge 抢回去

        Assert.Multiple(() =>
        {
            Assert.That(RedDotService.InternalGetNode("badge")!.Parent,
                Is.SameAs(RedDotService.InternalGetNode("toolbar")));
            Assert.That(RedDotService.InternalGetNode("toolbar")!.Children, Has.Member(RedDotService.InternalGetNode("badge")));
            Assert.That(RedDotService.InternalGetNode("menuTab")!.Children, Is.Empty);
        });
    }

    [Test]
    public void Configure_publishes_inactive_for_active_nodes()
    {
        RedDotService.RegisterNode("A", () => true);
        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);

        RedDotService.Configure();

        Assert.That(events.Exists(e => e.Id == "A" && e.Scope == RedDotChangeScope.Self && !e.Active), Is.True,
            "清空前必须让 watcher 收到 inactive");
        Assert.That(RedDotService.IsActive("A"), Is.False);
    }

    [Test]
    public void UnregisterNode_notifies_watching_parent_even_when_aggregate_unchanged()
    {
        RedDotService.RegisterNode("P");
        RedDotService.RegisterNode("P/C1", () => true);
        RedDotService.RegisterNode("P/C2", () => true);
        RedDotService.RegisterParent("P/C1", "P");
        RedDotService.RegisterParent("P/C2", "P");
        Assert.That(RedDotService.IsActive("P"), Is.True);

        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);

        RedDotService.UnregisterNode("P/C1");

        Assert.Multiple(() =>
        {
            Assert.That(Contains(events, "P/C1", RedDotChangeScope.Self, "P/C1", false), Is.True);
            Assert.That(Contains(events, "P", RedDotChangeScope.Ancestor, "P/C1", true), Is.True,
                "父级状态未变也必须收到子级被删除的关联通知");
            Assert.That(RedDotService.IsActive("P"), Is.True, "另一个子节点仍激活");
        });
    }

    [Test]
    public void Pending_parent_keeps_the_latest_explicit_request()
    {
        RedDotService.RegisterNode("badge", () => true);
        RedDotService.RegisterParent("badge", "menuTab");
        RedDotService.RegisterParent("badge", "toolbar");

        RedDotService.RegisterNode("toolbar");
        RedDotService.RegisterNode("menuTab");

        Assert.That(RedDotService.InternalGetNode("badge")!.Parent,
            Is.SameAs(RedDotService.InternalGetNode("toolbar")),
            "后一次显式请求必须覆盖更早的挂起父节点");
    }

    [Test]
    public void Configure_does_not_override_state_of_node_reregistered_by_unsubscribe()
    {
        (Action subscribe, Action unsubscribe) trigger = (() => { }, () =>
            RedDotService.RegisterNode("A", () => true));
        RedDotService.RegisterNode("A", () => true, trigger);

        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);

        RedDotService.Configure();

        Assert.Multiple(() =>
        {
            Assert.That(RedDotService.IsActive("A"), Is.True, "重注册的新节点必须保持 active");
            Assert.That(events.Exists(e => e.Id == "A" && e.Scope == RedDotChangeScope.Self && e.Active), Is.True);
            Assert.That(events.Exists(e => e.Id == "A" && e.Scope == RedDotChangeScope.Self && !e.Active), Is.False,
                "旧节点的 stale inactive 不得覆盖新节点");
        });
    }

    [Test]
    public void Configure_allows_handler_to_register_a_new_node()
    {
        RedDotService.RegisterNode("A", () => true);
        var listener = RedDotService.Events.On(RedDotEvents.Changed, (payload, _) =>
        {
            if (payload.Id == "A" && payload.Scope == RedDotChangeScope.Self && !payload.Active)
            {
                RedDotService.RegisterNode("B", () => true);
            }
        }, caller: null);

        try
        {
            RedDotService.Configure();
            Assert.That(RedDotService.IsActive("B"), Is.True, "Configure 广播期间注册的新节点不能被随后的 Clear 抹掉");
        }
        finally
        {
            listener.Off();
        }
    }

    [Test]
    public void Unregister_allows_handler_to_register_the_same_id()
    {
        RedDotService.RegisterNode("A", () => true);
        var listener = RedDotService.Events.On(RedDotEvents.Changed, (payload, _) =>
        {
            if (payload.Id == "A" && payload.Scope == RedDotChangeScope.Self && !payload.Active)
            {
                RedDotService.RegisterNode("A", () => true);
            }
        }, caller: null);

        try
        {
            RedDotService.UnregisterNode("A");
            Assert.That(RedDotService.IsActive("A"), Is.True, "反注册广播中重新注册的同 id 节点必须保留");
        }
        finally
        {
            listener.Off();
        }
    }

    [Test]
    public void UnregisterNode_cascade_does_not_reactivate_removing_parent()
    {
        RedDotService.RegisterNode("P");
        RedDotService.RegisterNode("P/C1", () => true);
        RedDotService.RegisterNode("P/C2", () => true);
        RedDotService.RegisterParent("P/C1", "P");
        RedDotService.RegisterParent("P/C2", "P");

        var events = new List<RedDotChangedPayload>();
        using var listener = Subscribe(events);

        RedDotService.UnregisterNode("P");

        Assert.Multiple(() =>
        {
            Assert.That(events.Exists(e => e.Id == "P" && e.Scope == RedDotChangeScope.Self && e.Active), Is.False,
                "正在级联删除的父节点不得被子节点摘除流程重新激活");
            Assert.That(RedDotService.IsActive("P"), Is.False);
        });
    }

    [Test]
    public void Reparenting_child_updates_both_old_and_new_parents()
    {
        RedDotService.RegisterNode("P1");
        RedDotService.RegisterNode("P2");
        RedDotService.RegisterNode("C", () => true);
        RedDotService.RegisterParent("C", "P1");
        Assert.That(RedDotService.IsActive("P1"), Is.True);

        RedDotService.RegisterParent("C", "P2");

        Assert.Multiple(() =>
        {
            Assert.That(RedDotService.IsActive("P2"), Is.True);
            Assert.That(RedDotService.IsActive("P1"), Is.False, "旧父节点失去唯一的激活子节点后必须重算");
            Assert.That(RedDotService.InternalGetNode("P1")!.Children, Is.Empty);
        });
    }

    [Test]
    public void RegisterParent_rejects_self_and_cycles()
    {
        RedDotService.RegisterNode("A", () => true);
        RedDotService.RegisterNode("B", () => true);

        RedDotService.RegisterParent("A", "A");
        RedDotService.RegisterParent("B", "A");   // 合法：B 是 A 的子节点
        RedDotService.RegisterParent("A", "B");   // 成环：A 不能再成为 B 的子节点

        Assert.That(RedDotService.InternalGetNode("A")!.Parent, Is.Null);
        Assert.That(RedDotService.InternalGetNode("B")!.Parent, Is.SameAs(RedDotService.InternalGetNode("A")));

        // 成环请求被拒后，UnregisterNode 仍能安全级联。
        Assert.DoesNotThrow(() => RedDotService.UnregisterNode("A"));
        Assert.DoesNotThrow(() => RedDotService.UnregisterNode("B"));
    }

    [Test]
    public void Configure_keeps_live_bus_listeners()
    {
        using var listener = Subscribe([]);
        Assert.That(RedDotService.Events.Has(RedDotEvents.Changed), Is.True);

        RedDotService.Configure();

        Assert.That(RedDotService.Events.Has(RedDotEvents.Changed), Is.True,
            "Configure 不得静默断开仍挂载的 RedDotCmp 等订阅方");
    }

    [Test]
    public void Nudge_re_evaluates_and_marks_dirty_when_no_override()
    {
        bool val = false;
        RedDotService.RegisterNode("B", () => val);
        Assert.That(RedDotService.IsActive("B"), Is.False);
        val = true;
        RedDotService.Nudge("B");
        RedDotService.InternalFlushAll();
        Assert.That(RedDotService.IsActive("B"), Is.True);
    }

    [Test]
    public void Nudge_skips_when_override_active()
    {
        RedDotService.RegisterNode("A", RedDotOverride.ForceInactive, () => true);
        RedDotService.InternalFlushAll();
        Assert.That(RedDotService.IsActive("A"), Is.False);
        RedDotService.Nudge("A");
        var node = RedDotService.InternalGetNode("A");
        Assert.That(node!.LastEvaluated, Is.True);
        RedDotService.InternalFlushAll();
        Assert.That(RedDotService.IsActive("A"), Is.False);
    }

    [Test]
    public void MarkDirty_triggers_deferred_flush()
    {
        bool val = false;
        RedDotService.RegisterNode("A", () => val);
        Assert.That(RedDotService.IsActive("A"), Is.False);

        val = true;
        var node = RedDotService.InternalGetNode("A");
        node!.LastEvaluated = true;
        RedDotService.MarkDirty(node);

        // 直接调 InternalFlushAll 验证脏标记 -> 评估的链（不依赖 CallDeferred 的帧延迟）
        RedDotService.InternalFlushAll();

        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void SetOverride_ForceActive_overrides_checkFunc()
    {
        RedDotService.RegisterNode("A", () => false);
        Assert.That(RedDotService.IsActive("A"), Is.False);
        RedDotService.SetOverride("A", RedDotOverride.ForceActive);
        RedDotService.InternalFlushAll();
        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void SetOverride_ForceInactive_overrides_checkFunc()
    {
        RedDotService.RegisterNode("A", () => true);
        Assert.That(RedDotService.IsActive("A"), Is.True);
        RedDotService.SetOverride("A", RedDotOverride.ForceInactive);
        RedDotService.InternalFlushAll();
        Assert.That(RedDotService.IsActive("A"), Is.False);
    }

    [Test]
    public void Force_override_still_caches_checkfunc_for_later_none()
    {
        RedDotService.RegisterNode("A", RedDotOverride.ForceInactive, () => true);
        Assert.That(RedDotService.IsActive("A"), Is.False);

        RedDotService.SetOverride("A", RedDotOverride.None);
        RedDotService.InternalFlushAll();

        Assert.That(RedDotService.IsActive("A"), Is.True, "Override 期间也必须执行并缓存 checkFunc");
    }

    [Test]
    public void SetOverride_back_to_None_uses_LastEvaluated()
    {
        RedDotService.RegisterNode("A", () => true);
        RedDotService.SetOverride("A", RedDotOverride.ForceInactive);
        RedDotService.InternalFlushAll();
        Assert.That(RedDotService.IsActive("A"), Is.False);
        RedDotService.SetOverride("A", RedDotOverride.None);
        RedDotService.InternalFlushAll();
        Assert.That(RedDotService.IsActive("A"), Is.True);
    }

    [Test]
    public void UnregisterNode_cascades_to_children()
    {
        RedDotService.RegisterNode("Parent");
        RedDotService.RegisterNode("Parent/Child1", () => true);
        RedDotService.RegisterNode("Parent/Child2", () => true);
        RedDotService.RegisterParent("Parent/Child1", "Parent");
        RedDotService.RegisterParent("Parent/Child2", "Parent");
        Assert.That(RedDotService.IsActive("Parent"), Is.True);

        RedDotService.UnregisterNode("Parent");
        Assert.That(RedDotService.IsActive("Parent"), Is.False);
        Assert.That(RedDotService.IsActive("Parent/Child1"), Is.False);
        Assert.That(RedDotService.IsActive("Parent/Child2"), Is.False);
    }

    [Test]
    public void UnregisterParent_only_disconnects_relationship()
    {
        RedDotService.RegisterNode("Parent", () => true);
        RedDotService.RegisterNode("Parent/Child", () => true);
        RedDotService.RegisterParent("Parent/Child", "Parent");
        Assert.That(RedDotService.IsActive("Parent"), Is.True);

        RedDotService.UnregisterParent("Parent/Child", "Parent");
        RedDotService.InternalFlushAll();

        Assert.That(RedDotService.IsActive("Parent"), Is.True);
        Assert.That(RedDotService.IsActive("Parent/Child"), Is.True);
    }

    [Test]
    public void SetOverride_same_value_noop()
    {
        RedDotService.RegisterNode("A", () => false);
        RedDotService.SetOverride("A", RedDotOverride.None);
        Assert.That(RedDotService.IsActive("A"), Is.False);
    }

    private static ListenerHandle Subscribe(List<RedDotChangedPayload> events)
    {
        var listener = RedDotService.Events.On(
            RedDotEvents.Changed,
            (payload, _) => events.Add(payload),
            caller: null);
        return new ListenerHandle(listener);
    }

    private static bool Contains(
        List<RedDotChangedPayload> events,
        string id,
        RedDotChangeScope scope,
        string sourceId,
        bool active)
        => events.Exists(e => e.Id == id && e.Scope == scope && e.SourceId == sourceId && e.Active == active);

    private sealed class ListenerHandle(IEventListener<RedDotChangedPayload> listener) : IDisposable
    {
        public void Dispose() => listener.Off();
    }
}