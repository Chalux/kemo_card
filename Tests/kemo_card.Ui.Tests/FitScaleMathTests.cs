using Godot;
using KemoCard.Frame.UI;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// <see cref="FitScaleBox"/> 的纯计算：空间富余时铺满不缩放，空间不足时按需求尺寸等比缩小（只缩不放）。
/// </summary>
[TestFixture]
public sealed class FitScaleMathTests
{
    private static readonly Vector2 Available = new(1280f, 720f);

    [Test]
    public void Fits_inside_available_space_and_fills_it_without_scaling()
    {
        var (childSize, scale) = FitScaleMath.Compute(Available, new Vector2(800f, 600f));

        Assert.That(scale, Is.EqualTo(1f), "空间富余时不能放大也不能缩小");
        Assert.That(childSize, Is.EqualTo(Available), "空间富余时子节点应铺满可用空间");
    }

    [Test]
    public void Content_that_is_too_wide_shrinks_uniformly_and_keeps_need_size()
    {
        var need = new Vector2(1600f, 752f);
        var (childSize, scale) = FitScaleMath.Compute(Available, need);

        Assert.That(scale, Is.EqualTo(1280f / 1600f).Within(1e-5f), "缩放取两边比例的较小者");
        Assert.That(childSize, Is.EqualTo(need), "缩小后子节点保持需求尺寸（由调用方居中）");
        Assert.That(need.Y * scale, Is.LessThanOrEqualTo(Available.Y), "等比缩小后高度不能超出可用高度");
    }

    [Test]
    public void Content_that_is_too_tall_shrinks_uniformly()
    {
        var need = new Vector2(1200f, 1000f);
        var (childSize, scale) = FitScaleMath.Compute(Available, need);

        Assert.That(scale, Is.EqualTo(720f / 1000f).Within(1e-5f));
        // 宽没超出、高超出：宽度用可用值（保持铺满），高度保持需求值，再由缩放统一压回可见区。
        Assert.That(childSize, Is.EqualTo(new Vector2(1280f, 1000f)));
        Assert.That(childSize.Y * scale, Is.LessThanOrEqualTo(Available.Y).Within(1e-3f));
    }

    [Test]
    public void Only_dimension_that_overflows_shrinks()
    {
        // 宽超出、高富余：宽度贴边缩小，高度方向按比例变小后由调用方垂直居中。
        var need = new Vector2(1440f, 600f);
        var (_, scale) = FitScaleMath.Compute(Available, need);

        Assert.That(scale, Is.EqualTo(1280f / 1440f).Within(1e-5f));
    }

    [Test]
    public void Degenerate_inputs_fall_back_to_available_size_without_scaling()
    {
        Assert.That(FitScaleMath.Compute(Vector2.Zero, new Vector2(1600f, 900f)).Scale, Is.EqualTo(1f),
            "首帧自身尺寸未就绪时不缩放");
        Assert.That(FitScaleMath.Compute(Available, Vector2.Zero).ChildSize, Is.EqualTo(Available),
            "子节点还没有需求尺寸时按可用空间铺满");
    }
}