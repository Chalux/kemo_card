using System.Text.RegularExpressions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 场景资源守卫：场景级 <c>ShaderMaterial</c> 子资源必须声明 <c>resource_local_to_scene = true</c>。
/// </summary>
/// <remarks>
/// <para>根因：写在 <c>.tscn</c> 里的 <c>[sub_resource]</c> 默认是<b>整场景共享</b>的一份资源，
/// 每次 <c>Instantiate()</c> 都指向同一个对象。而着色参数（<c>colors</c> / <c>color_count</c> …）
/// 是材质上的可变状态，组件在 <c>SetData</c> 里逐个写入时彼此覆盖——列表/对象池里后渲染的条目会把
/// 先渲染条目的颜色顶掉，表现为<b>所有条目颜色永远一样</b>（2026-09-21 在 <c>BaseCardItem.CRAttr</c> /
/// <c>BaseCharacterItem.CRAttr</c> 上实际发生：元素色块全部显示成最后一次写入的颜色）。</para>
/// <para>勾选 Local to Scene 后 <c>Instantiate()</c> 会为每个实例复制一份材质，参数互不影响。
/// 因此本守卫要求：场景里的 <c>ShaderMaterial</c> 一律显式打开该开关，不为"这个材质没被写过参数"
/// 留例外——例外迟早会因为后续接线而变成同一个 bug。</para>
/// </remarks>
[TestFixture]
public sealed class UiSceneResourceGuardTests
{
    private static readonly Regex SubResourceHeaderPattern = new(
        "^\\[sub_resource\\s+type=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private static readonly Regex SectionHeaderPattern = new("^\\[", RegexOptions.Compiled);

    private const string LocalToSceneLine = "resource_local_to_scene = true";

    [Test]
    public void Scene_level_shader_materials_are_local_to_scene()
    {
        var offenders = new List<string>();
        var checkedCount = 0;

        foreach (var file in ListScenes())
        {
            foreach (var (type, block) in EnumerateSubResources(File.ReadAllLines(file)))
            {
                if (!string.Equals(type, "ShaderMaterial", StringComparison.Ordinal))
                {
                    continue;
                }

                checkedCount++;
                if (!block.Any(line => string.Equals(line.Trim(), LocalToSceneLine, StringComparison.Ordinal)))
                {
                    offenders.Add(RelativeToRepo(file));
                }
            }
        }

        Assert.That(checkedCount, Is.GreaterThanOrEqualTo(2),
            "扫描到的场景级 ShaderMaterial 数量异常，场景解析可能失效");
        Assert.That(offenders, Is.Empty,
            "场景级 ShaderMaterial 必须写 resource_local_to_scene = true，否则同场景的所有实例共用一份"
            + "材质、逐个写入的着色参数互相覆盖（列表里所有条目颜色永远一样）：" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    #region 场景解析

    /// <summary>按 <c>[sub_resource]</c> 节切块，返回 (type, 该节正文行)。</summary>
    private static IEnumerable<(string Type, List<string> Lines)> EnumerateSubResources(string[] lines)
    {
        string? currentType = null;
        var currentLines = new List<string>();

        foreach (var line in lines)
        {
            if (SectionHeaderPattern.IsMatch(line))
            {
                if (currentType is not null)
                {
                    yield return (currentType, currentLines);
                }

                currentType = null;
                currentLines = [];

                var header = SubResourceHeaderPattern.Match(line);
                if (header.Success)
                {
                    currentType = header.Groups[1].Value;
                }

                continue;
            }

            if (currentType is not null)
            {
                currentLines.Add(line);
            }
        }

        if (currentType is not null)
        {
            yield return (currentType, currentLines);
        }
    }

    private static IEnumerable<string> ListScenes() =>
        Directory.EnumerateFiles(LocateRepoDir("Src"), "*.tscn", SearchOption.AllDirectories);

    private static string LocateRepoDir(string relativeDir)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativeDir);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"从测试输出目录向上找不到目录 {relativeDir}。");
    }

    private static string RelativeToRepo(string fullPath)
    {
        var root = LocateRepoDir("Src");
        var repoRoot = Directory.GetParent(root)!.FullName;
        return fullPath.StartsWith(repoRoot, StringComparison.Ordinal)
            ? fullPath[(repoRoot.Length + 1)..]
            : fullPath;
    }

    #endregion
}
