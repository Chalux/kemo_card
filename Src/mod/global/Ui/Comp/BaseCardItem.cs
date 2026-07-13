using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Content.Keywords;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui.Tip;

namespace KemoCard.Mod.Global.Ui.Comp;

public partial class BaseCardItem : Control
{
    [Export] private TextureRect? _trArt;
    [Export] private TextureRect? _trCardFrame;
    [Export] private Label? _txtCardCost;
    [Export] private Label? _txtCardType;
    [Export] private Label? _txtCardVal;
    [Export] private ColorRect? _crAttr;

    private CardDto? _card;
    private int _baseValue;
    private int? _displayOverride;
    private ECostType _costType = ECostType.None;

    #region 整体绑定

    public void SetData(CardDto? card)
    {
        _card = card;
        _baseValue = card?.BaseValue ?? 0;
        _displayOverride = null;
        _costType = card?.CostType ?? ECostType.None;

        if (card is null)
        {
            ClearVisuals();
            return;
        }

        ApplyCost(card.CostType, card.Cost);
        SetCardType(card.CardType);
        RefreshValueLabel();
        SetElement(card.Element);
        SetArtFromPath(card.ArtPath, card.Id);
        SetCardFrameFromRarity(card.Rarity);
    }

    public void SetDisplayValue(int? value)
    {
        _displayOverride = value;
        RefreshValueLabel();
    }

    public void ShowTips(IReadOnlyList<KeywordTipRequest> tips, TipSide preferSide = TipSide.Right)
    {
        var service = KeywordTipService.Current;
        if (service == null)
        {
            GD.PushWarning("BaseCardItem: KeywordTipService.Current 为空，无法显示词条提示。");
            return;
        }

        service.ShowTips(this, tips, preferSide);
    }

    public void HideTips()
    {
        KeywordTipService.Current?.HideTips(this);
    }

    #endregion

    #region 分项接口

    public void SetCost(int cost)
    {
        ApplyCost(_costType, cost);
    }

    public void SetCostVisible(bool visible)
    {
        if (_txtCardCost != null)
        {
            _txtCardCost.Visible = visible;
        }
    }

    public void SetCardType(ECardType type)
    {
        if (_txtCardType == null)
        {
            return;
        }

        if (CardUiDefinitions.TryGetCardTypeLocaleKey(type, out var key))
        {
            _txtCardType.Text = Localization.Tr(key);
        }
        else
        {
            _txtCardType.Text = type.ToString();
        }

        _txtCardType.Visible = true;
    }

    public void SetBaseValue(int value)
    {
        _baseValue = value;
        if (_displayOverride is null)
        {
            RefreshValueLabel();
        }
    }

    public void SetElement(int elementFlags)
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
                GD.PushWarning($"BaseCardItem: element flags truncated to 3 colors (flags={elementFlags}).");
            }
        }

        ApplyElementShader(colors);
        _crAttr.Visible = true;
    }

    public void SetArt(Texture2D? texture)
    {
        if (_trArt == null)
        {
            return;
        }

        _trArt.Texture = texture;
        _trArt.Visible = texture != null;
    }

    public void SetArtFromPath(string artPath, string? cardId = null)
    {
        if (string.IsNullOrWhiteSpace(artPath))
        {
            SetArt(null);
            return;
        }

        var texture = TryLoadArtTexture(artPath, cardId ?? _card?.Id);
        SetArt(texture);
    }

    public void SetCardFrame(Texture2D? texture)
    {
        if (_trCardFrame == null)
        {
            return;
        }

        _trCardFrame.Texture = texture;
        _trCardFrame.Visible = texture != null;
    }

    public void SetCardFrameFromRarity(ERarity rarity)
    {
        if (!CardUiDefinitions.TryGetCardFramePath(rarity, out var path))
        {
            SetCardFrame(null);
            return;
        }

        if (!ResourceLoader.Exists(path))
        {
            SetCardFrame(null);
            return;
        }

        var texture = ResourceLoader.Load<Texture2D>(path);
        SetCardFrame(texture);
    }

    #endregion

    #region 内部刷新

    private void ClearVisuals()
    {
        if (_txtCardCost != null)
        {
            _txtCardCost.Text = "";
            _txtCardCost.Visible = false;
        }

        if (_txtCardType != null)
        {
            _txtCardType.Text = "";
        }

        if (_txtCardVal != null)
        {
            _txtCardVal.Text = "";
        }

        ApplyElementShader([ColorDefinitions.NoneElement]);
        SetArt(null);
        SetCardFrame(null);
    }

    private void ApplyCost(ECostType costType, int cost)
    {
        _costType = costType;
        if (_txtCardCost == null)
        {
            return;
        }

        if (costType == ECostType.None)
        {
            _txtCardCost.Text = "";
            _txtCardCost.Visible = false;
            return;
        }

        _txtCardCost.Text = CardUiDefinitions.FormatCost(costType, cost);
        _txtCardCost.Visible = true;
    }

    private void RefreshValueLabel()
    {
        if (_txtCardVal == null)
        {
            return;
        }

        var value = _displayOverride ?? _baseValue;
        _txtCardVal.Text = value.ToString();
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

    private static Texture2D? TryLoadArtTexture(string artPath, string? cardId)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(cardId)
                && TryResolveModArtFile(cardId, artPath, out var modFile)
                && File.Exists(modFile))
            {
                var image = Image.LoadFromFile(modFile);
                if (image != null)
                {
                    return ImageTexture.CreateFromImage(image);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"BaseCardItem: mod art load failed: {ex.Message}");
        }

        var resPath = $"res://Resource/Assets/{artPath.Replace('\\', '/')}";
        try
        {
            if (ResourceLoader.Exists(resPath))
            {
                return ResourceLoader.Load<Texture2D>(resPath);
            }
        }
        catch (Exception ex)
        {
            GD.PushWarning($"BaseCardItem: resource art load failed: {ex.Message}");
        }

        return null;
    }

    private static bool TryResolveModArtFile(string cardId, string artPath, out string fullPath)
    {
        fullPath = "";
        try
        {
            var pipeline = AppRoot.Services.ContentModPipeline;
            if (!pipeline.Registry.TryGetOwnerModId(EContentCategory.Card, cardId, out var modId))
            {
                return false;
            }

            if (!pipeline.ScriptCatalog.TryGetContentRootPath(modId, out var contentRoot))
            {
                return false;
            }

            var relative = artPath.Replace('/', Path.DirectorySeparatorChar);
            fullPath = Path.GetFullPath(Path.Combine(contentRoot, relative));
            var rootFull = Path.GetFullPath(contentRoot);
            if (!fullPath.StartsWith(rootFull, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
            {
                fullPath = "";
                return false;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            // AppRoot 未初始化
            return false;
        }
    }

    #endregion
}
