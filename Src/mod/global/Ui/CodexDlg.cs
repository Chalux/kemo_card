using Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global.Ui.Comp;

namespace KemoCard.Mod.Global.Ui;

public record struct CodexDlgPayload;

public partial class CodexDlg : BaseDlg
{
	[Export] private OptionButton? _obField;
	[Export] private OptionButton? _obOp;
	[Export] private OptionButton? _obVal;
	[Export] private ItemList? _itemListConditions;
	[Export] private Button? _btnAdd;
	[Export] private LineEdit? _iptTxtFilter;
	[Export] private GridContainer? _gridCardList;
	[Export] private BasePager? _pager;

	public override string UIId => GlobalUiIds.Codex;
	public override string UIDir => "Src/mod/global/Ui";

	protected override void OnOpen()
	{
	}

	protected override void UpdateView()
	{
	}
}
