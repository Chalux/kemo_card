using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run.Ui.CombatUi;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 战斗界面主动技悬停：每一档都列出、只有当前可释放的档加【可用】并高亮；
/// 技能进度后缀按「满档优先于可用」给出 最大 / 可用。
/// </summary>
[TestFixture]
public sealed class CombatActiveSkillTipsTests
{
    private static string Tr(string key) => key switch
    {
        "UI_CHARACTER_ACTIVE_SKILL_THRESHOLD" => "（技能 {0}）",
        "UI_COMBAT_ACTIVE_SKILL_READY" => "【可用】",
        "UI_COMBAT_ACTIVE_SKILL_USABLE" => "主动技能可用",
        "UI_COMBAT_ACTIVE_SKILL_MAX" => "主动技能最大",
        "UI_COMBAT_SKILL_COUNTER" => "技能 {0} / {1}",
        _ => key,
    };

    private static SkillDto Skill(string id) => new()
    {
        Id = id,
        DisplayNameId = $"{id}.name",
        DescId = $"{id}.desc",
    };

    /// <summary>按各档 cooldown 建角色（技能 id = skill.t1..n；Tr 未覆盖的键回落键名）。</summary>
    private static CharacterBattleInstance Character(params int[] cooldowns) =>
        CharacterBattleInstance.CreateForTests(
            "test",
            new Dictionary<string, float>(StringComparer.Ordinal),
            activeSkillChain:
            [
                .. cooldowns.Select((cooldown, index) => new ActiveSkillChainEntryDto
                {
                    SkillId = $"skill.t{index + 1}",
                    Cooldown = cooldown,
                }),
            ]);

    [Test]
    public void Progress_suffix_is_null_before_the_first_tier()
    {
        var character = Character(6, 4);
        character.GainSkillCounter(5);

        Assert.That(CombatActiveSkillTips.ResolveProgressSuffix(character, Tr), Is.Null);
    }

    [Test]
    public void Progress_suffix_reports_usable_then_max()
    {
        var character = Character(6, 4);

        character.GainSkillCounter(6);
        Assert.That(
            CombatActiveSkillTips.ResolveProgressSuffix(character, Tr),
            Is.EqualTo("主动技能可用"),
            "第一档达标但未满档 → 可用");

        character.GainSkillCounter(4); // S = 10 = Cap
        Assert.That(
            CombatActiveSkillTips.ResolveProgressSuffix(character, Tr),
            Is.EqualTo("主动技能最大"),
            "满档优先于可用");
    }

    [Test]
    public void Progress_suffix_for_single_tier_reports_max_at_cap()
    {
        var character = Character(8);
        character.GainSkillCounter(8);

        Assert.That(
            CombatActiveSkillTips.ResolveProgressSuffix(character, Tr),
            Is.EqualTo("主动技能最大"),
            "单档角色可用即满档：只提示最大");
    }

    [Test]
    public void Progress_suffix_is_null_without_active_chain()
    {
        Assert.That(CombatActiveSkillTips.ResolveProgressSuffix(Character(), Tr), Is.Null);
    }

    [Test]
    public void BuildTip_lists_every_tier_and_highlights_the_castable_one()
    {
        var character = Character(6, 4, 6);
        character.GainSkillCounter(10); // 6 / 10 / 16 → 第二档达标

        var tip = CombatActiveSkillTips.BuildTip(character, Skill, Tr, "f5eedd");

        Assert.That(tip, Does.StartWith("技能 10 / 16\n\n"), "首行是当前进度");
        Assert.That(tip, Does.Contain("[font_size=16]skill.t1.name[/font_size]（技能 6）\nskill.t1.desc"), "未达标档照常列出（不上色）");
        Assert.That(
            tip,
            Does.Contain("[color=#f5eedd][font_size=16]skill.t2.name[/font_size]（技能 10）【可用】\nskill.t2.desc[/color]"),
            "当前可释放档加【可用】并整体高亮");
        Assert.That(tip, Does.Contain("[font_size=16]skill.t3.name[/font_size]（技能 16）\nskill.t3.desc"), "更高档照常列出");
        Assert.That(tip, Does.Not.Contain("[color=#f5eedd][font_size=16]skill.t1"), "只有可释放档上色");
    }

    [Test]
    public void BuildTip_is_empty_without_active_chain()
    {
        Assert.That(CombatActiveSkillTips.BuildTip(Character(), Skill, Tr), Is.Empty);
    }

    [Test]
    public void CanRelease_tracks_the_castable_tier()
    {
        var character = Character(6, 4);
        Assert.That(CombatActiveSkillTips.CanRelease(character), Is.False, "S 未达第一档门槛");

        character.GainSkillCounter(6);
        Assert.That(CombatActiveSkillTips.CanRelease(character), Is.True);

        Assert.That(CombatActiveSkillTips.CanRelease(Character()), Is.False, "无主动链恒 false");
    }
}
