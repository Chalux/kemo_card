using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod.Global.Ui.Tip;
using KemoCard.Frame.Logging;

namespace KemoCard.Mod.Global.Ui;

public record struct CardDetailsDlgPayload
{
    public string CardId { get; init; }
    public int? DisplayValue { get; init; }
}

public partial class CardDetailsDlg : BaseDlg
{
    [Export] private BaseCardItem? _cardItem;
    [Export] private Label? _txtCardName;
    [Export] private Label? _txtModName;
    [Export] private Label? _txtArtistName;
    [Export] private RichTextLabel? _rtCardDesc;

    private bool _eventsBound;

    public override string UIId => GlobalUiIds.CardDetails;
    public override string UIDir => "Src/mod/global/Ui";

    protected override void InitEvent()
    {
        if (_eventsBound)
        {
            return;
        }

        _eventsBound = true;
        if (_cardItem != null)
        {
            _cardItem.ClickAction = ECardClickAction.None;
        }

        if (_rtCardDesc != null)
        {
            _rtCardDesc.MetaHoverStarted += OnMetaHoverStarted;
            _rtCardDesc.MetaHoverEnded += OnMetaHoverEnded;
        }
    }

    protected override void OnOpen() => RefreshFromPayload();

    protected override void UpdateView() => RefreshFromPayload();

    protected override void OnClose()
    {
        HideKeywordTips();
    }

    #region 数据绑定

    private void RefreshFromPayload()
    {
        var payload = GetTypedPayload<CardDetailsDlgPayload>();
        if (string.IsNullOrWhiteSpace(payload.CardId))
        {
            AppLog.Warning("CardDetailsDlg: CardId 为空。", "CardDetailsDlg");
            Close();
            return;
        }

        var store = AppRoot.Services.ContentModPipeline.Registry.Store;
        if (!store.TryGetCard(payload.CardId, out var card))
        {
            AppLog.Warning($"CardDetailsDlg: 未找到卡牌 {payload.CardId}。", "CardDetailsDlg");
            Close();
            return;
        }

        BindCard(card, payload.DisplayValue);
    }

    private void BindCard(CardDto card, int? displayValue)
    {
        if (_cardItem != null)
        {
            _cardItem.ClickAction = ECardClickAction.None;
            _cardItem.SetData(card);
            if (displayValue is int v)
            {
                _cardItem.SetDisplayValue(v);
            }
        }

        if (_txtCardName != null)
        {
            _txtCardName.Text = Localization.Tr(card.DisplayNameId);
        }

        BindModName(card.Id);
        BindArtist(card.ArtistNameId);

        if (_rtCardDesc != null)
        {
            var store = AppRoot.Services.ContentModPipeline.Registry.Store;
            _rtCardDesc.Text = CardDescBuilder.Build(
                card,
                id => store.TryGetSkill(id, out var skill) ? skill : null,
                Localization.Tr);
        }
    }

    private void BindModName(string cardId)
    {
        if (_txtModName == null)
        {
            return;
        }

        var pipeline = AppRoot.Services.ContentModPipeline;
        if (!pipeline.Registry.TryGetOwnerModId(EContentCategory.Card, cardId, out var modId))
        {
            _txtModName.Text = "";
            return;
        }

        if (pipeline.ScriptCatalog.TryGetDisplayNameKey(modId, out var key)
            && !string.IsNullOrEmpty(key))
        {
            _txtModName.Text = Localization.Tr(key);
            return;
        }

        _txtModName.Text = modId;
    }

    private void BindArtist(string artistNameId)
    {
        if (_txtArtistName == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(artistNameId))
        {
            _txtArtistName.Text = "";
            _txtArtistName.Visible = false;
            return;
        }

        _txtArtistName.Visible = true;
        _txtArtistName.Text = Localization.Tr(artistNameId);
    }

    #endregion

    #region 词条悬停

    private void OnMetaHoverStarted(Variant meta)
    {
        var metaStr = meta.AsString();
        if (!CardDescBuilder.TryParseKeywordMeta(metaStr, out var keywordId))
        {
            AppLog.Warning($"CardDetailsDlg: 非法 keyword meta: {metaStr}", "CardDetailsDlg");
            return;
        }

        if (_rtCardDesc == null)
        {
            return;
        }

        var service = KeywordTipService.Current;
        if (service == null)
        {
            AppLog.Warning("CardDetailsDlg: KeywordTipService.Current 为空。", "CardDetailsDlg");
            return;
        }

        service.ShowTips(_rtCardDesc, [new KeywordTipRequest(keywordId)]);
    }

    private void OnMetaHoverEnded(Variant _)
    {
        HideKeywordTips();
    }

    private void HideKeywordTips()
    {
        if (_rtCardDesc != null)
        {
            KeywordTipService.Current?.HideTips(_rtCardDesc);
        }
        else
        {
            KeywordTipService.Current?.HideTips();
        }
    }

    #endregion
}