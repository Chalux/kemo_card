using KemoCard.Frame.UI;

namespace KemoCard.Ui.Headless;

public partial class TestFitScaleBox : FitScaleBox
{
    protected override void OnExitTree() => throw new InvalidOperationException("expected fit exit failure");
}