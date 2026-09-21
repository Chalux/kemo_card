using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentModBootstrapTests
{
    [Test]
    public void EnsureDefaultModsCopied_copies_when_destination_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_bootstrap_tests", Guid.NewGuid().ToString("N"));
        var bundled = Path.Combine(root, "bundled");
        var userMods = Path.Combine(root, "user-mods");
        ContentModTestHelper.CreateModFolder(bundled, "base-game", "base.game");

        ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled);

        Assert.That(File.Exists(Path.Combine(userMods, "base-game", "mod.json")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(userMods, "base-game.staging")), Is.False);
    }

    [Test]
    public void EnsureDefaultModsCopied_recopies_when_bundled_version_changes()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_bootstrap_tests", Guid.NewGuid().ToString("N"));
        var bundled = Path.Combine(root, "bundled");
        var userMods = Path.Combine(root, "user-mods");
        ContentModTestHelper.CreateModFolder(bundled, "base-game", "base.game", version: "1.0.0");

        ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled);

        var installedManifest = Path.Combine(userMods, "base-game", "mod.json");
        Assert.That(File.ReadAllText(installedManifest), Does.Contain("\"version\": \"1.0.0\""));

        File.WriteAllText(
            Path.Combine(bundled, "base-game", "mod.json"),
            File.ReadAllText(Path.Combine(bundled, "base-game", "mod.json")).Replace("1.0.0", "2.0.0"));

        ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled);

        Assert.That(File.ReadAllText(installedManifest), Does.Contain("\"version\": \"2.0.0\""));
    }

    [Test]
    public void EnsureDefaultModsCopied_does_not_leave_staging_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_bootstrap_tests", Guid.NewGuid().ToString("N"));
        var bundled = Path.Combine(root, "bundled");
        var userMods = Path.Combine(root, "user-mods");
        ContentModTestHelper.CreateModFolder(bundled, "base-game", "base.game");

        ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled);

        Assert.That(Directory.Exists(Path.Combine(userMods, "base-game.staging")), Is.False);
    }

    /// <summary>
    /// 调试构建的强制刷新：版本号不变也重新拷贝，开发期新增内容不必抬版本号即可生效。
    /// </summary>
    [Test]
    public void EnsureDefaultModsCopied_force_refresh_recopies_without_version_change()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_bootstrap_tests", Guid.NewGuid().ToString("N"));
        var bundled = Path.Combine(root, "bundled");
        var userMods = Path.Combine(root, "user-mods");
        ContentModTestHelper.CreateModFolder(bundled, "base-game", "base.game", version: "1.0.0");
        ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled);

        // 版本号不变、新增一个内容文件：默认语义不刷新……
        File.WriteAllText(Path.Combine(bundled, "base-game", "content", "new-character.json"), "{}");
        ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled);
        Assert.That(
            File.Exists(Path.Combine(userMods, "base-game", "content", "new-character.json")),
            Is.False,
            "同版本默认跳过拷贝");

        // ……强制刷新后必须同步过来。
        ContentModBootstrap.EnsureDefaultModsCopied(userMods, bundled, forceRefresh: true);

        Assert.That(
            File.Exists(Path.Combine(userMods, "base-game", "content", "new-character.json")),
            Is.True,
            "调试构建的强制刷新必须重新拷贝");
        Assert.That(Directory.Exists(Path.Combine(userMods, "base-game.staging")), Is.False, "不留暂存目录");
    }
}