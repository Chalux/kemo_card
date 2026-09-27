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
    /// 一档主动技的展示文本：<see cref="TierIndex"/> = 蓄力链下标（供调用方对齐"当前可释放档"），
    /// <see cref="Threshold"/> = 累计门槛，名称 / 描述为空时为 <c>""</c>。
    /// </summary>
    public readonly record struct ActiveSkillTierText(int TierIndex, int Threshold, string Name, string Description);

    /// <summary>
    /// 按蓄力链逐档解析展示文本（门槛 = 各档 <c>cooldown</c> 累计值）；解析不到 / 名称与描述全空的档位
    /// 跳过，但仍累计门槛。战斗界面的主动技悬停（<c>CombatActiveSkillTips</c>）与
    /// <see cref="Entries"/> 共用这里，排版各自处理。
    /// </summary>
    public static IReadOnlyList<ActiveSkillTierText> ResolveTiers(
        IEnumerable<(string SkillId, int Cooldown)> chain,
        Func<string, SkillDto?> resolveSkill,
        Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(resolveSkill);
        ArgumentNullException.ThrowIfNull(translate);

        var tiers = new List<ActiveSkillTierText>();
        var threshold = 0;
        var index = -1;
        foreach (var (skillId, cooldown) in chain)
        {
            index++;
            threshold += Math.Max(0, cooldown);
            if (string.IsNullOrWhiteSpace(skillId) || resolveSkill(skillId) is not { } skill)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(skill.DisplayNameId) ? "" : translate(skill.DisplayNameId);
            var description = string.IsNullOrWhiteSpace(skill.DescId) ? "" : translate(skill.DescId);
            if (name.Length == 0 && description.Length == 0)
            {
                continue;
            }

            tiers.Add(new ActiveSkillTierText(index, threshold, name, description));
        }

        return tiers;
    }

    /// <summary>
    /// 单档主动技的 BBCode 文案：<c>[b]名称[/b]（技能 N）{suffix}</c> 换行后接描述。
    /// 名与描述都为空时返回 <c>""</c>；<paramref name="suffix"/> 供调用方附「可用」这类状态标记。
    /// </summary>
    public static string Entry(
        string name,
        int threshold,
        string description,
        Func<string, string> translate,
        string suffix = "")
    {
        ArgumentNullException.ThrowIfNull(translate);

        var header = string.IsNullOrWhiteSpace(name) ? "" : $"[b]{name}[/b]";
        if (threshold > 0)
        {
            header += string.Format(translate(ThresholdKey), threshold);
        }

        header += suffix ?? "";

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

        return
        [
            .. ResolveTiers(
                character.ActiveSkillChain.Select(tier => (tier.SkillId, tier.Cooldown)),
                resolveSkill,
                translate).Select(tier => Entry(tier.Name, tier.Threshold, tier.Description, translate)),
        ];
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
