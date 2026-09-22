using Godot;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Global.Def;

public static class ColorDefinitions
{
    public static readonly Color NoneElement = new("#747474");
    public static readonly Color RedElement = new("#f10101");
    public static readonly Color BlueElement = new("#3099f2");
    public static readonly Color GreenElement = new("#28ff00");
    public static readonly Color YellowElement = new("#ffc800");
}

public static class CardUiDefinitions
{
    private static readonly Dictionary<ECardType, string> CardTypeLocaleKeys = new()
    {
        [ECardType.Physics] = "UI_CARD_TYPE_PHYSICS",
        [ECardType.Magical] = "UI_CARD_TYPE_MAGICAL",
        [ECardType.Support] = "UI_CARD_TYPE_SUPPORT",
        [ECardType.Guard] = "UI_CARD_TYPE_GUARD",
        [ECardType.Resist] = "UI_CARD_TYPE_RESIST",
        [ECardType.Weak] = "UI_CARD_TYPE_WEAK",
        [ECardType.Counter] = "UI_CARD_TYPE_COUNTER",
        [ECardType.Healing] = "UI_CARD_TYPE_HEALING",
        [ECardType.Curse] = "UI_CARD_TYPE_CURSE",
    };

    private static readonly Dictionary<ERarity, string> CardFramePaths = new()
    {
        [ERarity.Common] = "res://Resource/Assets/CardFrame/Common.png",
        [ERarity.Special] = "res://Resource/Assets/CardFrame/Special.png",
        [ERarity.Rare] = "res://Resource/Assets/CardFrame/Rare.png",
        [ERarity.Exclusive] = "res://Resource/Assets/CardFrame/Exclusive.png",
        [ERarity.Legendary] = "res://Resource/Assets/CardFrame/Legendary.png",
    };

    private static readonly EElement[] ElementOrder =
    [
        EElement.Red,
        EElement.Blue,
        EElement.Green,
        EElement.Yellow,
    ];

    public static bool TryGetCardTypeLocaleKey(ECardType type, out string key) =>
        CardTypeLocaleKeys.TryGetValue(type, out key!);

    public static bool TryGetCardFramePath(ERarity rarity, out string path) =>
        CardFramePaths.TryGetValue(rarity, out path!);

    public static bool TryGetElementColor(EElement element, out Color color)
    {
        switch (element)
        {
            case EElement.Red:
                color = ColorDefinitions.RedElement;
                return true;
            case EElement.Blue:
                color = ColorDefinitions.BlueElement;
                return true;
            case EElement.Green:
                color = ColorDefinitions.GreenElement;
                return true;
            case EElement.Yellow:
                color = ColorDefinitions.YellowElement;
                return true;
            default:
                color = default;
                return false;
        }
    }

    /// <summary>
    /// 按 EElement 位顺序收集最多 3 色；无有效位时返回单色 NoneElement。
    /// </summary>
    public static Color[] CollectElementColors(int flags)
    {
        var list = new List<Color>(3);
        foreach (var element in ElementOrder)
        {
            if ((flags & (int)element) == 0)
            {
                continue;
            }

            if (!TryGetElementColor(element, out var color))
            {
                continue;
            }

            list.Add(color);
            if (list.Count >= 3)
            {
                break;
            }
        }

        if (list.Count == 0)
        {
            return [ColorDefinitions.NoneElement];
        }

        return list.ToArray();
    }

    public static string FormatCost(ECostType costType, int cost) =>
        costType switch
        {
            ECostType.None => "",
            ECostType.X => "X",
            ECostType.Discard => "∅",
            _ => cost.ToString(),
        };

    public const string CostTipSuffixKey = "UI_CARD_TIP_COST_SUFFIX";
    public const string CostTipSuffixXKey = "UI_CARD_TIP_COST_SUFFIX_X";
    public const string ExclusivePrefixKey = "UI_CARD_TIP_EXCLUSIVE_PREFIX";

    /// <summary>按费用类型取 tip 后缀本地化键；未知类型回退到正常后缀。</summary>
    public static bool TryGetCostTipSuffixKey(ECostType costType, out string key)
    {
        switch (costType)
        {
            case ECostType.None:
                key = "";
                return false;
            case ECostType.X:
                key = CostTipSuffixXKey;
                return true;
            default:
                key = CostTipSuffixKey;
                return true;
        }
    }

    public static string FormatCostForTip(ECostType costType, int cost, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);
        if (costType == ECostType.None || !TryGetCostTipSuffixKey(costType, out var suffixKey))
        {
            return "";
        }

        var suffix = translate(suffixKey);
        return costType == ECostType.X
            ? $"X{suffix}"
            : $"{cost}{suffix}";
    }

}