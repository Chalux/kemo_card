using KemoCard.Frame.Ui;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

/// <summary>
/// 对话框切换相关的生命周期约束测试（不依赖 Godot 场景树）。
/// OpenDlgAsync 在切换挂载阶段使用 CancellationToken.None 关闭旧对话框，
/// 并在 PlayOpenAsync 失败时通过 QueueFree 清理；此处验证状态机是否支持该流程。
/// </summary>
[TestFixture]
public sealed class UiManagerDlgSwitchTests
{
	[Test]
	public void Dlg_switch_lifecycle_sequence_is_valid()
	{
		var sm = new UiStateMachine();
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Opening), Is.True);
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Opened), Is.True);
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Closing), Is.True);
		Assert.That(sm.TryTransitionTo(UiLifecycleState.Closed), Is.True);
	}

	[Test]
	public void Failed_open_can_reset_via_closed_to_created()
	{
		var sm = new UiStateMachine();
		sm.TryTransitionTo(UiLifecycleState.Opening);
		sm.TryTransitionTo(UiLifecycleState.Opened);
		sm.TryTransitionTo(UiLifecycleState.Closing);
		sm.TryTransitionTo(UiLifecycleState.Closed);
		sm.TryTransitionTo(UiLifecycleState.Created);

		Assert.That(sm.TryTransitionTo(UiLifecycleState.Opening), Is.True);
		Assert.That(sm.Current, Is.EqualTo(UiLifecycleState.Opening));
	}
}
