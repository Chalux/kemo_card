using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KemoCard.Mod.Global.Ui;

/// <summary>
/// 把 <see cref="BattleCodexDetail"/> 渲染成 <c>RichTextLabel</c> 的 BBCode 正文
/// （图鉴「战斗」页签右侧详情）。
/// </summary>
/// <remarks>
/// <para>纯逻辑、不引用 Godot（翻译经 <paramref name="translate"/> 委托注入），可单测；
/// 身份行沿用角色详情那一套键（<c>UI_CHARACTER_META_*</c>），保证两处显示口径一致。</para>
/// <para>敌人没有元素 / 种族时显示 <see cref="NoneKey"/> 而不是整段省略：调试时"确认这里真的是空"
/// 比"看不见这一行、怀疑是渲染漏了"更有用。</para>
/// </remarks>
public static class BattleCodexText
{
    /// <summary>列表与详情里「无该字段」的占位文案（敌人的元素 / 定位 / 种族为空时使用）。</summary>
    public const string NoneKey = "UI_BATTLE_VALUE_NONE";

    public const string TagsKey = "UI_BATTLE_TAGS";
    public const string RulesKey = "UI_BATTLE_RULES";
    public const string WavesKey = "UI_BATTLE_WAVES";
    public const string WaveTitleKey = "UI_BATTLE_WAVE_TITLE";
    public const string ActionTableKey = "UI_BATTLE_ACTION_TABLE";
    public const string ActionTableNoneKey = "UI_BATTLE_ACTION_TABLE_NONE";
    public const string LifeBarKey = "UI_BATTLE_LIFE_BAR";
    public const string ActionStepKey = "UI_BATTLE_ACTION_STEP";
    public const string EntrySkillsKey = "UI_BATTLE_ENTRY_SKILLS";
    public const string PrioritySkillKey = "UI_BATTLE_PRIORITY_SKILL";
    public const string SpawnLineKey = "UI_BATTLE_SPAWN_LINE";
    public const string HpKey = "UI_BATTLE_HP";
    public const string HpScaleKey = "UI_BATTLE_HP_SCALE";
    public const string StartIndexKey = "UI_BATTLE_START_INDEX";

    /// <summary>战斗详情空白态的提示文案（未选中任何战斗）。</summary>
    public const string EmptyDetailKey = "UI_BATTLE_DETAIL_EMPTY";

    private const string ElementLabelKey = "UI_CHARACTER_META_ELEMENT";
    private const string RoleLabelKey = "UI_CHARACTER_META_ROLE";
    private const string RaceLabelKey = "UI_CHARACTER_META_RACE";
    private const string FieldFormatKey = "UI_CHARACTER_META_FIELD";

    /// <summary>字段之间的连接符（与 <see cref="CharacterIdentityLabels"/> 同口径）。</summary>
    private const string FieldSeparator = " · ";

    /// <summary>多值（标签 / 战斗规则 / 入场技）之间的连接符。</summary>
    private const string ListSeparator = "、";

    public static string Render(BattleCodexDetail detail, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(translate);

        var sb = new StringBuilder();
        AppendLine(sb, $"[b]{Escape(detail.Title)}[/b]");

        if (!string.IsNullOrWhiteSpace(detail.BattleId))
        {
            AppendLine(sb, $"[i]{Escape(detail.BattleId)}[/i]");
        }

        if (!string.IsNullOrWhiteSpace(detail.Desc))
        {
            AppendLine(sb, "");
            AppendLine(sb, Escape(detail.Desc));
        }

        if (detail.Tags.Count > 0)
        {
            AppendLine(sb, $"{translate(TagsKey)}：{Join(detail.Tags)}");
        }

        if (detail.CombatRuleIds.Count > 0)
        {
            AppendLine(sb, $"{translate(RulesKey)}：{Join(detail.CombatRuleIds)}");
        }

        AppendLine(sb, $"{translate(WavesKey)}：{detail.Waves.Count}");

        foreach (var wave in detail.Waves)
        {
            AppendLine(sb, "");
            AppendLine(sb, $"[b]{Escape(string.Format(translate(WaveTitleKey), wave.Index))}[/b]");

            foreach (var spawn in wave.Spawns)
            {
                AppendLine(sb, "");
                AppendSpawn(sb, spawn, translate);
            }
        }

        return sb.ToString();
    }

    private static void AppendSpawn(StringBuilder sb, BattleCodexSpawn spawn, Func<string, string> translate)
    {
        var parts = new List<string>
        {
            string.Format(translate(SpawnLineKey), Escape(spawn.Name), spawn.Count),
            string.Format(translate(HpKey), spawn.MaxHp),
        };

        if (spawn.HpScale is { } scale)
        {
            parts.Add(string.Format(translate(HpScaleKey), FormatScale(scale)));
        }

        if (spawn.InitialActionIndex > 0)
        {
            parts.Add(string.Format(translate(StartIndexKey), spawn.InitialActionIndex + 1));
        }

        AppendLine(sb, $"[b]{string.Join(FieldSeparator, parts)}[/b]");
        AppendLine(sb, IdentityLine(spawn, translate));

        if (spawn.EntrySkillNames.Count > 0)
        {
            AppendLine(sb, string.Format(translate(EntrySkillsKey), Join(spawn.EntrySkillNames)));
        }

        AppendLine(sb, $"[b]{translate(ActionTableKey)}[/b]");
        if (spawn.LifeBars.Count == 0)
        {
            AppendLine(sb, translate(ActionTableNoneKey));
            return;
        }

        foreach (var bar in spawn.LifeBars)
        {
            AppendLine(sb, string.Format(translate(LifeBarKey), bar.Index, bar.MaxHp));

            if (bar.PrioritySkillName is { } priority)
            {
                AppendLine(sb, Indent(string.Format(translate(PrioritySkillKey), Escape(priority))));
            }

            if (bar.EntrySkillNames.Count > 0)
            {
                AppendLine(sb, Indent(string.Format(translate(EntrySkillsKey), Join(bar.EntrySkillNames))));
            }

            foreach (var action in bar.Actions)
            {
                AppendLine(sb, Indent(string.Format(
                    translate(ActionStepKey),
                    action.Index,
                    Escape(action.SkillName),
                    action.ActionCount)));
            }
        }
    }

    /// <summary>「元素：… · 定位：… · 种族：…」；空字段显示占位文案而不是省略整段。</summary>
    private static string IdentityLine(BattleCodexSpawn spawn, Func<string, string> translate)
    {
        var none = translate(NoneKey);
        return string.Join(
            FieldSeparator,
            Field(translate, ElementLabelKey, Value(spawn.ElementText, none)),
            Field(translate, RoleLabelKey, Value(spawn.RoleText, none)),
            Field(translate, RaceLabelKey, Value(spawn.RaceText, none)));
    }

    private static string Field(Func<string, string> translate, string labelKey, string value) =>
        string.Format(translate(FieldFormatKey), translate(labelKey), value);

    private static string Value(string text, string none) => string.IsNullOrEmpty(text) ? none : text;

    private static string Join(IEnumerable<string> values) => string.Join(ListSeparator, values);

    /// <summary>行动表条目缩进：BBCode 没有缩进，用两个半角空格排出层级。</summary>
    private static string Indent(string text) => "  " + text;

    private static string FormatScale(double scale) =>
        Math.Abs(scale - Math.Round(scale)) < 0.0001
            ? ((int)Math.Round(scale)).ToString()
            : scale.ToString("0.##");

    /// <summary>BBCode 转义：内容里的 <c>[</c> 会被当成标签起始（如 <c>[lb]</c> 自身）。</summary>
    private static string Escape(string text) => text.Replace("[", "[lb]");

    private static void AppendLine(StringBuilder sb, string text)
    {
        if (sb.Length > 0)
        {
            sb.Append('\n');
        }

        sb.Append(text);
    }
}
