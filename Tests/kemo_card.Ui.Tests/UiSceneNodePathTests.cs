using System.Text.RegularExpressions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 场景导出引用守卫：<c>[node … node_paths=PackedStringArray("_x")]</c> 列出的每个属性名，
/// 必须在该节点下真的有一条 <c>_x = NodePath("A/B")</c>，且指向的节点存在于同一场景文件中。
/// </summary>
/// <remarks>
/// <para>根因：Godot 只在**编辑器加载场景时**把 <c>NodePath</c> 解析成对象引用，路径写错
/// （改名、复制粘贴另一个界面的路径）不会让 <c>dotnet build</c> / <c>dotnet test</c> 失败，
/// 而是静默解析为 <c>null</c>：代码里的 <c>_lblMeta?.Text = …</c> 照旧跑，界面上却什么都不显示。
/// 更隐蔽的是「只在 <c>node_paths</c> 里登记了属性名、却漏写赋值行」——连 Godot 都不报错，
/// 该字段永远是 <c>null</c>（2026-09-23 手改 <c>CharacterDetailsDlg.tscn</c> 时真的踩到过）。
/// 手改 <c>.tscn</c>（新增节点、重排右栏）尤其容易留下这两类断链，因此在纯文本层面兜住。</para>
/// <para>只校验**本文件内**声明的节点路径；编辑器写出的节点属性顺序不影响解析。</para>
/// </remarks>
[TestFixture]
public sealed class UiSceneNodePathTests
{
    private static readonly Regex NodeHeaderPattern = new(
        "^(?:\\[node name=\"([^\"]+)\")(?:\\s+type=\"([^\"]+)\")?(?:\\s+instance=[^\\s]+)?(?:\\s+parent=\"([^\"]*)\")?",
        RegexOptions.Compiled);

    private static readonly Regex NodePathsPattern = new(
        "node_paths=PackedStringArray\\(([^)]*)\\)",
        RegexOptions.Compiled);

    private static readonly Regex NodePathAssignmentPattern = new(
        "^([A-Za-z_][A-Za-z0-9_]*) = NodePath\\(\"([^\"]*)\"\\)\\s*$",
        RegexOptions.Compiled);

    [Test]
    public void Node_paths_entries_resolve_to_nodes_declared_in_the_same_scene()
    {
        var offenders = new List<string>();
        var checkedCount = 0;

        foreach (var file in ListScenes())
        {
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var expectations = new List<(string NodePath, string Property)>();
            var bindings = new List<(string NodePath, string Property, string Target)>();

            // null = 还没读到任何节点节；"" = 场景根节点（根自身也可能挂导出引用）。
            string? current = null;
            var pendingProperties = new List<string>();

            foreach (var rawLine in File.ReadAllLines(file))
            {
                var line = rawLine.TrimEnd();
                if (line.StartsWith("[node ", StringComparison.Ordinal))
                {
                    var header = NodeHeaderPattern.Match(line);
                    var name = header.Groups[1].Value;

                    // .tscn 的 parent 一律相对「场景根」书写（"." 就是根），根节点自己没有 parent：
                    // 因此根记为 ""，其余记为 parent + "/" + name。
                    if (!header.Groups[3].Success)
                    {
                        current = "";
                    }
                    else
                    {
                        var parentPath = header.Groups[3].Value.Trim('/');
                        current = parentPath.Length == 0 || parentPath == "."
                            ? name
                            : $"{parentPath}/{name}";
                    }

                    declared.Add(current);

                    pendingProperties.Clear();
                    var nodePaths = NodePathsPattern.Match(line);
                    if (nodePaths.Success)
                    {
                        foreach (var rawProperty in nodePaths.Groups[1].Value.Split(','))
                        {
                            var property = rawProperty.Trim().Trim('"');
                            if (property.Length > 0)
                            {
                                pendingProperties.Add(property);
                                expectations.Add((current, property));
                            }
                        }
                    }

                    continue;
                }

                if (line.StartsWith('[') || current is null)
                {
                    continue;
                }

                var assignment = NodePathAssignmentPattern.Match(line);
                if (assignment.Success && pendingProperties.Contains(assignment.Groups[1].Value))
                {
                    bindings.Add((current, assignment.Groups[1].Value, assignment.Groups[2].Value));
                }
            }

            // 登记了属性名却没有赋值行：Godot 不报错，该字段永远是 null（最隐蔽的一类断链）。
            foreach (var (nodePath, property) in expectations)
            {
                checkedCount++;
                if (!bindings.Exists(binding => binding.NodePath == nodePath && binding.Property == property))
                {
                    offenders.Add(
                        $"{RelativeToRepo(file)} -> {nodePath}.{property}（node_paths 里登记了属性名，却没有 NodePath 赋值行）");
                }
            }

            foreach (var (nodePath, property, target) in bindings)
            {
                checkedCount++;
                if (target.Length == 0 || IsResolvable(nodePath, target, declared))
                {
                    continue;
                }

                offenders.Add($"{RelativeToRepo(file)} -> {nodePath}.{property} = NodePath(\"{target}\")");
            }
        }

        Assert.That(checkedCount, Is.GreaterThanOrEqualTo(40),
            "扫描到的场景导出引用数量异常，场景解析可能失效");
        Assert.That(offenders, Is.Empty,
            "场景 node_paths 指向的节点必须在同一 .tscn 里声明（路径写错只会在运行时静默拿到 null）："
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// 场景里的 <c>NodePath</c> 值是**相对持有该属性的节点**的（<c>"Scroll"</c> = 自己的子节点），
    /// <c>".."</c> 向上一层；先归一到"从根算起"的路径再比对。
    /// </summary>
    private static bool IsResolvable(string fromNodePath, string target, HashSet<string> declared)
    {
        // %UniqueName / @Node 这类编辑器专有写法不在本守卫的覆盖范围内。
        if (target.Contains('%') || target.Contains('@'))
        {
            return true;
        }

        var segments = new List<string>(fromNodePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var part in target.Split('/'))
        {
            switch (part)
            {
                case "" or ".":
                    continue;
                case "..":
                    if (segments.Count > 0)
                    {
                        segments.RemoveAt(segments.Count - 1);
                    }

                    continue;
                default:
                    segments.Add(part);
                    continue;
            }
        }

        return declared.Contains(string.Join("/", segments));
    }

    #region 装配

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