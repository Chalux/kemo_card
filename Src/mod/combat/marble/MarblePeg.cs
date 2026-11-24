namespace KemoCard.Mod.Combat.Marble;

/// <summary>盘面上的一枚撞钉（运行时只读视图；合并 / 膨胀留给后续批次）。</summary>
public sealed class MarblePeg
{
    internal MarblePeg(MarblePegDefinition definition, int spawnOrder)
    {
        Id = definition.Id;
        Position = definition.Position;
        Radius = definition.Radius;
        Points = definition.Points;
        SpawnOrder = spawnOrder;
    }

    public string Id { get; }

    public MarbleVec Position { get; }

    public float Radius { get; }

    public int Points { get; }

    /// <summary>生成序号：后续合并平手时按它取主钉。</summary>
    public int SpawnOrder { get; }
}
