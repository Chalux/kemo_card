using System.Reflection;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 图鉴「战斗」页签的数据装配与正文渲染（<see cref="BattleCodexBuilder"/> / <see cref="BattleCodexText"/>）。
/// 两个类都是纯逻辑（不引用 Godot），因此不需要引擎就能验证「行动表 / 种族 / 元素」的取数与文案口径。
/// </summary>
[TestFixture]
public sealed class BattleCodexBuilderTests
{
    /// <summary>
    /// 假翻译表按**字面键**映射（与 <see cref="CharacterIdentityLabelsTests"/> 同约定）：
    /// 键名写错时 <c>Tr</c> 原样返回键，断言里的中文就不会出现——本表同时充当「键名没写错」的守卫。
    /// </summary>
    private static string Tr(string key) => key switch
    {
        "UI_BATTLE_TAGS" => "标签",
        "UI_BATTLE_RULES" => "战斗规则",
        "UI_BATTLE_WAVES" => "波次",
        "UI_BATTLE_WAVE_TITLE" => "第 {0} 波",
        "UI_BATTLE_ACTION_TABLE" => "行动表",
        "UI_BATTLE_ACTION_TABLE_NONE" => "无生命条配置（随机技能）",
        "UI_BATTLE_LIFE_BAR" => "生命条 {0} · 生命 {1}",
        "UI_BATTLE_ACTION_STEP" => "{0}. {1}（行动计数 {2}）",
        "UI_BATTLE_ENTRY_SKILLS" => "入场技：{0}",
        "UI_BATTLE_PRIORITY_SKILL" => "优先行动：{0}",
        "UI_BATTLE_SPAWN_LINE" => "{0} ×{1}",
        "UI_BATTLE_HP" => "生命 {0}",
        "UI_BATTLE_HP_SCALE" => "生命倍率 {0}",
        "UI_BATTLE_START_INDEX" => "起手第 {0} 项",
        "UI_BATTLE_VALUE_NONE" => "无",
        "UI_CHARACTER_META_FIELD" => "{0}：{1}",
        "UI_CHARACTER_META_ELEMENT" => "元素",
        "UI_CHARACTER_META_ROLE" => "定位",
        "UI_CHARACTER_META_RACE" => "种族",
        "UI_ELEMENT_YELLOW" => "黄",
        "UI_ROLE_NONE" => "无",
        "UI_RACE_ANIMAL" => "动物",
        "battle.bear.name" => "黑熊战",
        "battle.bear.desc" => "森林里的两头熊。",
        "enemy.bear.name" => "黑熊",
        "skill.bear_double.name" => "双击",
        "skill.bear_roar.name" => "咆哮",
        "skill.bear_entry.name" => "入场怒吼",
        _ => key,
    };

    private static GameDefinitionStore Store()
    {
        var store = new GameDefinitionStore();

        store.SkillsMutable["bear_double"] = new SkillDto { Id = "bear_double", DisplayNameId = "skill.bear_double.name" };
        store.SkillsMutable["bear_roar"] = new SkillDto { Id = "bear_roar", DisplayNameId = "skill.bear_roar.name" };
        store.SkillsMutable["bear_entry"] = new SkillDto { Id = "bear_entry", DisplayNameId = "skill.bear_entry.name" };

        store.EnemiesMutable["bear"] = new EnemyDto
        {
            Id = "bear",
            DisplayNameId = "enemy.bear.name",
            MaxHp = 300,
            Element = EElement.Yellow,
            Role = ERole.None,
            Race = ERace.Animal,
            EntrySkillRefs = [new SkillRefDto { SkillId = "bear_entry" }],
            LifeBars =
            [
                new EnemyLifeBarDto
                {
                    MaxHp = 1200,
                    PrioritySkillId = "bear_roar",
                    EntrySkillRefs = [new SkillRefDto { SkillId = "bear_entry" }],
                    ActionSequence =
                    [
                        new EnemyActionDto { SkillId = "bear_double", ActionCount = 1 },
                        new EnemyActionDto { SkillId = "bear_roar", ActionCount = 2 },
                    ],
                },
            ],
        };

        // 无生命条、无元素 / 种族的敌人：沿用 maxHp 与随机技能。
        store.EnemiesMutable["dummy"] = new EnemyDto { Id = "dummy", DisplayNameId = "", MaxHp = 100 };

        store.BattlesMutable["bear"] = new BattleDto
        {
            Id = "bear",
            DisplayNameId = "battle.bear.name",
            DescId = "battle.bear.desc",
            Tags = ["boss"],
            CombatRuleIds = ["all_enemies_defeated_victory"],
            Waves =
            [
                new BattleWaveDto
                {
                    EnemySpawns =
                    [
                        new EnemySpawnDto { EnemyId = "bear", Count = 2, HpScale = 1.5, InitialActionIndex = 1 },
                        new EnemySpawnDto { EnemyId = "dummy" },
                    ],
                },
            ],
        };

        store.BattlesMutable["alpha"] = new BattleDto { Id = "alpha", DisplayNameId = "battle.alpha.name" };

        return store;
    }

    [Test]
    public void Collect_returns_all_battles_sorted_by_id()
    {
        var battles = BattleCodexBuilder.Collect(Store(), "", Tr);

        Assert.That(battles.Select(b => b.Id), Is.EqualTo(new[] { "alpha", "bear" }));
    }

    [Test]
    public void Collect_matches_battle_id_name_desc_tag_and_enemy()
    {
        var store = Store();

        Assert.That(BattleCodexBuilder.Collect(store, "bear", Tr).Select(b => b.Id), Is.EqualTo(new[] { "bear" }), "战斗 id");
        Assert.That(BattleCodexBuilder.Collect(store, "黑熊战", Tr).Select(b => b.Id), Is.EqualTo(new[] { "bear" }), "翻译后的战斗名");
        Assert.That(BattleCodexBuilder.Collect(store, "森林", Tr).Select(b => b.Id), Is.EqualTo(new[] { "bear" }), "描述");
        Assert.That(BattleCodexBuilder.Collect(store, "boss", Tr).Select(b => b.Id), Is.EqualTo(new[] { "bear" }), "标签");
        Assert.That(BattleCodexBuilder.Collect(store, "黑熊", Tr).Select(b => b.Id), Is.EqualTo(new[] { "bear" }), "波次里的敌人名");
        Assert.That(BattleCodexBuilder.Collect(store, "   ", Tr).Count, Is.EqualTo(2), "空搜索词不过滤");
        Assert.That(BattleCodexBuilder.Collect(store, "没有这场战斗", Tr), Is.Empty);
    }

    [Test]
    public void RowTitle_falls_back_to_battle_id_without_a_name_key()
    {
        var store = Store();

        Assert.That(BattleCodexBuilder.RowTitle(store.Battles["bear"], Tr), Is.EqualTo("黑熊战"));
        Assert.That(BattleCodexBuilder.RowTitle(store.Battles["alpha"], Tr), Is.EqualTo("battle.alpha.name"));
    }

    [Test]
    public void BuildDetail_expands_waves_spawns_and_action_tables()
    {
        var store = Store();
        var detail = BattleCodexBuilder.BuildDetail(store.Battles["bear"], store, Tr);

        Assert.That(detail.Title, Is.EqualTo("黑熊战"));
        Assert.That(detail.Desc, Is.EqualTo("森林里的两头熊。"));
        Assert.That(detail.Tags, Is.EqualTo(new[] { "boss" }));
        Assert.That(detail.CombatRuleIds, Is.EqualTo(new[] { "all_enemies_defeated_victory" }));
        Assert.That(detail.Waves, Has.Count.EqualTo(1));

        var wave = detail.Waves[0];
        Assert.That(wave.Index, Is.EqualTo(1), "波次序号从 1 起");
        Assert.That(wave.Spawns, Has.Count.EqualTo(2));

        var bear = wave.Spawns[0];
        Assert.That(bear.Name, Is.EqualTo("黑熊"));
        Assert.That(bear.Count, Is.EqualTo(2));
        Assert.That(bear.HpScale, Is.EqualTo(1.5));
        Assert.That(bear.InitialActionIndex, Is.EqualTo(1));
        Assert.That(bear.MaxHp, Is.EqualTo(1200), "取首条生命条的 HP");
        Assert.That(bear.ElementText, Is.EqualTo("黄"));
        Assert.That(bear.RoleText, Is.EqualTo("无"));
        Assert.That(bear.RaceText, Is.EqualTo("动物"));
        Assert.That(bear.EntrySkillNames, Is.EqualTo(new[] { "入场怒吼" }));

        var lifeBar = bear.LifeBars.Single();
        Assert.That(lifeBar.Index, Is.EqualTo(1));
        Assert.That(lifeBar.MaxHp, Is.EqualTo(1200));
        Assert.That(lifeBar.PrioritySkillName, Is.EqualTo("咆哮"));
        Assert.That(lifeBar.EntrySkillNames, Is.EqualTo(new[] { "入场怒吼" }));
        Assert.That(lifeBar.Actions.Select(a => a.Index), Is.EqualTo(new[] { 1, 2 }), "行动表序号从 1 起");
        Assert.That(lifeBar.Actions.Select(a => a.SkillName), Is.EqualTo(new[] { "双击", "咆哮" }));
        Assert.That(lifeBar.Actions.Select(a => a.ActionCount), Is.EqualTo(new[] { 1, 2 }));

        var dummy = wave.Spawns[1];
        Assert.That(dummy.MaxHp, Is.EqualTo(100), "无生命条时取 maxHp");
        Assert.That(dummy.LifeBars, Is.Empty);
        Assert.That(dummy.ElementText, Is.Empty, "无元素：由渲染层补「无」占位");
        Assert.That(dummy.Name, Is.EqualTo("dummy"), "缺 displayNameId 时回落 id");
    }

    [Test]
    public void BuildDetail_keeps_a_dangling_enemy_reference_visible()
    {
        var store = Store();
        store.BattlesMutable["broken"] = new BattleDto
        {
            Id = "broken",
            Waves = [new BattleWaveDto { EnemySpawns = [new EnemySpawnDto { EnemyId = "missing" }] }],
        };

        var detail = BattleCodexBuilder.BuildDetail(store.Battles["broken"], store, Tr);
        var spawn = detail.Waves.Single().Spawns.Single();

        Assert.That(spawn.Name, Is.EqualTo("missing"));
        Assert.That(spawn.MaxHp, Is.Zero);
        Assert.That(spawn.LifeBars, Is.Empty);
    }

    [Test]
    public void Render_prints_identity_line_and_action_table()
    {
        var store = Store();
        var detail = BattleCodexBuilder.BuildDetail(store.Battles["bear"], store, Tr);

        var text = BattleCodexText.Render(detail, Tr);

        Assert.That(text, Does.Contain("[b]黑熊战[/b]"));
        Assert.That(text, Does.Contain("波次：1"));
        Assert.That(text, Does.Contain("[b]第 1 波[/b]"));
        Assert.That(text, Does.Contain("[b]黑熊 ×2 · 生命 1200 · 生命倍率 1.5 · 起手第 2 项[/b]"));
        Assert.That(text, Does.Contain("元素：黄 · 定位：无 · 种族：动物"));
        Assert.That(text, Does.Contain("[b]行动表[/b]"));
        Assert.That(text, Does.Contain("生命条 1 · 生命 1200"));
        Assert.That(text, Does.Contain("  优先行动：咆哮"));
        Assert.That(text, Does.Contain("  1. 双击（行动计数 1）"));
        Assert.That(text, Does.Contain("  2. 咆哮（行动计数 2）"));

        // 无生命条 / 无元素种族的敌人：占位文案 + 明示没有行动循环。
        Assert.That(text, Does.Contain("元素：无 · 定位：无 · 种族：无"));
        Assert.That(text, Does.Contain("无生命条配置（随机技能）"));
    }

    [Test]
    public void Render_escapes_square_brackets_in_content_text()
    {
        var store = Store();
        store.BattlesMutable["bracket"] = new BattleDto
        {
            Id = "bracket",
            DisplayNameId = "battle.[b]notatag[/b]",
            Waves = [],
        };

        var detail = BattleCodexBuilder.BuildDetail(store.Battles["bracket"], store, Tr);
        var text = BattleCodexText.Render(detail, Tr);

        Assert.That(text, Does.Not.Contain("[b]notatag"));
        Assert.That(text, Does.Contain("[b]battle.[lb]b]notatag[lb]/b][/b]"));
    }

    /// <summary>
    /// <see cref="BattleCodexText"/> 的键有一部分以常量形式传给 <c>Localization.Tr</c>，
    /// <c>LocaleIntegrityTests</c> 的「字面 Tr(\"KEY\")」扫描看不到它们——漏一个只会在界面上显示原始键名，
    /// 因此在这里按常量清单单独校验（键的值仍在 <c>Resource/Locale/strings.csv</c>）。
    /// </summary>
    [Test]
    public void Every_battle_text_key_exists_in_the_resource_locale_csv()
    {
        var keys = ReadCsvKeys();
        var referenced = typeof(BattleCodexText)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Where(value => value.StartsWith("UI_", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.That(referenced, Is.Not.Empty, "BattleCodexText 未引用任何 UI_ 键，本用例需同步");
        Assert.That(
            referenced.Where(key => !keys.Contains(key)),
            Is.Empty,
            "BattleCodexText 引用的键必须写进 Resource/Locale/strings.csv");
    }

    #region 装配

    private static HashSet<string> ReadCsvKeys()
    {
        var path = LocateRepoFile(Path.Combine("Resource", "Locale", "strings.csv"));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            var key = line.Split(',')[0];
            if (key.Length > 0)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static string LocateRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"从测试输出目录向上找不到 {relativePath}。");
    }

    #endregion
}
