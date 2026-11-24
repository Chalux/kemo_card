namespace KemoCard.Mod.Combat.Marble;

/// <summary>
/// 撞钉的静态定义（内容 / 固定布局提供）。首版四枚固定钉只读；
/// 后续膨胀、增钉与合并在运行时扩展盘面时仍以本类型作为来源。
/// </summary>
public sealed record MarblePegDefinition
{
    public MarblePegDefinition(string id, MarbleVec position, float radius, int points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!position.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(position), "撞钉坐标必须为有限值。");
        if (!float.IsFinite(radius) || radius <= 0f)
            throw new ArgumentOutOfRangeException(nameof(radius), "撞钉半径必须为正有限值。");
        if (points < 0)
            throw new ArgumentOutOfRangeException(nameof(points), "撞钉点数不能为负。");

        Id = id;
        Position = position;
        Radius = radius;
        Points = points;
    }

    public string Id { get; }

    public MarbleVec Position { get; }

    public float Radius { get; }

    /// <summary>撞钉点数：每次有效接触增加 <c>弹珠点数 × 本值</c>。</summary>
    public int Points { get; }
}
