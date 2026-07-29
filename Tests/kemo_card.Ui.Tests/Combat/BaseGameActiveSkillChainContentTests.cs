using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 针对 **出货内容** 的冒烟测试:其余用例都用合成 DTO,真实的
/// <c>Config/mods/base-game</c> 内容此前没有任何覆盖,主动链配置写错不会被发现。
/// </summary>
[TestFixture]
public sealed class BaseGameActiveSkillChainContentTests
{
    private static ModDefinitionsBundle LoadBaseGame()
    {
        var modFolder = LocateBaseGameFolder();
        var manifestPath = Path.Combine(modFolder, "mod.json");
        Assert.That(File.Exists(manifestPath), Is.True, $"找不到 base-game 清单:{manifestPath}");

        var manifest = System.Text.Json.JsonSerializer.Deserialize<ContentModManifestDto>(
            File.ReadAllText(manifestPath),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.That(manifest, Is.Not.Null);

        return ContentModLoader.Load(new DiscoveredModEntry(modFolder, manifest!)).Definitions;
    }

    private static string LocateBaseGameFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Config", "mods", "base-game");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("从测试输出目录向上找不到 Config/mods/base-game。");
    }

    [Test]
    public void Every_shipped_character_has_a_valid_active_skill_chain()
    {
        var definitions = LoadBaseGame();

        Assert.That(definitions.Characters, Is.Not.Empty, "base-game 至少应有一个角色");
        foreach (var (characterId, character) in definitions.Characters)
        {
            Assert.That(
                CombatContentValidator.TryValidateActiveSkillChain(character, out var error),
                Is.True,
                $"角色 {characterId} 的 activeSkillChain 非法:{error}");
        }
    }

    [Test]
    public void Every_active_skill_chain_tier_resolves_to_a_shipped_skill()
    {
        var definitions = LoadBaseGame();

        foreach (var (characterId, character) in definitions.Characters)
        {
            foreach (var tier in character.ActiveSkillChain)
            {
                Assert.That(
                    definitions.Skills.ContainsKey(tier.SkillId),
                    Is.True,
                    $"角色 {characterId} 的档位技能 {tier.SkillId} 在内容中不存在");
            }
        }
    }

    /// <summary>
    /// 档位技能若缺 <c>targetOverride</c>,按 §5.4 决议会被当成 Self 单体,
    /// 伤害类主动技会打到施法者自己身上——这正是 kemo_dash 迁移时踩过的坑。
    /// </summary>
    [Test]
    public void Every_active_skill_chain_tier_declares_an_explicit_target_override()
    {
        var definitions = LoadBaseGame();

        foreach (var (characterId, character) in definitions.Characters)
        {
            foreach (var tier in character.ActiveSkillChain)
            {
                Assert.That(definitions.Skills.TryGetValue(tier.SkillId, out var skill), Is.True);
                Assert.That(
                    skill!.TargetOverride,
                    Is.Not.Null,
                    $"角色 {characterId} 的档位技能 {tier.SkillId} 未声明 targetOverride");
            }
        }
    }

    [Test]
    public void Kemo_chain_is_ordered_low_to_high_with_a_charged_tier()
    {
        var definitions = LoadBaseGame();

        Assert.That(definitions.Characters.TryGetValue("kemo", out var kemo), Is.True);
        Assert.That(kemo!.ActiveSkillChain, Has.Count.EqualTo(2), "kemo 应有基础档 + 蓄力档");
        Assert.That(kemo.ActiveSkillChain[0].SkillId, Is.EqualTo("kemo_dash"));
        Assert.That(kemo.ActiveSkillChain[1].SkillId, Is.EqualTo("kemo_dash_charged"));
        Assert.That(
            kemo.ActiveSkillChain.Select(tier => tier.Cooldown),
            Is.All.GreaterThanOrEqualTo(1),
            "每档 cooldown 必须 ≥ 1,否则累计阈值不单调递增");
    }
}