using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace KemoCard.Frame.Mvc;

/// <summary>
/// <see cref="EventSubscribeAttribute"/> 的扫描、校验与注册实现。
/// </summary>
/// <remarks>
/// <para>方法发现结果按目标类型缓存；注册时只对每个载荷类型做一次泛型方法闭合反射，
/// 派发阶段走普通强类型委托，不再经过 <see cref="MethodInfo.Invoke"/>。</para>
/// <para>契约来源与源生成器一致：事件 id = 枚举整型值，载荷类型 = 成员上的
/// <see cref="EventPayloadAttribute"/>。</para>
/// </remarks>
internal static class EventDecoratorBinder
{
    private sealed record DecoratedMethod(
        MethodInfo Method,
        int EventId,
        Type PayloadType,
        bool Once,
        EventBusScope BusScope);

    private static readonly ConcurrentDictionary<Type, DecoratedMethod[]> MethodCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> ClosedBindMethodCache = new();

    public static EventBinding Bind(
        EventDispatcher dispatcher,
        object target,
        object caller,
        EventBusScope? scopeFilter = null)
    {
        var binding = new EventBinding(dispatcher);
        DecoratedMethod[] methods;
        try
        {
            methods = MethodCache.GetOrAdd(target.GetType(), static type => Discover(type));
        }
        catch (Exception ex)
        {
            EventDispatcher.LogError($"事件装饰器扫描失败 {target.GetType().FullName}：{ex.Message}");
            return binding;
        }

        foreach (var entry in methods)
        {
            if (scopeFilter.HasValue && entry.BusScope != scopeFilter.Value)
            {
                continue;
            }

            try
            {
                var bindMethod = ClosedBindMethodCache.GetOrAdd(
                    entry.PayloadType,
                    static payloadType => typeof(EventDecoratorBinder)
                        .GetMethod(nameof(BindTyped), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(payloadType));

                var listener = bindMethod.Invoke(
                    null,
                    [dispatcher, entry.EventId, target, entry.Method, caller, entry.Once]);

                if (listener is IEventListener untyped)
                {
                    binding.Add(untyped);
                }
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } nested } ? nested : ex;
                EventDispatcher.LogError(
                    $"事件装饰器绑定失败 {Describe(target.GetType(), entry.Method)}：{inner.Message}");
            }
        }

        return binding;
    }

    private static IEventListener<TPayload> BindTyped<TPayload>(
        EventDispatcher dispatcher,
        int eventId,
        object target,
        MethodInfo method,
        object caller,
        bool once)
    {
        var handler = CreateHandler<TPayload>(target, method);
        return dispatcher.On(new EventKey<TPayload>(eventId), handler, caller, once);
    }

    private static Action<TPayload, IEventListener<TPayload>> CreateHandler<TPayload>(
        object target,
        MethodInfo method)
    {
        var parameters = method.GetParameters();

        if (parameters.Length == 0)
        {
            var action = (Action)Delegate.CreateDelegate(typeof(Action), target, method);
            return (_, _) => action();
        }

        if (parameters.Length == 1)
        {
            if (parameters[0].ParameterType == typeof(TPayload))
            {
                var action = (Action<TPayload>)Delegate.CreateDelegate(typeof(Action<TPayload>), target, method);
                return (payload, _) => action(payload);
            }

            var listenerAction = (Action<IEventListener<TPayload>>)Delegate.CreateDelegate(
                typeof(Action<IEventListener<TPayload>>), target, method);
            return (_, listener) => listenerAction(listener);
        }

        return (Action<TPayload, IEventListener<TPayload>>)Delegate.CreateDelegate(
            typeof(Action<TPayload, IEventListener<TPayload>>), target, method);
    }

    private static DecoratedMethod[] Discover(Type type)
    {
        var entries = new List<DecoratedMethod>();
        var seenMethods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        // 从派生类型向基类扫描：new 隐藏方法只保留最派生的实现，virtual override 也能拿到
        // 继承自基类的装饰器（GetCustomAttributes(inherit: true)）。
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var method in current.GetMethods(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                // 属性访问器 / 事件 add/remove 不是订阅入口；抽象与开放泛型方法无法绑定到具体实例。
                if (method.IsSpecialName || method.IsAbstract || method.ContainsGenericParameters)
                {
                    continue;
                }

                // 保持原有语义：基类的 private 方法不参与派生类型扫描。
                if (current != type && method.IsPrivate)
                {
                    continue;
                }

                var signature = BuildSignature(method);
                if (seenMethods.TryGetValue(signature, out var derivedMethod))
                {
                    // 正常 virtual override（含多级链）：双方 GetBaseDefinition 指向同一根声明，无需告警。
                    // 只有 new 隐藏才说明基类实现被丢弃。
                    if (!Equals(derivedMethod.GetBaseDefinition(), method.GetBaseDefinition()))
                    {
                        LogHiddenBaseDecoration(method);
                    }
                    continue;
                }

                seenMethods[signature] = method;

                try
                {
                    // 先放本类标注，再补基类标注：同一事件契约以最派生/首个标注为准。
                    var own = method.GetCustomAttributes<EventSubscribeAttribute>(inherit: false).ToArray();
                    var inherited = method.GetCustomAttributes<EventSubscribeAttribute>(inherit: true);
                    var ordered = new List<EventSubscribeAttribute>(own);
                    foreach (var attribute in inherited)
                    {
                        if (!own.Any(existing => Equals(existing.EventValue, attribute.EventValue)))
                        {
                            ordered.Add(attribute);
                        }
                    }

                    var seenContracts = new HashSet<(int EventId, Type PayloadType, EventBusScope Bus)>();
                    foreach (var attribute in ordered)
                    {
                        if (!TryCreateEntry(method, attribute, out var entry, out var error))
                        {
                            EventDispatcher.LogError(error);
                            continue;
                        }

                        if (!seenContracts.Add((entry.EventId, entry.PayloadType, entry.BusScope)))
                        {
                            EventDispatcher.LogError(
                                $"事件装饰器 {Describe(method.DeclaringType, method)} 对同一事件重复标注，"
                                + "已采用最派生/首个标注。");
                            continue;
                        }

                        entries.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    // 一个坏标注（构造器抛异常 / 枚举值溢出等）只跳过当前方法，不能拖垮整个类型。
                    EventDispatcher.LogError($"事件装饰器扫描失败 {Describe(current, method)}：{ex.Message}");
                }
            }
        }

        return [.. entries];
    }

    private static string BuildSignature(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var builder = new System.Text.StringBuilder(method.Name);
        builder.Append('(');
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }
            builder.Append(parameters[i].ParameterType.FullName);
        }
        builder.Append(')');
        return builder.ToString();
    }

    private static void LogHiddenBaseDecoration(MethodInfo hiddenBaseMethod)
    {
        try
        {
            foreach (var _ in hiddenBaseMethod.GetCustomAttributes<EventSubscribeAttribute>(inherit: false))
            {
                EventDispatcher.LogError(
                    $"事件装饰器 {Describe(hiddenBaseMethod.DeclaringType, hiddenBaseMethod)} "
                    + "被派生类型的 new 方法隐藏，已忽略基类标注。");
                return;
            }
        }
        catch (Exception ex)
        {
            EventDispatcher.LogError(
                $"事件装饰器扫描失败 {Describe(hiddenBaseMethod.DeclaringType, hiddenBaseMethod)}：{ex.Message}");
        }
    }

    /// <summary>
    /// 目标类型是否声明了至少一个 <see cref="EventSubscribeAttribute"/> 方法。
    /// 供 UI 层在无法解析总线时给出明确诊断。
    /// </summary>
    internal static bool HasDecoratedMethods(Type type, EventBusScope? scope = null)
    {
        try
        {
            var methods = MethodCache.GetOrAdd(type, static t => Discover(t));
            if (!scope.HasValue)
            {
                return methods.Length > 0;
            }

            foreach (var entry in methods)
            {
                if (entry.BusScope == scope.Value)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            EventDispatcher.LogError($"事件装饰器扫描失败 {type.FullName}：{ex.Message}");
            return false;
        }
    }

    /// <summary>清空方法发现缓存；仅供测试隔离使用。</summary>
    internal static void ClearCache()
    {
        MethodCache.Clear();
        ClosedBindMethodCache.Clear();
    }

    private static bool TryCreateEntry(
        MethodInfo method,
        EventSubscribeAttribute attribute,
        out DecoratedMethod entry,
        out string error)
    {
        entry = null!;
        error = string.Empty;

        var eventValue = attribute.EventValue;
        var enumType = eventValue.GetType();
        if (!enumType.IsEnum)
        {
            error = $"事件装饰器 {Describe(method.DeclaringType, method)} 的参数不是枚举成员：{eventValue}";
            return false;
        }

        // 枚举允许别名（多个字段同值）；优先选择带 [EventPayload] 的字段，
        // 不依赖 Enum.GetName 对重复值的未定义选择。
        EventPayloadAttribute? payloadAttribute = null;
        string? memberName = null;
        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!Equals(field.GetValue(null), eventValue))
            {
                continue;
            }

            memberName ??= field.Name;
            var fieldPayload = field.GetCustomAttribute<EventPayloadAttribute>();
            if (fieldPayload is null)
            {
                continue;
            }

            if (payloadAttribute is not null && payloadAttribute.PayloadType != fieldPayload.PayloadType)
            {
                error = $"事件装饰器 {Describe(method.DeclaringType, method)} 引用的 "
                    + $"{enumType.Name} 同值成员声明了冲突的载荷类型。";
                return false;
            }

            payloadAttribute = fieldPayload;
            memberName = field.Name;
        }

        if (payloadAttribute is null)
        {
            error = $"事件装饰器 {Describe(method.DeclaringType, method)} 引用的 "
                + $"{enumType.Name}.{memberName ?? "?"} 未用 [EventPayload] 声明载荷类型。";
            return false;
        }

        var payloadType = payloadAttribute.PayloadType;
        var signatureError = ValidateSignature(method, payloadType);
        if (signatureError is not null)
        {
            error = $"事件装饰器 {Describe(method.DeclaringType, method)} 签名不匹配：{signatureError}";
            return false;
        }

        entry = new DecoratedMethod(
            method,
            ToEventId(eventValue, enumType),
            payloadType,
            attribute.Once,
            attribute.Bus);
        return true;
    }

    /// <summary>
    /// 与生成器的 <c>(int)枚举成员</c> 保持一致：高位 uint/ulong 按 unchecked 截断为 int。
    /// </summary>
    private static int ToEventId(object eventValue, Type enumType)
    {
        var underlying = Enum.GetUnderlyingType(enumType);
        if (underlying == typeof(ulong))
        {
            return unchecked((int)Convert.ToUInt64(eventValue, CultureInfo.InvariantCulture));
        }
        if (underlying == typeof(uint))
        {
            return unchecked((int)Convert.ToUInt32(eventValue, CultureInfo.InvariantCulture));
        }
        if (underlying == typeof(long))
        {
            return unchecked((int)Convert.ToInt64(eventValue, CultureInfo.InvariantCulture));
        }
        if (underlying == typeof(ushort))
        {
            return Convert.ToUInt16(eventValue, CultureInfo.InvariantCulture);
        }
        if (underlying == typeof(byte))
        {
            return Convert.ToByte(eventValue, CultureInfo.InvariantCulture);
        }

        return Convert.ToInt32(eventValue, CultureInfo.InvariantCulture);
    }

    private static string? ValidateSignature(MethodInfo method, Type payloadType)
    {
        if (method.ReturnType != typeof(void))
        {
            return "返回值必须为 void";
        }

        var parameters = method.GetParameters();
        switch (parameters.Length)
        {
            case 0:
                return null;
            case 1:
                if (parameters[0].ParameterType == payloadType)
                {
                    return null;
                }
                if (parameters[0].ParameterType == typeof(IEventListener<>).MakeGenericType(payloadType))
                {
                    return null;
                }
                return $"单个参数必须是 {payloadType.Name} 或 IEventListener<{payloadType.Name}>";
            case 2:
                if (parameters[0].ParameterType == payloadType
                    && parameters[1].ParameterType == typeof(IEventListener<>).MakeGenericType(payloadType))
                {
                    return null;
                }
                return $"两个参数必须是 ({payloadType.Name}, IEventListener<{payloadType.Name}>)";
            default:
                return "参数个数必须为 0、1 或 2";
        }
    }

    private static string Describe(Type? declaringType, MethodInfo method) =>
        $"{declaringType?.Name ?? "<unknown>"}.{method.Name}";
}