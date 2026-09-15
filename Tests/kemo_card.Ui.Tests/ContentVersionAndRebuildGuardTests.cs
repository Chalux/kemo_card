using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 覆盖内容版本指纹（内容 Mod 规格 §5：与全局存档 <c>ContentVersionHash</c> 联动）、
/// 管线最近一次报告（<c>LatestReport</c> / <c>HasIssues</c>）与 Run 进行中的重建守卫。
/// </summary>
[TestFixture]
public sealed class ContentVersionAndRebuildGuardTests
{
    [Test]
    public void ContentVersionHash_is_stable_for_same_definition_sets()
    {
        var bundle = CardBundle("base.game", "strike", "slash");

        var first = RebuildRegistry(bundle);
        var second = RebuildRegistry(bundle);

        AssertFingerprintShape(first.ContentVersionHash);
        Assert.That(second.ContentVersionHash, Is.EqualTo(first.ContentVersionHash));
        Assert.That(
            first.ContentVersionHash,
            Is.Not.EqualTo(new GameDefinitionRegistry().ContentVersionHash),
            "内容集不同（空集 vs 两张卡）时指纹必须不同。");
    }

    [Test]
    public void ContentVersionHash_changes_when_definition_id_differs()
    {
        var strike = RebuildRegistry(CardBundle("base.game", "strike"));
        var slash = RebuildRegistry(CardBundle("base.game", "slash"));

        AssertFingerprintShape(strike.ContentVersionHash);
        Assert.That(slash.ContentVersionHash, Is.Not.EqualTo(strike.ContentVersionHash));
    }

    [Test]
    public void ContentVersionHash_is_independent_of_bundle_order_and_rebuild_count()
    {
        var baseBundle = CardBundle("base.game", "alpha");
        var addonBundle = CardBundle("addon.mod", "beta");

        var forward = new GameDefinitionRegistry();
        forward.Rebuild(new[] { baseBundle, addonBundle }, out _);

        var reversed = new GameDefinitionRegistry();
        reversed.Rebuild(new[] { addonBundle, baseBundle }, out _);

        Assert.That(reversed.ContentVersionHash, Is.EqualTo(forward.ContentVersionHash));

        // 同一注册表重复重建：DefinitionVersion 递增，但内容指纹必须保持不变（指纹只取定义 id 集合）。
        var previousVersion = forward.DefinitionVersion;
        forward.Rebuild(new[] { baseBundle, addonBundle }, out _);

        Assert.That(forward.DefinitionVersion, Is.EqualTo(previousVersion + 1));
        Assert.That(forward.ContentVersionHash, Is.EqualTo(reversed.ContentVersionHash));
    }

    [Test]
    public void LatestReport_is_populated_and_flags_validation_issue()
    {
        var root = CreateTempRoot();
        var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.AddCard(modDir, "good_card");
        ContentModTestHelper.AddCard(
            modDir,
            "bad_card",
            """{ "displayNameId": "card.bad_card.name", "skillRefs": [{ "skillId": "missing_skill" }] }""");

        var pipeline = CreatePipeline(root);
        var report = pipeline.Rebuild(["base.game"]);

        Assert.That(report.HasIssues, Is.True);
        Assert.That(report.RemovedValidationErrors, Has.Count.EqualTo(1));
        Assert.That(report.RemovedValidationErrors[0].Category, Is.EqualTo(EContentCategory.Card));
        Assert.That(report.RemovedValidationErrors[0].DefinitionId, Is.EqualTo("bad_card"));
        Assert.That(pipeline.Registry.Contains(EContentCategory.Card, "bad_card"), Is.False);
        Assert.That(pipeline.Registry.Contains(EContentCategory.Card, "good_card"), Is.True);
        Assert.That(pipeline.LatestReport, Is.SameAs(report));
    }

    [Test]
    public void Rebuild_is_rejected_when_guard_denies_and_works_after_guard_allows()
    {
        var root = CreateTempRoot();
        var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.AddCard(modDir, "strike");

        var resetter = new RecordingScriptRuntimeResetter();
        var pipeline = CreatePipeline(root, resetter);
        pipeline.CanRebuild = static () => false;

        var rejected = pipeline.Rebuild(["base.game"]);

        Assert.That(rejected.HasIssues, Is.True);
        Assert.That(rejected.SkippedMods, Has.Count.EqualTo(1));
        Assert.That(rejected.SkippedMods[0].ModId, Is.EqualTo("base.game"));
        Assert.That(rejected.SkippedMods[0].Reason, Is.EqualTo(ModSkipReason.RebuildRejected));
        Assert.That(pipeline.LatestReport, Is.SameAs(rejected));

        // 被拒绝时必须保持 Run 现场：注册表与脚本运行时都不动。
        Assert.That(pipeline.Registry.Contains(EContentCategory.Card, "strike"), Is.False);
        Assert.That(pipeline.Registry.DefinitionVersion, Is.EqualTo(0));
        Assert.That(resetter.BeginCount, Is.EqualTo(0));
        Assert.That(resetter.RecreateCount, Is.EqualTo(0));
        Assert.That(resetter.EndCount, Is.EqualTo(0));

        pipeline.CanRebuild = static () => true;
        var accepted = pipeline.Rebuild(["base.game"]);

        Assert.That(accepted.HasIssues, Is.False);
        Assert.That(accepted.SkippedMods, Is.Empty);
        Assert.That(pipeline.LatestReport, Is.SameAs(accepted));
        Assert.That(pipeline.Registry.Contains(EContentCategory.Card, "strike"), Is.True);
        Assert.That(pipeline.Registry.DefinitionVersion, Is.EqualTo(1));
        Assert.That(resetter.BeginCount, Is.EqualTo(1));
        Assert.That(resetter.RecreateCount, Is.EqualTo(1));
        Assert.That(resetter.EndCount, Is.EqualTo(1));
    }

    [Test]
    public void Rebuild_is_allowed_when_guard_is_unset()
    {
        var root = CreateTempRoot();
        var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.AddCard(modDir, "strike");

        var pipeline = CreatePipeline(root);

        Assert.That(pipeline.CanRebuild, Is.Null);

        var report = pipeline.Rebuild(["base.game"]);

        Assert.That(report.HasIssues, Is.False);
        Assert.That(report.SkippedMods, Is.Empty);
        Assert.That(pipeline.Registry.Contains(EContentCategory.Card, "strike"), Is.True);
        Assert.That(pipeline.Registry.DefinitionVersion, Is.EqualTo(1));
        Assert.That(pipeline.LatestReport, Is.SameAs(report));
    }

    /// <summary>
    /// 指纹必须是稳定哈希派生的 16 位小写十六进制（FNV-1a 64 位）：<c>string.GetHashCode()</c> /
    /// <c>HashCode.Combine</c> 带进程级随机种子并返回十进制 int，跨会话不可比较，无法用于存档比对。
    /// </summary>
    private static void AssertFingerprintShape(string hash)
    {
        Assert.That(hash, Has.Length.EqualTo(16));
        Assert.That(hash, Does.Match("^[0-9a-f]{16}$"));
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
    }

    private static GameDefinitionRegistry RebuildRegistry(params ModContentBundle[] bundles)
    {
        var registry = new GameDefinitionRegistry();
        registry.Rebuild(bundles, out _);
        return registry;
    }

    private static ModContentBundle CardBundle(string modId, params string[] cardIds)
    {
        var cards = new Dictionary<string, CardDto>(StringComparer.Ordinal);
        foreach (var cardId in cardIds)
        {
            cards[cardId] = new CardDto
            {
                Id = cardId,
                DisplayNameId = $"card.{cardId}.name",
            };
        }

        return ContentModTestHelper.Bundle(modId, ModDefinitionsBundle.Empty with { Cards = cards });
    }

    private static ContentModPipeline CreatePipeline(string root)
    {
        return CreatePipeline(root, NullScriptRuntimeResetter.Instance);
    }

    private static ContentModPipeline CreatePipeline(string root, IScriptRuntimeResetter resetter)
    {
        return new ContentModPipeline(
            root,
            new GameDefinitionRegistry(),
            new NullContentModLogger(),
            new NullContentModUserNotifier(),
            resetter,
            new ModScriptCatalog());
    }

    /// <summary>记录脚本运行时重建调用次数，用于断言「被拒绝的重建不触碰运行时」。</summary>
    private sealed class RecordingScriptRuntimeResetter : IScriptRuntimeResetter
    {
        public int BeginCount { get; private set; }

        public int RecreateCount { get; private set; }

        public int EndCount { get; private set; }

        public void BeginRebuild()
        {
            BeginCount++;
        }

        public void Recreate()
        {
            RecreateCount++;
        }

        public void EndRebuild()
        {
            EndCount++;
        }
    }
}