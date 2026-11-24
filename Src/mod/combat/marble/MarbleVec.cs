namespace KemoCard.Mod.Combat.Marble;

/// <summary>
/// 弹珠模拟的二维向量：纯 float 标量运算，不引用 Godot，保证无头固定步长下的确定性。
/// 界面只用它与 <c>Vector2</c> 做等比映射。
/// </summary>
public readonly struct MarbleVec : IEquatable<MarbleVec>
{
    public MarbleVec(float x, float y)
    {
        X = x;
        Y = y;
    }

    public float X { get; }
    public float Y { get; }

    public static MarbleVec Zero => new(0f, 0f);

    public float LengthSquared => X * X + Y * Y;

    public float Length => MathF.Sqrt(LengthSquared);

    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y);

    /// <summary>单位化；零向量返回零向量（调用方负责处理退化法线）。</summary>
    public MarbleVec Normalized
    {
        get
        {
            var length = Length;
            return length > 1e-6f ? new MarbleVec(X / length, Y / length) : Zero;
        }
    }

    /// <summary>角度 → 单位方向。0 为 +X（右），<c>π/2</c> 为 +Y（下）；不限制象限。</summary>
    public static MarbleVec FromAngle(float radians) => new(MathF.Cos(radians), MathF.Sin(radians));

    public static float Dot(MarbleVec a, MarbleVec b) => a.X * b.X + a.Y * b.Y;

    public static float Distance(MarbleVec a, MarbleVec b) => (a - b).Length;

    public static MarbleVec operator +(MarbleVec a, MarbleVec b) => new(a.X + b.X, a.Y + b.Y);

    public static MarbleVec operator -(MarbleVec a, MarbleVec b) => new(a.X - b.X, a.Y - b.Y);

    public static MarbleVec operator -(MarbleVec value) => new(-value.X, -value.Y);

    public static MarbleVec operator *(MarbleVec value, float scale) => new(value.X * scale, value.Y * scale);

    public static MarbleVec operator *(float scale, MarbleVec value) => value * scale;

    public bool Equals(MarbleVec other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) => obj is MarbleVec other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(MarbleVec left, MarbleVec right) => left.Equals(right);

    public static bool operator !=(MarbleVec left, MarbleVec right) => !left.Equals(right);

    public override string ToString() => $"({X:0.###}, {Y:0.###})";
}
