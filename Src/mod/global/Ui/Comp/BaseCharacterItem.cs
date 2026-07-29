using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.Logging;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui.Tip;

namespace KemoCard.Mod.Global.Ui.Comp;

public partial class BaseCharacterItem : Control
{
    [Export] private CharacterPresenter? _presenter;
    [Export] private Label? _txtName;
    [Export] private ColorRect? _crAttr;
    [Export] public ECharacterClickAction ClickAction { get; set; } = ECharacterClickAction.OpenDetails;
    [Export] public bool EnableHoverTip { get; set; }
    [Export] public float TipDelaySec { get; set; } = 0.15f;
    [Export] public TipSide PreferTipSide { get; set; } = TipSide.Right;

    private CharacterDto? _character;
    private Tween? _tipDelayTween;
    private bool _hoverTipActive;

    #region 点击交互

    public override void _Ready()
    {
        base._Ready();
        MouseFilter = MouseFilterEnum.Stop;
        IgnoreMouseOnDescendants(this);
        MouseEntered += OnHoverTipEntered;
        MouseExited += OnHoverTipExited;
    }

    public override void _ExitTree()
    {
        CancelHoverTipDelay();
        KeywordTipService.Current?.HideTips(this);
        MouseEntered -= OnHoverTipEntered;
        MouseExited -= OnHoverTipExited;
        base._ExitTree();
    }

    private static void IgnoreMouseOnDescendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Control control)
            {
                control.MouseFilter = MouseFilterEnum.Ignore;
            }

            IgnoreMouseOnDescendants(child);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (ClickAction != ECharacterClickAction.OpenDetails || _character == null)
        {
            return;
        }

        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb
            && !mb.IsEcho())
        {
            TryOpenDetails();
            AcceptEvent();
        }
    }

    private void TryOpenDetails()
    {
        if (_character == null)
        {
            return;
        }

        var ui = UIManager.Instance;
        if (ui == null)
        {
            AppLog.Warning("BaseCharacterItem: UIManager.Instance 为空，无法打开角色详情。", "BaseCharacterItem");
            return;
        }

        _ = ui.OpenAsync(
            new UiId<CharacterDetailsDlgPayload>(GlobalUiIds.CharacterDetails),
            new CharacterDetailsDlgPayload
            {
                CharacterId = _character.Id,
            });
    }

    #endregion

    #region 整体绑定

    public void SetData(CharacterDto? character)
    {
        _character = character;

        if (character is null)
        {
            ClearVisuals();
            return;
        }

        if (_presenter != null)
        {
            _presenter.Visible = true;
            _presenter.Bind(character);
        }

        if (_txtName != null)
        {
            _txtName.Text = string.IsNullOrWhiteSpace(character.DisplayNameId)
                ? ""
                : Localization.Tr(character.DisplayNameId);
            _txtName.Visible = true;
        }

        SetElement((int)character.Element);
    }

    #endregion

    #region 悬停摘要 Tip

    private void OnHoverTipEntered()
    {
        if (!EnableHoverTip || _character == null)
        {
            return;
        }

        _hoverTipActive = true;
        ScheduleHoverTip();
    }

    private void OnHoverTipExited()
    {
        _hoverTipActive = false;
        CancelHoverTipDelay();
        KeywordTipService.Current?.HideTips(this);
    }

    private void ScheduleHoverTip()
    {
        CancelHoverTipDelay();
        if (TipDelaySec <= 0f)
        {
            ShowHoverTipNow();
            return;
        }

        _tipDelayTween = CreateTween();
        _tipDelayTween.TweenInterval(TipDelaySec);
        _tipDelayTween.TweenCallback(Callable.From(ShowHoverTipNow));
    }

    private void CancelHoverTipDelay()
    {
        _tipDelayTween?.Kill();
        _tipDelayTween = null;
    }

    private void ShowHoverTipNow()
    {
        if (!_hoverTipActive || !EnableHoverTip || _character == null)
        {
            return;
        }

        var service = KeywordTipService.Current;
        if (service == null)
        {
            AppLog.Warning("BaseCharacterItem: KeywordTipService.Current 为空，无法显示角色摘要提示。", "BaseCharacterItem");
            return;
        }

        GameDefinitionStore store;
        try
        {
            store = AppRoot.Services.ContentModPipeline.Registry.Store;
        }
        catch (InvalidOperationException)
        {
            AppLog.Warning("BaseCharacterItem: AppRoot 未初始化，无法构建角色摘要。", "BaseCharacterItem");
            return;
        }

        var tip = CharacterSummaryBuilder.Build(
            _character,
            id => store.TryGetSkill(id, out var skill) ? skill : null,
            Localization.Tr);

        if (string.IsNullOrWhiteSpace(tip.Title) && string.IsNullOrWhiteSpace(tip.Body))
        {
            return;
        }

        service.ShowCustomTips(this, [(tip.Title, tip.Body)], PreferTipSide);
    }

    #endregion

    #region 内部刷新

    private void ClearVisuals()
    {
        if (_txtName != null)
        {
            _txtName.Text = "";
            _txtName.Visible = false;
        }

        ApplyElementShader([ColorDefinitions.NoneElement]);
        if (_crAttr != null)
        {
            _crAttr.Visible = false;
        }

        if (_presenter != null)
        {
            _presenter.Visible = false;
        }
    }

    private void SetElement(int elementFlags)
    {
        if (_crAttr == null)
        {
            return;
        }

        var colors = CardUiDefinitions.CollectElementColors(elementFlags);
        if (elementFlags != 0)
        {
            var bitCount = 0;
            foreach (EElement e in Enum.GetValues<EElement>())
            {
                if (e == EElement.None)
                {
                    continue;
                }

                if ((elementFlags & (int)e) != 0)
                {
                    bitCount++;
                }
            }

            if (bitCount > 3)
            {
                AppLog.Warning($"BaseCharacterItem: element flags truncated to 3 colors (flags={elementFlags}).", "BaseCharacterItem");
            }
        }

        ApplyElementShader(colors);
        _crAttr.Visible = true;
    }

    private void ApplyElementShader(Color[] colors)
    {
        if (_crAttr?.Material is not ShaderMaterial mat)
        {
            return;
        }

        var packed = new Vector4[3];
        for (var i = 0; i < 3; i++)
        {
            if (i < colors.Length)
            {
                var c = colors[i];
                packed[i] = new Vector4(c.R, c.G, c.B, c.A);
            }
            else
            {
                packed[i] = Vector4.Zero;
            }
        }

        mat.SetShaderParameter("colors", packed);
        mat.SetShaderParameter("color_count", colors.Length);
    }

    #endregion
}