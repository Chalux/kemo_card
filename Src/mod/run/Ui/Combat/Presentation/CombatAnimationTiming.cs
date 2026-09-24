namespace KemoCard.Mod.Run.Ui.CombatUi.Presentation;

/// <summary>
/// 战斗表现的时长常量（秒）。后续调动画节奏只改这里；<see cref="CombatAnimator"/> 不写魔法数字。
/// </summary>
public static class CombatAnimationTiming
{
    /// <summary>施法者前移到目标面前 / 回原位。</summary>
    public const float UnitMove = 0.18f;

    /// <summary>攻击动作（缩放脉冲或序列帧 attack 的保底等待）。</summary>
    public const float UnitAttack = 0.16f;

    /// <summary>受击抖动。</summary>
    public const float UnitShake = 0.22f;

    /// <summary>阵亡淡出。</summary>
    public const float UnitFadeOut = 0.35f;

    /// <summary>血条数值过渡。</summary>
    public const float HpBar = 0.25f;

    /// <summary>飘字上浮 + 淡出。</summary>
    public const float DamageNumber = 0.7f;

    /// <summary>抽到的牌放入手牌槽（缩放弹出）。</summary>
    public const float CardDraw = 0.16f;

    /// <summary>弃牌飞向墓地。</summary>
    public const float CardDiscard = 0.2f;

    /// <summary>buff 图标弹入 / 弹出。</summary>
    public const float BuffPop = 0.15f;

    /// <summary>充能球入队亮起 / 触发整排闪光。</summary>
    public const float OrbFlash = 0.25f;

    /// <summary>相位横幅停留。</summary>
    public const float PhaseBanner = 0.6f;

    /// <summary>敌人行动前后的短暂停顿，让玩家看清是谁在动。</summary>
    public const float EnemyBeat = 0.12f;

    /// <summary>施法者站在目标面前的停顿（等伤害飘字先出现）。</summary>
    public const float StrikeHold = 0.08f;

    /// <summary>前移时与目标保持的水平间距（像素）。</summary>
    public const float ApproachGap = 24f;
}