using KemoCard.Frame.Content;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Reward;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunRewardDistributorTests
{
    /// <summary>
    /// 金币奖励的显示名翻译键；必须与 <c>Resource/Locale/strings.csv</c> 中的行一致。
    /// </summary>
    private const string GoldRewardKey = "UI_REWARD_GOLD_NAME";

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

    [Test]
    public void GenerateRewards_options_carry_translation_key_and_positive_amount()
    {
        var distributor = new RunRewardDistributor();
        var states = new PlayerRunState?[] { new() };
        var registry = new GameDefinitionRegistry();
        var rng = new HostRng(42, "test");

        var result = distributor.GenerateRewards(states, registry, ringIndex: 0, optionCount: 4, rng);

        var options = result.Rewards[0].Options;
        Assert.That(options, Has.Length.EqualTo(4));
        foreach (var option in options)
        {
            Assert.That(option.Type, Is.EqualTo(ERewardType.Gold), "当前生成器只产出金币奖励");
            Assert.That(
                option.DisplayNameKey,
                Is.EqualTo(GoldRewardKey),
                "选项必须携带翻译键，不得预格式化可见文案");
            Assert.That(
                option.DisplayNameKey,
                Does.StartWith("UI_"),
                "面向用户文案一律走 UI_ 前缀的翻译键");
            Assert.That(option.Amount, Is.GreaterThan(0), "金币数量必须为正数，供 UI 填入翻译键占位符");
        }
    }

    /// <summary>
    /// 翻译键必须真的能在 strings.csv 里查到（键写错等于没有本地化），
    /// 并保留 <c>{0}</c> 占位符供 UI 用 <c>string.Format</c> 填入 <c>Amount</c>。
    /// </summary>
    [Test]
    public void Gold_reward_key_is_declared_in_strings_csv_with_placeholder()
    {
        var csvPath = LocateRepoFile(Path.Combine("Resource", "Locale", "strings.csv"));
        var lines = File.ReadAllLines(csvPath);

        Assert.That(
            lines[0].TrimStart('\uFEFF'),
            Is.EqualTo("keys,zh_CN,en"),
            "strings.csv 列布局变化时本用例需同步");

        var row = lines.FirstOrDefault(
            line => line.StartsWith(GoldRewardKey + ",", StringComparison.Ordinal));
        Assert.That(row, Is.Not.Null, $"{GoldRewardKey} 必须写入 Resource/Locale/strings.csv");

        var cells = row!.Split(',');
        Assert.That(cells, Has.Length.EqualTo(3), "每行须含 keys/zh_CN/en 三列");
        Assert.That(cells[1], Is.Not.Empty, "缺少 zh_CN 文案");
        Assert.That(cells[2], Is.Not.Empty, "缺少 en 文案");
        Assert.That(cells[1], Does.Contain("{0}"), "zh_CN 文案须保留 {0} 占位符");
        Assert.That(cells[2], Does.Contain("{0}"), "en 文案须保留 {0} 占位符");
    }

    [Test]
    public void GenerateRewards_different_ring_index_yields_different_options()
    {
        var distributor = new RunRewardDistributor();
        var registry = new GameDefinitionRegistry();

        var ring1 = distributor.GenerateRewards(
            new PlayerRunState?[] { new() }, registry, ringIndex: 1, optionCount: 6,
            new HostRng(42, "test"));
        var ring2 = distributor.GenerateRewards(
            new PlayerRunState?[] { new() }, registry, ringIndex: 2, optionCount: 6,
            new HostRng(42, "test"));

        Assert.That(
            Describe(ring2.Rewards[0]),
            Is.Not.EqualTo(Describe(ring1.Rewards[0])),
            "ringIndex 必须参与随机流派生，否则该参数等同于被丢弃");
    }

    [Test]
    public void GenerateRewards_same_inputs_are_deterministic()
    {
        var distributor = new RunRewardDistributor();
        var registry = new GameDefinitionRegistry();

        var first = distributor.GenerateRewards(
            new PlayerRunState?[] { new(), new() }, registry, ringIndex: 3, optionCount: 3,
            new HostRng(2026, "test"));
        var second = distributor.GenerateRewards(
            new PlayerRunState?[] { new(), new() }, registry, ringIndex: 3, optionCount: 3,
            new HostRng(2026, "test"));

        Assert.That(Describe(second.Rewards[0]), Is.EqualTo(Describe(first.Rewards[0])));
        Assert.That(Describe(second.Rewards[1]), Is.EqualTo(Describe(first.Rewards[1])));
    }

    [Test]
    public void GenerateRewards_different_slots_yield_different_options()
    {
        var distributor = new RunRewardDistributor();
        var registry = new GameDefinitionRegistry();

        var result = distributor.GenerateRewards(
            new PlayerRunState?[] { new(), new() }, registry, ringIndex: 0, optionCount: 6,
            new HostRng(7, "test"));

        Assert.That(
            Describe(result.Rewards[1]),
            Is.Not.EqualTo(Describe(result.Rewards[0])),
            "不同槽位应派生独立随机流");
    }

    [Test]
    public void ApplyReward_gold_adds_amount_to_player_gold()
    {
        var distributor = new RunRewardDistributor();
        var player = new PlayerRunState();
        player.SetGold(7);
        var options = new RewardOptionsDto
        {
            Options =
            [
                new RewardOptionDto
                {
                    Type = ERewardType.Gold,
                    DisplayNameKey = GoldRewardKey,
                    Amount = 25,
                },
            ],
        };

        distributor.ApplyReward(player, options, 0);

        Assert.That(player.Gold, Is.EqualTo(32), "选中金币奖励必须真正入账");
    }

    /// <summary>
    /// 端到端：<c>GenerateRewards</c> 产出的 <c>Amount</c> 必须等于 <c>ApplyReward</c> 实际入账的数量。
    /// </summary>
    [Test]
    public void ApplyReward_gold_on_generated_option_adds_exactly_its_amount()
    {
        var distributor = new RunRewardDistributor();
        var player = new PlayerRunState();
        var registry = new GameDefinitionRegistry();
        var generated = distributor.GenerateRewards(
            new PlayerRunState?[] { player }, registry, ringIndex: 1, optionCount: 3,
            new HostRng(42, "test"));
        var options = generated.Rewards[0];
        var amount = options.Options[1].Amount;

        distributor.ApplyReward(player, options, 1);

        Assert.That(player.Gold, Is.EqualTo(amount));
    }

    [TestCase(ERewardType.Character)]
    [TestCase(ERewardType.Card)]
    [TestCase(ERewardType.Modifier)]
    [TestCase(ERewardType.Heal)]
    public void ApplyReward_throws_for_unimplemented_kind(ERewardType type)
    {
        var distributor = new RunRewardDistributor();
        var player = new PlayerRunState();
        player.SetGold(5);
        var options = new RewardOptionsDto
        {
            Options =
            [
                new RewardOptionDto
                {
                    Type = type,
                    DisplayNameKey = "UI_TEST_REWARD",
                    Amount = 1,
                },
            ],
        };

        var ex = Assert.Throws<NotSupportedException>(() => distributor.ApplyReward(player, options, 0));

        Assert.That(ex!.Message, Does.Contain(type.ToString()), "异常必须点名未实现的奖励类型");
        Assert.That(player.Gold, Is.EqualTo(5), "拒绝执行时不得改动玩家状态");
    }

    [Test]
    public void ApplyReward_throws_when_index_out_of_range()
    {
        var distributor = new RunRewardDistributor();
        var player = new PlayerRunState();
        var options = new RewardOptionsDto
        {
            Options =
            [
                new RewardOptionDto { Type = ERewardType.Gold, DisplayNameKey = GoldRewardKey, Amount = 3 },
            ],
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => distributor.ApplyReward(player, options, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => distributor.ApplyReward(player, options, 1));
        Assert.That(player.Gold, Is.EqualTo(0));
    }

    /// <summary>
    /// 把一组选项压成可比较的字符串，用于断言序列差异 / 可复现性。
    /// </summary>
    private static string Describe(RewardOptionsDto options)
    {
        return string.Join(
            "|",
            options.Options.Select(option => $"{option.Type}:{option.DisplayNameKey}:{option.Amount}"));
    }

    /// <summary>
    /// 从测试输出目录向上找到仓库内文件（与 <c>BaseGameActiveSkillChainContentTests</c> 同约定）。
    /// </summary>
    private static string LocateRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"从测试输出目录向上找不到 {relativePath}。");
    }
}