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
	public void TryAddSlotEffect_stores_buff_ref_for_future_pipeline()
	{
		var slot = new HandSlot(2);
		var added = slot.TryAddSlotEffect("poison", new Dictionary<string, object> { ["stacks"] = 1 });

		Assert.That(added, Is.True);
		Assert.That(slot.SlotEffects, Has.Count.EqualTo(1));
		Assert.That(slot.SlotEffects[0].BuffId, Is.EqualTo("poison"));
	}
}
