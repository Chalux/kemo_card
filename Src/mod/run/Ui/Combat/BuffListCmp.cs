using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat.Buffs;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// buff 图标列（角色 / 敌人 / 手牌槽共用）：按 <see cref="BuffContainer.Visible"/> 铺 <see cref="BuffIconCmp"/>，
/// 节点按需实例化并复用。
/// </summary>
public partial class BuffListCmp : BaseCmp
{
    [Export] private Container? _row;
    [Export] private PackedScene? _iconScene;

    private readonly List<BuffIconCmp> _icons = [];

    public void Bind(IReadOnlyList<BuffInstance> instances)
    {
        if (_row == null || _iconScene == null)
            return;

        while (_icons.Count < instances.Count)
        {
            if (_iconScene.Instantiate() is not BuffIconCmp icon)
                break;

            _row.AddChild(icon);
            _icons.Add(icon);
        }

        for (var i = 0; i < _icons.Count; i++)
        {
            var icon = _icons[i];
            if (i < instances.Count)
            {
                var instance = instances[i];
                icon.Bind(instance.Def, instance.Stacks, instance.RemainingTurns);
                icon.Visible = true;
            }
            else
            {
                icon.Visible = false;
            }
        }
    }

    /// <summary>按 buffId 找到当前显示的图标（动画高亮用）；不存在返回 null。</summary>
    public BuffIconCmp? FindIcon(string buffId)
    {
        foreach (var icon in _icons)
        {
            if (icon.Visible && string.Equals(icon.BuffId, buffId, StringComparison.Ordinal))
                return icon;
        }

        return null;
    }
}