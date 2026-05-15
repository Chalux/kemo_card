using System.Threading.Tasks;
using Godot;

namespace KemoCard.Frame.Ui;

public abstract partial class BaseUI : Control
{
	public UiStateMachine Lifecycle { get; } = new();

	public abstract void ApplyPayload(object payload);

	public virtual Task PlayOpenAsync()
	{
		return Task.CompletedTask;
	}

	public virtual Task PlayCloseAsync()
	{
		return Task.CompletedTask;
	}
}
