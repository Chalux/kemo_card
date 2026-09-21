using System.Text.Json.Serialization;

namespace KemoCard.Frame.Content.Definitions;

/// <summary>
/// 充能球的攻击加成来源：产球者的哪一项攻击以 <see cref="OrbTypeDto.AttackBonusScale"/> 的比例参与加算。
/// </summary>
public enum EOrbAttackSource
{
    /// <summary>物攻 / 魔攻取较高者（内建四属性球）。</summary>
    Higher,

    /// <summary>物理攻击（内建物理球）。</summary>
    Physical,

    /// <summary>魔法攻击（内建魔法球）。</summary>
    Magic,
}

/// <summary>
/// 充能球类型（内容类别 <c>orbs</c>）：内建四属性球 + 物理/魔法球，Mod 可注册特殊球。
/// </summary>
/// <remarks>
/// <para>充能球是全队共享的队列资源（容量见 <c>OrbQueue.Capacity</c>）：出牌阶段球数达标时可主动触发，
/// 满员时获得即自动触发。触发时清空队列，按入队顺序<b>逐个球</b>结算：每个球执行一次本类型的效果，
/// 源为<b>该球的产球者</b>。</para>
/// <para>伤害球的单球伤害 = <see cref="PerOrbAmount"/> + <see cref="AttackBonusScale"/> × 产球者攻击
/// （按 <see cref="AttackSource"/> 取物攻/魔攻/两者较高者），再乘产球者的全伤害增加与目标受伤倍率；
/// <b>不吃目标物防/魔防、不吃连携</b>（充能球是团队触发，不属于任何单卡的连携区间）。</para>
/// <para>伤害维度自 2026-09-20 起拆成两维：<see cref="DamageKind"/>（元素球 = <c>Elemental</c>、
/// 物理球 = <c>Physical</c>、魔法球 = <c>Magical</c>）与 <see cref="Element"/>（元素球必填，物理/魔法球留空）。</para>
/// <para>特殊球不能由回合结束统计产出（回合结束只产出内建 6 种），只能由 Mod 的角色/卡牌/效果授予。</para>
/// </remarks>
public sealed class OrbTypeDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayNameId")]
    public string DisplayNameId { get; init; } = "";

    [JsonPropertyName("iconPath")]
    public string IconPath { get; init; } = "";

    /// <summary>是否在触发时造成伤害；<c>false</c> = 纯效果球（只跑 <see cref="TriggerEffects"/>）。</summary>
    [JsonPropertyName("dealsDamage")]
    public bool DealsDamage { get; init; } = true;

    /// <summary>
    /// 伤害类型维度：<c>Elemental</c>（元素球，不吃防御）/ <c>Physical</c>（物理球）/
    /// <c>Magical</c>（魔法球）。球伤害一律不吃防御，因此该字段只影响伤害标签与攻击来源口径。
    /// </summary>
    [JsonPropertyName("damageKind")]
    public EDamageKind DamageKind { get; init; } = EDamageKind.Elemental;

    /// <summary>元素维度：元素球必填（可多元素）；物理/魔法球留空。</summary>
    [JsonPropertyName("element")]
    public EElement Element { get; init; } = EElement.None;

    /// <summary>每球固定伤害基数（内建 6）。</summary>
    [JsonPropertyName("perOrbAmount")]
    public float PerOrbAmount { get; init; }

    /// <summary>产球者攻击的加成比例：1 = 100%（内建 1）。</summary>
    [JsonPropertyName("attackBonusScale")]
    public float AttackBonusScale { get; init; }

    [JsonPropertyName("attackSource")]
    public EOrbAttackSource AttackSource { get; init; }

    /// <summary>每球额外执行的效果（源 = 该球产球者）；特殊球用它表达收益。</summary>
    [JsonPropertyName("triggerEffects")]
    public List<EffectRefDto> TriggerEffects { get; init; } = [];

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];
}
