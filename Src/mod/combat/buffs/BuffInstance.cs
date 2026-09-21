using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;

namespace KemoCard.Mod.Combat.Buffs;

/// <summary>
/// Buff 运行时实例：挂点为角色/敌人的 <see cref="BuffContainer"/> 或手牌槽位容器。
/// 属性修正按自身 Handle 注册进持有者 ASC 的聚合器（与 GameplayEffect 同一聚合管线），
/// 休眠（持有者条件不满足）= 撤销句柄，激活 = 重新注册；到期由 <see cref="BuffRuntime"/> 统一 tick。
/// </summary>
public sealed class BuffInstance
{
    public Guid Handle { get; } = Guid.NewGuid();
    public BuffDto Def { get; }
    public IReadOnlyDictionary<string, object>? Params { get; }

    public int Stacks { get; private set; } = 1;

    /// <summary><see cref="EBuffDurationType.Turns"/> 时的剩余回合数；其余时长类型为 <c>null</c>。</summary>
    public int? RemainingTurns { get; private set; }

    /// <summary>持有者条件不满足时休眠：不参与属性聚合、不触发钩子、不显示图标，但不移除。</summary>
    public bool IsDormant { get; private set; }

    public bool IsExpired => RemainingTurns is <= 0;

    /// <summary>充能计数（<see cref="BuiltinBuffTags.SlotCharge"/>）：打出一张牌递减，归零触发并重置。</summary>
    public int ChargeCounter { get; private set; }

    public BuffInstance(BuffDto def, IReadOnlyDictionary<string, object>? parameters)
    {
        ArgumentNullException.ThrowIfNull(def);
        Def = def;
        Params = parameters is { Count: > 0 }
            ? new Dictionary<string, object>(parameters, StringComparer.Ordinal)
            : null;
        RemainingTurns = def.DurationType == EBuffDurationType.Turns
            ? Math.Max(1, def.Duration)
            : null;
        ChargeCounter = Math.Max(1, ReadIntParam("charge") ?? 1);
    }

    public void AddStack(int maxStacks)
    {
        if (Stacks < Math.Max(1, maxStacks))
            Stacks++;
    }

    /// <summary>StackRule.Refresh：重置时长（保持层数）。</summary>
    public void RefreshDuration()
    {
        if (Def.DurationType == EBuffDurationType.Turns)
            RemainingTurns = Math.Max(1, Def.Duration);
    }

    public void TickTurnEnd()
    {
        if (RemainingTurns is > 0)
            RemainingTurns--;
    }

    /// <summary>消耗一层充能计数；归零返回 true（调用方触发载荷后 ResetCharge）。</summary>
    public bool TryConsumeCharge()
    {
        if (ChargeCounter <= 0)
            return false;
        ChargeCounter--;
        return ChargeCounter == 0;
    }

    public void ResetCharge() => ChargeCounter = Math.Max(1, ReadIntParam("charge") ?? 1);

    public void SetDormant(bool dormant) => IsDormant = dormant;

    /// <summary>把属性修正（× 层数）注册进持有者 ASC 聚合器；休眠实例或无 ASC 的容器（槽位）为空操作。</summary>
    public void RegisterModifiers(AbilitySystemComponent? asc)
    {
        if (asc is null || IsDormant || Def.Modifiers.Count == 0)
            return;

        var grouped = new Dictionary<string, List<AttributeModifier>>(StringComparer.Ordinal);
        foreach (var modifierDef in Def.Modifiers)
        {
            if (string.IsNullOrWhiteSpace(modifierDef.AttributeId))
                continue;

            var magnitude = EvaluateMagnitude(modifierDef.Magnitude) * Stacks;
            if (!grouped.TryGetValue(modifierDef.AttributeId, out var list))
            {
                list = [];
                grouped[modifierDef.AttributeId] = list;
            }

            list.Add(new AttributeModifier(modifierDef.Operation, magnitude, sourceHandle: Handle));
        }

        foreach (var pair in grouped)
            asc.Aggregator.SetModifiersForHandle(pair.Key, Handle, pair.Value);
    }

    /// <summary>从持有者 ASC 聚合器撤销全部修正句柄（休眠或移除时调用）。</summary>
    public void UnregisterModifiers(AbilitySystemComponent? asc)
    {
        asc?.Aggregator.RemoveModifiersForHandle(Handle);
    }

    /// <summary>
    /// buff 修正幅度只支持 Scalar 与 SetByCaller（取自挂载参数）；AttributeBased/Custom 按 0 处理。
    /// </summary>
    private float EvaluateMagnitude(MagnitudeDefDto magnitudeDef) => magnitudeDef.Kind switch
    {
        EMagnitudeKind.Scalar => magnitudeDef.Scalar,
        EMagnitudeKind.SetByCaller when Params is not null &&
            !string.IsNullOrWhiteSpace(magnitudeDef.CallerName) &&
            Params.TryGetValue(magnitudeDef.CallerName, out var value) &&
            float.TryParse(value.ToString(), out var parsed) => parsed,
        _ => 0f,
    };

    private int? ReadIntParam(string key)
    {
        if (Params is null || !Params.TryGetValue(key, out var value) || value is null)
            return null;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}
