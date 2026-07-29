using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentModActivationPlannerTests
{
    [Test]
    public void Plan_skips_mod_when_required_not_enabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
        ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.CreateModFolder(root, "ext", "ext.mod", required: new[] { "missing.dep" });

        var discovery = new ContentModDiscovery();
        var scan = discovery.Scan(root);
        var planner = new ContentModActivationPlanner();
        var result = planner.Plan(scan.ValidMods, new[] { "ext.mod" }, scan.SkippedMods);

        Assert.That(result.OrderedActiveMods, Is.Empty);
        Assert.That(result.SkippedMods, Has.Some.Matches<ModSkipEntry>(e =>
            e.ModId == "ext.mod" && e.Reason == ModSkipReason.MissingRequiredDependency));
    }

    [Test]
    public void Plan_orders_by_dependency_then_loadOrder()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
        ContentModTestHelper.CreateModFolder(root, "base", "base.game", loadOrder: 0);
        ContentModTestHelper.CreateModFolder(root, "addon", "addon.mod", loadOrder: 10, required: new[] { "base.game" });

        var discovery = new ContentModDiscovery();
        var scan = discovery.Scan(root);
        var planner = new ContentModActivationPlanner();
        var result = planner.Plan(scan.ValidMods, new[] { "addon.mod", "base.game" }, scan.SkippedMods);

        Assert.That(result.OrderedActiveMods.Select(static m => m.Manifest.ModId), Is.EqualTo(new[] { "base.game", "addon.mod" }));
    }

    [Test]
    public void Plan_skips_all_mods_in_cycle()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
        ContentModTestHelper.CreateModFolder(root, "a", "mod.a", required: new[] { "mod.b" });
        ContentModTestHelper.CreateModFolder(root, "b", "mod.b", required: new[] { "mod.a" });

        var discovery = new ContentModDiscovery();
        var scan = discovery.Scan(root);
        var planner = new ContentModActivationPlanner();
        var result = planner.Plan(scan.ValidMods, new[] { "mod.a", "mod.b" }, scan.SkippedMods);

        Assert.That(result.OrderedActiveMods, Is.Empty);
        Assert.That(result.SkippedMods, Has.Some.Matches<ModSkipEntry>(e => e.Reason == ModSkipReason.CyclicDependency));
    }
}