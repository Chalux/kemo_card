using KemoCard.Frame.UI.Def;

namespace KemoCard.Frame.UI.Base;

/// <summary>
/// 带强类型载荷的基础窗口。
/// 不设为 partial，避免 Godot 源生成器与泛型冲突；用作文本基类，实际场景脚本继承非泛型类。
/// </summary>
public abstract partial class BaseWin<TPayload> : BaseWin
{
    public TPayload TypedPayload
    {
        get => Payload is TPayload p ? p : throw new InvalidOperationException(
            $"UI<{UIId}> 的 Payload 类型与注册类型不匹配，期望 {typeof(TPayload).Name}，实际 {Payload?.GetType().Name ?? "null"}");
        set => Payload = value;
    }
}