using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class HandSlotTests
{
    [Test]
    public void PlaceCard_and_ClearCard()
    {
        var slot = new HandSlot(0);

        slot.PlaceCard("strike", "rt-1");
        Assert.That(slot.CardId, Is.EqualTo("strike"));
        Assert.That(slot.RuntimeInstanceId, Is.EqualTo("rt-1"));

        slot.ClearCard();
        Assert.That(slot.IsEmpty, Is.True);
    }

    [Test]
    public void BuffContainer_starts_empty_and_accepts_instances()
    {
        var slot = new HandSlot(2);
        var buff = new BuffDto { Id = "poison" };

        var instance = slot.Buffs.Add(buff, null);

        Assert.That(slot.Buffs.All, Has.Count.EqualTo(1));
        Assert.That(instance.Def.Id, Is.EqualTo("poison"));
        Assert.That(slot.Buffs.Find("poison"), Is.SameAs(instance));
    }

    #region 标记态

    [Test]
    public void Newly_placed_card_is_not_marked()
    {
        var slot = new HandSlot(0);
        slot.PlaceCard("strike", "rt-1");

        Assert.That(slot.IsMarked, Is.False);
        Assert.That(slot.MarkedSequence, Is.Null);
    }

    [Test]
    public void Mark_records_sequence_and_keeps_card_in_slot()
    {
        var slot = new HandSlot(0);
        slot.PlaceCard("strike", "rt-1");

        slot.Mark(7);

        Assert.That(slot.IsMarked, Is.True);
        Assert.That(slot.MarkedSequence, Is.EqualTo(7));
        Assert.That(slot.IsEmpty, Is.False, "标记不离手");
        Assert.That(slot.CardId, Is.EqualTo("strike"));
    }

    [Test]
    public void Unmark_clears_sequence_but_keeps_card()
    {
        var slot = new HandSlot(0);
        slot.PlaceCard("strike", "rt-1");
        slot.Mark(7);

        slot.Unmark();

        Assert.That(slot.IsMarked, Is.False);
        Assert.That(slot.CardId, Is.EqualTo("strike"));
    }

    [Test]
    public void ClearCard_also_drops_the_mark()
    {
        var slot = new HandSlot(0);
        slot.PlaceCard("strike", "rt-1");
        slot.Mark(7);

        slot.ClearCard();

        Assert.That(slot.IsEmpty, Is.True);
        Assert.That(slot.IsMarked, Is.False);
    }

    #endregion
}