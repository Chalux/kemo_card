namespace KemoCard.Frame.UI.Base;

public abstract partial class BaseDlg<TPayload> : BaseDlg
{
    public TPayload TypedPayload
    {
        get => Payload is TPayload p ? p : throw new InvalidOperationException(
            $"UI<{UIId}> 的 Payload 类型与注册类型不匹配，期望 {typeof(TPayload).Name}，实际 {Payload?.GetType().Name ?? "null"}");
        set => Payload = value;
    }
}