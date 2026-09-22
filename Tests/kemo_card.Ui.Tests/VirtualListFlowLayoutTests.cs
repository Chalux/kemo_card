using Godot;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 虚拟列表「流动（换行）」布局的纯计算：行容量、行数、内容尺寸、条目位置与可见窗口。
/// </summary>
/// <remarks>
/// 主轴 = 行内排列方向，横轴 = 换行方向且恒等于滚动轴：
/// <c>isVerticalScroll = true</c> → 从左到右排行、塞不下换行（竖向滚动）；
/// false → 从上到下排列、塞不下换列（横向滚动）。
/// </remarks>
[TestFixture]
public sealed class VirtualListFlowLayoutTests
{
    private const float CellMain = 160f;   // 卡片宽
    private const float CellCross = 208f;  // 卡片高
    private const float Spacing = 10f;     // 行内间距（= 行间距，除非另配）
    private const float LineSpacing = 10f;

    private static VirtualListFlowLayout.Metrics Compute(
        int itemCount,
        float lineLength,
        float spacing = Spacing,
        float lineSpacing = LineSpacing) =>
        VirtualListFlowLayout.Compute(itemCount, lineLength, CellMain, CellCross, spacing, lineSpacing);

    #region 行容量与行数

    [Test]
    public void Per_line_count_fits_only_whole_items()
    {
        // 2 张卡占 160+10+160 = 330；3 张要 500。
        Assert.That(Compute(10, lineLength: 340f).PerLine, Is.EqualTo(2));
        Assert.That(Compute(10, lineLength: 330f).PerLine, Is.EqualTo(2), "刚好放下两张");
        Assert.That(Compute(10, lineLength: 329f).PerLine, Is.EqualTo(1), "差 1px 就只放得下一张");
        Assert.That(Compute(10, lineLength: 900f).PerLine, Is.EqualTo(5), "(900+10)/170 = 5.35 → 5");
    }

    [Test]
    public void Line_count_ceils_and_content_size_has_no_trailing_gap()
    {
        var metrics = Compute(5, lineLength: 340f);

        Assert.That(metrics.PerLine, Is.EqualTo(2));
        Assert.That(metrics.LineCount, Is.EqualTo(3), "5 张 → 2+2+1 共 3 行");
        // 3 行 × (208+10) − 末尾 10 = 644
        Assert.That(metrics.ContentSize, Is.EqualTo(644f).Within(0.001f));
    }

    [Test]
    public void Single_item_content_size_equals_the_cell()
    {
        Assert.That(Compute(1, lineLength: 340f).ContentSize, Is.EqualTo(CellCross).Within(0.001f));
    }

    [Test]
    public void Empty_list_has_no_lines()
    {
        var metrics = Compute(0, lineLength: 340f);

        Assert.That(metrics.LineCount, Is.Zero);
        Assert.That(metrics.ContentSize, Is.Zero);
        Assert.That(metrics.PerLine, Is.EqualTo(1), "空列表也要给合法的行容量，避免后续除零");
    }

    /// <summary>宿主同帧调用 SetData 时可视区还没布局完（行长度为 0），此时必须收敛成每行 1 个。</summary>
    [Test]
    public void Zero_line_length_falls_back_to_one_per_line()
    {
        var metrics = Compute(3, lineLength: 0f);

        Assert.That(metrics.PerLine, Is.EqualTo(1));
        Assert.That(metrics.LineCount, Is.EqualTo(3));
        Assert.That(metrics.ContentSize, Is.EqualTo((3 * (CellCross + LineSpacing)) - LineSpacing).Within(0.001f));
    }

    [Test]
    public void Negative_spacing_and_line_spacing_are_tolerated()
    {
        var metrics = Compute(4, lineLength: 340f, spacing: -5f, lineSpacing: -5f);

        Assert.That(metrics.PerLine, Is.EqualTo(2), "负间距按 0 处理：(340+0)/160 = 2");
        Assert.That(metrics.ContentSize, Is.EqualTo(2 * CellCross).Within(0.001f));
    }

    #endregion

    #region 位置换算

    [Test]
    public void Vertical_scroll_wraps_rows_left_to_right()
    {
        var metrics = Compute(10, lineLength: 340f); // 每行 2

        Assert.That(
            VirtualListFlowLayout.Position(0, metrics, CellMain, CellCross, Spacing, LineSpacing, isVerticalScroll: true),
            Is.EqualTo(new Vector2(0f, 0f)));
        Assert.That(
            VirtualListFlowLayout.Position(1, metrics, CellMain, CellCross, Spacing, LineSpacing, isVerticalScroll: true),
            Is.EqualTo(new Vector2(170f, 0f)), "同一行第 2 个 → 沿 X 走一格");
        Assert.That(
            VirtualListFlowLayout.Position(2, metrics, CellMain, CellCross, Spacing, LineSpacing, isVerticalScroll: true),
            Is.EqualTo(new Vector2(0f, 218f)), "第 3 个换行 → X 归零、Y 走一格");
        Assert.That(
            VirtualListFlowLayout.Position(5, metrics, CellMain, CellCross, Spacing, LineSpacing, isVerticalScroll: true),
            Is.EqualTo(new Vector2(170f, 436f)));
    }

    [Test]
    public void Horizontal_scroll_wraps_columns_top_to_bottom()
    {
        var metrics = Compute(10, lineLength: 340f); // 每列 2

        // 横向滚动 = 转置：列内沿 Y 走、换列沿 X 走。
        Assert.That(
            VirtualListFlowLayout.Position(1, metrics, CellMain, CellCross, Spacing, LineSpacing, isVerticalScroll: false),
            Is.EqualTo(new Vector2(0f, 170f)));
        Assert.That(
            VirtualListFlowLayout.Position(2, metrics, CellMain, CellCross, Spacing, LineSpacing, isVerticalScroll: false),
            Is.EqualTo(new Vector2(218f, 0f)));
    }

    [Test]
    public void Position_of_a_negative_index_is_origin()
    {
        var metrics = Compute(4, lineLength: 340f);

        Assert.That(
            VirtualListFlowLayout.Position(-1, metrics, CellMain, CellCross, Spacing, LineSpacing, isVerticalScroll: true),
            Is.EqualTo(Vector2.Zero));
    }

    [Test]
    public void Line_of_maps_index_to_its_line()
    {
        var metrics = Compute(10, lineLength: 340f); // 每行 2

        Assert.That(VirtualListFlowLayout.LineOf(0, metrics), Is.Zero);
        Assert.That(VirtualListFlowLayout.LineOf(1, metrics), Is.Zero);
        Assert.That(VirtualListFlowLayout.LineOf(2, metrics), Is.EqualTo(1));
        Assert.That(VirtualListFlowLayout.LineOf(9, metrics), Is.EqualTo(4));
    }

    #endregion

    #region 可见窗口

    [Test]
    public void Visible_range_covers_the_whole_rows_of_the_scrolled_lines()
    {
        var metrics = Compute(10, lineLength: 340f); // 每行 2，共 5 行
        const float viewport = 500f;                 // ceil(500/218)+1 = 4 行

        var (first, count) = VirtualListFlowLayout.VisibleRange(
            10, metrics, scrollOffset: 0f, viewportSize: viewport, cellCross: CellCross, lineSpacing: LineSpacing);
        Assert.That(first, Is.Zero);
        Assert.That(count, Is.EqualTo(8), "4 行 × 每行 2 = 8 个条目（整行取，不能半行）");

        var (scrolledFirst, scrolledCount) = VirtualListFlowLayout.VisibleRange(
            10, metrics, scrollOffset: 436f, viewportSize: viewport, cellCross: CellCross, lineSpacing: LineSpacing);
        Assert.That(scrolledFirst, Is.EqualTo(4), "滚到第 3 行 → 索引从 2×2 起");
        Assert.That(scrolledCount, Is.EqualTo(6), "余下 3 行 × 2 = 6 个");
    }

    [Test]
    public void Visible_range_is_clamped_for_short_lists()
    {
        var metrics = Compute(3, lineLength: 340f);

        var (first, count) = VirtualListFlowLayout.VisibleRange(
            3, metrics, scrollOffset: 0f, viewportSize: 2000f, cellCross: CellCross, lineSpacing: LineSpacing);

        Assert.That(first, Is.Zero);
        Assert.That(count, Is.EqualTo(3), "可视区再大也不能超出条目总数");
    }

    [Test]
    public void Visible_range_ignores_out_of_range_scroll_offset()
    {
        var metrics = Compute(10, lineLength: 340f); // 5 行

        var (first, count) = VirtualListFlowLayout.VisibleRange(
            10, metrics, scrollOffset: 99999f, viewportSize: 100f, cellCross: CellCross, lineSpacing: LineSpacing);

        Assert.That(first, Is.EqualTo(8), "越界滚动钳到最后一行（索引 8 起）");
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public void Visible_range_of_an_empty_list_is_empty()
    {
        var metrics = Compute(0, lineLength: 340f);

        var (first, count) = VirtualListFlowLayout.VisibleRange(
            0, metrics, scrollOffset: 0f, viewportSize: 500f, cellCross: CellCross, lineSpacing: LineSpacing);

        Assert.That(first, Is.Zero);
        Assert.That(count, Is.Zero);
    }

    #endregion

    #region 步长

    [Test]
    public void Steps_guard_against_zero_sized_cells()
    {
        Assert.That(VirtualListFlowLayout.MainStep(0f, 0f), Is.EqualTo(1f), "尺寸为 0 时至少走 1px，避免除零/重叠");
        Assert.That(VirtualListFlowLayout.LineStep(208f, 10f), Is.EqualTo(218f));
        Assert.That(VirtualListFlowLayout.MainStep(160f, -3f), Is.EqualTo(160f));
    }

    #endregion
}
