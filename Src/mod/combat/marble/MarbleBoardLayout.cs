namespace KemoCard.Mod.Combat.Marble;

/// <summary>
/// 首版固定盘面布局：四枚固定撞钉，便于用同一发射角反复检验路线。
/// 坐标按盘面比例给出，换分辨率不影响逻辑；点数与半径是首轮试验值。
/// </summary>
public static class MarbleBoardLayout
{
    public const int FixedPegCount = 4;

    /// <summary>四钉初始布局：中央一对高低错落，左右各一枚侧钉。</summary>
    public static IReadOnlyList<MarblePegDefinition> CreateDefaultPegs(MarblePhysicsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var radius = config.PegRadius;
        var width = config.Width;
        var height = config.Height;
        return
        [
            new MarblePegDefinition("peg-0", new MarbleVec(width * 0.50f, height * 0.28f), radius, 1),
            new MarblePegDefinition("peg-1", new MarbleVec(width * 0.28f, height * 0.47f), radius, 2),
            new MarblePegDefinition("peg-2", new MarbleVec(width * 0.72f, height * 0.47f), radius, 2),
            new MarblePegDefinition("peg-3", new MarbleVec(width * 0.50f, height * 0.66f), radius, 3),
        ];
    }
}
