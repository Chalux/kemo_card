using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Global.Ui.Tip;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 底栏最左：当前操控角色的信息——名字、元素·定位、能量（可用 / 当前 / 上限）、技能计数 S / Cap
/// （+ 主动技状态行：可用 / 最大）、物理攻击 / 魔法攻击、物理防御 / 魔法防御、回复量、buff 列表。
/// 悬停面板显示该角色的主动技（每一档 + 当前可释放档高亮，见 <see cref="CombatActiveSkillTips"/>）。
/// </summary>
public partial class ActorInfoCmp : BaseCmp
{
    [Export] private Label? _lblName;
    [Export] private Label? _lblMeta;
    [Export] private Label? _lblEnergy;
    [Export] private Label? _lblSkill;
    [Export] private Label? _lblSkillHint;
    [Export] private Button? _btnRelease;
    [Export] private Label? _lblAttack;
    [Export] private Label? _lblMagicAttack;
    [Export] private Label? _lblDefense;
    [Export] private Label? _lblMagicDefense;
    [Export] private Label? _lblHeal;
    [Export] private Label? _lblState;
    [Export] private Label? _lblMark;
    [Export] private BuffListCmp? _buffs;

    public BuffListCmp? BuffList => _buffs;

    /// <summary>「释放主动技」回调（按钮只在有可释放档时可见）。</summary>
    public Action? ReleaseRequested { get; set; }

    private CharacterBattleInstance? _character;

    protected override void InitEvent()
    {
        if (_btnRelease != null)
            OnClicks(_btnRelease, () => ReleaseRequested?.Invoke());

        Binder.OnMouseEnterExit(this, OnHoverEntered, OnHoverExited);
    }

    protected override void OnExitTree() => KeywordTipService.Current?.HideTips(this);

    public void Bind(CharacterBattleInstance character, CharacterDto? definition, ECombatActionMark mark)
    {
        _character = character;

        if (_lblName != null)
            _lblName.Text = CombatUnitFormat.DisplayName(definition?.DisplayNameId, character.DefinitionId);

        if (_lblMeta != null)
        {
            var meta = CombatUnitFormat.ElementAndRole(character.Element, definition?.Role ?? ERole.None);
            _lblMeta.Visible = meta.Length > 0;
            _lblMeta.Text = meta;
        }

        SetEnergy(character.CurrentEnergy, character.AvailableEnergy, character.MaxEnergy);

        if (_lblSkill != null)
        {
            _lblSkill.Visible = character.SkillCounterCap > 0;
            _lblSkill.Text = string.Format(
                Localization.Tr("UI_COMBAT_SKILL_COUNTER"),
                character.SkillCounter,
                character.SkillCounterCap);
        }

        if (_lblSkillHint != null)
        {
            // 「主动技能可用 / 最大」（2026-09-27）：满档优先于可用；无主动链时留空但保持占位，
            // 避免卡片高度随状态变化 → FitScale 整屏缩放抖动。
            _lblSkillHint.Text = CombatActiveSkillTips.ResolveProgressSuffix(character, Localization.Tr) ?? "";
        }

        if (_lblAttack != null)
            _lblAttack.Text = CombatUnitFormat.PhysicalAttack(character.Asc);
        if (_lblMagicAttack != null)
            _lblMagicAttack.Text = CombatUnitFormat.MagicAttack(character.Asc);
        if (_lblDefense != null)
            _lblDefense.Text = CombatUnitFormat.PhysicalDefense(character.Asc);
        if (_lblMagicDefense != null)
            _lblMagicDefense.Text = CombatUnitFormat.MagicDefense(character.Asc);
        if (_lblHeal != null)
            _lblHeal.Text = CombatUnitFormat.Heal(character.Asc);

        if (_lblState != null)
        {
            var key = character.IsSealed ? "UI_COMBAT_SEALED" : character.HasActed ? "UI_COMBAT_CONFIRMED" : "";
            _lblState.Visible = key.Length > 0;
            _lblState.Text = key.Length > 0 ? Localization.Tr(key) : "";
        }

        CombatActionMarks.Apply(_lblMark, mark);

        _buffs?.Bind(character.Buffs.Visible);
    }

    /// <summary>能量单独可刷（<c>EnergyChangedEvent</c> 驱动）。</summary>
    public void SetEnergy(int current, int available, int max)
    {
        if (_lblEnergy != null)
            _lblEnergy.Text = string.Format(Localization.Tr("UI_COMBAT_ENERGY"), available, current, max);
    }

    private void OnHoverEntered()
    {
        if (_character is not null)
            CombatActiveSkillTips.TryShow(this, _character, TipSide.Right);
    }

    private void OnHoverExited() => KeywordTipService.Current?.HideTips(this);

    /// <summary>「释放主动技」按钮的显隐（判据见 <see cref="CombatActiveSkillTips.CanRelease"/>）。</summary>
    public void SetSkillRelease(bool visible)
    {
        if (_btnRelease != null)
            _btnRelease.Visible = visible;
    }
}