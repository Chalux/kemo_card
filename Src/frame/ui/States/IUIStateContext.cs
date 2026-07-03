namespace KemoCard.Frame.UI.States;

public interface IUIStateContext
{
    UIVo UIVo { get; }
    UIManager UIManager { get; }

    void OpenNext();
}