using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Condition;

/// <summary>
/// 条件主体的身份解析（2026-09-26）：把战斗目标翻译成属性/种族位标志，供身份类条件使用。
/// 口径与 <c>CardDto.Element</c>、<c>CharacterDto.Race</c> 一致。
/// </summary>
internal static class CombatIdentity
{
    /// <summary>玩家槽位 / 敌人的属性与种族位标志；队伍账本、越界、无持有者一律 0（None）。</summary>
    public static (int ElementFlags, int RaceFlags) Resolve(CombatSimulation simulation, CombatTargetRef target)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        if (target.Side == ECombatSide.Player)
        {
            var characters = simulation.PlayerTeam.Characters;
            if (target.Index < 0 || target.Index >= characters.Count)
                return (0, 0);

            var character = characters[target.Index];
            return ((int)character.Element, (int)character.Race);
        }

        var enemies = simulation.EnemyTeam.Enemies;
        if (target.Index < 0 || target.Index >= enemies.Count)
            return (0, 0);

        var enemy = enemies[target.Index];
        return ((int)enemy.Element, (int)enemy.Race);
    }
}
