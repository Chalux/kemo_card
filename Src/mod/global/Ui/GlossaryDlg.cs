using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global.Glossary;
using KemoCard.Mod.Global.Ui.Themes;

namespace KemoCard.Mod.Global.Ui;

/// <summary>
/// 词典：把「机制词条」（<c>KeywordCatalog</c>）与内容侧玩法元素（充能球类型）列出来供玩家查阅。
/// </summary>
/// <remarks>
/// <para>内容装配在 <see cref="GlossaryBuilder"/>（纯函数、可单测）；本类只做渲染：
/// 左侧按分组列出条目（分组标题行不可选），右侧显示标题 + 正文，搜索框同时匹配标题与正文。</para>
/// <para>正文里的 <c>[url=kw:&lt;id&gt;]</c> 链接与卡面描述同一套格式（<see cref="CardDescBuilder.TryParseKeywordMeta"/>），
/// 点击后跳到词典内对应条目；条目被当前搜索过滤掉时会先清空搜索再定位。</para>
/// </remarks>
public partial class GlossaryDlg : BaseDlg
{
    [Export] private Label? _lblTitle;
    [Export] private LineEdit? _iptFilter;
    [Export] private ItemList? _listEntries;
    [Export] private RichTextLabel? _rtDetail;
    [Export] private Label? _lblEmpty;

    /// <summary>当前展示行（含分组标题行），下标与 <see cref="_listEntries"/> 的条目一一对应。</summary>
    private readonly List<GlossaryRow> _rows = [];

    private IReadOnlyList<GlossarySection> _sections = [];

    public override string UIId => GlobalUiIds.Glossary;
    public override string UIDir => "Src/mod/global/Ui";

    protected override void InitEvent()
    {
        if (_lblTitle != null)
        {
            _lblTitle.Text = Localization.Tr("UI_GLOSSARY_TITLE");
        }

        if (_iptFilter != null)
        {
            _iptFilter.PlaceholderText = Localization.Tr("UI_GLOSSARY_FILTER");
            Binder.OnTextChanged(_iptFilter, _ => RebuildRows());
        }

        if (_listEntries != null)
        {
            Binder.OnItemSelected(_listEntries, OnEntrySelected);
        }

        if (_rtDetail != null)
        {
            var detail = _rtDetail;
            Binder.Bind(() => detail.MetaClicked += OnDetailMetaClicked, () => detail.MetaClicked -= OnDetailMetaClicked);
        }
    }

    protected override void OnOpen()
    {
        _sections = GlossaryBuilder.Build(AppRoot.Services.ContentModPipeline.Registry.Store);
        if (_rtDetail != null)
        {
            _rtDetail.Text = "";
        }

        RebuildRows();
    }

    /// <summary>词典是静态内容，没有需要跟随状态刷新的字段。</summary>
    protected override void UpdateView()
    {
    }

    #region 列表构建

    /// <summary>
    /// 按当前搜索词重建列表；<paramref name="preferredId"/> 指定优先选中的词条 id
    /// （正文链接跳转用；为 null 时选中第一个词条）。行的装配规则见 <see cref="GlossaryView"/>。
    /// </summary>
    private void RebuildRows(string? preferredId = null)
    {
        _rows.Clear();
        if (_listEntries is null)
        {
            return;
        }

        _listEntries.Clear();
        var filter = _iptFilter?.Text ?? "";
        _rows.AddRange(GlossaryView.BuildRows(_sections, filter, ResolveTitle, ResolveBody));

        foreach (var row in _rows)
        {
            var index = _listEntries.ItemCount;
            _listEntries.AddItem(row.IsHeader ? Localization.Tr(row.TitleKey) : ResolveTitleOf(row));
            if (row.IsHeader)
            {
                // 分组标题：不可选、用次要色区分（ItemList 没有内建分组行）。
                _listEntries.SetItemSelectable(index, false);
                _listEntries.SetItemCustomFgColor(index, KemoPalette.TextSecondary);
            }
        }

        if (_lblEmpty != null)
        {
            _lblEmpty.Visible = _rows.Count == 0;
        }

        SelectRow(GlossaryView.ResolveSelection(_rows, preferredId));
    }

    /// <summary>行标题：词条行取词条自己的标题键（分组行的标题键由调用点直接翻译）。</summary>
    private string ResolveTitleOf(GlossaryRow row) =>
        FindEntry(row.EntryId) is { } entry ? ResolveTitle(entry) : Localization.Tr(row.TitleKey);

    private GlossaryEntry? FindEntry(string? entryId)
    {
        if (string.IsNullOrEmpty(entryId))
        {
            return null;
        }

        foreach (var section in _sections)
        {
            foreach (var entry in section.Entries)
            {
                if (string.Equals(entry.Id, entryId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }
        }

        return null;
    }

    private void SelectRow(int index)
    {
        if (_listEntries is null || index < 0 || index >= _rows.Count)
        {
            ShowRow(-1);
            return;
        }

        _listEntries.Select(index);
        ShowRow(index);
    }

    private void OnEntrySelected(long index) => ShowRow((int)index);

    private void ShowRow(int index)
    {
        if (_rtDetail is null)
        {
            return;
        }

        if (index < 0 || index >= _rows.Count || FindEntry(_rows[index].EntryId) is not { } entry)
        {
            _rtDetail.Text = "";
            return;
        }

        _rtDetail.Text = $"[b]{ResolveTitle(entry)}[/b]\n\n{ResolveBody(entry)}";
    }

    #endregion

    #region 跳转与渲染

    private void OnDetailMetaClicked(Variant meta)
    {
        if (!CardDescBuilder.TryParseKeywordMeta(meta.AsString(), out var keywordId))
        {
            return;
        }

        if (GlossaryView.FindRow(_rows, keywordId) < 0 && _iptFilter is not null && !string.IsNullOrEmpty(_iptFilter.Text))
        {
            // 目标被搜索词挡住了：清掉搜索（TextChanged 会重建一次），下面再按 id 定位一次。
            _iptFilter.Text = "";
        }

        RebuildRows(preferredId: keywordId);
    }

    private static string ResolveTitle(GlossaryEntry entry) => Localization.Tr(entry.TitleKey);

    /// <summary>正文：带形式参数时做一次格式化（键查不到时 <c>Tr</c> 会原样返回键）。</summary>
    private static string ResolveBody(GlossaryEntry entry) =>
        entry.Args is { Count: > 0 } args
            ? string.Format(Localization.Tr(entry.BodyKey), args.ToArray())
            : Localization.Tr(entry.BodyKey);

    #endregion
}
