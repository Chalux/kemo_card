namespace KemoCard.Frame.Content.Definitions;

/// <summary>
/// 伤害的两个维度（战斗规格「伤害包管线」§1.3）在内容侧的声明结果：
/// <see cref="Kind"/> = 物理 / 魔法 / 元素（决定吃不吃防御、吃哪一条防御），
/// <see cref="Element"/> = 红/蓝/绿/黄属性标签（供连携统计与未来的克制/抗性使用）。
/// </summary>
public readonly record struct DamageTypeSpec(EDamageKind Kind, EElement Element)
{
    /// <summary>缺省口径：物理 + 无属性，与 <see cref="ExecutionDefDto"/> 的默认值一致。</summary>
    public static DamageTypeSpec Default { get; } = new(EDamageKind.Physical, EElement.None);
}

/// <summary>
/// <c>ExecutionDefDto.damageType</c> / <c>element</c> 字符串的解析器（2026-09-21 落地，此前是战斗规格 §7 的后置项）。
/// </summary>
/// <remarks>
/// <para><b>kind</b>：<c>Physical</c>（默认）/ <c>Magical</c> / <c>Elemental</c>，大小写不敏感。</para>
/// <para><b>element</b>：<c>None</c> / <c>Red</c> / <c>Blue</c> / <c>Green</c> / <c>Yellow</c>，
/// 多属性用 <c>,</c> 或 <c>|</c> 分隔（如 <c>"Blue,Green"</c>）。</para>
/// <para><b>旧写法兼容</b>：<c>damageType</c> 直接写属性名（如 <c>"Blue"</c>）等价于
/// 物理 + 该属性；出货内容已迁移为显式的 <c>damageType</c> + <c>element</c> 两字段，但旧值仍被接受，
/// 避免第三方 Mod 的既有内容在升级后静默变成"无属性物理伤害"。</para>
/// </remarks>
public static class DamageTypeParser
{
    private const string PhysicalName = nameof(EDamageKind.Physical);
    private const string MagicalName = nameof(EDamageKind.Magical);
    private const string ElementalName = nameof(EDamageKind.Elemental);
    private const string NoneName = nameof(EElement.None);

    /// <summary>宽松解析：无法识别的取值回落到 <see cref="DamageTypeSpec.Default"/>（运行期不做校验，校验归内容准入）。</summary>
    public static DamageTypeSpec Parse(string? damageType, string? element) =>
        TryParse(damageType, element, out var spec, out _) ? spec : DamageTypeSpec.Default;

    /// <summary>
    /// 严格解析：未知取值返回 false 并给出可直接进 <c>ContentDefinitionValidator</c> 的错误文案。
    /// </summary>
    public static bool TryParse(
        string? damageType,
        string? element,
        out DamageTypeSpec spec,
        out string? error)
    {
        spec = DamageTypeSpec.Default;
        error = null;

        var kind = EDamageKind.Physical;
        var parsedElement = EElement.None;

        if (!string.IsNullOrWhiteSpace(damageType))
        {
            var text = damageType.Trim();
            if (IsKindName(text))
            {
                kind = ParseKind(text);
            }
            else if (Enum.TryParse<EElement>(text, ignoreCase: true, out var legacyElement) &&
                     legacyElement != EElement.None)
            {
                // 旧写法：damageType 承担属性标签，伤害类型按物理算（迁移前 blue_damage 等即此形态）。
                parsedElement = legacyElement;
            }
            else
            {
                error = $"Unknown damageType '{damageType}' (expected {PhysicalName}/{MagicalName}/{ElementalName}"
                    + $" or an element name {NoneName}/{nameof(EElement.Red)}/{nameof(EElement.Blue)}"
                    + $"/{nameof(EElement.Green)}/{nameof(EElement.Yellow)}).";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(element) &&
            !TryParseElements(element, out parsedElement, out error))
        {
            return false;
        }

        spec = new DamageTypeSpec(kind, parsedElement);
        return true;
    }

    private static bool IsKindName(string text) =>
        text.Equals(PhysicalName, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(MagicalName, StringComparison.OrdinalIgnoreCase) ||
        text.Equals(ElementalName, StringComparison.OrdinalIgnoreCase);

    private static EDamageKind ParseKind(string text) =>
        text.Equals(MagicalName, StringComparison.OrdinalIgnoreCase) ? EDamageKind.Magical
        : text.Equals(ElementalName, StringComparison.OrdinalIgnoreCase) ? EDamageKind.Elemental
        : EDamageKind.Physical;

    /// <summary>属性字段：允许 <c>None</c>、单个属性名，或 <c>,</c> / <c>|</c> 分隔的多属性（按位或合并）。</summary>
    private static bool TryParseElements(string text, out EElement element, out string? error)
    {
        element = EElement.None;
        error = null;

        foreach (var raw in text.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries))
        {
            var name = raw.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            if (!Enum.TryParse<EElement>(name, ignoreCase: true, out var parsed))
            {
                error = $"Unknown element '{name}' (expected {NoneName}/{nameof(EElement.Red)}/{nameof(EElement.Blue)}"
                    + $"/{nameof(EElement.Green)}/{nameof(EElement.Yellow)}).";
                element = EElement.None;
                return false;
            }

            element |= parsed;
        }

        return true;
    }
}
