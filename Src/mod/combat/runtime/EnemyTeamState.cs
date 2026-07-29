using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Mod.Combat.Gas;

namespace KemoCard.Mod.Combat.Runtime;

public sealed class EnemyTeamState
{
    private readonly List<EnemyUnit> _enemies;

    public AbilitySystemComponent Asc { get; }
    public CombatDomain? ActiveDomain { get; set; }
    public IReadOnlyList<EnemyUnit> Enemies => _enemies;
    public bool AllDefeated => _enemies.Count == 0 || _enemies.All(e => !e.IsAlive);

    public EnemyTeamState(IEnumerable<EnemyUnit> enemies, AbilitySystemComponent? teamAsc = null)
    {
        _enemies = enemies.ToList();
        Asc = teamAsc ?? new CombatAscFactory().CreateTeamAsc(
            new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal),
            ComputeTotalMaxHealth());
    }

    public void ReplaceEnemies(IEnumerable<EnemyUnit> enemies)
    {
        ArgumentNullException.ThrowIfNull(enemies);
        _enemies.Clear();
        _enemies.AddRange(enemies);
        var maxHealth = ComputeTotalMaxHealth();
        Asc.Attributes.SetBaseValue(AttributeIds.MaxHealth, maxHealth);
        Asc.Attributes.SetCurrentValue(AttributeIds.Health, maxHealth);
    }

    private float ComputeTotalMaxHealth() => _enemies.Sum(enemy => enemy.Asc.GetCurrentValue(AttributeIds.MaxHealth));
}