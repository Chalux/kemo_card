using KemoCard.Mod.Global.Glossary;

namespace KemoCard.Mod.Global.Ui;

/// <summary>词典列表的一行：分组标题行（<see cref="EntryId"/> 为 null）或词条行。</summary>
public sealed record GlossaryRow(bool IsHeader, string TitleKey, string? EntryId);

/// <summary>
/// 词典列表的装配（纯函数）：按搜索词过滤分组、生成"分组标题 + 词条"的扁平行序列，并给出应选中的行。
/// </summary>
/// <remarks>
/// 从界面里抽出来是为了可测：过滤命中（标题或正文）、空分组的标题不出现、按 id 定位（正文里的
/// <c>[url=kw:*]</c> 跳转）与"找不到时回落第一个词条"这几条规则都能被 NUnit 直接覆盖，
/// 界面（<c>GlossaryDlg</c>）只剩把行渲染到 ItemList。
/// </remarks>
public static class GlossaryView
{
    /// <summary>
    /// 生成展示行。<paramref name="titleOf"/> / <paramref name="bodyOf"/> 由界面注入（翻译在这里发生，
    /// 因此本类不需要 Godot）。
    /// </summary>
    public static IReadOnlyList<GlossaryRow> BuildRows(
        IReadOnlyList<GlossarySection> sections,
        string filter,
        Func<GlossaryEntry, string> titleOf,
        Func<GlossaryEntry, string> bodyOf)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(titleOf);
        ArgumentNullException.ThrowIfNull(bodyOf);

        var rows = new List<GlossaryRow>();
        var keyword = filter?.Trim() ?? "";

        foreach (var section in sections)
        {
            var matched = section.Entries
                .Where(entry => Matches(entry, keyword, titleOf, bodyOf))
                .ToList();
            if (matched.Count == 0)
            {
                // 整个分组都被过滤掉时不显示标题（否则会出现空标题）。
                continue;
            }

            rows.Add(new GlossaryRow(IsHeader: true, section.TitleKey, EntryId: null));
            foreach (var entry in matched)
            {
                rows.Add(new GlossaryRow(IsHeader: false, entry.TitleKey, entry.Id));
            }
        }

        return rows;
    }

    /// <summary>按词条 id 找行下标；找不到返回 -1（被搜索过滤掉或不存在）。</summary>
    public static int FindRow(IReadOnlyList<GlossaryRow> rows, string entryId)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (string.IsNullOrEmpty(entryId))
        {
            return -1;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (string.Equals(rows[i].EntryId, entryId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 决定要选中的行：优先 <paramref name="preferredId"/>（跳转目标），否则第一个词条行；
    /// 没有任何词条时返回 -1。
    /// </summary>
    public static int ResolveSelection(IReadOnlyList<GlossaryRow> rows, string? preferredId)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (!string.IsNullOrEmpty(preferredId))
        {
            var preferred = FindRow(rows, preferredId!);
            if (preferred >= 0)
            {
                return preferred;
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].IsHeader)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Matches(
        GlossaryEntry entry,
        string filter,
        Func<GlossaryEntry, string> titleOf,
        Func<GlossaryEntry, string> bodyOf)
    {
        if (filter.Length == 0)
        {
            return true;
        }

        return titleOf(entry).Contains(filter, StringComparison.OrdinalIgnoreCase) ||
            bodyOf(entry).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
