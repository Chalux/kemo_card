using KemoCard.Frame.Content;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentModLoaderTests
{
    [Test]
    public void Load_collects_card_ids_from_filenames()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
        var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.AddCard(modDir, "strike");

        var discovery = new ContentModDiscovery();
        var entry = discovery.Scan(root).ValidMods[0];
        var bundle = ContentModLoader.Load(entry);

        Assert.That(bundle.Definitions.Cards.ContainsKey("strike"), Is.True);
    }

    /// <summary>
    /// 预解析必须沿用与反序列化同样的宽松策略：<c>JsonDocument.Parse</c> 不继承
    /// <c>JsonSerializerOptions</c>，不显式传 <c>JsonDocumentOptions</c> 会让
    /// AllowTrailingCommas / ReadCommentHandling 形同虚设。
    /// </summary>
    [Test]
    public void Load_accepts_trailing_commas_and_comments()
    {
        var root = NewRoot();
        var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.AddCard(
            modDir,
            "strike",
            """
            {
              // 注释也应当被跳过
              "cost": 2,
              "costType": "Energy",
            }
            """);

        var bundle = LoadSingle(root);

        Assert.That(bundle.Definitions.Cards.ContainsKey("strike"), Is.True);
        Assert.That(bundle.Definitions.Cards["strike"].Cost, Is.EqualTo(2));
    }

    /// <summary>
    /// 规格 §4.3：内容 id 由文件名推导。反序列化开了 <c>PropertyNameCaseInsensitive</c>，
    /// 若只跳过小写 <c>id</c>，文件中写 <c>"Id"</c> 就会覆盖文件名 id —— 同一字段两种大小写两种行为。
    /// </summary>
    [TestCase("id")]
    [TestCase("Id")]
    [TestCase("ID")]
    public void Load_derives_id_from_filename_regardless_of_casing(string idKey)
    {
        var root = NewRoot();
        var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.AddCard(modDir, "from_filename", $$"""{ "{{idKey}}": "hijacked", "cost": 3 }""");

        var bundle = LoadSingle(root);

        Assert.That(bundle.Definitions.Cards.ContainsKey("from_filename"), Is.True);
        Assert.That(bundle.Definitions.Cards.ContainsKey("hijacked"), Is.False, "文件内的 id 不得覆盖文件名");
        Assert.That(bundle.Definitions.Cards["from_filename"].Id, Is.EqualTo("from_filename"));
    }

    /// <summary>加载失败必须带上文件路径，否则多文件 mod 无法定位是哪个定义写错。</summary>
    [Test]
    public void Load_reports_file_path_when_definition_is_invalid()
    {
        var root = NewRoot();
        var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.AddCard(modDir, "broken", """{ "cost": }""");

        var ex = Assert.Throws<ContentModLoadException>(() => LoadSingle(root));

        Assert.That(ex!.Message, Does.Contain("broken.json"));
        Assert.That(ex.ModId, Is.EqualTo("base.game"));
    }

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));

    private static ModContentBundle LoadSingle(string root)
    {
        var discovery = new ContentModDiscovery();
        var entry = discovery.Scan(root).ValidMods[0];
        return ContentModLoader.Load(entry);
    }
}