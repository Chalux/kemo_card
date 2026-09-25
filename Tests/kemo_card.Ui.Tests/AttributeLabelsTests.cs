using KemoCard.Frame.Gas;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 属性显示名与卡组属性加成文案的唯一拼接口径：<c>attr.&lt;snake_case&gt;.name</c> 取键、
/// 缺键回落 id、展示顺序（核心属性在前）、带符号的加成文案。
/// </summary>
[TestFixture]
public sealed class AttributeLabelsTests
{
    private const string MaxHealth = "MaxHealth";
    private const string PhysicalAttack = "PhysicalAttack";

    /// <summary>内容侧翻译：只认识两个测试键，其余按 <c>Localization.Tr</c> 的约定回显键名。</summary>
    private static string Translate(string key) => key switch
    {
        "attr.max_health.name" => "最大生命",
        "attr.physical_attack.name" => "物攻",
        _ => key,
    };

    [Test]
    public void Name_uses_snake_case_key_and_falls_back_to_id()
    {
        Assert.That(AttributeLabels.Name(MaxHealth, Translate), Is.EqualTo("最大生命"));
        Assert.That(AttributeLabels.Name("NormalAttackCount", Translate), Is.EqualTo("NormalAttackCount"),
            "缺键时回落原始 id，而不是露出原始键名");
    }

    [Test]
    public void Order_puts_core_attributes_first_and_unknown_last()
    {
        var values = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["UnknownAttr"] = 1f,
            [PhysicalAttack] = 2f,
            [MaxHealth] = 20f,
            [AttributeIds.MagicAttack] = 1f,
        };

        var ordered = AttributeLabels.Order(values).Select(pair => pair.Key).ToArray();

        Assert.That(ordered, Is.EqualTo(new[] { MaxHealth, PhysicalAttack, AttributeIds.MagicAttack, "UnknownAttr" }),
            "核心面板属性按 最大生命 → 物攻 → … 的顺序，未知属性排在最后");
    }

    [Test]
    public void FormatContributions_signs_values_and_joins_with_separator()
    {
        var values = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            [PhysicalAttack] = 2f,
            [MaxHealth] = 20f,
        };

        var text = AttributeLabels.FormatContributions(values, Translate);

        Assert.That(text, Is.EqualTo("最大生命 +20 · 物攻 +2"),
            "正数补 +，展示顺序与 Order 一致");
    }

    [Test]
    public void FormatContributions_handles_negative_and_empty_values()
    {
        Assert.That(
            AttributeLabels.FormatContributions(new Dictionary<string, float>(StringComparer.Ordinal)
            {
                [PhysicalAttack] = -1f,
            }, Translate),
            Is.EqualTo("物攻 -1"));

        Assert.That(
            AttributeLabels.FormatContributions(new Dictionary<string, float>(StringComparer.Ordinal), Translate),
            Is.Empty);
    }
}