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
/// 悬停显示名字 / 描述（BBCode 关键词高亮）/ 剩余时间 / 层数，并附上描述引用关键词的效果块。
/// </summary>
public partial class BuffIconCmp : BaseCmp
{
    [Export] private TextureRect? _icon;
    [Export] private Label? _lblShort;
    [Export] private Label? _lblStacks;
    [Export] private Label? _lblTurns;

    private BuffDto? _def;
    private int _stacks;
    private int? _remainingTurns;

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
        _stacks = stacks;
        _remainingTurns = remainingTurns;
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
        var lines = new List<string>();
        if (desc.Length > 0)
            lines.Add(desc);

        // 剩余时间按时长类型分派；Turns 用实例的剩余回合（叠层独立计时取最大值）。
        switch (_def.DurationType)
        {
            case EBuffDurationType.Turns:
                lines.Add(string.Format(Localization.Tr("UI_COMBAT_BUFF_TURNS"), Math.Max(0, _remainingTurns ?? 0)));
                break;
            case EBuffDurationType.Permanent:
                lines.Add(Localization.Tr("UI_COMBAT_BUFF_PERMANENT"));
                break;
            case EBuffDurationType.Combat:
                lines.Add(Localization.Tr("UI_COMBAT_BUFF_COMBAT"));
                break;
            case EBuffDurationType.UntilDispelled:
                lines.Add(Localization.Tr("UI_COMBAT_BUFF_UNTIL_DISPELLED"));
                break;
        }

        // 层数「当前 / 上限」；无上限或可无限叠（maxStacks ≤ 0 或 ≥ 99）显示 ∞。
        lines.Add(string.Format(
            Localization.Tr("UI_COMBAT_BUFF_STACKS"),
            CombatUnitFormat.StacksText(_stacks, _def.MaxStacks)));

        // 描述里 [url=kw:id] 引用的关键词效果作为附加块列在下方（不用悬停也能看到效果）。
        var tips = new List<(string Title, string Desc)>
        {
            (title, string.Join("\n", lines)),
        };
        tips.AddRange(KeywordTipService.BuildKeywordEffectTips(desc, KeywordCatalog.Shared, Localization.Tr));
        KeywordTipService.Current?.ShowCustomTips(this, tips, TipSide.Right);
    }

    private void OnHoverExited() => KeywordTipService.Current?.HideTips(this);
}