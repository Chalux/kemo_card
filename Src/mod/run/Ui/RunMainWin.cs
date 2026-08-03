using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Global;
using KemoCard.Mod.Global.Ui;

namespace KemoCard.Mod.Run.Ui;

public partial class RunMainWin : BaseWin
{
    [Export] private Label? _lblStory;
    [Export] private Label? _lblPhase;
    [Export] private Label? _lblRing;
    [Export] private Label? _lblSeed;
    [Export] private Label? _lblGold;
    [Export] private Button? _btnAbandon;

    public override string UIId => RunUiIds.RunMain;
    public override string UIDir => "Src/mod/run/Ui";

    protected override void InitEvent()
    {
        if (_btnAbandon != null)
        {
            OnClicks(_btnAbandon, OnAbandon);
        }
    }

    protected override void OnOpen()
    {
        UpdateView();
    }

    protected override void UpdateView()
    {
        var run = RunRuntime.Current;
        if (run == null)
        {
            Close();
            return;
        }

        var state = run.State;
        var storyName = state.StoryId;
        if (AppRoot.Services.ContentModPipeline.Registry.Store.TryGetStory(state.StoryId, out var story)
            && !string.IsNullOrWhiteSpace(story.DisplayNameId))
        {
            storyName = Localization.Tr(story.DisplayNameId);
        }

        if (_lblStory != null)
        {
            _lblStory.Text = storyName;
        }

        if (_lblPhase != null)
        {
            _lblPhase.Text = Localization.Tr($"UI_RUN_PHASE_{state.Phase.ToString().ToUpperInvariant()}");
        }

        if (_lblRing != null)
        {
            _lblRing.Text = $"{state.CurrentRing} / {state.MaxRing}";
        }

        if (_lblSeed != null)
        {
            _lblSeed.Text = state.RunSeed.ToString();
        }

        if (_lblGold != null)
        {
            _lblGold.Text = run.GetGold().ToString();
        }
    }

    #region 放弃

    private void OnAbandon()
    {
        _ = GlobalModController.OpenAlertAsync(new AlertDlgPayload
        {
            TitleKey = "UI_RUN_CONFIRM_ABANDON_TITLE",
            DescKey = "UI_RUN_CONFIRM_ABANDON_DESC",
            OkTextKey = "UI_ALERT_OK",
            CancelTextKey = "UI_ALERT_CANCEL",
            Time = 0,
            OkCallback = AbandonAndExit,
        });
    }

    private void AbandonAndExit()
    {
        RunRuntime.Abandon();
        Close();
        _ = GlobalModController.OpenMenuAsync();
    }

    #endregion
}