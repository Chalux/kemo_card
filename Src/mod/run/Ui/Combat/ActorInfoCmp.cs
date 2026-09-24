using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 底栏最左：当前操控角色的信息——名字、元素·定位、能量（可用 / 当前 / 上限）、技能计数 S / Cap、
/// 物攻·魔攻、物防·魔防、回复量、buff 列表。
/// </summary>
public partial class ActorInfoCmp : BaseCmp
{
    [Export] private Label? _lblName;
    [Export] private Label? _lblMeta;
    [Export] private Label? _lblEnergy;
    [Export] private Label? _lblSkill;
    [Export] private Label? _lblAttack;
    [Export] private Label? _lblDefense;
    [Export] private Label? _lblHeal;
    [Export] private Label? _lblState;
    [Export] private BuffListCmp? _buffs;

    public BuffListCmp? BuffList => _buffs;

    public void Bind(CharacterBattleInstance character, CharacterDto? definition)
    {
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

        if (_lblAttack != null)
            _lblAttack.Text = CombatUnitFormat.Attack(character.Asc);
        if (_lblDefense != null)
            _lblDefense.Text = CombatUnitFormat.Defense(character.Asc);
        if (_lblHeal != null)
            _lblHeal.Text = CombatUnitFormat.Heal(character.Asc);

        if (_lblState != null)
        {
            var key = character.IsSealed ? "UI_COMBAT_SEALED" : character.HasActed ? "UI_COMBAT_CONFIRMED" : "";
            _lblState.Visible = key.Length > 0;
            _lblState.Text = key.Length > 0 ? Localization.Tr(key) : "";
        }

        _buffs?.Bind(character.Buffs.Visible);
    }

    /// <summary>能量单独可刷（<c>EnergyChangedEvent</c> 驱动）。</summary>
    public void SetEnergy(int current, int available, int max)
    {
        if (_lblEnergy != null)
            _lblEnergy.Text = string.Format(Localization.Tr("UI_COMBAT_ENERGY"), available, current, max);
    }
}