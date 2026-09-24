using KemoCard.Frame.Content.Keywords;
using KemoCard.Mod.Global.Ui.Tip;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 关键词效果块展开（buff 预览把描述引用的词条附在下方）：顺序按首次出现、未知 id 跳过、普通文本为空。
/// </summary>
[TestFixture]
public sealed class KeywordTipServiceTests
{
    [Test]
    public void Builds_effect_tips_for_referenced_keywords_in_order()
    {
        var catalog = new KeywordCatalog();
        catalog.Register(new KeywordEntry("follow_up", "KW_FOLLOW_UP_TITLE", "KW_FOLLOW_UP_DESC"));
        catalog.Register(new KeywordEntry("normal_attack", "KW_NORMAL_ATTACK_TITLE", "KW_NORMAL_ATTACK_DESC"));
        const string text =
            "以 [url=kw:follow_up]追打[/url] 参与 [url=kw:normal_attack]普通攻击[/url]，"
            + "[url=kw:follow_up]追打[/url] 只取最高值。";

        var tips = KeywordTipService.BuildKeywordEffectTips(text, catalog, key => $"T({key})");

        Assert.That(tips, Has.Count.EqualTo(2));
        Assert.That(tips[0].Title, Is.EqualTo("T(KW_FOLLOW_UP_TITLE)"));
        Assert.That(tips[0].Desc, Is.EqualTo("T(KW_FOLLOW_UP_DESC)"));
        Assert.That(tips[1].Title, Is.EqualTo("T(KW_NORMAL_ATTACK_TITLE)"));
    }

    [Test]
    public void Unknown_keyword_reference_is_skipped()
    {
        var catalog = new KeywordCatalog();

        var tips = KeywordTipService.BuildKeywordEffectTips("[url=kw:missing]未知词条[/url]", catalog, key => key);

        Assert.That(tips, Is.Empty);
    }

    [Test]
    public void Plain_text_yields_no_effect_tips()
    {
        var catalog = new KeywordCatalog();

        Assert.That(KeywordTipService.BuildKeywordEffectTips("普通描述。", catalog, key => key), Is.Empty);
    }
}
