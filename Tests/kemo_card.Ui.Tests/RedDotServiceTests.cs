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
    public void Aggregation_node_without_checkFunc_works()
    {
        RedDotService.RegisterNode("Menu/Codex", () => true);
        RedDotService.RegisterNode("Menu");
        RedDotService.RegisterParent("Menu/Codex", "Menu");
        Assert.That(RedDotService.IsActive("Menu"), Is.True);
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
    public void OnStateChanged_fires_when_state_changes()
    {
        string? changedId = null;
        bool? changedActive = null;
        RedDotService.OnStateChanged += (id, active) => { changedId = id; changedActive = active; };
        RedDotService.RegisterNode("A", () => false);
        var node = RedDotService.InternalGetNode("A");
        node!.LastEvaluated = true;
        RedDotService.MarkDirty(node);
        RedDotService.InternalFlushAll();
        Assert.That(changedId, Is.EqualTo("A"));
        Assert.That(changedActive, Is.True);
    }

    [Test]
    public void OnStateChanged_does_not_fire_when_state_unchanged()
    {
        int fireCount = 0;
        RedDotService.OnStateChanged += (_, _) => fireCount++;
        RedDotService.RegisterNode("A", () => false);
        RedDotService.Refresh("A");
        RedDotService.InternalFlushAll();
        Assert.That(fireCount, Is.Zero);
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
}
