using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 本地化完整性守卫：CSV 结构（列数 / 键唯一 / 收尾换行）、场景与代码引用的键存在、
/// 内置词条键存在、base-game 内容引用的翻译 id 存在。
/// 由 2026-09-19 评审发现的「CSV 追加行未换行导致前一行吞键」缺陷催生：
/// 这类错误只在个别键查询时静默失效，必须靠全表校验拦住。
/// </summary>
[TestFixture]
public sealed class LocaleIntegrityTests
{
    private static readonly Regex LocaleKeyPattern = new("^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$", RegexOptions.Compiled);
    private static readonly Regex SceneTextKeyPattern = new("text = \"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex CodeTrKeyPattern = new(@"Tr\(\""([^""]+)\""\)", RegexOptions.Compiled);
    private static readonly Regex KeywordEntryPattern =
        new("new KeywordEntry\\(\"([^\"]+)\", \"([^\"]+)\", \"([^\"]+)\"\\)", RegexOptions.Compiled);
    private static readonly Regex ContentLocaleIdPattern =
        new("\"(?:displayNameId|descId|artistNameId|textId|labelId)\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

    [Test]
    public void Both_locale_csvs_are_well_formed()
    {
        foreach (var csvPath in new[] { ResourceCsvPath(), ModCsvPath() })
        {
            var text = File.ReadAllText(csvPath);
            Assert.That(text.EndsWith('\n'), $"{csvPath} 必须以换行收尾：否则后续追加会并进最后一行");

            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.That(
                lines[0].TrimStart('\uFEFF'),
                Is.EqualTo("keys,zh_CN,en"),
                $"{csvPath} 列布局变化时本用例需同步");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in lines.Skip(1))
            {
                var cells = line.Split(',');
                Assert.That(cells.Length, Is.GreaterThanOrEqualTo(3), $"{csvPath} 行「{Truncate(line)}」至少要有 keys/zh_CN/en 三列");
                Assert.That(cells[0], Is.Not.Empty, $"{csvPath} 行「{Truncate(line)}」键为空");
                Assert.That(cells[1], Is.Not.Empty, $"{csvPath} 键 {cells[0]} 缺少 zh_CN 文案");
                Assert.That(cells[2], Is.Not.Empty, $"{csvPath} 键 {cells[0]} 缺少 en 文案");
                Assert.That(seen.Add(cells[0]), Is.True, $"{csvPath} 键 {cells[0]} 重复定义");
            }
        }
    }

    [Test]
    public void All_caps_keys_referenced_by_scenes_and_code_exist_in_resource_csv()
    {
        var keys = ReadCsvKeys(ResourceCsvPath());
        var missing = new List<string>();

        foreach (var file in ListRepoFiles(Path.Combine("Src"), "*.tscn"))
        {
            foreach (var match in SceneTextKeyPattern.Matches(File.ReadAllText(file)))
            {
                var candidate = ((Match)match).Groups[1].Value;
                if (LocaleKeyPattern.IsMatch(candidate) && !keys.Contains(candidate))
                    missing.Add($"{RelativeToRepo(file)}: text = {candidate}");
            }
        }

        foreach (var file in ListRepoFiles(Path.Combine("Src"), "*.cs"))
        {
            foreach (var match in CodeTrKeyPattern.Matches(File.ReadAllText(file)))
            {
                var candidate = ((Match)match).Groups[1].Value;
                if (LocaleKeyPattern.IsMatch(candidate) && !keys.Contains(candidate))
                    missing.Add($"{RelativeToRepo(file)}: Tr({candidate})");
            }
        }

        Assert.That(missing, Is.Empty,
            "场景 text / 代码 Tr() 引用的全大写键必须写进 Resource/Locale/strings.csv：" + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
    }

    [Test]
    public void Builtin_keyword_title_and_desc_keys_exist_in_resource_csv()
    {
        var keys = ReadCsvKeys(ResourceCsvPath());
        var source = File.ReadAllText(LocateRepoFile(Path.Combine(
            "Src", "mod", "global", "Def", "BuiltinKeywords.cs")));

        var entries = KeywordEntryPattern.Matches(source);
        Assert.That(entries, Has.Count.GreaterThanOrEqualTo(6), "内置词条注册数量变化时本用例需同步");

        var missing = new List<string>();
        foreach (Match entry in entries)
        {
            if (!keys.Contains(entry.Groups[2].Value))
                missing.Add($"词条 {entry.Groups[1].Value} 缺标题键 {entry.Groups[2].Value}");
            if (!keys.Contains(entry.Groups[3].Value))
                missing.Add($"词条 {entry.Groups[1].Value} 缺描述键 {entry.Groups[3].Value}");
        }

        Assert.That(missing, Is.Empty, string.Join("; ", missing));
    }

    [Test]
    public void Base_game_content_locale_ids_exist_in_mod_csv()
    {
        var keys = ReadCsvKeys(ModCsvPath());
        var contentRoot = LocateRepoFile(Path.Combine(
            "Config", "mods", "base-game", "content", "attributes", "Health.json"))
            .Replace(Path.Combine("attributes", "Health.json"), "");

        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(contentRoot, "*.json", SearchOption.AllDirectories))
        {
            foreach (var match in ContentLocaleIdPattern.Matches(File.ReadAllText(file)))
            {
                var id = ((Match)match).Groups[1].Value;
                if (!keys.Contains(id))
                    missing.Add($"{RelativeToRepo(file)} -> {id}");
            }
        }

        Assert.That(missing, Is.Empty,
            "base-game 内容的 displayNameId/descId/textId/labelId 必须写进 mod 的 strings.csv："
            + Environment.NewLine + string.Join(Environment.NewLine, missing.Distinct()));
    }

    #region 装配

    private static string ResourceCsvPath() => LocateRepoFile(Path.Combine("Resource", "Locale", "strings.csv"));

    private static string ModCsvPath() => LocateRepoFile(Path.Combine(
        "Config", "mods", "base-game", "content", "translations", "strings.csv"));

    private static HashSet<string> ReadCsvKeys(string csvPath)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(csvPath).Skip(1))
        {
            var key = line.Split(',')[0];
            if (key.Length > 0)
                keys.Add(key);
        }

        return keys;
    }

    private static IEnumerable<string> ListRepoFiles(string relativeDir, string pattern)
    {
        var root = LocateRepoFile(Path.Combine("Resource", "Locale", "strings.csv"))
            .Replace(Path.Combine("Resource", "Locale", "strings.csv"), "");
        var dir = Path.Combine(root, relativeDir);
        Assert.That(Directory.Exists(dir), Is.True, $"找不到目录 {dir}");
        return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories);
    }

    /// <summary>从测试输出目录向上找到仓库内文件（与 <c>RunRewardDistributorTests</c> 同约定）。</summary>
    private static string LocateRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"从测试输出目录向上找不到 {relativePath}。");
    }

    private static string RelativeToRepo(string fullPath)
    {
        var root = LocateRepoFile(Path.Combine("Resource", "Locale", "strings.csv"))
            .Replace(Path.Combine("Resource", "Locale", "strings.csv"), "");
        return fullPath.StartsWith(root, StringComparison.Ordinal)
            ? fullPath[root.Length..]
            : fullPath;
    }

    private static string Truncate(string line) =>
        line.Length <= 60 ? line : line[..60] + "…";

    #endregion
}
