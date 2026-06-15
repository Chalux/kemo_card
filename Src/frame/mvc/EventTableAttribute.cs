using System;

namespace KemoCard.Frame.Mvc;

/// <summary>
/// 标记一个事件表（partial static class）。Source Generator 会据此生成各事件的
/// <see cref="EventKey{TPayload}"/> 静态字段，并为 <paramref name="modType"/> 生成
/// On*/Notify* 包装方法。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class EventTableAttribute(Type enumType, Type modType) : Attribute
{
    /// <summary>事件 ID 来源枚举类型。</summary>
    public Type EnumType { get; } = enumType;

    /// <summary>承载 On*/Notify* 包装方法的 Mod 类型（须为 partial 的 <see cref="BaseMod"/> 子类）。</summary>
    public Type ModType { get; } = modType;
}

/// <summary>
/// 声明某个事件表字段对应的载荷类型，供 Source Generator 生成 <see cref="EventKey{TPayload}"/>。
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class EventPayloadAttribute(Type payloadType) : Attribute
{
    /// <summary>该事件载荷的 CLR 类型。</summary>
    public Type PayloadType { get; } = payloadType;
}
