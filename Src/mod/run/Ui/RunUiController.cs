using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Def;

namespace KemoCard.Mod.Run.Ui;

public static class RunUiController
{
    public static async Task<UIVo?> OpenStorySelectAsync()
    {
        return await (UIManager.Instance?.OpenAsync<StorySelectDlg>(new UiId<StorySelectDlg>(RunUiIds.StorySelect), default, null)
            ?? Task.FromResult<UIVo?>(null));
    }

    public static async Task<UIVo?> OpenRunMainAsync()
    {
        return await (UIManager.Instance?.OpenAsync<RunMainWin>(new UiId<RunMainWin>(RunUiIds.RunMain), default, null)
            ?? Task.FromResult<UIVo?>(null));
    }
}