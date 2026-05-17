using Godot;
using KemoCard.Frame.Ui;

namespace KemoCard.Mod.Global.Ui;

public readonly record struct MenuDlgPayload(Action Close);

public partial class MenuDlg : BaseDlg
{
	private Button? _closeButton;
	private MenuDlgPayload _payload;

	public override void _Ready()
	{
		BuildChrome("主菜单");
	}

	public override void ApplyPayload(object payload)
	{
		_payload = (MenuDlgPayload)payload;
		if (_closeButton is not null)
		{
			_closeButton.Pressed -= OnClosePressed;
			_closeButton.Pressed += OnClosePressed;
		}
	}

	private void BuildChrome(string title)
	{
		var root = new MarginContainer
		{
			AnchorRight = 1,
			AnchorBottom = 1,
			OffsetRight = 0,
			OffsetBottom = 0,
		};
		root.AddThemeConstantOverride("margin_left", 48);
		root.AddThemeConstantOverride("margin_top", 48);
		root.AddThemeConstantOverride("margin_right", 48);
		root.AddThemeConstantOverride("margin_bottom", 48);
		AddChild(root);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 16);
		root.AddChild(column);

		var titleLabel = new Label
		{
			Text = title,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		titleLabel.AddThemeFontSizeOverride("font_size", 32);
		column.AddChild(titleLabel);

		column.AddChild(new Label { Text = "继续游戏" });
		column.AddChild(new Label { Text = "设置" });

		_closeButton = new Button { Text = "返回" };
		column.AddChild(_closeButton);
	}

	private void OnClosePressed()
	{
		_payload.Close();
	}
}
