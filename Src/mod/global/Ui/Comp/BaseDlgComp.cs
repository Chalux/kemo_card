using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Frame.Logging;

namespace KemoCard.Mod.Global.Ui.Comp;

public partial class BaseDlgComp : Control
{
    [Export] private Button? _btnClose;

    private bool _bound;

    public override void _EnterTree()
    {
        base._EnterTree();
        EnsureBound();
    }

    public override void _ExitTree()
    {
        UnbindControls();
        base._ExitTree();
    }

    #region 绑定与关闭

    private void EnsureBound()
    {
        if (_bound)
        {
            return;
        }

        _bound = true;
        if (_btnClose != null)
        {
            _btnClose.Pressed += OnClosePressed;
        }
    }

    private void UnbindControls()
    {
        if (!_bound)
        {
            return;
        }

        _bound = false;
        if (_btnClose != null)
        {
            _btnClose.Pressed -= OnClosePressed;
        }
    }

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

    #endregion
}