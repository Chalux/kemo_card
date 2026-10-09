using KemoCard.Frame.Gas;

namespace KemoCard.Mod.Combat.Runtime;

/// <summary>仅观察最终聚合值，从正护盾变为零时触发；初始零与重复零不触发。</summary>
internal sealed class ShieldLifecycle : IDisposable
{
    private readonly List<(AttributeAggregator Aggregator, Action<string> Listener)> _listeners = [];

    public ShieldLifecycle(CombatSimulation simulation)
    {
        for (var index = 0; index < simulation.PlayerTeam.Characters.Count; index++)
        {
            var slot = index;
            var asc = simulation.PlayerTeam.Characters[slot].Asc;
            var previous = asc.GetCurrentValue(AttributeIds.Shield);
            void Observe(string attribute)
            {
                if (attribute != AttributeIds.Shield)
                    return;
                var current = asc.GetCurrentValue(AttributeIds.Shield);
                var depleted = previous > 0 && current <= 0;
                previous = current;
                if (depleted)
                    simulation.Buffs.FireShieldDepleted(simulation, slot);
            }
            asc.Aggregator.CurrentValuePublished += Observe;
            _listeners.Add((asc.Aggregator, Observe));
        }
    }

    public void Dispose()
    {
        foreach (var (aggregator, listener) in _listeners)
            aggregator.CurrentValuePublished -= listener;
        _listeners.Clear();
    }
}