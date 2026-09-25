using System.Globalization;
using KemoCard.Frame.Gas;

namespace KemoCard.Mod.Global.Ui;

/// <summary>
/// 属性显示名与「卡组属性」列表的唯一拼接口径：队伍编辑预览与卡牌详情的卡组属性加成共用。
/// </summary>
/// <remarks>
/// <para>属性名走内容侧键 <c>attr.&lt;snake_case&gt;.name</c>（见 Run 规格 §12.3 的队伍编辑属性行），
/// 缺键时回落原始 id——直接把属性 id 插进界面会露出 <c>PhysicalAttack</c> 这类英文标识符。</para>
/// <para>展示顺序按 <see cref="PreferredOrder"/>（与战斗面板「物攻·魔攻 / 物防·魔防 / 回复量」的
/// 阅读顺序一致，核心属性在前），未知属性按 id 序排在最后。</para>
/// <para>取 <paramref name="translate"/> 委托而不是直接调 <c>Localization.Tr</c>：拼接逻辑要能脱离
/// Godot 单测（与 <see cref="CharacterIdentityLabels"/> 同约定）。</para>
/// </remarks>
public static class AttributeLabels
{
    /// <summary>属性之间的连接符（单行成列时用，与身份行的字段连接符一致）。</summary>
    public const string Separator = " · ";

    /// <summary>展示顺序：核心面板属性在前，其余按 id 序。</summary>
    private static readonly string[] PreferredOrder =
    [
        AttributeIds.MaxHealth,
        AttributeIds.PhysicalAttack,
        AttributeIds.PhysicalDefense,
        AttributeIds.MagicAttack,
        AttributeIds.MagicDefense,
        AttributeIds.HealPower,
        AttributeIds.Damage,
        AttributeIds.Healing,
        AttributeIds.DamageDealtScale,
        AttributeIds.DamageTakenScale,
        AttributeIds.MaxEnergy,
        AttributeIds.InitialEnergy,
        AttributeIds.NormalAttackCount,
        AttributeIds.NormalAttackDamageDealtScale,
        AttributeIds.NormalAttackDamageTakenScale,
    ];

    /// <summary>属性显示名（如「最大生命」）；缺键时回落原始 id。</summary>
    public static string Name(string attributeId, Func<string, string> translate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeId);
        ArgumentNullException.ThrowIfNull(translate);

        var key = $"attr.{ToSnakeCase(attributeId)}.name";
        var text = translate(key);
        return string.Equals(text, key, StringComparison.Ordinal) ? attributeId : text;
    }

    /// <summary>按展示顺序返回属性对（未知属性排在最后，同序内按 id 排列）。</summary>
    public static IReadOnlyList<KeyValuePair<string, float>> Order(IReadOnlyDictionary<string, float> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return
        [
            .. values
                .OrderBy(pair => PreferredIndexOf(pair.Key))
                .ThenBy(pair => pair.Key, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// 一行式卡组属性加成文案，如「最大生命 +20 · 物攻 +2」；无有效属性时返回 <c>""</c>。
    /// 数值带正负号（正数补 <c>+</c>），便于玩家识别这是相对面板的加成而非面板值。
    /// </summary>
    public static string FormatContributions(
        IReadOnlyDictionary<string, float> values,
        Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(translate);

        var entries = Order(values);
        if (entries.Count == 0)
        {
            return "";
        }

        return string.Join(
            Separator,
            entries.Select(pair =>
                $"{Name(pair.Key, translate)} {pair.Value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture)}"));
    }

    private static int PreferredIndexOf(string attributeId)
    {
        var index = Array.IndexOf(PreferredOrder, attributeId);
        return index < 0 ? int.MaxValue : index;
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}