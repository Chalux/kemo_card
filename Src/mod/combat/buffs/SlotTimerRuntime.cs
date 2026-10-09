using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Combat.Buffs;

/// <summary>所有到期定时先移除，再结算伤害与扩散；新实例不进入本回合递减快照。</summary>
internal static class SlotTimerRuntime
{
    public static void ResolveExpired(CombatSimulation simulation,
        IReadOnlyList<(CombatTargetRef Holder, int Slot, BuffInstance Timer)> timers)
    {
        foreach (var (holder, slot, timer) in timers)
        {
            if (holder.Side != ECombatSide.Player || holder.Index < 0 ||
                holder.Index >= simulation.PlayerTeam.Characters.Count)
                continue;
            var character = simulation.PlayerTeam.Characters[holder.Index];
            if (timer.IsDormant || character.Buffs.HasTag(BuiltinBuffTags.TraitImmuneSlotTimer))
                continue;
            var amount = ContentParameters.ReadFloat(timer.Params ?? new Dictionary<string, object>(), "amount", 0);
            // 槽位自结算伤害不记作敌方攻击，仍吃角色全伤害减免与统一伤害规则。
            amount *= DamageScaling.CombineBonuses(0, DamagePipeline.ResolveTakenScale(simulation, holder));
            simulation.EffectExecutor.ApplyFixedDamage(simulation, holder, [holder], MathF.Max(0, amount), timer.Def.Id);
            var parameters = timer.Params?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<string, object>();
            parameters["timerTurns"] = timer.FullDuration;
            foreach (var neighbor in new[] { slot - 1, slot + 1 })
                if (neighbor >= 0 && neighbor < character.HandSlots.Count)
                    simulation.Buffs.ApplyToSlot(simulation, holder.Index, neighbor, timer.Def.Id, parameters);
        }
    }
}