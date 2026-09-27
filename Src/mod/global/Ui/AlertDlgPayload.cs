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

    /// <summary>
    /// 正文格式参数（2026-09-27）：非空时 <see cref="DescKey"/> 走 <c>string.Format</c> 注入角色名 / 技能名等，
    /// 文案自行承担已翻译文本（不再走节点自动翻译）。倒计时文案不使用本参数。
    /// </summary>
    public object[]? DescArgs { get; init; }
}