using System;
using Godot;

namespace KemoCard.Mod.Global.Ui;

/// <summary>
/// 虚拟列表「流动（换行）」布局的纯计算：给定条目尺寸、行长度与条目数，算出每行容量、行数、
/// 内容总尺寸、条目位置与可见条目窗口。刻意不接触场景树，便于直接单测。
/// </summary>
/// <remarks>
/// <para>主轴 = 行内排列方向，横轴 = 换行方向，且横轴<b>恒等于滚动轴</b>：
/// <paramref name="isVerticalScroll"/> 为 true → 条目从左到右排满一行后换行（竖向滚动）；
/// false → 从上到下排满一列后换列（横向滚动）。"塞不下"＝
/// <c>已用主轴长度 + 间距 + 条目主轴尺寸 &gt; 行长度</c>，故每行容量为
/// <c>floor((lineLength + spacing) / (cellMain + spacing))</c>（至少 1 个）。</para>
/// <para>本类只做取整与位置换算；节点渲染、对象池与测量仍由 <c>VirtualList</c> 负责。</para>
/// </remarks>
internal static class VirtualListFlowLayout
{
    /// <summary>流动布局度量：每行（列）容量、行（列）数、滚动轴上的内容总尺寸。</summary>
    internal readonly record struct Metrics(int PerLine, int LineCount, float ContentSize);

    /// <summary>同一条线（行 / 列）内相邻条目的步长。</summary>
    internal static float MainStep(float cellMain, float spacing) =>
        MathF.Max(1f, cellMain) + MathF.Max(0f, spacing);

    /// <summary>相邻两条线（行 / 列）的步长。</summary>
    internal static float LineStep(float cellCross, float lineSpacing) =>
        MathF.Max(1f, cellCross) + MathF.Max(0f, lineSpacing);

    /// <summary>
    /// 计算流动布局度量。<paramref name="lineLength"/> ≤ 0（可视区尚未布局完成）时按每行 1 个处理，
    /// 否则会算出"一行放不下任何东西"的 0 容量。
    /// </summary>
    internal static Metrics Compute(
        int itemCount,
        float lineLength,
        float cellMain,
        float cellCross,
        float spacing,
        float lineSpacing)
    {
        if (itemCount <= 0)
        {
            return new Metrics(1, 0, 0f);
        }

        var perLine = 1;
        if (lineLength > 0f)
        {
            perLine = Math.Max(
                1,
                (int)MathF.Floor((lineLength + MathF.Max(0f, spacing)) / MainStep(cellMain, spacing)));
        }

        var lineCount = (itemCount + perLine - 1) / perLine;
        // 末尾不留间距：行距只在"行与行之间"生效。
        var contentSize = (lineCount * LineStep(cellCross, lineSpacing)) - MathF.Max(0f, lineSpacing);
        return new Metrics(perLine, lineCount, MathF.Max(0f, contentSize));
    }

    /// <summary>
    /// 第 <paramref name="index"/> 个条目相对内容容器左上角的位置。
    /// <paramref name="isVerticalScroll"/> 为 true 时沿 X 排行、换行累加 Y；否则转置。
    /// </summary>
    internal static Vector2 Position(
        int index,
        Metrics metrics,
        float cellMain,
        float cellCross,
        float spacing,
        float lineSpacing,
        bool isVerticalScroll)
    {
        if (index < 0 || metrics.PerLine <= 0)
        {
            return Vector2.Zero;
        }

        var line = index / metrics.PerLine;
        var column = index % metrics.PerLine;
        var along = column * MainStep(cellMain, spacing);
        var across = line * LineStep(cellCross, lineSpacing);
        return isVerticalScroll ? new Vector2(along, across) : new Vector2(across, along);
    }

    /// <summary>
    /// 可见条目窗口（连续索引区间）：按滚动位置定当前线，再多取一条线缓冲
    /// （与线性布局的 <c>ceil(viewport / step) + 1</c> 同思路，避免滚动时露白）。
    /// </summary>
    internal static (int FirstIndex, int Count) VisibleRange(
        int itemCount,
        Metrics metrics,
        float scrollOffset,
        float viewportSize,
        float cellCross,
        float lineSpacing)
    {
        if (itemCount <= 0 || metrics.PerLine <= 0 || metrics.LineCount <= 0)
        {
            return (0, 0);
        }

        var lineStep = LineStep(cellCross, lineSpacing);
        var firstLine = (int)MathF.Floor(MathF.Max(0f, scrollOffset) / lineStep);
        firstLine = Math.Clamp(firstLine, 0, metrics.LineCount - 1);

        var visibleLines = (int)MathF.Ceiling(MathF.Max(0f, viewportSize) / lineStep) + 1;
        var firstIndex = firstLine * metrics.PerLine;
        var lastIndex = Math.Min(itemCount - 1, ((firstLine + visibleLines) * metrics.PerLine) - 1);
        if (lastIndex < firstIndex)
        {
            return (0, 0);
        }

        return (firstIndex, lastIndex - firstIndex + 1);
    }

    /// <summary>包含第 <paramref name="index"/> 个条目的线序号（滚动定位用）。</summary>
    internal static int LineOf(int index, Metrics metrics) =>
        metrics.PerLine > 0 ? Math.Max(0, index) / metrics.PerLine : 0;
}
