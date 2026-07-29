using KemoCard.Frame.Content;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Reward;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunRewardDistributorTests
{
    [Test]
    public void GenerateRewards_creates_equal_option_count_per_slot()
    {
        var distributor = new RunRewardDistributor();
        var states = new PlayerRunState?[] { new(), new(), null, null };
        var registry = new GameDefinitionRegistry();
        var rng = new KemoCard.Frame.Scripting.HostRng(42, "test");

        var result = distributor.GenerateRewards(states, registry, ringIndex: 1, optionCount: 3, rng);

        Assert.That(result.Rewards.Count, Is.EqualTo(2));
        foreach (var (_, options) in result.Rewards)
        {
            Assert.That(options.Options, Has.Length.EqualTo(3));
        }
    }

    [Test]
    public void GenerateRewards_skips_null_slots()
    {
        var distributor = new RunRewardDistributor();
        var states = new PlayerRunState?[] { new(), null, new(), null };
        var registry = new GameDefinitionRegistry();
        var rng = new KemoCard.Frame.Scripting.HostRng(99, "test");

        var result = distributor.GenerateRewards(states, registry, ringIndex: 2, optionCount: 2, rng);

        Assert.That(result.Rewards.Count, Is.EqualTo(2));
        Assert.That(result.Rewards.ContainsKey(0));
        Assert.That(result.Rewards.ContainsKey(2));
    }
}