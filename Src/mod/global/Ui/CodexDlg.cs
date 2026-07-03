using Godot;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Global.Ui;

public readonly record struct CodexDlgPayload(Action Close);

public partial class CodexDlg : BaseDlg
{
    public override string UIId => throw new NotImplementedException();

    public override string UIDir => throw new NotImplementedException();

    protected override void OnOpen()
    {
        throw new NotImplementedException();
    }

    protected override void UpdateView()
    {
        throw new NotImplementedException();
    }
}
