namespace KemoCard.Mod.Global.Ui;

public enum AlertClosePolicy
{
    Cancel = 0,
    Ok = 1,
    None = 2,
}

public sealed class AlertDlgPayload
{
    public string TitleKey { get; init; } = "UI_ALERT_TITLE";
    public string DescKey { get; init; } = "UI_ALERT_DESC";
    public string OkTextKey { get; init; } = "UI_ALERT_OK";
    public string CancelTextKey { get; init; } = "UI_ALERT_CANCEL";
    public Action? OkCallback { get; init; }
    public Action? CancelCallback { get; init; }
    public AlertClosePolicy CallbackWhenClose { get; init; } = AlertClosePolicy.Cancel;
    /// <summary>秒；≤0 表示无倒计时自动关闭。</summary>
    public double Time { get; init; } = 10;
}