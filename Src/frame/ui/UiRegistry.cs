using System;
using System.Collections.Generic;

namespace KemoCard.Frame.Ui;

public sealed class UiRegistry
{
	private sealed record Entry(UiKind Kind, Type UiType, Type PayloadType, Func<object, BaseUI> Factory);

	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
	private readonly UiIdDuplicateGuard _ids = new();

	public void RegisterDlg<TDlg, TPayload>(string id, Func<TPayload, TDlg> factory)
		where TDlg : BaseDlg
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		ArgumentNullException.ThrowIfNull(factory);
		_ids.Add(id);
		_entries[id] = new Entry(
			UiKind.Dlg,
			typeof(TDlg),
			typeof(TPayload),
			p => factory((TPayload)p));
	}

	public void RegisterPopup<TPopup, TPayload>(string id, Func<TPayload, TPopup> factory)
		where TPopup : BasePopup
	{
		ArgumentException.ThrowIfNullOrEmpty(id);
		ArgumentNullException.ThrowIfNull(factory);
		_ids.Add(id);
		_entries[id] = new Entry(
			UiKind.Popup,
			typeof(TPopup),
			typeof(TPayload),
			p => factory((TPayload)p));
	}

	public bool TryGet(string id, out UiKind kind, out Type uiType, out Type payloadType, out Func<object, BaseUI> factory)
	{
		if (!_entries.TryGetValue(id, out var e))
		{
			kind = default;
			uiType = typeof(void);
			payloadType = typeof(void);
			factory = null!;
			return false;
		}

		kind = e.Kind;
		uiType = e.UiType;
		payloadType = e.PayloadType;
		factory = e.Factory;
		return true;
	}
}
