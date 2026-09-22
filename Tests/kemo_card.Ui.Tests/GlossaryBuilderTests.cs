using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Glossary;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 词典装配（<see cref="GlossaryBuilder"/>）与词条表枚举：分组、排序、文案键、形式参数与回退。
/// </summary>
/// <remarks>
/// 词典条目只带翻译键（不带成品文案），所以这里断言的是"键与参数是否正确"，
/// 键是否存在由 <see cref="LocaleIntegrityTests"/> 一类的守卫负责。
/// </remarks>
[TestFixture]
public sealed class GlossaryBuilderTests
{
    [Test]
    public void Keyword_catalog_keeps_registration_order()
    {
        var catalog = new KeywordCatalog();
        catalog.Register(new KeywordEntry("b", "KW_B_TITLE", "KW_B_DESC"));
        catalog.Register(new KeywordEntry("a", "KW_A_TITLE", "KW_A_DESC"));

        Assert.That(catalog.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { "b", "a" }),
            "词典按机制引入顺序展示，而不是字典序");

        // 覆盖注册保持首次出现的位置，且条目内容被替换。
        catalog.Register(new KeywordEntry("b", "KW_B2_TITLE", "KW_B2_DESC"));
        Assert.That(catalog.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { "b", "a" }));
        Assert.That(catalog.Entries[0].TitleKey, Is.EqualTo("KW_B2_TITLE"));

        catalog.Unregister("b");
        Assert.That(catalog.Entries.Select(entry => entry.Id), Is.EqualTo(new[] { "a" }));

        catalog.Clear();
        Assert.That(catalog.Entries, Is.Empty);
    }

    [Test]
    public void Builtin_keywords_include_the_mechanics_a_player_needs_to_look_up()
    {
        var catalog = new KeywordCatalog();
        BuiltinKeywords.RegisterAll(catalog);

        var ids = catalog.Entries.Select(entry => entry.Id).ToList();

        Assert.That(ids, Does.Contain("charge"), "充能");
        Assert.That(ids, Does.Contain("orb"), "充能球");
        Assert.That(ids, Does.Contain("orb_element"), "属性球");
        Assert.That(ids, Does.Contain("normal_attack"), "普通攻击");
        Assert.That(ids, Does.Contain("team_potential"), "团体潜能");
        Assert.That(ids, Does.Contain("character_passive"), "角色被动");
        Assert.That(ids, Does.Contain("shared_hp"), "共享血量");
        Assert.That(ids, Does.Contain("chain"), "连携");

        foreach (var entry in catalog.Entries)
        {
            Assert.That(entry.TitleKey, Is.Not.Empty, entry.Id);
            Assert.That(entry.DescKey, Is.Not.Empty, entry.Id);
        }
    }

    [Test]
    public void Build_groups_keywords_and_orbs_with_expected_order()
    {
        var store = new GameDefinitionStore();
        store.OrbTypesMutable["yellow"] = Orb("yellow", EDamageKind.Elemental, EElement.Yellow, 6f, 1f);
        store.OrbTypesMutable["blue"] = Orb("blue", EDamageKind.Elemental, EElement.Blue, 6f, 1f);
        store.OrbTypesMutable["physical"] = Orb("physical", EDamageKind.Physical, EElement.None, 5f, 0.5f);

        var catalog = new KeywordCatalog();
        catalog.Register(new KeywordEntry("charge", "KW_CHARGE_TITLE", "KW_CHARGE_DESC"));

        var sections = GlossaryBuilder.Build(store, catalog);

        Assert.That(sections, Has.Count.EqualTo(2));
        Assert.That(sections[0].TitleKey, Is.EqualTo(GlossaryBuilder.KeywordsSectionKey));
        Assert.That(sections[0].Entries.Select(entry => entry.Id), Is.EqualTo(new[] { "charge" }));

        Assert.That(sections[1].TitleKey, Is.EqualTo(GlossaryBuilder.OrbsSectionKey));
        Assert.That(
            sections[1].Entries.Select(entry => entry.Id),
            Is.EqualTo(new[] { "blue", "physical", "yellow" }),
            "充能球按内容 id 字典序，展示顺序稳定");
    }

    [Test]
    public void Orb_entries_pick_body_key_by_damage_kind_and_format_args()
    {
        var store = new GameDefinitionStore();
        store.OrbTypesMutable["elemental"] = Orb("elemental", EDamageKind.Elemental, EElement.Blue, 6f, 1f);
        store.OrbTypesMutable["physical"] = Orb("physical", EDamageKind.Physical, EElement.None, 4f, 0.25f);
        store.OrbTypesMutable["magical"] = Orb("magical", EDamageKind.Magical, EElement.None, 3f, 0.5f);
        store.OrbTypesMutable["support"] = new OrbTypeDto
        {
            Id = "support",
            DisplayNameId = "orb.support.name",
            DealsDamage = false,
        };

        var sections = GlossaryBuilder.Build(store, new KeywordCatalog());
        Assert.That(sections[0].Entries, Is.Empty, "词条表为空时该分组没有条目");
        var orbSection = sections[1].Entries.ToDictionary(entry => entry.Id, entry => entry);

        Assert.That(orbSection["elemental"].BodyKey, Is.EqualTo(GlossaryBuilder.OrbBodyElementalKey));
        Assert.That(orbSection["physical"].BodyKey, Is.EqualTo(GlossaryBuilder.OrbBodyPhysicalKey));
        Assert.That(orbSection["magical"].BodyKey, Is.EqualTo(GlossaryBuilder.OrbBodyMagicalKey));
        Assert.That(orbSection["support"].BodyKey, Is.EqualTo(GlossaryBuilder.OrbBodySupportKey));

        Assert.That(orbSection["elemental"].Args, Is.EqualTo(new object[] { 6f, 100f }), "每球固定值 + 攻击加成百分比");
        Assert.That(orbSection["physical"].Args, Is.EqualTo(new object[] { 4f, 25f }));
        Assert.That(orbSection["support"].Args, Is.Null, "纯效果球正文没有形式参数");
    }

    [Test]
    public void Orb_without_display_name_falls_back_to_its_id()
    {
        var store = new GameDefinitionStore();
        store.OrbTypesMutable["odd"] = new OrbTypeDto { Id = "odd", PerOrbAmount = 1f, AttackBonusScale = 0f };
        store.OrbTypesMutable["named"] = Orb("named", EDamageKind.Elemental, EElement.Red, 1f, 0f);

        var section = GlossaryBuilder.Build(store, new KeywordCatalog())[1];

        Assert.That(section.Entries.Single(entry => entry.Id == "odd").TitleKey, Is.EqualTo("odd"),
            "没有显示名时直接用 id 作为键（Tr 回落即显示 id）");
        Assert.That(section.Entries.Single(entry => entry.Id == "named").TitleKey, Is.EqualTo("orb.named.name"));
    }

    [Test]
    public void Build_without_orbs_only_returns_the_keyword_section()
    {
        var sections = GlossaryBuilder.Build(new GameDefinitionStore(), new KeywordCatalog());

        Assert.That(sections, Has.Count.EqualTo(1));
        Assert.That(sections[0].Entries, Is.Empty);
    }

    private static OrbTypeDto Orb(string id, EDamageKind kind, EElement element, float perOrb, float scale) => new()
    {
        Id = id,
        DisplayNameId = $"orb.{id}.name",
        DealsDamage = true,
        DamageKind = kind,
        Element = element,
        PerOrbAmount = perOrb,
        AttackBonusScale = scale,
    };
}
