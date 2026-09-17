using System;
using Godot;

using KemoCard.Frame.UI;
namespace KemoCard.Mod.Global.Ui.Comp;

public partial class BasePager : Control
{
    [Signal]
    public delegate void PageChangedEventHandler(int page);

    [Export] private Button? _btnFirst;
    [Export] private Button? _btnPrevious;
    [Export] private Label? _txtCurrent;
    [Export] private Button? _btnNext;
    [Export] private Button? _btnFinal;
    [Export] private LineEdit? _iptPage;
    [Export] private Button? _btnGoto;

    private int _currentPage;
    private int _totalPages;
    /// <summary>订阅登记簿：任何订阅都必须经此登记，离场统一解绑（见 ui-mod-binding 规格 §4.3）。</summary>
    private readonly BindingScope _binder = new();

    /// <summary>
    /// 页码实际变化时的逻辑回调（与 PageChanged 信号并行）。
    /// </summary>
    public Action<int>? OnPageChanged { get; set; }

    /// <summary>
    /// 当前页（0-based）。
    /// </summary>
    public int CurrentPage
    {
        get => _currentPage;
        set => SetPage(value);
    }

    /// <summary>
    /// 总页数（≥0）。为 0 时显示 0/0，并禁用全部导航。
    /// </summary>
    public int TotalPages
    {
        get => _totalPages;
        set
        {
            var next = Math.Max(0, value);
            if (_totalPages == next)
            {
                RefreshView();
                return;
            }

            _totalPages = next;
            var clamped = ClampPage(_currentPage);
            if (clamped != _currentPage)
            {
                _currentPage = clamped;
                RefreshView();
                NotifyPageChanged();
                return;
            }

            RefreshView();
        }
    }

    #region 生命周期

    /// <summary>
    /// 订阅登记挂在 <c>_EnterTree</c>：Godot 的 <c>_Ready</c> 每个节点只调用一次，
    /// 界面进缓存走 <c>RemoveChild</c>，重开时 <c>AddChild</c> 不会再触发 <c>_Ready</c>，
    /// 挂在 <c>_Ready</c> 上会让翻页按钮在缓存重开后永久失效。
    /// </summary>
    public override void _EnterTree()
    {
        base._EnterTree();

        // 全部订阅经账本登记，离场统一解绑（ui-mod-binding 规格 §4.3 的 6 行模式）。
        if (_btnFirst != null) _binder.OnPressed(_btnFirst, OnFirstPressed);
        if (_btnPrevious != null) _binder.OnPressed(_btnPrevious, OnPreviousPressed);
        if (_btnNext != null) _binder.OnPressed(_btnNext, OnNextPressed);
        if (_btnFinal != null) _binder.OnPressed(_btnFinal, OnFinalPressed);
        if (_btnGoto != null) _binder.OnPressed(_btnGoto, OnGotoPressed);
        if (_iptPage != null) _binder.OnTextSubmitted(_iptPage, OnPageSubmitted);
    }

    public override void _Ready()
    {
        base._Ready();
        RefreshView();
    }

    /// <summary>框架唯一离场入口。sealed：子类不得 override —— 请改 override OnExitTree。</summary>
    public sealed override void _ExitTree()
    {
        OnExitTree();
        _binder.UnbindAll();
        base._ExitTree();
    }

    /// <summary>框架级离场生命周期：只做非订阅类清理。</summary>
    protected virtual void OnExitTree() { }

    #endregion

    #region 公共方法

    /// <summary>
    /// 设置当前页（0-based）。页码真正变化时刷新 UI，并并行触发信号与 OnPageChanged。
    /// </summary>
    public void SetPage(int page)
    {
        var next = ClampPage(page);
        if (next == _currentPage)
        {
            RefreshView();
            return;
        }

        _currentPage = next;
        RefreshView();
        NotifyPageChanged();
    }

    #endregion

    private void OnFirstPressed() => SetPage(0);

    private void OnPreviousPressed() => SetPage(_currentPage - 1);

    private void OnNextPressed() => SetPage(_currentPage + 1);

    private void OnFinalPressed() => SetPage(_totalPages - 1);

    private void OnGotoPressed() => TryGotoFromInput();

    private void OnPageSubmitted(string _) => TryGotoFromInput();

    private void TryGotoFromInput()
    {
        if (_iptPage == null)
        {
            return;
        }

        var text = _iptPage.Text.Trim();
        if (string.IsNullOrEmpty(text) || !int.TryParse(text, out var oneBased) || oneBased < 1)
        {
            return;
        }

        SetPage(oneBased - 1);
        _iptPage.Text = "";
    }

    #region 视图刷新

    private void RefreshView()
    {
        if (_txtCurrent != null)
        {
            _txtCurrent.Text = _totalPages <= 0
                ? "0/0"
                : $"{_currentPage + 1}/{_totalPages}";
        }

        var hasPages = _totalPages > 0;
        var atFirst = !hasPages || _currentPage <= 0;
        var atLast = !hasPages || _currentPage >= _totalPages - 1;

        if (_btnFirst != null)
        {
            _btnFirst.Disabled = atFirst;
        }

        if (_btnPrevious != null)
        {
            _btnPrevious.Disabled = atFirst;
        }

        if (_btnNext != null)
        {
            _btnNext.Disabled = atLast;
        }

        if (_btnFinal != null)
        {
            _btnFinal.Disabled = atLast;
        }

        if (_btnGoto != null)
        {
            _btnGoto.Disabled = !hasPages;
        }

        if (_iptPage != null)
        {
            _iptPage.Editable = hasPages;
        }
    }

    private int ClampPage(int page)
    {
        if (_totalPages <= 0)
        {
            return 0;
        }

        return Math.Clamp(page, 0, _totalPages - 1);
    }

    private void NotifyPageChanged()
    {
        EmitSignal(SignalName.PageChanged, _currentPage);
        OnPageChanged?.Invoke(_currentPage);
    }

    #endregion
}