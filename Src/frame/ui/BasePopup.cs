using System;
using Godot;

namespace KemoCard.Frame.Ui;

public abstract partial class BasePopup : BaseUI
{
	private ColorRect? _modalMask;
	private bool _modalMaskBound;

	[Export]
	public ColorRect? ModalMask
	{
		get => _modalMask;
		set
		{
			UnbindModalMask();
			_modalMask = value;
			BindModalMask();
		}
	}

	public bool MaskClickClosesPopup { get; set; } = true;

	public event Action? CloseRequested;

	public override void _Ready()
	{
		base._Ready();
		BindModalMask();
	}

	public override void _ExitTree()
	{
		UnbindModalMask();
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

	private void BindModalMask()
	{
		if (_modalMaskBound || _modalMask is null || !IsInsideTree())
		{
			return;
		}

		_modalMask.GuiInput += OnModalMaskGuiInput;
		_modalMaskBound = true;
	}

	private void UnbindModalMask()
	{
		if (!_modalMaskBound || _modalMask is null)
		{
			return;
		}

		_modalMask.GuiInput -= OnModalMaskGuiInput;
		_modalMaskBound = false;
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
