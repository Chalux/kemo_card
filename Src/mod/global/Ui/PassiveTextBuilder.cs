namespace KemoCard.Mod.Global.Ui;

/// <summary>
/// 角色被动的展示文案口径：门槛（潜能 N）+ 可选的解锁状态 + 描述（buff 的 descId）。
/// 角色详情与卡组编辑左栏共用，避免两处各拼一套（格式漂移会导致同一被动显示不一致）。
/// </summary>
/// <remarks>
/// <para>取 <paramref name="translate"/> 委托而不是直接调 <c>Localization.Tr</c>：拼接逻辑要能脱离
/// Godot 单测（与 <see cref="CharacterIdentityLabels"/> / <see cref="AttributeLabels"/> 同约定）。</para>
/// <para>解锁状态只在有 Run 上下文时显示（图鉴里还没进 Run 的角色不显示）；文案里的
/// <c>[url=kw:*]</c> 词条标记由 RichTextLabel 的 BBCode 管线渲染。</para>
/// </remarks>
public static class PassiveTextBuilder
{
    /// <summary>区标题键（「潜能被动」）；角色详情内联在正文里，卡组编辑用作 Caption 标题。</summary>
    public const string TitleKey = "UI_CHARACTER_PASSIVES_TITLE";

    private const string ThresholdKey = "UI_CHARACTER_PASSIVE_THRESHOLD";
    private const string ThresholdZeroKey = "UI_CHARACTER_PASSIVE_THRESHOLD_ZERO";
    private const string UnlockedKey = "UI_CHARACTER_PASSIVE_UNLOCKED";
    private const string LockedKey = "UI_CHARACTER_PASSIVE_LOCKED";

    /// <summary>
    /// 单条被动的 BBCode 文案：<c>[b]潜能 N[/b]【已解锁】</c> 换行后接描述。
    /// <paramref name="unlocked"/> 为 <c>null</c>（无 Run 上下文）时不显示解锁状态；
    /// 描述为空时只留门槛行。
    /// </summary>
    public static string Entry(int requiredPotential, bool? unlocked, string description, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);

        var threshold = requiredPotential > 0
            ? string.Format(translate(ThresholdKey), requiredPotential)
            : translate(ThresholdZeroKey);
        var state = unlocked switch
        {
            true => translate(UnlockedKey),
            false => translate(LockedKey),
            _ => "",
        };

        return $"[b]{threshold}[/b]{state}\n{description}";
    }
}