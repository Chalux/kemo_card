using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 战斗界面「已标记卡牌」条（2026-09-26）：挂在队友卡右侧的浮层，按结算顺序显示该角色
/// 已标记（入队）的卡。**不可点击**（图标只收悬停，悬停显示卡牌摘要）；半透明（场景 modulate）；
/// 空列表整条隐藏、不占位。
/// </summary>
/// <remarks>
/// 槽位数量 = 手牌槽数（每张手牌最多标记一次），图标在场景里预先摆好，代码只做绑定与显隐。
/// 数据由 <see cref="CombatMarkedCards"/> 按角色分桶，宿主 <c>CombatWin</c> 在选目标时传空列表隐藏。
/// </remarks>
public partial class MarkedCardStripCmp : BaseCmp
{
    private readonly List<MarkedCardIconCmp> _icons = [];

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
}
