using System;
using Godot;

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
    private bool _bound;

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

    public override void _Ready()
    {
        EnsureBound();
        RefreshView();
    }

    public override void _ExitTree()
    {
        UnbindControls();
        base._ExitTree();
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 设置当前页（0-based）。页码真正变化时刷新 UI，并并行触发信号与 OnPageChanged。
    /// </summary>
    public void SetPage(int page)
    {
        EnsureBound();
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

    #region 绑定与交互

    private void EnsureBound()
    {
        if (_bound)
        {
            return;
        }

        _bound = true;

        if (_btnFirst != null)
        {
            _btnFirst.Pressed += OnFirstPressed;
        }

        if (_btnPrevious != null)
        {
            _btnPrevious.Pressed += OnPreviousPressed;
        }

        if (_btnNext != null)
        {
            _btnNext.Pressed += OnNextPressed;
        }

        if (_btnFinal != null)
        {
            _btnFinal.Pressed += OnFinalPressed;
        }

        if (_btnGoto != null)
        {
            _btnGoto.Pressed += OnGotoPressed;
        }

        if (_iptPage != null)
        {
            _iptPage.TextSubmitted += OnPageSubmitted;
        }
    }

    private void UnbindControls()
    {
        if (!_bound)
        {
            return;
        }

        _bound = false;

        if (_btnFirst != null)
        {
            _btnFirst.Pressed -= OnFirstPressed;
        }

        if (_btnPrevious != null)
        {
            _btnPrevious.Pressed -= OnPreviousPressed;
        }

        if (_btnNext != null)
        {
            _btnNext.Pressed -= OnNextPressed;
        }

        if (_btnFinal != null)
        {
            _btnFinal.Pressed -= OnFinalPressed;
        }

        if (_btnGoto != null)
        {
            _btnGoto.Pressed -= OnGotoPressed;
        }

        if (_iptPage != null)
        {
            _iptPage.TextSubmitted -= OnPageSubmitted;
        }
    }

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

    #endregion

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