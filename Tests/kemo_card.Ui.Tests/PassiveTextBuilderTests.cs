using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 角色被动的展示文案口径：门槛（潜能 N / 潜能 0）、可选的解锁状态、描述；
/// 角色详情与卡组编辑左栏共用同一拼接口径。
/// </summary>
[TestFixture]
public sealed class PassiveTextBuilderTests
{
    private static string Translate(string key) => key switch
    {
        "UI_CHARACTER_PASSIVE_THRESHOLD" => "潜能 {0}",
        "UI_CHARACTER_PASSIVE_THRESHOLD_ZERO" => "潜能 0",
        "UI_CHARACTER_PASSIVE_UNLOCKED" => "【已解锁】",
        "UI_CHARACTER_PASSIVE_LOCKED" => "【未解锁】",
        _ => key,
    };

    [Test]
    public void Entry_formats_threshold_and_description()
    {
        Assert.That(
            PassiveTextBuilder.Entry(10, true, "造成额外伤害。", Translate),
            Is.EqualTo("[b]潜能 10[/b]【已解锁】\n造成额外伤害。"));
    }

    [Test]
    public void Entry_uses_zero_threshold_text_for_default_unlocked_passives()
    {
        Assert.That(
            PassiveTextBuilder.Entry(0, false, "描述", Translate),
            Is.EqualTo("[b]潜能 0[/b]【未解锁】\n描述"),
            "门槛 0 走专用键（不带 {0} 占位符）");
    }

    [Test]
    public void Entry_omits_unlock_state_without_run_context()
    {
        Assert.That(
            PassiveTextBuilder.Entry(30, null, "描述", Translate),
            Is.EqualTo("[b]潜能 30[/b]\n描述"),
            "图鉴里还没进 Run 的角色不显示解锁状态");
    }

    [Test]
    public void Entry_keeps_threshold_line_when_description_is_missing()
    {
        Assert.That(
            PassiveTextBuilder.Entry(50, true, "", Translate),
            Is.EqualTo("[b]潜能 50[/b]【已解锁】\n"));
    }

    [Test]
    public void Title_key_is_the_shared_section_key()
    {
        Assert.That(PassiveTextBuilder.TitleKey, Is.EqualTo("UI_CHARACTER_PASSIVES_TITLE"));
    }
}