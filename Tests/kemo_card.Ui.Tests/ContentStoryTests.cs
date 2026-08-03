using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Condition;
using KemoCard.Mod.Global.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentStoryTests
{
    [SetUp]
    public void SetUp()
    {
        ConditionDomains.Persistent.Clear();
        BuiltinPersistentConditions.RegisterAll(ConditionDomains.Persistent);
    }

    [Test]
    public void Rebuild_loads_stories_into_store()
    {
        var root = Directory.CreateTempSubdirectory("story_test").FullName;
        try
        {
            var modDir = ContentModTestHelper.CreateModFolder(root, "mod_a", "mod.a");
            ContentModTestHelper.AddStory(modDir, "story_one", $$"""
                {
                  "displayNameId": "story.one.name",
                  "descId": "story.one.desc",
                  "author": "Tester",
                  "singlePlayerOnly": true
                }
                """);

            var registry = new GameDefinitionRegistry();
            registry.Rebuild([ContentModTestHelper.CreateBundleFromFolder(root, "mod.a")], out _);

            Assert.That(registry.Store.TryGetStory("story_one", out var story), Is.True);
            Assert.That(story.Author, Is.EqualTo("Tester"));
            Assert.That(story.SinglePlayerOnly, Is.True);
            Assert.That(story.Id, Is.EqualTo("story_one"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Rebuild_reports_story_id_conflict()
    {
        var root = Directory.CreateTempSubdirectory("story_conflict_test").FullName;
        try
        {
            var modA = ContentModTestHelper.CreateModFolder(root, "mod_a", "mod.a", loadOrder: 1);
            ContentModTestHelper.AddStory(modA, "dup", "{}");
            var modB = ContentModTestHelper.CreateModFolder(root, "mod_b", "mod.b", loadOrder: 2);
            ContentModTestHelper.AddStory(modB, "dup", "{}");

            var registry = new GameDefinitionRegistry();
            registry.Rebuild(
                [ContentModTestHelper.CreateBundleFromFolder(root, "mod.a"),
                 ContentModTestHelper.CreateBundleFromFolder(root, "mod.b")],
                out var report);

            Assert.That(report.IdConflicts, Has.Some.Matches<ContentIdConflictEntry>(c =>
                c.Category == EContentCategory.Story && c.ContentId == "dup"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Rebuild_removes_story_with_unknown_cond_type()
    {
        var root = Directory.CreateTempSubdirectory("story_cond_test").FullName;
        try
        {
            var modDir = ContentModTestHelper.CreateModFolder(root, "mod_a", "mod.a");
            ContentModTestHelper.AddStory(modDir, "bad_story", """
                {
                  "displayNameId": "story.bad.name",
                  "unlock": { "NoSuchCondType": ["x"] }
                }
                """);

            var registry = new GameDefinitionRegistry();
            registry.Rebuild([ContentModTestHelper.CreateBundleFromFolder(root, "mod.a")], out var report);

            Assert.That(report.RemovedValidationErrors, Has.Some.Matches<ContentDefinitionValidationError>(e =>
                e.Category == EContentCategory.Story && e.DefinitionId == "bad_story"));
            Assert.That(registry.Store.Stories.ContainsKey("bad_story"), Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Rebuild_keeps_story_with_valid_has_flag_unlock()
    {
        var root = Directory.CreateTempSubdirectory("story_cond_ok_test").FullName;
        try
        {
            var modDir = ContentModTestHelper.CreateModFolder(root, "mod_a", "mod.a");
            ContentModTestHelper.AddStory(modDir, "locked_story", """
                {
                  "displayNameId": "story.locked.name",
                  "unlock": { "HasFlag": ["story.prev.clear"] }
                }
                """);

            var registry = new GameDefinitionRegistry();
            registry.Rebuild([ContentModTestHelper.CreateBundleFromFolder(root, "mod.a")], out _);

            Assert.That(registry.Store.Stories.ContainsKey("locked_story"), Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Rebuild_loads_base_rogue_story_with_definition_fields()
    {
        var root = Directory.CreateTempSubdirectory("story_base_rogue_test").FullName;
        try
        {
            var modDir = ContentModTestHelper.CreateModFolder(root, "mod_a", "mod.a");
            ContentModTestHelper.AddStory(modDir, "base-rogue", """
                {
                  "displayNameId": "story.base_rogue.name",
                  "descId": "story.base_rogue.desc",
                  "author": "Chalux",
                  "scriptPath": "stories/base_rogue.js",
                  "singlePlayerOnly": true
                }
                """);

            var registry = new GameDefinitionRegistry();
            registry.Rebuild([ContentModTestHelper.CreateBundleFromFolder(root, "mod.a")], out _);

            Assert.That(registry.Store.TryGetStory("base-rogue", out var story), Is.True);
            Assert.That(story.Id, Is.EqualTo("base-rogue"));
            Assert.That(story.Author, Is.EqualTo("Chalux"));
            Assert.That(story.DisplayNameId, Is.EqualTo("story.base_rogue.name"));
            Assert.That(story.ScriptPath, Is.EqualTo("stories/base_rogue.js"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}