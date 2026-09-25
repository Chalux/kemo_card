namespace KemoCard.Frame.Gas;

public static class AttributeIds
{
    public const string Health = "Health";
    public const string MaxHealth = "MaxHealth";
    public const string PhysicalAttack = "PhysicalAttack";
    public const string PhysicalDefense = "PhysicalDefense";
    public const string MagicAttack = "MagicAttack";
    public const string MagicDefense = "MagicDefense";
    public const string HealPower = "HealPower";
    public const string MaxEnergy = "MaxEnergy";
    public const string InitialEnergy = "InitialEnergy";
    public const string Damage = "Damage";
    public const string Healing = "Healing";
    public const string DamageTakenScale = "DamageTakenScale";

    /// <summary>
    /// 嘲讽值（2026-09-25 新增）：敌方<b>单体 / 随机</b>攻击优先选中嘲讽值最高的友方角色
    /// （见战斗规格「嘲讽」）；0 = 不嘲讽。范围/全体攻击不受影响。
    /// </summary>
    public const string Taunt = "Taunt";

    /// <summary>
    /// 全伤害增加：与受伤增加同桶加算，连携单独乘算——
    /// <c>伤害 × (1 + 本属性 + Σ受伤增加) × (1 + 连携加成)</c>（战斗规格 §1.3）。
    /// </summary>
    public const string DamageDealtScale = "DamageDealtScale";

    /// <summary>
    /// 只对<b>普通攻击</b>生效的目标受伤倍率（2026-09-21 新增）：
    /// 普攻伤害额外 × (1 + 本属性)，与 <see cref="DamageTakenScale"/> 同桶加算。
    /// 卡牌伤害不受影响——「受到的普通攻击伤害 +25%」这类弱化用它表达。
    /// </summary>
    public const string NormalAttackDamageTakenScale = "NormalAttackDamageTakenScale";

    /// <summary>
    /// 普通攻击的<b>额外</b>次数（2026-09-21 新增）：本回合普攻执行次数 = <c>1 + max(0, 本属性)</c>。
    /// 卡牌用 <c>Add</c> 叠加（<c>maxStacks</c> 控制上限），默认 0 = 只有一次普攻。
    /// </summary>
    public const string NormalAttackCount = "NormalAttackCount";

    /// <summary>
    /// 只对<b>普通攻击</b>生效的伤害增加（2026-09-21 新增）：普攻伤害额外 × (1 + 本属性)，
    /// 与 <see cref="DamageDealtScale"/> 同桶加算。卡牌伤害不受影响——「自身普通攻击伤害 +50%」用它表达。
    /// </summary>
    public const string NormalAttackDamageDealtScale = "NormalAttackDamageDealtScale";

    /// <summary>
    /// 充能球伤害增加（2026-09-21 新增）：球伤害额外 <c>×(1 + 本属性)</c>，与 <see cref="DamageDealtScale"/>
    /// 按"加算"口径并入同一桶（见战斗规格「增伤与受伤增加一律加算」）。
    /// </summary>
    /// <remarks>
    /// 带元素掩码的修正（<c>AttributeModifierDefDto.elementMask</c>）不写在本键上，而是按元素拆到
    /// <see cref="OrbDamageScaleFor"/>；球结算时读「本键 + 该球元素对应键」之和。
    /// </remarks>
    public const string OrbDamageScale = "OrbDamageScale";

    /// <summary>某元素专属的球伤害增加键（由带掩码的修正写入，形如 <c>OrbDamageScale:Green</c>）。</summary>
    public static string OrbDamageScaleFor(KemoCard.Frame.Content.Definitions.EElement element) =>
        $"{OrbDamageScale}:{element}";
}