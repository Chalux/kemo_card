using KemoCard.Frame.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Condition;

[TestFixture]
public sealed class ConditionResultShapeTests
{
    [Test]
    public void LeafResult_holds_optional_progress_and_refs()
    {
        var leaf = new LeafResult
        {
            CondType = "HasFlag",
            Passed = true,
            ShortTipKey = "COND_HAS_FLAG_SHORT",
            LongTipKey = "COND_HAS_FLAG_LONG",
            Fill = ["intro"],
            Progress = new ConditionProgress(1, 1),
            Refs = new ConditionRefs { FlagIds = ["intro"] },
        };

        Assert.That(leaf.Progress!.Value.Current, Is.EqualTo(1));
        Assert.That(leaf.Refs!.FlagIds[0], Is.EqualTo("intro"));
    }
}
