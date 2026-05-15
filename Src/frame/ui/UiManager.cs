using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace KemoCard.Frame.Ui;

public partial class UiManager : Node, IUiManager
{
	private readonly UiRegistry _registry = new();
	private Control? _dlgHost;
	private Control? _popupStack;
	private BaseDlg? _currentDlg;
	private CancellationTokenSource? _dlgLoadCts;
	private readonly Dictionary<string, BasePopup> _popupById = new(StringComparer.Ordinal);
	private BasePopup? _maskSubscribedPopup;

	#region Configure and registration

	public void Configure(Control dlgHost, Control popupStack)
	{
		_dlgHost = dlgHost ?? throw new ArgumentNullException(nameof(dlgHost));
		_popupStack = popupStack ?? throw new ArgumentNullException(nameof(popupStack));
	}

	public void RegisterDlg<TDlg, TPayload>(string id, Func<TPayload, TDlg> factory)
		where TDlg : BaseDlg
	{
		_registry.RegisterDlg(id, factory);
	}

	public void RegisterPopup<TPopup, TPayload>(string id, Func<TPayload, TPopup> factory)
		where TPopup : BasePopup
	{
		_registry.RegisterPopup(id, factory);
	}

	#endregion

	#region IUiManager open / close

	public async Task OpenDlgAsync<TPayload>(string id, TPayload payload, CancellationToken cancellationToken = default)
	{
		if (!_registry.TryGet(id, out var kind, out _, out var payloadType, out var factory))
		{
			GD.PushError($"UiManager: unregistered UI id '{id}'.");
			return;
		}

		if (kind != UiKind.Dlg)
		{
			GD.PushError($"UiManager: id '{id}' is not a dialog.");
			return;
		}

		if (payloadType != typeof(TPayload))
		{
			GD.PushError($"UiManager: payload type mismatch for dlg '{id}'.");
			return;
		}

		if (_dlgHost is null)
		{
			GD.PushError("UiManager: DlgHost not configured.");
			return;
		}

		_dlgLoadCts?.Cancel();
		_dlgLoadCts?.Dispose();
		_dlgLoadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var token = _dlgLoadCts.Token;

		BaseDlg newDlg;
		try
		{
			newDlg = (BaseDlg)factory(payload!);
		}
		catch (Exception ex)
		{
			GD.PushError($"UiManager: factory failed for dlg '{id}': {ex.Message}");
			return;
		}

		newDlg.Visible = false;
		AddChild(newDlg);

		if (token.IsCancellationRequested)
		{
			newDlg.QueueFree();
			return;
		}

		newDlg.ApplyPayload(payload!);
		if (!newDlg.Lifecycle.TryTransitionTo(UiLifecycleState.Opening))
		{
			GD.PushError("UiManager: dlg lifecycle cannot enter Opening.");
			newDlg.QueueFree();
			return;
		}

		await newDlg.PlayOpenAsync();
		if (token.IsCancellationRequested)
		{
			newDlg.QueueFree();
			return;
		}

		if (!newDlg.Lifecycle.TryTransitionTo(UiLifecycleState.Opened))
		{
			GD.PushError("UiManager: dlg lifecycle cannot enter Opened.");
			newDlg.QueueFree();
			return;
		}

		if (token.IsCancellationRequested)
		{
			newDlg.QueueFree();
			return;
		}

		RemoveChild(newDlg);

		var previous = _currentDlg;
		if (previous is not null && IsInstanceValid(previous) && previous.GetParent() == _dlgHost)
		{
			_dlgHost.RemoveChild(previous);
			await CloseDlgInstanceAsync(previous, token);
		}

		_dlgHost.AddChild(newDlg);
		ApplyFullRectAnchors(newDlg);
		newDlg.Visible = true;
		_currentDlg = newDlg;
	}

	public async Task OpenPopupAsync<TPayload>(
		string id,
		TPayload payload,
		PopupReopenBehavior behavior,
		bool maskClickClosesPopup = true,
		CancellationToken cancellationToken = default)
	{
		if (_popupStack is null)
		{
			GD.PushError("UiManager: PopupStack not configured.");
			return;
		}

		if (!_registry.TryGet(id, out var kind, out _, out var payloadType, out var factory))
		{
			GD.PushError($"UiManager: unregistered UI id '{id}'.");
			return;
		}

		if (kind != UiKind.Popup)
		{
			GD.PushError($"UiManager: id '{id}' is not a popup.");
			return;
		}

		if (payloadType != typeof(TPayload))
		{
			GD.PushError($"UiManager: payload type mismatch for popup '{id}'.");
			return;
		}

		cancellationToken.ThrowIfCancellationRequested();

		if (_popupById.TryGetValue(id, out var existing))
		{
			switch (behavior)
			{
				case PopupReopenBehavior.None:
				{
					var idx = existing.GetIndex();
					var last = _popupStack.GetChildCount() - 1;
					if (idx < last)
					{
						_popupStack.MoveChild(existing, last);
					}

					existing.MaskClickClosesPopup = maskClickClosesPopup;
					RefreshPopupMasks();
					return;
				}
				case PopupReopenBehavior.ReplayOpenAnimation:
				{
					var idx = existing.GetIndex();
					var last = _popupStack.GetChildCount() - 1;
					if (idx < last)
					{
						_popupStack.MoveChild(existing, last);
					}

					existing.MaskClickClosesPopup = maskClickClosesPopup;
					await existing.PlayOpenAsync();
					RefreshPopupMasks();
					return;
				}
				case PopupReopenBehavior.ReplaceInstance:
				{
					UnsubscribeMaskHandler(existing);
					_popupById.Remove(id);
					await ClosePopupInternalAsync(existing);
					break;
				}
				default:
					throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Unknown PopupReopenBehavior.");
			}
		}

		cancellationToken.ThrowIfCancellationRequested();

		if (_popupById.ContainsKey(id))
		{
			return;
		}

		BasePopup newPopup;
		try
		{
			newPopup = (BasePopup)factory(payload!);
		}
		catch (Exception ex)
		{
			GD.PushError($"UiManager: factory failed for popup '{id}': {ex.Message}");
			return;
		}

		newPopup.MaskClickClosesPopup = maskClickClosesPopup;
		_popupStack.AddChild(newPopup);
		ApplyFullRectAnchors(newPopup);
		newPopup.ApplyPayload(payload!);

		if (!newPopup.Lifecycle.TryTransitionTo(UiLifecycleState.Opening))
		{
			GD.PushError("UiManager: popup lifecycle cannot enter Opening.");
			newPopup.QueueFree();
			return;
		}

		await newPopup.PlayOpenAsync();
		cancellationToken.ThrowIfCancellationRequested();

		if (!newPopup.Lifecycle.TryTransitionTo(UiLifecycleState.Opened))
		{
			GD.PushError("UiManager: popup lifecycle cannot enter Opened.");
			newPopup.QueueFree();
			return;
		}

		_popupById[id] = newPopup;
		RefreshPopupMasks();
	}

	public void CloseTopPopup()
	{
		var top = GetTopBasePopup();
		if (top is null)
		{
			return;
		}

		_ = RunCloseTopPopupAsync(top);
	}

	public void CloseDlg()
	{
		if (_currentDlg is null || !IsInstanceValid(_currentDlg))
		{
			_currentDlg = null;
			return;
		}

		_ = RunCloseDlgAsync(_currentDlg);
	}

	#endregion

	#region Lifecycle helpers

	public override void _ExitTree()
	{
		_dlgLoadCts?.Cancel();
		_dlgLoadCts?.Dispose();
		_dlgLoadCts = null;
		base._ExitTree();
	}

	private static void ApplyFullRectAnchors(Control control)
	{
		control.AnchorLeft = 0;
		control.AnchorTop = 0;
		control.AnchorRight = 1;
		control.AnchorBottom = 1;
		control.OffsetLeft = 0;
		control.OffsetTop = 0;
		control.OffsetRight = 0;
		control.OffsetBottom = 0;
	}

	private async Task CloseDlgInstanceAsync(BaseDlg dlg, CancellationToken cancellationToken)
	{
		if (!IsInstanceValid(dlg))
		{
			return;
		}

		if (dlg.Lifecycle.Current is UiLifecycleState.Opened or UiLifecycleState.Opening)
		{
			if (!dlg.Lifecycle.TryTransitionTo(UiLifecycleState.Closing))
			{
				GD.PushError("UiManager: dlg lifecycle cannot enter Closing.");
			}

			await dlg.PlayCloseAsync();
			cancellationToken.ThrowIfCancellationRequested();
			dlg.Lifecycle.TryTransitionTo(UiLifecycleState.Closed);
		}

		var parent = dlg.GetParent();
		parent?.RemoveChild(dlg);
		if (IsInstanceValid(dlg))
		{
			dlg.QueueFree();
		}
	}

	private async Task ClosePopupInternalAsync(BasePopup popup)
	{
		if (!IsInstanceValid(popup))
		{
			return;
		}

		if (popup.Lifecycle.Current is UiLifecycleState.Opened or UiLifecycleState.Opening)
		{
			if (!popup.Lifecycle.TryTransitionTo(UiLifecycleState.Closing))
			{
				GD.PushError("UiManager: popup lifecycle cannot enter Closing.");
			}

			await popup.PlayCloseAsync();
			popup.Lifecycle.TryTransitionTo(UiLifecycleState.Closed);
		}

		var parent = popup.GetParent();
		parent?.RemoveChild(popup);
		if (IsInstanceValid(popup))
		{
			popup.QueueFree();
		}
	}

	private async Task RunCloseTopPopupAsync(BasePopup top)
	{
		UnsubscribeMaskHandler(top);
		RemovePopupFromMap(top);
		await ClosePopupInternalAsync(top);
		RefreshPopupMasks();
	}

	private async Task RunCloseDlgAsync(BaseDlg dlg)
	{
		if (_currentDlg == dlg)
		{
			_currentDlg = null;
		}

		await CloseDlgInstanceAsync(dlg, CancellationToken.None);
	}

	private BasePopup? GetTopBasePopup()
	{
		if (_popupStack is null)
		{
			return null;
		}

		for (var i = _popupStack.GetChildCount() - 1; i >= 0; i--)
		{
			if (_popupStack.GetChild(i) is BasePopup p)
			{
				return p;
			}
		}

		return null;
	}

	private static void RemovePopupFromMapCore(Dictionary<string, BasePopup> map, BasePopup instance)
	{
		string? foundKey = null;
		foreach (var kv in map)
		{
			if (ReferenceEquals(kv.Value, instance))
			{
				foundKey = kv.Key;
				break;
			}
		}

		if (foundKey is not null)
		{
			map.Remove(foundKey);
		}
	}

	private void RemovePopupFromMap(BasePopup instance)
	{
		RemovePopupFromMapCore(_popupById, instance);
	}

	#endregion

	#region Popup mask coordination

	private void RefreshPopupMasks()
	{
		if (_popupStack is null)
		{
			return;
		}

		if (_maskSubscribedPopup is not null)
		{
			_maskSubscribedPopup.CloseRequested -= OnTopPopupCloseRequested;
			_maskSubscribedPopup = null;
		}

		BasePopup? top = null;
		for (var i = _popupStack.GetChildCount() - 1; i >= 0; i--)
		{
			if (_popupStack.GetChild(i) is BasePopup p)
			{
				top = p;
				break;
			}
		}

		for (var i = 0; i < _popupStack.GetChildCount(); i++)
		{
			if (_popupStack.GetChild(i) is not BasePopup popup)
			{
				continue;
			}

			var isTop = ReferenceEquals(popup, top);
			popup.SetMaskVisible(isTop);
		}

		if (top is not null && top.MaskClickClosesPopup)
		{
			top.CloseRequested += OnTopPopupCloseRequested;
			_maskSubscribedPopup = top;
		}
	}

	private void UnsubscribeMaskHandler(BasePopup popup)
	{
		if (_maskSubscribedPopup == popup)
		{
			popup.CloseRequested -= OnTopPopupCloseRequested;
			_maskSubscribedPopup = null;
		}
	}

	private void OnTopPopupCloseRequested()
	{
		CloseTopPopup();
	}

	#endregion
}
