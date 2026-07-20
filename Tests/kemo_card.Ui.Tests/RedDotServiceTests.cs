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
}
