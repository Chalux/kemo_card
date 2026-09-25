using Godot;

namespace KemoCard.Frame.UI;

/// <summary>
/// <see cref="FitScaleBox"/> 的纯计算：由「可用尺寸 + 内容需求尺寸」求出子节点的布局尺寸与统一缩放。
/// </summary>
/// <remarks>
/// 规则：空间富余时子节点铺满可用空间（缩放恒为 1，不改变既有铺满布局）；
/// 空间不足时保持内容需求尺寸并整体等比缩小（只缩不放），调用方据此居中摆放。
/// </remarks>
internal static class FitScaleMath
{
    /// <summary>
    /// 计算子节点布局尺寸与缩放系数。<paramref name="available"/> / <paramref name="need"/> 任一维非正时
    /// 退化为「按可用尺寸、不缩放」，让调用方在首帧尺寸未就绪时保持原样。
    /// </summary>
    internal static (Vector2 ChildSize, float Scale) Compute(Vector2 available, Vector2 need)
    {
        if (available.X <= 0f || available.Y <= 0f || need.X <= 0f || need.Y <= 0f)
        {
            return (available, 1f);
        }

        var scale = MathF.Min(1f, MathF.Min(available.X / need.X, available.Y / need.Y));
        return (new Vector2(MathF.Max(available.X, need.X), MathF.Max(available.Y, need.Y)), scale);
    }
}