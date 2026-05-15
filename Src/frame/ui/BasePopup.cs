using System;
using Godot;

namespace KemoCard.Frame.Ui;

public abstract partial class BasePopup : BaseUI
{
	[Export]
	public ColorRect? ModalMask { get; set; }

	public bool MaskClickClosesPopup { get; set; } = true;

	public event Action? CloseRequested;

	public override void _Ready()
	{
		base._Ready();
		if (ModalMask is not null)
		{
			ModalMask.GuiInput += OnModalMaskGuiInput;
		}
	}

	public override void _ExitTree()
	{
		if (ModalMask is not null)
		{
			ModalMask.GuiInput -= OnModalMaskGuiInput;
		}

		base._ExitTree();
	}

	public void SetMaskVisible(bool visible)
	{
		if (ModalMask is null)
		{
			return;
		}

		ModalMask.Visible = visible;
		ModalMask.MouseFilter = visible ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
	}

	private void OnModalMaskGuiInput(InputEvent @event)
	{
		if (!MaskClickClosesPopup || ModalMask is null || !ModalMask.Visible)
		{
			return;
		}

		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			CloseRequested?.Invoke();
		}
	}
}
