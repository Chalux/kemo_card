using KemoCard.Mod.Combat.Presentation;

namespace KemoCard.Mod.Run.Ui.CombatUi.Presentation;

/// <summary>
/// 表现事件的播放器：把一条战斗表现事件变成动画（Godot 实现见 <see cref="CombatAnimator"/>）。
/// 未处理的事件类型必须立即完成，不得卡住 <see cref="CombatPresentationDirector"/> 的队列。
/// </summary>
public interface ICombatEventPlayer
{
    Task PlayAsync(CombatPresentationEvent presentationEvent);
}