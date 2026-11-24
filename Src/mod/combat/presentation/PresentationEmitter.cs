using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Presentation;

/// <summary>
/// 表现事件的组装工具（战斗规格 §16）：把"读一遍当前状态再 Emit"这类重复代码收敛到一处，
/// 编排层只需一行调用。全部方法都是纯函数式记账，不改变任何战斗状态。
/// </summary>
public static class PresentationEmitter
{
    /// <summary>从共用牌堆抽牌，按实际入手顺序及付费角色记录动画。定向补满可限定归属。</summary>
    public static int DrawTeamAndEmit(CombatSimulation simulation, int count, int? requiredOwner = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var owner = -1;
        var cards = new List<DrawnCard>();
        var drawn = simulation.PlayerTeam.CardPiles.Draw(count, simulation.DrawRng, (index, slot) =>
        {
            if (owner != index && cards.Count > 0)
            {
                simulation.Presentation.Emit(new CardsDrawnEvent(owner, cards));
                cards = [];
            }
            owner = index;
            cards.Add(new DrawnCard(slot.SlotIndex, slot.CardId!, slot.RuntimeInstanceId!));
        }, requiredOwner);
        if (cards.Count > 0)
            simulation.Presentation.Emit(new CardsDrawnEvent(owner, cards));
        return drawn;
    }

    /// <summary>明确指定角色的抽牌效果，仍从队伍牌堆取该角色的牌。</summary>
    public static int DrawAndEmit(CombatSimulation simulation, int characterIndex, int count) =>
        DrawTeamAndEmit(simulation, count, requiredOwner: characterIndex);

    /// <summary>手牌槽占用快照（RuntimeInstanceId 与 CardId），供弃牌前后比对。</summary>
    public static (string? RuntimeId, string? CardId)[] SnapshotHand(CharacterBattleInstance character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var snapshot = new (string?, string?)[character.HandSlots.Count];
        for (var i = 0; i < snapshot.Length; i++)
        {
            var slot = character.HandSlots[i];
            snapshot[i] = (slot.RuntimeInstanceId, slot.CardId);
        }

        return snapshot;
    }

    /// <summary>
    /// 与 <see cref="SnapshotHand"/> 比对：原来有牌、现在为空或换了牌的槽位各记一条 <see cref="CardDiscardedEvent"/>。
    /// 中途弃牌通道（§4.6）都走这里，避免在每个弃牌分支手写事件。
    /// </summary>
    public static void EmitDiscardsByDiff(
        CombatSimulation simulation,
        int characterIndex,
        (string? RuntimeId, string? CardId)[] before,
        EDiscardChannel channel)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(before);
        var characters = simulation.PlayerTeam.Characters;
        if (characterIndex < 0 || characterIndex >= characters.Count)
            return;

        var character = characters[characterIndex];
        for (var i = 0; i < before.Length && i < character.HandSlots.Count; i++)
        {
            var (runtimeId, cardId) = before[i];
            if (runtimeId is null || cardId is null)
                continue;
            if (string.Equals(character.HandSlots[i].RuntimeInstanceId, runtimeId, StringComparison.Ordinal))
                continue;

            simulation.Presentation.Emit(new CardDiscardedEvent(characterIndex, i, cardId, channel, runtimeId));
        }
    }

    /// <summary>记录某角色当前能量快照。</summary>
    public static void EmitEnergy(CombatSimulation simulation, int characterIndex)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var characters = simulation.PlayerTeam.Characters;
        if (characterIndex < 0 || characterIndex >= characters.Count)
            return;

        var character = characters[characterIndex];
        simulation.Presentation.Emit(new EnergyChangedEvent(
            characterIndex,
            character.CurrentEnergy,
            character.AvailableEnergy,
            character.MaxEnergy));
    }

    /// <summary>伤害写入后记账：目标当前 / 最大生命与存活状态一并快照。</summary>
    public static void EmitDamage(
        CombatSimulation simulation,
        CombatTargetRef source,
        CombatTargetRef target,
        float amount,
        EDamageKind kind,
        EElement element,
        string? effectId)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        var (hp, maxHp, alive) = SnapshotHealth(simulation, target);
        simulation.Presentation.Emit(new DamageDealtEvent(
            source,
            target,
            amount,
            kind,
            element,
            effectId,
            hp,
            maxHp,
            alive));
    }

    /// <summary>治疗写入后记账。</summary>
    public static void EmitHeal(
        CombatSimulation simulation,
        CombatTargetRef source,
        CombatTargetRef target,
        float amount)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (amount <= 0f)
            return;

        var (hp, maxHp, _) = SnapshotHealth(simulation, target);
        simulation.Presentation.Emit(new HealedEvent(source, target, amount, hp, maxHp));
    }

    /// <summary>
    /// 目标生命快照：玩家侧一律回落到队伍账本（玩家角色没有独立当前生命，规格 §1.2）；
    /// 敌方读该单位；越界目标返回 (0, 0, false)。
    /// </summary>
    public static (int Hp, int MaxHp, bool Alive) SnapshotHealth(CombatSimulation simulation, CombatTargetRef target)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (target.Side == ECombatSide.Player)
        {
            var team = simulation.PlayerTeam;
            return (team.SharedHp, team.MaxHp, !team.IsDefeated);
        }

        var enemies = simulation.EnemyTeam.Enemies;
        if (target.Index < 0 || target.Index >= enemies.Count)
            return (0, 0, false);

        var enemy = enemies[target.Index];
        return (enemy.CurrentHp, enemy.MaxHp, enemy.IsAlive);
    }
}