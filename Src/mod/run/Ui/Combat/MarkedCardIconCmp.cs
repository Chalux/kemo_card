using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.Logging;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui;
using KemoCard.Mod.Global.Ui.Tip;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 战斗界面「已标记卡牌」条里的一枚小卡图标（2026-09-26）：费用 + 属性色条 + 卡面美术。
/// <b>不可点击</b>（只登记悬停，不处理点击）；悬停显示卡牌摘要（与卡面同一
/// <see cref="CardSummaryBuilder"/> 口径，含关键词高亮）。
/// </summary>
public partial class MarkedCardIconCmp : BaseCmp
{
    [Export] private TextureRect? _art;
    [Export] private ColorRect? _attr;
    [Export] private Label? _cost;
    [Export] private Label? _type;

    private CardDto? _card;

    protected override void OnReady()
    {
        // 只为收悬停而 Stop：不绑任何点击处理 → 点上去没有任何行为（"不可点击"）。
        MouseFilter = MouseFilterEnum.Stop;
    }

    protected override void InitEvent()
    {
        Binder.OnMouseEnterExit(this, OnHoverEntered, OnHoverExited);
    }

    protected override void OnExitTree()
    {
        KeywordTipService.Current?.HideTips(this);
    }

    /// <summary>绑定卡牌；<c>null</c> 表示该槽位没有标记（整枚隐藏，不占列表宽度）。</summary>
    public void Bind(CardDto? card)
    {
        _card = card;
        Visible = card is not null;
        if (card is null)
        {
            return;
        }

        if (_cost != null)
        {
            _cost.Visible = card.CostType != ECostType.None;
            _cost.Text = CardUiDefinitions.FormatCost(card.CostType, card.Cost);
        }

        if (_type != null)
        {
            _type.Text = CardUiDefinitions.TryGetCardTypeLocaleKey(card.CardType, out var typeKey)
                ? Localization.Tr(typeKey)
                : card.CardType.ToString();
        }

        if (_attr != null)
        {
            _attr.Color = CardUiDefinitions.CollectElementColors(card.Element)[0];
        }

        if (_art != null)
        {
            var texture = CardArtLoader.TryLoadTexture(card.ArtPath, card.Id);
            _art.Texture = texture;
            _art.Visible = texture != null;
        }

        TooltipText = "";
    }

    private void OnHoverEntered()
    {
        if (_card is null)
        {
            return;
        }

        GameDefinitionStore store;
        try
        {
            store = AppRoot.Services.ContentModPipeline.Registry.Store;
        }
        catch (InvalidOperationException)
        {
            AppLog.Warning("MarkedCardIconCmp: AppRoot 未初始化，无法构建卡牌摘要。", "MarkedCardIconCmp");
            return;
        }

        var tip = CardSummaryBuilder.Build(
            _card,
            id => store.TryGetSkill(id, out var skill) ? skill : null,
            Localization.Tr,
            cardId => CardSummaryBuilder.ResolveExclusiveCharacterName(store, cardId, Localization.Tr));

        if (string.IsNullOrWhiteSpace(tip.Title) && string.IsNullOrWhiteSpace(tip.Body))
        {
            return;
        }

        KeywordTipService.Current?.ShowCustomTips(this, [(tip.Title, tip.Body)], TipSide.Right);
    }

    private void OnHoverExited() => KeywordTipService.Current?.HideTips(this);
}
