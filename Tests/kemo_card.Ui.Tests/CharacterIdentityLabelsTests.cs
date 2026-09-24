using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 角色身份显示名（元素 / 定位 / 种族）的拼接口径：角色详情身份行、队伍编辑信息行与角色悬停摘要
/// 共用同一套实现，这里的期望值同时被 <see cref="CharacterSummaryBuilderTests"/> 从另一个入口验证。
/// </summary>
[TestFixture]
public sealed class CharacterIdentityLabelsTests
{
    /// <summary>
    /// 假翻译表按**字面键**映射：键名写错时 <c>Tr</c> 会原样返回键，断言里的中文就不会出现——
    /// 因此本表同时充当「键名没写错」的守卫（键是否写进 CSV 由 <see cref="LocaleIntegrityTests"/> 负责）。
    /// </summary>
    private static string Tr(string key) => key switch
    {
        "UI_CHARACTER_META_FIELD" => "{0}：{1}",
        "UI_CHARACTER_META_ELEMENT" => "元素",
        "UI_CHARACTER_META_ROLE" => "定位",
        "UI_CHARACTER_META_RACE" => "种族",
        "UI_ELEMENT_RED" => "红",
        "UI_ELEMENT_BLUE" => "蓝",
        "UI_ELEMENT_YELLOW" => "黄",
        "UI_ROLE_NONE" => "无",
        "UI_ROLE_WARRIOR" => "战士",
        "UI_ROLE_ELEMENTIST" => "元素师",
        "UI_RACE_ANIMAL" => "动物",
        "UI_RACE_DRAGON" => "龙族",
        "UI_RACE_HUMAN" => "人族",
        _ => key,
    };

    [Test]
    public void MetaLine_lists_element_role_and_race_with_field_labels()
    {
        var character = new CharacterDto
        {
            Id = "chalux",
            Element = EElement.Blue,
            Role = ERole.Warrior,
            Race = ERace.Animal | ERace.Dragon,
        };

        Assert.That(
            CharacterIdentityLabels.MetaLine(character, Tr),
            Is.EqualTo("元素：蓝 · 定位：战士 · 种族：动物、龙族"));
    }

    [Test]
    public void MetaLine_keeps_flag_order_of_enum_not_definition_order()
    {
        var character = new CharacterDto
        {
            Element = EElement.Yellow | EElement.Red,
            Role = ERole.Elementist,
            Race = ERace.Dragon | ERace.Human,
        };

        // 多标志按枚举声明顺序输出（红 → 黄、人族 → 龙族），与内容 JSON 里的书写顺序无关。
        Assert.That(
            CharacterIdentityLabels.MetaLine(character, Tr),
            Is.EqualTo("元素：红、黄 · 定位：元素师 · 种族：人族、龙族"));
    }

    [Test]
    public void MetaLine_omits_fields_without_a_value()
    {
        // ERole.None / EElement.None / ERace.None 都表示"这个角色没有该字段"：
        // 整段省略，而不是显示「定位：无」或留一个空冒号。
        var character = new CharacterDto { Id = "blank" };

        Assert.That(CharacterIdentityLabels.MetaLine(character, Tr), Is.Empty);

        var elementOnly = new CharacterDto { Id = "red", Element = EElement.Red };
        Assert.That(CharacterIdentityLabels.MetaLine(elementOnly, Tr), Is.EqualTo("元素：红"));
    }

    [Test]
    public void Element_joins_multiple_flags_and_skips_none()
    {
        Assert.That(CharacterIdentityLabels.Element(EElement.None, Tr), Is.Empty);
        Assert.That(CharacterIdentityLabels.Element(EElement.Blue, Tr), Is.EqualTo("蓝"));
        Assert.That(
            CharacterIdentityLabels.Element(EElement.Red | EElement.Blue, Tr),
            Is.EqualTo("红、蓝"));
    }

    [Test]
    public void Race_skips_flags_without_a_locale_key()
    {
        // 未登记翻译键的种族位不进文案——否则界面会露出枚举名（如 "Unknown" 拼在中文里）。
        Assert.That(CharacterIdentityLabels.Race((ERace)(1 << 20), Tr), Is.Empty);
        Assert.That(CharacterIdentityLabels.Race(ERace.Animal, Tr), Is.EqualTo("动物"));
    }

    [Test]
    public void Role_keeps_None_as_a_visible_value_for_the_team_editor()
    {
        // 与元素 / 种族不同：队伍编辑预览的信息行是「值 / 值 / 值」，
        // 角色无定位时必须仍显示「无」（UI_ROLE_NONE），由调用方决定是否整段省略。
        Assert.That(CharacterIdentityLabels.Role(ERole.None, Tr), Is.EqualTo("无"));
        Assert.That(CharacterIdentityLabels.Role((ERole)999, Tr), Is.EqualTo("999"), "缺键时才回落枚举原名");
    }
}