using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Rules.Builtin;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class CombatRuleEngineTests
{
	private sealed class CaptureRule : ICombatRule
	{
		public string Id { get; }
		public int Priority { get; }
		public int TurnStartCount { get; private set; }

		public CaptureRule(string id, int priority)
		{
			Id = id;
			Priority = priority;
		}

		public void OnTurnStart(CombatContext ctx) => TurnStartCount++;
	}

	[Test]
	public void Dispatch_runs_rules_in_priority_order()
	{
		var low = new CaptureRule("low", priority: 10);
		var high = new CaptureRule("high", priority: 1);
		var engine = new CombatRuleEngine([low, high]);
		var order = new List<string>();
		engine.DispatchTurnStart(new CombatContext(null!, turnNumber: 1), r => order.Add(r.Id));
		Assert.That(order, Is.EqualTo(new[] { "high", "low" }));
	}

	[Test]
	public void Rules_collection_is_readonly_after_construction()
	{
		var engine = new CombatRuleEngine([new SharedHpDefeatRule()]);
		Assert.That(engine.Rules, Is.InstanceOf<IReadOnlyList<ICombatRule>>());
		Assert.That(engine.Rules.Count, Is.EqualTo(1));
	}
}
