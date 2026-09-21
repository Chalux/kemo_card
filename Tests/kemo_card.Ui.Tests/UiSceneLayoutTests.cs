using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 场景布局约定守卫：容器里的自动换行 Label 必须声明**非零宽度**的 <c>custom_minimum_size</c>。
/// </summary>
/// <remarks>
/// <para>Godot 里自动换行的 Label 自身最小宽度几乎为 0（它"可以折行"），因此容器的可用宽度一旦被
/// 兄弟节点挤压，Label 会被压成竖排单字甚至 0 宽而看不见；容器若是"按内容撑开"的
/// （PanelContainer / 提示气泡），则整个面板都会塌成一条。显式给出最小宽度才能让折行
/// 在预期列宽下发生——这是 2026-09-21 定下的约定。</para>
/// <para>只检查**容器**的子节点：锚定在普通 <c>Control</c> 上的 Label 由锚点决定宽度，
/// 折行本来就正常（例如 <c>AlertDlg.LblDesc</c>）。</para>
/// </remarks>
[TestFixture]
public sealed class UiSceneLayoutTests
{
    private static readonly Regex NodeHeaderPattern = new(
        "^\\[node name=\"([^\"]+)\"(?:\\s+type=\"([^\"]+)\")?(?:\\s+instance=[^\\s]+)?(?:\\s+parent=\"([^\"]*)\")?",
        RegexOptions.Compiled);

    private static readonly Regex AutowrapPattern = new("^autowrap_mode = (\\d+)\\s*$", RegexOptions.Compiled);
    private static readonly Regex MinSizePattern = new(
        "^custom_minimum_size = Vector2\\(([-0-9.eE]+),\\s*([-0-9.eE]+)\\)\\s*$",
        RegexOptions.Compiled);

    [Test]
    public void Autowrap_labels_inside_containers_declare_a_non_zero_min_width()
    {
        var offenders = new List<string>();
        var checkedCount = 0;

        foreach (var file in ListScenes())
        {
            var nodes = ParseNodes(file);
            foreach (var node in nodes.Values)
            {
                if (node.Type is not ("Label" or "RichTextLabel") || node.AutowrapMode is null or 0)
                {
                    continue;
                }

                if (node.ParentPath is null || !nodes.TryGetValue(node.ParentPath, out var parent))
                {
                    continue;
                }

                // 只有容器会"挤压"子节点；普通 Control 上的锚定 Label 不受此约定约束。
                if (parent.Type is null || !parent.Type.EndsWith("Container", StringComparison.Ordinal))
                {
                    continue;
                }

                checkedCount++;
                if (node.MinSizeWidth is null or <= 0f)
                {
                    offenders.Add($"{RelativeToRepo(file)} -> {node.Path}（父容器 {parent.Type}）");
                }
            }
        }

        Assert.That(checkedCount, Is.GreaterThanOrEqualTo(8),
            "扫描到的「容器内自动换行 Label」数量异常，场景解析可能失效");
        Assert.That(offenders, Is.Empty,
            "容器内的自动换行 Label 必须配置 custom_minimum_size 的宽度，否则会被压成竖排单字：" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    #region 场景解析

    private sealed class SceneNode
    {
        public required string Path { get; init; }
        public string? Type { get; init; }
        public string? ParentPath { get; init; }
        public int? AutowrapMode { get; set; }
        public float? MinSizeWidth { get; set; }
    }

    /// <summary>
    /// 极简 .tscn 解析：只取节点头、<c>autowrap_mode</c> 与 <c>custom_minimum_size</c>。
    /// 属性顺序不敏感（编辑器写入的位置与手写可能不同）。
    /// </summary>
    private static Dictionary<string, SceneNode> ParseNodes(string file)
    {
        var nodes = new Dictionary<string, SceneNode>(StringComparer.Ordinal);
        SceneNode? current = null;

        foreach (var line in File.ReadAllLines(file))
        {
            var header = NodeHeaderPattern.Match(line);
            if (header.Success && line.StartsWith("[node ", StringComparison.Ordinal))
            {
                var name = header.Groups[1].Value;
                var parentPath = header.Groups[3].Success ? header.Groups[3].Value : "";
                var path = parentPath.Length == 0 ? name : $"{parentPath}/{name}";
                current = new SceneNode
                {
                    Path = path,
                    Type = header.Groups[2].Success ? header.Groups[2].Value : null,
                    ParentPath = parentPath.Length == 0 ? null : parentPath,
                };
                nodes[path] = current;
                continue;
            }

            if (line.StartsWith('['))
            {
                current = null;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            var autowrap = AutowrapPattern.Match(line);
            if (autowrap.Success)
            {
                current.AutowrapMode = int.Parse(autowrap.Groups[1].Value, CultureInfo.InvariantCulture);
                continue;
            }

            var minSize = MinSizePattern.Match(line);
            if (minSize.Success)
            {
                current.MinSizeWidth = float.Parse(minSize.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        return nodes;
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
