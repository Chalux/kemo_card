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
    [Export] private Label? _txtTitle;
    [Export] private Label? _txtCharName;
    [Export] private Label? _lblMeta;
    [Export] private Label? _lblCardsCaption;
    [Export] private VirtualList? _cardList;
    [Export] private RichTextLabel? _rtPassives;
    [Export] private Label? _lblAnim;
    [Export] private OptionButton? _optAnim;

    /// <summary>当前展示的专属卡 id（横向虚拟列表的渲染回调按索引取用）。</summary>
    private readonly List<string> _cardIds = [];

    private bool _animSelectSuppress;
    private string _defaultAnim = "idle";

    public override string UIId => GlobalUiIds.CharacterDetails;
    public override string UIDir => "Src/mod/global/Ui";

    protected override void InitEvent()
    {
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

        _presenter?.Bind(character);

        if (_txtCharName != null)
        {
            _txtCharName.Text = string.IsNullOrWhiteSpace(character.DisplayNameId)
                ? ""
                : Localization.Tr(character.DisplayNameId);
        }

        BindMeta(character);

        BindCards(character);

        if (_lblCardsCaption != null)
        {
            // 没有专属卡时连区标题一起收起，不留一个空标题。
            _lblCardsCaption.Visible = _cardIds.Count > 0;
        }

        BindPassives(character);
        BindAnimOptions();
    }

    /// <summary>
    /// 身份行：元素 / 定位 / 种族（如「元素：蓝 · 定位：战士 · 种族：动物、龙族」）。
    /// </summary>
    /// <remarks>
    /// 三项全在角色定义里，因此**不依赖 Run**——从图鉴点开的角色（还没进 Run 角色池）也有值；
    /// 显示名走 <see cref="CharacterIdentityLabels"/>，与队伍编辑预览、角色悬停摘要同一套口径。
    /// 三者皆无时 <see cref="CharacterIdentityLabels.MetaLine"/> 返回空串，标签自然不显示内容。
    /// </remarks>
    private void BindMeta(CharacterDto character)
    {
        if (_lblMeta == null)
        {
            return;
        }

        _lblMeta.Text = CharacterIdentityLabels.MetaLine(character, Localization.Tr);
    }

    /// <summary>
    /// 专属卡牌区：横向虚拟列表（<c>BaseCardItem</c> 卡面），只列角色的专属卡（<c>isExclusive</c>）。
    /// </summary>
    /// <remarks>
    /// 不再把卡牌名称与描述拼成文本——那与卡面重复表达同一件事，而且把描述文本塞进 RichTextLabel
    /// 会让「看一下这个角色的牌」变成要读一大段文字。卡面本身带费用 / 类型 / 属性色，
    /// 点一下即打开现有的卡牌详情（<see cref="ECardClickAction.OpenDetails"/>），描述在那里看。
    /// </remarks>
    private void BindCards(CharacterDto character)
    {
        _cardIds.Clear();
        var store = AppRoot.Services.ContentModPipeline.Registry.Store;
        foreach (var cardId in character.Cards)
        {
            if (store.TryGetCard(cardId, out var card) && card.IsExclusive)
            {
                _cardIds.Add(cardId);
            }
        }

        _cardList?.SetData(_cardIds.Count, (index, item) => RenderCard(store, index, item));
    }

    private void RenderCard(GameDefinitionStore store, int index, Control item)
    {
        if (item is not BaseCardItem cardItem || index < 0 || index >= _cardIds.Count)
        {
            return;
        }

        // 列表项是对象池复用的：交互契约每次渲染都显式重置，别让上一张牌留下的闭包 / 开关生效。
        cardItem.Clicked = null;
        cardItem.LongPressed = null;
        cardItem.EnableLongPress = false;
        cardItem.ClickAction = ECardClickAction.OpenDetails;
        cardItem.EnableHoverTip = true;
        cardItem.SetData(store.TryGetCard(_cardIds[index], out var card) ? card : null);
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
        foreach (var passive in character.Passives)
        {
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

            // 被动没有名字（2026-09-21 决议，撤销 2026-09-19 的「被动技能N」序号显示）：
            // 直接以「潜能门槛 + 描述」呈现，描述取 buff 的 descId。
            var desc = store.TryGetBuff(passive.BuffId, out var buff) &&
                !string.IsNullOrWhiteSpace(buff.DescId)
                    ? Localization.Tr(buff.DescId)
                    : "";

            builder.Append($"[b]{thresholdText}[/b]{stateText}\n{desc}\n");
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