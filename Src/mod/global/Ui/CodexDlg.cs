using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Global.Ui;

public record struct CodexDlgPayload;

public partial class CodexDlg : BaseDlg
{
    public override string UIId => GlobalUiIds.Codex;
    public override string UIDir => "Src/mod/global/Ui";

    protected override void OnOpen()
    {
    }

    protected override void UpdateView()
    {
    }
}