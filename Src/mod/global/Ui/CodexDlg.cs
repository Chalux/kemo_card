using Godot;
using KemoCard.Frame.Ui;

namespace KemoCard.Mod.Global.Ui;

public readonly record struct CodexDlgPayload(Action Close);

public partial class CodexDlg : BaseDlg
{
	private Button? _closeButton;
	private CodexDlgPayload _payload;

	public override void _Ready()
	{
		BuildChrome(Tr("UI_CODEX_TITLE"));
	}

	public override void ApplyPayload(object payload)
	{
		_payload = (CodexDlgPayload)payload;
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

		column.AddChild(new Label { Text = Tr("UI_CODEX_PLACEHOLDER") });

		_closeButton = new Button { Text = Tr("UI_COMMON_BACK") };
		column.AddChild(_closeButton);
	}

	private void OnClosePressed()
	{
		_payload.Close();
	}
}
