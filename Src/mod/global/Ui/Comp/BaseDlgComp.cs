using Godot;
using KemoCard.Frame.Logging;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Global.Ui.Comp;

/// <summary>
/// 对话框通用组件（关闭按钮等）。
/// </summary>
/// <remarks>
/// 本类无法继承 <see cref="BaseUI"/>（需继承 <c>Control</c>），因此按 ui-mod-binding 规格 §4.3
/// 自行组合 <see cref="BindingScope"/> 并收敛 <c>_ExitTree</c>，订阅生命周期与 <c>BaseWin</c> 一致。
/// </remarks>
public partial class BaseDlgComp : Control
{
    [Export] private Button? _btnClose;

    /// <summary>订阅登记簿：任何订阅都必须经此登记，离场统一解绑。</summary>
    private readonly BindingScope _binder = new();

    public override void _EnterTree()
    {
        base._EnterTree();

        // 每次进树都重新登记：账本在离场时已清空，因此不需要 _bound 之类的手写守卫
        // （手写守卫一旦忘记复位，重开时就会提前返回、控件失联——见规格 §2.2）。
        if (_btnClose != null)
        {
            _binder.OnPressed(_btnClose, OnClosePressed);
        }
    }

    /// <summary>框架唯一离场入口。<b>sealed：子类不得 override</b> —— 请改 override <see cref="OnExitTree"/>。</summary>
    public sealed override void _ExitTree()
    {
        OnExitTree();
        _binder.UnbindAll();
        base._ExitTree();
    }

    /// <summary>框架级离场生命周期：只做非订阅类清理。</summary>
    protected virtual void OnExitTree() { }

    private void OnClosePressed()
    {
        var win = FindOwnerWin();
        if (win == null)
        {
            AppLog.Warning("BaseDlgComp: 未找到父级 BaseWin，无法关闭。", "BaseDlgComp");
            return;
        }

        win.Close();
    }

    private BaseWin? FindOwnerWin()
    {
        Node? node = this;
        while (node != null)
        {
            if (node is BaseWin win)
            {
                return win;
            }

            node = node.GetParent();
        }

        return null;
    }
}