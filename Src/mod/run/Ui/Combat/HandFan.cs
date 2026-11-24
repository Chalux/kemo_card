using Godot;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>仅排列有牌的槽位，子节点顺序始终对应角色与战斗槽位。</summary>
public partial class HandFan : Container
{
    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren)
            return;

        var cards = GetChildren().OfType<HandSlotCmp>().Where(slot => slot.Visible).ToArray();
        if (cards.Length == 0)
            return;

        var size = cards[0].GetCombinedMinimumSize();
        var half = (cards.Length - 1) * 0.5f;
        var angle = Mathf.Min(14f, half * 4.5f);
        var edgeWidth = size.X + size.Y * Mathf.Sin(Mathf.DegToRad(angle));
        var step = Mathf.Clamp((Size.X - edgeWidth) / Math.Max(1, cards.Length - 1), 0f, size.X * 0.86f);
        for (var i = 0; i < cards.Length; i++)
        {
            var side = half > 0f ? (i - half) / half : 0f;
            var x = (Size.X - size.X) * 0.5f + (i - half) * step;
            var y = Size.Y - size.Y + side * side * angle * 1.35f;
            cards[i].Size = size;
            cards[i].SetFanPose(new Vector2(x, y), Mathf.DegToRad(side * angle));
        }
    }
}