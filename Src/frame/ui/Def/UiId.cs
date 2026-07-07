namespace KemoCard.Frame.UI.Def;

/// <summary>
/// 强类型 UI 标识符，将 Id 与载荷类型 TPayload 在编译期绑定。
/// </summary>
public sealed record UiId<TPayload>(string Value)
{
    public static implicit operator string(UiId<TPayload> id) => id.Value;

    public override string ToString() => Value;
}