using Godot;
using KemoCard.Mod.Combat.Presentation;

namespace KemoCard.Mod.Run.Ui.CombatUi.Presentation;

/// <summary>
/// <see cref="CombatAnimator"/> 对战斗界面的只读访问面：按索引取单位节点、取血条 / 手牌槽 / 球位，
/// 生成飘字。界面（<see cref="CombatWin"/>）实现它；动画不直接依赖界面类型。
/// </summary>
public interface ICombatStageView
{
    /// <summary>当前操控的槽位（手牌 / 能量类事件只对它可见）。</summary>
    int ControlledSlot { get; }

    AllyUnitCmp? GetAllyUnit(int slotIndex);

    EnemyUnitCmp? GetEnemyUnit(int enemyIndex);

    HpBarCmp? SharedHpBar { get; }

    /// <summary>只有当前操控角色的手牌槽在界面上；其余角色返回 null。</summary>
    HandSlotCmp? GetHandSlot(int characterIndex, int slotIndex);

    ActorInfoCmp? ActorInfo { get; }

    OrbQueueCmp? OrbQueue { get; }

    /// <summary>某个 buff 持有者当前对应的图标列（不在界面上的持有者返回 null）。</summary>
    BuffListCmp? GetBuffList(BuffHolderRef holder);

    /// <summary>飘字等特效的挂载层（全屏、不吃鼠标）。</summary>
    Control FxLayer { get; }

    /// <summary>墓地组件中心（弃牌飞向此处）。</summary>
    Vector2 GraveyardGlobalCenter { get; }

    /// <summary>生成一枚飘字节点（已挂到 <see cref="FxLayer"/>）；场景缺失返回 null。</summary>
    DamageNumberCmp? SpawnDamageNumber();

    /// <summary>顶部相位横幅短暂显示。</summary>
    Task ShowPhaseBannerAsync(string text, float duration);

    /// <summary>换波：按模拟器状态重建敌方舞台。</summary>
    void RebuildEnemies();

    /// <summary>立即按状态重绘某个敌人（阵亡态等）。</summary>
    void RefreshEnemy(int enemyIndex);

    /// <summary>立即按状态重绘 buff 列表（图标弹入前先把新图标画出来）。</summary>
    void RefreshBuffs(BuffHolderRef holder);

    /// <summary>立即按状态重绘当前操控角色的手牌槽。</summary>
    void RefreshHandSlot(int characterIndex, int slotIndex);

    /// <summary>立即按状态重绘充能球队列（入队 / 触发的闪光前先把颜色画对）。</summary>
    void RefreshOrbs();

    /// <summary>
    /// 按事件载荷单独上色一格球位（不读模拟器状态）：满员自动触发时队列已被清空，
    /// 靠状态重绘看不到刚入队的球，必须用 <c>OrbGainedEvent</c> 的载荷把这一格画出来。
    /// </summary>
    void PaintOrb(int index, string orbTypeId, int queueCount);
}