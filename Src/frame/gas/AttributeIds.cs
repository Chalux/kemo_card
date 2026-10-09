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
    /// 治疗输出增加（2026-10-06 新增）：治疗量额外 <c>×(1 + 本属性)</c>，
    /// 与 <see cref="DamageDealtScale"/> 对伤害的关系同构。
    /// 「队伍获得绿属性的恢复的效果 +50%」这类增益用它表达。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Healing"/>（历史遗留、当前无任何读取路径）区分：本键才是治疗结算真正读的那一份。
    /// 作用域 = 施疗者（与 <see cref="HealPower"/> 同侧）；多个来源同桶加算后一次性乘算。
    /// </remarks>
    public const string HealingDealtScale = "HealingDealtScale";

    /// <summary>
    /// 嘲讽值（2026-09-25 新增）：敌方<b>单体 / 随机</b>攻击优先选中嘲讽值最高的友方角色
    /// （见战斗规格「嘲讽」）；0 = 不嘲讽。范围/全体攻击不受影响。
    /// </summary>
    public const string Taunt = "Taunt";

    /// <summary>
    /// 护盾（2026-09-26 新增）：点名该角色槽位、且来源为敌方的伤害先按 <b>1 点护盾抵 1 点伤害</b>抵扣，
    /// 抵扣量从护盾里扣掉，余额再落到队伍共享账本（见战斗规格「护盾」）。
    /// </summary>
    /// <remarks>
    /// 护盾是<b>可消耗资源</b>而非修饰符：授予走 <c>GainShield</c>（写属性 base 值），
    /// 受击抵扣时同样写 base 值，因此聚合重算不会把它抹掉。0 = 无护盾，无上限，战斗内永久。
    /// 队伍账本（<c>scope: Team</c>）与自身结算的伤害（中毒 / 手牌槽伤害）不抵扣——与受击钩子同口径。
    /// </remarks>
    public const string Shield = "Shield";

    /// <summary>
    /// 全伤害增加：与受伤增加同桶加算，连携单独乘算——
    /// <c>伤害 × (1 + 本属性 + Σ受伤增加) × (1 + 连携加成)</c>（战斗规格 §1.3）。
    /// </summary>
    public const string DamageDealtScale = "DamageDealtScale";

    /// <summary>只对来源角色当前结算的卡牌伤害生效，与全伤害增加同桶加算。</summary>
    public const string CardDamageDealtScale = "CardDamageDealtScale";

    /// <summary>
    /// 只对<b>普通攻击</b>生效的目标受伤倍率（2026-09-21 新增）：
    /// 普攻伤害额外 × (1 + 本属性)，与 <see cref="DamageTakenScale"/> 同桶加算。
    /// 卡牌伤害不受影响——「受到的普通攻击伤害 +25%」这类弱化用它表达。
    /// </summary>
    public const string NormalAttackDamageTakenScale = "NormalAttackDamageTakenScale";

    /// <summary>
    /// 只对<b>魔法伤害</b>生效的目标受伤倍率（2026-09-26 新增）：
    /// 魔法伤害额外 × (1 + 本属性)，与 <see cref="DamageTakenScale"/> 同桶加算。
    /// 物理 / 元素伤害不受影响——「受到的魔法伤害降低 25%」这类弱化用它表达。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="NormalAttackDamageTakenScale"/> 同构：只在伤害包维度为 <c>Magical</c> 时并入加算桶
    /// （见 <c>DamagePipeline.ResolveTakenScale</c> 与 GAS 公式通道的 <c>DamageExecution</c>）。
    /// 与其它属性一样，内容侧需要在 <c>content/attributes/</c> 声明同名定义才会有默认值与面板展示。
    /// </remarks>
    public const string MagicDamageTakenScale = "MagicDamageTakenScale";

    /// <summary>
    /// 普通攻击的<b>额外</b>次数（2026-09-21 新增）：本回合普攻执行次数 = <c>1 + max(0, 本属性)</c>。
    /// 卡牌用 <c>Add</c> 叠加（<c>maxStacks</c> 控制上限），默认 0 = 只有一次普攻。
    /// </summary>
    public const string NormalAttackCount = "NormalAttackCount";

    /// <summary>
    /// 物理攻击<b>卡牌</b>的额外攻击次数（2026-10-06 新增）：卡牌结算时若该牌是
    /// <c>ECardType.Physics</c>，本次伤害的总段数 = 卡牌自身声明的段数 + <c>max(0, 本属性)</c>。
    /// 「物理攻击的卡牌攻击次数 +1」用它表达。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="NormalAttackCount"/> 的区别：后者只加<b>普通攻击</b>的轮数，
    /// 本键只加<b>物理卡牌</b>的伤害段数，两者互不影响（对应「物理攻击的卡牌·普攻的攻击次数 +1」的两半）。
    /// 只对 <c>CardType == Physics</c> 的卡生效——魔法 / 支援 / 治疗等卡牌不吃；
    /// 卡牌未声明任何次数参数时基准为 1 段，加成后同样至少 1 段。
    /// 判定依赖当前结算卡牌的类型（<c>CombatSimulation.CurrentCardType</c>），卡牌结算区间之外不生效。
    /// </remarks>
    public const string PhysicalCardAttackCount = "PhysicalCardAttackCount";

    /// <summary>
    /// 只对<b>普通攻击</b>生效的伤害增加（2026-09-21 新增）：普攻伤害额外 × (1 + 本属性)，
    /// 与 <see cref="DamageDealtScale"/> 同桶加算。卡牌伤害不受影响——「自身普通攻击伤害 +50%」用它表达。
    /// </summary>
    public const string NormalAttackDamageDealtScale = "NormalAttackDamageDealtScale";

    /// <summary>普通攻击（含追打）实际造成的生命损失按此比例额外授予攻击者护盾，默认 0。</summary>
    public const string NormalAttackShieldGainScale = "NormalAttackShieldGainScale";

    /// <summary>持有者产出的充能球恢复量增加，与其他恢复量加成加算。</summary>
    public const string OrbHealingScale = "OrbHealingScale";

    /// <summary>全队充能球自动触发时，整批球的伤害与恢复量增加。</summary>
    public const string AutoOrbPowerScale = "AutoOrbPowerScale";

    /// <summary>回合结束每次产球时额外复制的数量，复制球的产球者为属性持有者。</summary>
    public const string TurnEndOrbBonusCount = "TurnEndOrbBonusCount";

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