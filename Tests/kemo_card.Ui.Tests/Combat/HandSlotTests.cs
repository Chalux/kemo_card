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

}