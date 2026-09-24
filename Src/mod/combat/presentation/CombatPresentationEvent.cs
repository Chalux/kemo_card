using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat.Effects;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
// EDiscardChannel 在 Effects；ECombatPhase 在 StateMachine；EDamageKind / EElement 在内容定义。

namespace KemoCard.Mod.Combat.Presentation;

/// <summary>
/// 表现事件基类（战斗规格 §16）：战斗逻辑在编排层记录"发生了什么"，界面事后取走并安排动画。
/// 所有事件只携带<b>值</b>（索引 / id / 数值 / 目标引用），不得持有运行时对象引用，也不得引用 Godot。
/// </summary>
public abstract record CombatPresentationEvent;

/// <summary>相位切换（只在真的变化时发）。</summary>
public sealed record PhaseChangedEvent(ECombatPhase From, ECombatPhase To, int TurnNumber) : CombatPresentationEvent;

/// <summary>BattleStart 管线开始。</summary>
public sealed record BattleStartedEvent : CombatPresentationEvent;

/// <summary>换波完成（敌人已替换，界面应按状态重建敌方舞台）。</summary>
public sealed record WaveStartedEvent(int WaveIndex) : CombatPresentationEvent;

/// <summary>一次抽到的牌：落到哪个手牌槽、是哪张卡。</summary>
public sealed record DrawnCard(int SlotIndex, string CardId);

/// <summary>某角色一次抽牌步骤抽到的全部牌（开局抽满 / 阶段抽牌各发一次）。</summary>
public sealed record CardsDrawnEvent(int CharacterIndex, IReadOnlyList<DrawnCard> Cards) : CombatPresentationEvent;

/// <summary>一张牌离开手牌槽进入弃牌堆。</summary>
public sealed record CardDiscardedEvent(int CharacterIndex, int SlotIndex, string CardId, EDiscardChannel Channel)
    : CombatPresentationEvent;

/// <summary>一张已标记牌开始结算（"角色移动到目标面前攻击"挂这里）。</summary>
public sealed record CardSettleStartedEvent(
    int CharacterIndex,
    int SlotIndex,
    string CardId,
    IReadOnlyList<CombatTargetRef> Targets) : CombatPresentationEvent;

/// <summary>一张牌结算结束（"回到原位"挂这里）。</summary>
public sealed record CardSettleEndedEvent(int CharacterIndex, int SlotIndex, string CardId) : CombatPresentationEvent;

/// <summary>伤害已写入（唯一 choke point：<c>DamagePipeline.NotifyAfter</c>）。</summary>
public sealed record DamageDealtEvent(
    CombatTargetRef Source,
    CombatTargetRef Target,
    float Amount,
    EDamageKind Kind,
    EElement Element,
    string? EffectId,
    int TargetHpAfter,
    int TargetMaxHp,
    bool TargetAlive) : CombatPresentationEvent;

/// <summary>治疗已写入（队伍账本或敌方单位）。</summary>
public sealed record HealedEvent(
    CombatTargetRef Source,
    CombatTargetRef Target,
    float Amount,
    int HpAfter,
    int MaxHp) : CombatPresentationEvent;

/// <summary>buff 持有者：角色 / 敌人（<see cref="HandSlotIndex"/> 为 null）或角色的手牌槽位容器。</summary>
public readonly record struct BuffHolderRef(CombatTargetRef Target, int? HandSlotIndex = null)
{
    public bool IsHandSlot => HandSlotIndex.HasValue;
}

/// <summary>新 buff 实例挂上持有者。</summary>
public sealed record BuffAppliedEvent(BuffHolderRef Holder, string BuffId, int Stacks) : CombatPresentationEvent;

/// <summary>已有 buff 层数变化（叠层或逐层脱落）。</summary>
public sealed record BuffStacksChangedEvent(BuffHolderRef Holder, string BuffId, int Stacks) : CombatPresentationEvent;

/// <summary>buff 实例被移除（到期 / 驱散 / 替换 / 互斥覆盖）。</summary>
public sealed record BuffRemovedEvent(BuffHolderRef Holder, string BuffId) : CombatPresentationEvent;

/// <summary>
/// 普通攻击的一次打击开始（本体或追打）：在其后的 <see cref="DamageDealtEvent"/> 之前发出，
/// <see cref="Targets"/> 为本次打击将命中的存活敌人。
/// </summary>
public sealed record NormalAttackStrikeEvent(
    int CharacterIndex,
    EDamageKind Kind,
    EElement Element,
    bool IsOwnerStrike,
    IReadOnlyList<CombatTargetRef> Targets) : CombatPresentationEvent;

/// <summary>敌人开始行动（意图已定）。</summary>
public sealed record EnemyActionStartedEvent(int EnemyIndex, string SkillId) : CombatPresentationEvent;

/// <summary>敌人行动结束。</summary>
public sealed record EnemyActionEndedEvent(int EnemyIndex, string SkillId) : CombatPresentationEvent;

/// <summary>充能球入队。</summary>
public sealed record OrbGainedEvent(string OrbTypeId, int ProducerIndex, int QueueCount) : CombatPresentationEvent;

/// <summary>充能球触发并清空队列（按入队顺序列出球类型）。</summary>
public sealed record OrbsTriggeredEvent(IReadOnlyList<string> OrbTypeIds, bool Automatic) : CombatPresentationEvent;

/// <summary>角色能量变化（当前 / 可用 / 上限）。</summary>
public sealed record EnergyChangedEvent(int CharacterIndex, int Current, int Available, int Max) : CombatPresentationEvent;