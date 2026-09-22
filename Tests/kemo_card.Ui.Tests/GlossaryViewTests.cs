using KemoCard.Mod.Global.Glossary;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 词典列表的装配：搜索过滤（标题或正文命中）、空分组不出标题、按 id 定位（正文链接跳转）
/// 与"找不到就回落第一个词条"。翻译由界面注入，这里用键本身当文案。
/// </summary>
[TestFixture]
public sealed class GlossaryViewTests
{
    private static readonly IReadOnlyList<GlossarySection> Sections =
    [
        new("UI_GLOSSARY_SECTION_KEYWORDS",
        [
            new GlossaryEntry("charge", "KW_CHARGE_TITLE", "KW_CHARGE_DESC"),
            new GlossaryEntry("orb", "KW_ORB_TITLE", "KW_ORB_DESC"),
        ]),
        new("UI_GLOSSARY_SECTION_ORBS",
        [
            new GlossaryEntry("elemental", "orb.elemental.name", "UI_GLOSSARY_ORB_BODY_ELEMENTAL", [6f, 100f]),
        ]),
    ];

    private static IReadOnlyList<GlossaryRow> Build(string filter) =>
        GlossaryView.BuildRows(Sections, filter, entry => entry.TitleKey, entry => entry.BodyKey);

    /// <summary>过滤后命中的词条 id（不含分组标题行）。</summary>
    private static string?[] Entries(string filter) =>
        [.. Build(filter).Where(row => !row.IsHeader).Select(row => row.EntryId)];

    [Test]
    public void Rows_contain_section_headers_before_their_entries()
    {
        var rows = Build("");

        Assert.That(rows.Select(row => row.IsHeader), Is.EqualTo(new[] { true, false, false, true, false }));
        Assert.That(rows[0].TitleKey, Is.EqualTo("UI_GLOSSARY_SECTION_KEYWORDS"));
        Assert.That(rows[0].EntryId, Is.Null, "分组标题行不是词条");
        Assert.That(rows.Skip(1).Take(2).Select(row => row.EntryId), Is.EqualTo(new[] { "charge", "orb" }));
        Assert.That(rows[3].TitleKey, Is.EqualTo("UI_GLOSSARY_SECTION_ORBS"));
        Assert.That(rows[4].EntryId, Is.EqualTo("elemental"));
    }

    [Test]
    public void Filter_matches_title_or_body_case_insensitively()
    {
        Assert.That(Entries("orb"), Is.EqualTo(new[] { "orb", "elemental" }),
            "标题命中（KW_ORB_TITLE 里含 orb）");
        Assert.That(Entries("KW_CHARGE_DESC"), Is.EqualTo(new[] { "charge" }), "正文命中");
        Assert.That(Entries("cHaRgE"), Is.EqualTo(new[] { "charge" }), "大小写无关");
        Assert.That(Entries("elemental"), Is.EqualTo(new[] { "elemental" }));
    }

    [Test]
    public void Empty_sections_do_not_emit_their_header()
    {
        var rows = Build("charge");

        Assert.That(rows, Has.Count.EqualTo(2), "只剩机制词条分组的标题 + 命中项");
        Assert.That(rows[0].IsHeader, Is.True);
        Assert.That(rows[1].EntryId, Is.EqualTo("charge"));
        Assert.That(rows.Any(row => row.TitleKey == "UI_GLOSSARY_SECTION_ORBS"), Is.False,
            "整组被过滤掉时不能留下空标题");
    }

    [Test]
    public void No_match_yields_no_rows()
    {
        Assert.That(Build("不存在的词"), Is.Empty);
    }

    [Test]
    public void Selection_prefers_the_requested_entry_then_falls_back_to_the_first()
    {
        var rows = Build("");

        Assert.That(GlossaryView.ResolveSelection(rows, null), Is.EqualTo(1), "默认选第一个词条行（跳过标题）");
        Assert.That(GlossaryView.ResolveSelection(rows, "elemental"), Is.EqualTo(4));
        Assert.That(GlossaryView.ResolveSelection(rows, "missing"), Is.EqualTo(1), "目标不在列表里时回落第一个词条");
        Assert.That(GlossaryView.ResolveSelection([], null), Is.EqualTo(-1), "没有词条时无可选项");
        Assert.That(
            GlossaryView.ResolveSelection([new GlossaryRow(true, "SECTION", null)], "x"),
            Is.EqualTo(-1),
            "只有标题行时同样无可选项");
    }

    [Test]
    public void Find_row_only_matches_entries()
    {
        var rows = Build("");

        Assert.That(GlossaryView.FindRow(rows, "orb"), Is.EqualTo(2));
        Assert.That(GlossaryView.FindRow(rows, "UI_GLOSSARY_SECTION_KEYWORDS"), Is.EqualTo(-1), "标题行不参与定位");
        Assert.That(GlossaryView.FindRow(rows, ""), Is.EqualTo(-1));
    }
}
