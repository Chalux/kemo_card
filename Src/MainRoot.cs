using Godot;
using KemoCard.Frame.Ui;

namespace MainRoot;

public partial class MainRoot : Control
{
	public override void _Ready()
	{
		var uiManager = GetNodeOrNull<UiManager>("UiManager");
		var dlgHost = GetNodeOrNull<Control>("DlgCanvas/DlgHost");
		var popupStack = GetNodeOrNull<Control>("PopupCanvas/PopupStack");
		if (uiManager is null || dlgHost is null || popupStack is null)
		{
			GD.PushError("MainRoot: missing UiManager, DlgCanvas/DlgHost, or PopupCanvas/PopupStack.");
			return;
		}

		uiManager.Configure(dlgHost, popupStack);
	}
}
