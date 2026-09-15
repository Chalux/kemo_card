using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class HostRngTests
{
    [Test]
    public void NextInt_is_deterministic_for_same_seed()
    {
        var a = new HostRng(12345, "effect:fx_a");
        var b = new HostRng(12345, "effect:fx_a");
        Assert.That(a.NextInt(0, 100), Is.EqualTo(b.NextInt(0, 100)));
        Assert.That(a.NextInt(0, 100), Is.EqualTo(b.NextInt(0, 100)));
    }

    /// <summary>
    /// 硬编码期望值，钉住种子派生跨进程稳定。
    /// 若实现回退到 <c>HashCode.Combine(runSeed, streamKey)</c> 或 <c>string.GetHashCode()</c>，
    /// .NET Core 的字符串哈希按进程随机化，本断言必然失败——这正是「同 RunSeed 重启后不可复现」的回归防线。
    /// </summary>
    [Test]
    public void DeriveSeed_is_stable_across_processes()
    {
        Assert.That(HostRng.DeriveSeed(12345, "effect:fx_a"), Is.EqualTo(-810681193));
        Assert.That(HostRng.DeriveSeed(7, "combat.ai"), Is.EqualTo(-1977715186));
        Assert.That(HostRng.DeriveSeed(1, "reward.p0"), Is.EqualTo(1608665087));
    }

    [Test]
    public void DeriveSeed_separates_run_seed_and_stream_key()
    {
        Assert.That(
            HostRng.DeriveSeed(42, "combat"),
            Is.Not.EqualTo(HostRng.DeriveSeed(43, "combat")),
            "不同 RunSeed 必须派生不同流");
        Assert.That(
            HostRng.DeriveSeed(42, "combat.ai"),
            Is.Not.EqualTo(HostRng.DeriveSeed(42, "combat.draw")),
            "同一 RunSeed 的不同 streamKey 必须派生出独立流");
    }

    [Test]
    public void DeriveSeed_matches_independent_fnv1a_reference()
    {
        // 独立参考实现（FNV-1a 64，取低 32 位），避免「实现与测试同源」的假绿。
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        static int Reference(int runSeed, string streamKey)
        {
            var hash = offsetBasis;
            for (var i = 0; i < sizeof(int); i++)
            {
                hash = unchecked((hash ^ (byte)(runSeed >> (i * 8))) * prime);
            }

            foreach (var b in System.Text.Encoding.UTF8.GetBytes(streamKey))
            {
                hash = unchecked((hash ^ b) * prime);
            }

            return unchecked((int)hash);
        }

        foreach (var streamKey in new[] { "combat", "combat.ai", "reward.p0", "story_select", "" })
        {
            foreach (var seed in new[] { 0, 1, 42, int.MaxValue, -1 })
            {
                Assert.That(
                    HostRng.DeriveSeed(seed, streamKey),
                    Is.EqualTo(Reference(seed, streamKey)),
                    $"seed={seed} streamKey='{streamKey}'");
            }
        }
    }

    [Test]
    public void Constructor_rejects_null_stream_key()
    {
        Assert.Throws<ArgumentNullException>(() => new HostRng(1, null!));
    }
}