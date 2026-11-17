using System;
using System.Collections.Generic;
using System.Linq;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Global.Ui;

/// <summary>行动表里的一项：技能显示名 + 该项需要等待的敌方回合数（内容字段 <c>actionCount</c>）。</summary>
/// <param name="Index">展示序号（从 1 起），对应内容里 <c>actionSequence</c> 的下标 + 1。</param>
public sealed record BattleCodexAction(int Index, string SkillName, int ActionCount);

/// <summary>一条生命条的行动表：敌人按 <see cref="Actions"/> 循环行动。</summary>
/// <param name="Index">生命条序号（从 1 起）。</param>
/// <param name="MaxHp">该条的满血值（未乘生成配置的 <c>hpScale</c>）。</param>
/// <param name="PrioritySkillName">满足同伴标签 / 召唤条件时替代常规循环的技能；无条件时为 <c>null</c>。</param>
public sealed record BattleCodexLifeBar(
    int Index,
    int MaxHp,
    string? PrioritySkillName,
    IReadOnlyList<string> EntrySkillNames,
    IReadOnlyList<BattleCodexAction> Actions);

/// <summary>一波里的一个敌人生成条目（含其元素 / 定位 / 种族与行动表）。</summary>
/// <param name="InitialActionIndex">首条循环的 0 起起手下标（<c>initialActionIndex</c> 错位起手）。</param>
public sealed record BattleCodexSpawn(
    string EnemyId,
    string Name,
    int Count,
    double? HpScale,
    int InitialActionIndex,
    int MaxHp,
    string ElementText,
    string RoleText,
    string RaceText,
    IReadOnlyList<string> EntrySkillNames,
    IReadOnlyList<BattleCodexLifeBar> LifeBars);

/// <summary>一波：按 <paramref name="Index"/>（从 1 起）排序的敌人生成列表。</summary>
public sealed record BattleCodexWave(int Index, IReadOnlyList<BattleCodexSpawn> Spawns);

/// <summary>一场战斗的展示模型（右侧详情面板的数据源）。</summary>
public sealed record BattleCodexDetail(
    string BattleId,
    string Title,
    string Desc,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> CombatRuleIds,
    IReadOnlyList<BattleCodexWave> Waves);

/// <summary>
/// 图鉴「战斗」页签的数据装配：列出全部战斗、按文本筛选，并把选中战斗展开成
/// 「波次 → 敌人生成 → 生命条行动表」的展示模型。
/// </summary>
/// <remarks>
/// <para>纯逻辑、不引用 Godot（翻译经 <paramref name="translate"/> 委托注入），可脱离引擎单测；
/// 文案渲染在 <see cref="BattleCodexText"/>，对话框只做取值与显示。</para>
/// <para>该页签只随调试构建出现（见 <c>CodexDlg</c> 的 <c>OS.IsDebugBuild()</c> 门控），
/// 因此这里不做解锁 / 已见过滤——调试时就是要看到全部内容，包括尚未在任何环里出现的战斗。</para>
/// </remarks>
public static class BattleCodexBuilder
{
    /// <summary>
    /// 收集全部战斗（按 id 升序，保证列表顺序稳定）。
    /// <paramref name="textQuery"/> 同时匹配战斗 id / 名称 / 描述 / 标签与波次里的敌人 id / 名称。
    /// </summary>
    public static IReadOnlyList<BattleDto> Collect(
        GameDefinitionStore store,
        string textQuery,
        Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(translate);

        var query = textQuery?.Trim() ?? "";
        return store.Battles.Values
            .Where(battle => string.IsNullOrEmpty(query) || MatchesText(battle, query, store, translate))
            .OrderBy(battle => battle.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>战斗的列表行文案：缺翻译时回落 id，避免出现空白行。</summary>
    public static string RowTitle(BattleDto battle, Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(translate);

        var name = Named(battle.DisplayNameId, battle.Id, translate);
        return string.IsNullOrWhiteSpace(name) ? battle.Id : name;
    }

    /// <summary>
    /// 把一场战斗展开成展示模型：描述 / 标签 / 战斗规则 + 每波的每个敌人生成条目
    /// （元素 · 定位 · 种族 + 各生命条的行动表 + 入场技）。
    /// </summary>
    public static BattleCodexDetail BuildDetail(
        BattleDto battle,
        GameDefinitionStore store,
        Func<string, string> translate)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(translate);

        var waves = new List<BattleCodexWave>(battle.Waves.Count);
        for (var i = 0; i < battle.Waves.Count; i++)
        {
            var wave = battle.Waves[i];
            var spawns = wave.EnemySpawns
                .Select(spawn => BuildSpawn(spawn, store, translate))
                .ToList();
            waves.Add(new BattleCodexWave(i + 1, spawns));
        }

        return new BattleCodexDetail(
            battle.Id,
            Named(battle.DisplayNameId, battle.Id, translate),
            Named(battle.DescId, "", translate),
            Clean(battle.Tags),
            Clean(battle.CombatRuleIds),
            waves);
    }

    private static BattleCodexSpawn BuildSpawn(
        EnemySpawnDto spawn,
        GameDefinitionStore store,
        Func<string, string> translate)
    {
        if (!store.TryGetEnemy(spawn.EnemyId, out var enemy))
        {
            // 内容校验会拦住悬空引用；这里只保证调试页签不因单个坏条目整页失败。
            return new BattleCodexSpawn(
                spawn.EnemyId,
                spawn.EnemyId,
                spawn.Count,
                spawn.HpScale,
                spawn.InitialActionIndex,
                0,
                "",
                "",
                "",
                [],
                []);
        }

        return new BattleCodexSpawn(
            enemy.Id,
            Named(enemy.DisplayNameId, enemy.Id, translate),
            spawn.Count,
            spawn.HpScale,
            spawn.InitialActionIndex,
            ResolveMaxHp(enemy),
            CharacterIdentityLabels.Element(enemy.Element, translate),
            CharacterIdentityLabels.Role(enemy.Role, translate),
            CharacterIdentityLabels.Race(enemy.Race, translate),
            SkillNames(enemy.EntrySkillRefs, store, translate),
            BuildLifeBars(enemy, store, translate));
    }

    private static IReadOnlyList<BattleCodexLifeBar> BuildLifeBars(
        EnemyDto enemy,
        GameDefinitionStore store,
        Func<string, string> translate)
    {
        var bars = new List<BattleCodexLifeBar>(enemy.LifeBars.Count);
        for (var i = 0; i < enemy.LifeBars.Count; i++)
        {
            var bar = enemy.LifeBars[i];
            var actions = new List<BattleCodexAction>(bar.ActionSequence.Count);
            for (var step = 0; step < bar.ActionSequence.Count; step++)
            {
                var action = bar.ActionSequence[step];
                actions.Add(new BattleCodexAction(
                    step + 1,
                    SkillName(action.SkillId, store, translate),
                    action.ActionCount));
            }

            bars.Add(new BattleCodexLifeBar(
                i + 1,
                bar.MaxHp,
                string.IsNullOrWhiteSpace(bar.PrioritySkillId) ? null : SkillName(bar.PrioritySkillId, store, translate),
                SkillNames(bar.EntrySkillRefs, store, translate),
                actions));
        }

        return bars;
    }

    /// <summary>无生命条配置的敌人沿用 <c>maxHp</c> 与随机技能，展示时取其单条血量。</summary>
    private static int ResolveMaxHp(EnemyDto enemy) =>
        enemy.LifeBars.Count > 0 ? enemy.LifeBars[0].MaxHp : enemy.MaxHp;

    private static IReadOnlyList<string> SkillNames(
        IReadOnlyList<SkillRefDto> refs,
        GameDefinitionStore store,
        Func<string, string> translate) =>
        Clean(refs.Select(reference => SkillName(reference.SkillId, store, translate)));

    /// <summary>技能显示名：优先翻译 <c>displayNameId</c>，缺定义 / 缺翻译时回落技能 id。</summary>
    private static string SkillName(string skillId, GameDefinitionStore store, Func<string, string> translate)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return "";
        }

        if (store.TryGetSkill(skillId, out var skill) && !string.IsNullOrWhiteSpace(skill.DisplayNameId))
        {
            var name = translate(skill.DisplayNameId);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return skillId;
    }

    private static bool MatchesText(
        BattleDto battle,
        string query,
        GameDefinitionStore store,
        Func<string, string> translate)
    {
        if (ContainsIgnoreCase(battle.Id, query)
            || ContainsIgnoreCase(battle.DisplayNameId, query)
            || ContainsIgnoreCase(translate(battle.DisplayNameId), query)
            || ContainsIgnoreCase(translate(battle.DescId), query)
            || battle.Tags.Any(tag => ContainsIgnoreCase(tag, query)))
        {
            return true;
        }

        foreach (var wave in battle.Waves)
        {
            foreach (var spawn in wave.EnemySpawns)
            {
                if (ContainsIgnoreCase(spawn.EnemyId, query))
                {
                    return true;
                }

                if (store.TryGetEnemy(spawn.EnemyId, out var enemy)
                    && (ContainsIgnoreCase(enemy.Id, query)
                        || ContainsIgnoreCase(enemy.DisplayNameId, query)
                        || ContainsIgnoreCase(translate(enemy.DisplayNameId), query)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>翻译显示名；<paramref name="displayNameId"/> 为空时直接返回 <paramref name="fallback"/>。</summary>
    private static string Named(string? displayNameId, string fallback, Func<string, string> translate) =>
        string.IsNullOrWhiteSpace(displayNameId) ? fallback : translate(displayNameId);

    /// <summary>去掉空项并按 id 顺序去重（保持内容声明顺序）。</summary>
    private static IReadOnlyList<string> Clean(IEnumerable<string> values)
    {
        var result = new List<string>();
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value, StringComparer.Ordinal))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static bool ContainsIgnoreCase(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
