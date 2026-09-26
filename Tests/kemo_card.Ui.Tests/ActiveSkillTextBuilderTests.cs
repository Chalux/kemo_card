using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 角色主动技的展示文案口径：名称 + 蓄力门槛（技能 N，按各档 <c>cooldown</c> 累计）+ 描述；
/// 角色详情、卡组编辑左栏与角色悬停摘要共用同一拼接口径。
/// </summary>
[TestFixture]
public sealed class ActiveSkillTextBuilderTests
{
    private static string Translate(string key) => key switch
    {
        "UI_CHARACTER_ACTIVE_SKILL_TITLE" => "主动技",
        "UI_CHARACTER_ACTIVE_SKILL_THRESHOLD" => "（技能 {0}）",
        "skill.glacial.name" => "冰川溢出",
        "skill.glacial.desc" => "自身 3 回合物理攻击 +6。",
        _ => key,
    };

    private static SkillDto Skill(string id) => new()
    {
        Id = id,
        DisplayNameId = $"{id}.name",
        DescId = $"{id}.desc",
    };

    [Test]
    public void Entry_formats_name_threshold_and_description()
    {
        Assert.That(
            ActiveSkillTextBuilder.Entry("冰川溢出", 8, "自身 3 回合物理攻击 +6。", Translate),
            Is.EqualTo("[b]冰川溢出[/b]（技能 8）\n自身 3 回合物理攻击 +6。"));
    }

    [Test]
    public void Entry_without_name_and_threshold_keeps_description_only()
    {
        Assert.That(
            ActiveSkillTextBuilder.Entry("", 0, "描述", Translate),
            Is.EqualTo("描述"));
    }

    [Test]
    public void Entry_without_description_keeps_header_only()
    {
        Assert.That(
            ActiveSkillTextBuilder.Entry("冰川溢出", 8, "", Translate),
            Is.EqualTo("[b]冰川溢出[/b]（技能 8）"));
    }

    [Test]
    public void Entries_accumulate_cooldowns_across_tiers()
    {
        var character = new CharacterDto
        {
            ActiveSkillChain =
            [
                new ActiveSkillChainEntryDto { SkillId = "s1", Cooldown = 6 },
                new ActiveSkillChainEntryDto { SkillId = "s2", Cooldown = 4 },
                new ActiveSkillChainEntryDto { SkillId = "s3", Cooldown = 6 },
            ],
        };

        var entries = ActiveSkillTextBuilder.Entries(character, Skill, Translate);

        Assert.That(entries, Has.Count.EqualTo(3));
        Assert.That(entries[0], Does.StartWith("[b]s1.name[/b]（技能 6）"));
        Assert.That(entries[1], Does.StartWith("[b]s2.name[/b]（技能 10）"), "第二档门槛 = 6 + 4");
        Assert.That(entries[2], Does.StartWith("[b]s3.name[/b]（技能 16）"), "第三档门槛 = 6 + 4 + 6");
    }

    [Test]
    public void Entries_skip_unresolved_tier_but_keep_accumulating_threshold()
    {
        var character = new CharacterDto
        {
            ActiveSkillChain =
            [
                new ActiveSkillChainEntryDto { SkillId = "missing", Cooldown = 5 },
                new ActiveSkillChainEntryDto { SkillId = "s2", Cooldown = 5 },
            ],
        };

        var entries = ActiveSkillTextBuilder.Entries(
            character,
            id => id == "s2" ? Skill(id) : null,
            Translate);

        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.That(entries[0], Does.StartWith("[b]s2.name[/b]（技能 10）"), "解析不到的档位仍累计门槛");
    }

    [Test]
    public void Entries_return_empty_for_empty_chain()
    {
        Assert.That(
            ActiveSkillTextBuilder.Entries(new CharacterDto(), _ => null, Translate),
            Is.Empty);
    }

    [Test]
    public void Block_includes_section_title_and_entries()
    {
        var character = new CharacterDto
        {
            ActiveSkillChain = [new ActiveSkillChainEntryDto { SkillId = "glacial", Cooldown = 8 }],
        };

        var block = ActiveSkillTextBuilder.Block(
            character,
            id => id == "glacial" ? Skill("skill.glacial") : null,
            Translate);

        Assert.That(
            block,
            Is.EqualTo("[b]主动技[/b]\n[b]冰川溢出[/b]（技能 8）\n自身 3 回合物理攻击 +6。"));
    }

    [Test]
    public void Block_is_empty_without_active_skills()
    {
        Assert.That(ActiveSkillTextBuilder.Block(new CharacterDto(), _ => null, Translate), Is.Empty);
    }

    [Test]
    public void Title_key_is_the_shared_section_key()
    {
        Assert.That(ActiveSkillTextBuilder.TitleKey, Is.EqualTo("UI_CHARACTER_ACTIVE_SKILL_TITLE"));
    }
}
