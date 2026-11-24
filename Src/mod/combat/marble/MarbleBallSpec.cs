namespace KemoCard.Mod.Combat.Marble;

/// <summary>
/// 一颗弹珠的静态属性：点数决定撞钉得分，半径决定碰撞体大小。
/// 待发队列按 FIFO 保存这些属性；场上活跃球持有同一份属性副本。
/// </summary>
public readonly record struct MarbleBallSpec
{
    public MarbleBallSpec(int points, float radius)
    {
        if (points < 0)
            throw new ArgumentOutOfRangeException(nameof(points), "弹珠点数不能为负。");
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), "弹珠半径必须为正有限值。");

        Points = points;
        Radius = radius;
    }

    public int Points { get; }

    public float Radius { get; }

    /// <summary>
    /// 是否为有效属性：构造函数保证点数非负、半径为正有限值；
    /// <c>default(MarbleBallSpec)</c> 的半径为零，不能进入队列或盘面。
    /// </summary>
    public bool IsValid => Points >= 0 && float.IsFinite(Radius) && Radius > 0f;
}
