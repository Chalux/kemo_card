namespace KemoCard.Mod.Combat.Effects;

/// <summary>一次命中的不同数额；实际损血用于吸血、战报和伤害后事件。</summary>
internal readonly record struct DamageSettlementResult(
    float CalculatedAmount,
    float ShieldAbsorbed,
    float HealthLoss,
    float Overkill);