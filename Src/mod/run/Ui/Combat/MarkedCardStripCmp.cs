using Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 战斗界面「已标记卡牌」条（2026-09-26）：显示某个角色本回合已标记（入队）的卡，按结算顺序。
/// **不可点击**（图标只收悬停，悬停显示卡牌摘要）；半透明（场景 modulate）；空列表整条隐藏。
/// </summary>
/// <remarks>
/// <para>条挂在 <c>CombatWin</c> 的 <c>StripLayer</c>（FitScale 下、晚于 Root 的全屏层）里，
/// 由 <see cref="Follow"/> 跟随对应队友卡定位——**不能**改挂到队友卡内部：Godot 的 GUI 拾取按
/// 树序（晚的兄弟优先）而非 z_index，挂在左栏时战场单位会先截走悬停（2026-09-27 实测）。</para>
/// <para>槽位数量 = 手牌槽数（每张手牌最多标记一次），图标在场景里预先摆好，代码只做绑定与显隐。
/// 数据由 <see cref="CombatMarkedCards"/> 按角色分桶，宿主 <c>CombatWin</c> 在选目标时传空列表隐藏。</para>
/// </remarks>
public partial class MarkedCardStripCmp : BaseCmp
{
    private readonly List<MarkedCardIconCmp> _icons = [];
    private Control? _target;

    /// <summary>
    /// 跟随目标卡（队友卡）：条贴在其右缘 +8、垂直居中；每帧按目标全局矩形重算，
    /// 布局变化 / FitScale 缩放自动跟手。
    /// </summary>
    public void Follow(Control? target)
    {
        _target = target;
        SetProcess(target is not null);
        if (target is not null)
        {
            UpdatePosition();
        }
    }

    public override void _Process(double delta) => UpdatePosition();

    /// <summary>绑定该角色已标记的卡（按结算顺序）；空列表时整条隐藏。</summary>
    public void Bind(IReadOnlyList<CardDto> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        if (_icons.Count == 0)
        {
            foreach (var child in GetChildren())
            {
                if (child is MarkedCardIconCmp icon)
                {
                    _icons.Add(icon);
                }
            }
        }

        for (var i = 0; i < _icons.Count; i++)
        {
            _icons[i].Bind(i < cards.Count ? cards[i] : null);
        }

        Visible = cards.Count > 0;
    }

    private void UpdatePosition()
    {
        if (_target is null || !GodotObject.IsInstanceValid(_target) || GetParent() is not Control parent)
        {
            return;
        }

        var scale = _target.GetGlobalTransformWithCanvas().Scale.X;
        if (scale <= 0f)
        {
            return;
        }

        // 条挂在未缩放的浮层里，目标在 FitScale 的缩放空间里：位置换算到浮层局部坐标、
        // 自身 Scale 补偿 UI 缩放（尺寸与卡片同比例），避免手写各层缩放系数。
        var toLocal = parent.GetGlobalTransformWithCanvas().AffineInverse();
        var cardRect = _target.GetGlobalRect();
        var topLeft = toLocal * cardRect.Position;
        var cardSize = toLocal.BasisXform(cardRect.Size);
        Scale = new Vector2(scale, scale);
        Position = new Vector2(
            topLeft.X + cardSize.X + 8f * scale,
            topLeft.Y + cardSize.Y / 2f - Size.Y * scale / 2f);
    }
}
