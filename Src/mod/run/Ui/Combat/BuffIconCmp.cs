using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global.Ui;
using KemoCard.Mod.Global.Ui.Tip;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 单个 buff 图标：<c>iconPath</c> 能加载则显示贴图，否则显示名字前两个字符；角标为层数、下方为剩余回合。
/// 悬停显示名字 / 描述（走关键词提示层）。
/// </summary>
public partial class BuffIconCmp : BaseCmp
{
    [Export] private TextureRect? _icon;
    [Export] private Label? _lblShort;
    [Export] private Label? _lblStacks;
    [Export] private Label? _lblTurns;

    private BuffDto? _def;

    public string? BuffId => _def?.Id;

    protected override void InitEvent()
    {
        Binder.OnMouseEnterExit(this, OnHoverEntered, OnHoverExited);
    }

    protected override void OnExitTree()
    {
        KeywordTipService.Current?.HideTips(this);
    }

    public void Bind(BuffDto def, int stacks, int? remainingTurns)
    {
        _def = def;
        var name = string.IsNullOrWhiteSpace(def.DisplayNameId) ? def.Id : Localization.Tr(def.DisplayNameId);

        Texture2D? texture = null;
        if (!string.IsNullOrWhiteSpace(def.IconPath))
            texture = CharacterArtLoader.TryLoadTexture(def.IconPath);

        if (_icon != null)
        {
            _icon.Texture = texture;
            _icon.Visible = texture != null;
        }

        if (_lblShort != null)
        {
            _lblShort.Visible = texture == null;
            _lblShort.Text = name.Length <= 2 ? name : name[..2];
        }

        if (_lblStacks != null)
        {
            _lblStacks.Visible = stacks > 1;
            _lblStacks.Text = stacks.ToString();
        }

        if (_lblTurns != null)
        {
            _lblTurns.Visible = remainingTurns is > 0;
            _lblTurns.Text = remainingTurns?.ToString() ?? "";
        }

        TooltipText = "";
    }

    private void OnHoverEntered()
    {
        if (_def is null)
            return;

        var title = string.IsNullOrWhiteSpace(_def.DisplayNameId) ? _def.Id : Localization.Tr(_def.DisplayNameId);
        var desc = string.IsNullOrWhiteSpace(_def.DescId) ? "" : Localization.Tr(_def.DescId);
        KeywordTipService.Current?.ShowCustomTips(this, [(title, desc)], TipSide.Right);
    }

    private void OnHoverExited() => KeywordTipService.Current?.HideTips(this);
}