using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Logging;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Potential;

namespace KemoCard.Mod.Global.Ui;

public record struct CharacterDetailsDlgPayload
{
    public string CharacterId { get; init; }
}

public partial class CharacterDetailsDlg : BaseDlg
{
    [Export] private CharacterPresenter? _presenter;
    [Export] private BaseCharacterItem? _characterItem;
    [Export] private Label? _txtTitle;
    [Export] private Label? _txtCharName;
    [Export] private RichTextLabel? _rtCharDesc;
    [Export] private RichTextLabel? _rtPassives;
    [Export] private Label? _lblAnim;
    [Export] private OptionButton? _optAnim;

    private bool _animSelectSuppress;
    private string _defaultAnim = "idle";

    public override string UIId => GlobalUiIds.CharacterDetails;
    public override string UIDir => "Src/mod/global/Ui";

    protected override void InitEvent()
    {
        if (_characterItem != null)
        {
            _characterItem.ClickAction = ECharacterClickAction.None;
        }

        if (_optAnim != null)
        {
            Binder.OnItemSelected(_optAnim, OnAnimItemSelected);
        }
    }

    protected override void OnOpen() => RefreshFromPayload();

    protected override void UpdateView() => RefreshFromPayload();

    protected override void OnClose()
    {
    }

    #region 数据绑定

    private void RefreshFromPayload()
    {
        var payload = GetTypedPayload<CharacterDetailsDlgPayload>();
        if (string.IsNullOrWhiteSpace(payload.CharacterId))
        {
            AppLog.Warning("CharacterDetailsDlg: CharacterId 为空。", "CharacterDetailsDlg");
            Close();
            return;
        }

        var store = AppRoot.Services.ContentModPipeline.Registry.Store;
        if (!store.TryGetCharacter(payload.CharacterId, out var character))
        {
            AppLog.Warning($"CharacterDetailsDlg: 未找到角色 {payload.CharacterId}。", "CharacterDetailsDlg");
            Close();
            return;
        }

        BindCharacter(character);
    }

    private void BindCharacter(CharacterDto character)
    {
        _defaultAnim = string.IsNullOrWhiteSpace(character.Presentation?.DefaultAnim)
            ? "idle"
            : character.Presentation!.DefaultAnim;

        if (_txtTitle != null)
        {
            _txtTitle.Text = Localization.Tr("UI_CHARACTER_DETAILS_TITLE");
        }

        if (_characterItem != null)
        {
            _characterItem.ClickAction = ECharacterClickAction.None;
            _characterItem.EnableHoverTip = false;
            _characterItem.SetData(character);
        }

        _presenter?.Bind(character);

        if (_txtCharName != null)
        {
            _txtCharName.Text = string.IsNullOrWhiteSpace(character.DisplayNameId)
                ? ""
                : Localization.Tr(character.DisplayNameId);
        }

        if (_rtCharDesc != null)
        {
            _rtCharDesc.Text = string.IsNullOrWhiteSpace(character.DescId)
                ? ""
                : Localization.Tr(character.DescId);
        }

        BindPassives(character);
        BindAnimOptions();
    }

    /// <summary>
    /// 被动列表：名称（潜能门槛）+ 描述；进行中的 Run 内持有该角色实例时附加解锁状态。
    /// 词条标记（[url=kw:*]）由 RichTextLabel 的词条提示管线处理。
    /// </summary>
    private void BindPassives(CharacterDto character)
    {
        if (_rtPassives == null)
        {
            return;
        }

        if (character.Passives.Count == 0)
        {
            _rtPassives.Text = "";
            return;
        }

        var store = AppRoot.Services.ContentModPipeline.Registry.Store;
        var runInstance = RunRuntime.Current?.State.CharacterPool.FirstOrDefault(instance =>
            string.Equals(instance.DefinitionId, character.Id, StringComparison.Ordinal));

        var builder = new System.Text.StringBuilder();
        builder.Append($"[b]{Localization.Tr("UI_CHARACTER_PASSIVES_TITLE")}[/b]\n");
        var passiveIndex = 0;
        foreach (var passive in character.Passives)
        {
            passiveIndex++;
            var thresholdText = passive.RequiredPotential > 0
                ? string.Format(Localization.Tr("UI_CHARACTER_PASSIVE_THRESHOLD"), passive.RequiredPotential)
                : Localization.Tr("UI_CHARACTER_PASSIVE_THRESHOLD_ZERO");
            var unlocked = runInstance is not null &&
                PotentialService.IsPassiveUnlocked(RunRuntime.Current!.State, runInstance, passive);
            var stateText = runInstance is null
                ? ""
                : unlocked
                    ? Localization.Tr("UI_CHARACTER_PASSIVE_UNLOCKED")
                    : Localization.Tr("UI_CHARACTER_PASSIVE_LOCKED");

            // 被动没有独立技能名：一律按序号显示「被动技能N」（2026-09-19 约定），描述取 buff 的 descId。
            var name = string.Format(Localization.Tr("UI_CHARACTER_PASSIVE_NAME"), passiveIndex);
            var desc = store.TryGetBuff(passive.BuffId, out var buff) &&
                !string.IsNullOrWhiteSpace(buff.DescId)
                    ? Localization.Tr(buff.DescId)
                    : "";

            builder.Append($"[b]{name}[/b]（{thresholdText}）{stateText}\n{desc}\n");
        }

        _rtPassives.Text = builder.ToString().TrimEnd();
    }

    private void BindAnimOptions()
    {
        if (_optAnim == null)
        {
            return;
        }

        _animSelectSuppress = true;
        _optAnim.Clear();

        var hasPresentation = _presenter?.HasPresentation == true;
        if (_lblAnim != null)
        {
            _lblAnim.Visible = hasPresentation;
            if (hasPresentation)
            {
                _lblAnim.Text = Localization.Tr("UI_CODEX_ANIM");
            }
        }

        _optAnim.Visible = hasPresentation;
        if (!hasPresentation || _presenter == null)
        {
            _animSelectSuppress = false;
            return;
        }

        var anims = _presenter.ListAnims();
        var selectIndex = 0;
        for (var i = 0; i < anims.Count; i++)
        {
            _optAnim.AddItem(anims[i]);
            if (string.Equals(anims[i], _defaultAnim, StringComparison.Ordinal))
            {
                selectIndex = i;
            }
        }

        if (anims.Count > 0)
        {
            _optAnim.Select(selectIndex);
        }

        _animSelectSuppress = false;
    }

    private void OnAnimItemSelected(long index)
    {
        if (_animSelectSuppress || _presenter == null || _optAnim == null)
        {
            return;
        }

        var i = (int)index;
        if (i < 0 || i >= _optAnim.ItemCount)
        {
            return;
        }

        _presenter.Play(_optAnim.GetItemText(i));
    }

    #endregion
}