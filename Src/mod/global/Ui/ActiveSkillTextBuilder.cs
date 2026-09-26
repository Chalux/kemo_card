using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Global.Ui;

/// <summary>
/// 角色主动技的展示文案口径：名称 + 蓄力门槛（技能 N）+ 描述（技能的 <c>descId</c>）。
/// 角色详情、卡组编辑左栏与角色悬停摘要共用，避免三处各拼一套。
/// </summary>
/// <remarks>
/// <para>门槛 = 蓄力链各档 <c>cooldown</c> 的**累计值**（战斗规格 §5.1/§5.2：第 k 档在
/// <c>S ≥ ΣC0..Ck</c> 时可放），因此多档角色逐档显示 6 / 10 / 16 这类阈值。</para>
/// <para>取 <paramref name="translate"/> 委托而不是直接调 <c>Localization.Tr</c>：拼接逻辑要能脱离
/// Godot 单测（与 <see cref="PassiveTextBuilder"/> / <see cref="AttributeLabels"/> 同约定）。</para>
/// </remarks>
public static class ActiveSkillTextBuilder
{
    /// <summary>区标题键（「主动技」）；角色详情内联在正文里，卡组编辑用作 Caption 标题。</summary>
    public const string TitleKey = "UI_CHARACTER_ACTIVE_SKILL_TITLE";

    private const string ThresholdKey = "UI_CHARACTER_ACTIVE_SKILL_THRESHOLD";

    /// <summary>
    /// 单档主动技的 BBCode 文案：<c>[b]名称[/b]（技能 N）</c> 换行后接描述。
    /// 名与描述都为空时返回 <c>""</c>。
    /// </summary>
    public static string Entry(string name, int threshold, string description, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(translate);

        var header = string.IsNullOrWhiteSpace(name) ? "" : $"[b]{name}[/b]";
        if (threshold > 0)
        {
            header += string.Format(translate(ThresholdKey), threshold);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return header;
        }

        return header.Length == 0 ? description : $"{header}\n{description}";
    }

    /// <summary>
    /// 按蓄力链逐档生成条目（门槛为各档 <c>cooldown</c> 的累计值）；空链 / 全部无法解析时返回空列表。
    /// 解析不到的档位仍累计门槛（后续档位的阈值不变），只是不产出条目。
    /// </summary>
    public static IReadOnlyList<string> Entries(
        CharacterDto character,
        Func<string, SkillDto?> resolveSkill,
        Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(resolveSkill);
        ArgumentNullException.ThrowIfNull(translate);

        var entries = new List<string>(character.ActiveSkillChain.Count);
        var threshold = 0;
        foreach (var tier in character.ActiveSkillChain)
        {
            threshold += Math.Max(0, tier.Cooldown);
            if (string.IsNullOrWhiteSpace(tier.SkillId) || resolveSkill(tier.SkillId) is not { } skill)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(skill.DisplayNameId) ? "" : translate(skill.DisplayNameId);
            var description = string.IsNullOrWhiteSpace(skill.DescId) ? "" : translate(skill.DescId);
            var entry = Entry(name, threshold, description, translate);
            if (entry.Length > 0)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>整块 BBCode 文案（区标题 + 各档，<c>\n</c> 分隔）；无内容时返回 <c>""</c>。</summary>
    public static string Block(
        CharacterDto character,
        Func<string, SkillDto?> resolveSkill,
        Func<string, string> translate)
    {
        var entries = Entries(character, resolveSkill, translate);
        return entries.Count == 0
            ? ""
            : $"[b]{translate(TitleKey)}[/b]\n{string.Join("\n", entries)}";
    }
}
