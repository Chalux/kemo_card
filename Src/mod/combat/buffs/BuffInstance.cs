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

    /// <summary>
    /// 充能（<see cref="BuiltinBuffTags.SlotCharge"/>）触发所需牌数：参数 <c>charge</c>，缺省 1
    /// （即「充能 I / II」的罗马数字）。非充能 buff 无意义。
    /// </summary>
    public int ChargeRequired => Math.Max(1, ReadIntParam("charge") ?? 1);

    /// <summary>充能进度：已打出的牌数（触发所需 − 剩余计数）；触发并重置后回到 0。</summary>
    public int ChargePlayed => Math.Max(0, ChargeRequired - ChargeCounter);

    /// <summary>充能进度比例（0..1），供进度条直接使用。</summary>
    public float ChargeProgress => Math.Clamp(ChargePlayed / (float)ChargeRequired, 0f, 1f);

    /// <summary><see cref="EBuffDurationType.Turns"/> 时的剩余回合数；其余时长类型为 <c>null</c>。</summary>
    public int? RemainingTurns { get; private set; }

    /// <summary>持有者条件不满足时休眠：不参与属性聚合、不触发钩子、不显示图标，但不移除。</summary>
    public bool IsDormant { get; private set; }

    public bool IsExpired => RemainingTurns is <= 0;

    /// <summary>充能计数（<see cref="BuiltinBuffTags.SlotCharge"/>）：打出一张牌递减，归零触发并重置。</summary>
    public int ChargeCounter { get; private set; }

    private readonly Func<MagnitudeDefDto, float>? _magnitudeResolver;
    private readonly HashSet<string> _firedThisTurn = new(StringComparer.Ordinal);
    private readonly HashSet<string> _firedThisWave = new(StringComparer.Ordinal);

    /// <summary>
    /// <see cref="EMagnitudeKind.TeamMaxHealthScaled"/> 的快照值（按 MagnitudeDefDto 实例缓存）：
    /// 首次求值即结算当场，之后不随队伍生命上限变化。
    /// </summary>
    private readonly Dictionary<MagnitudeDefDto, float> _snapshotMagnitudes = [];

    /// <summary>
    /// <see cref="EBuffDurationType.Turns"/> 下**每层各自的剩余回合**（2026-09-24）：
    /// <c>stackRule: Add</c> 叠层时新层从完整时长起算、旧层不受影响（"持续时间各自独立计算"），
    /// 层随时间逐层脱落；<see cref="RemainingTurns"/> 取其最大值供 UI 显示。
    /// </summary>
    private readonly List<int> _stackTurns = [];

    public BuffInstance(
        BuffDto def,
        IReadOnlyDictionary<string, object>? parameters,
        Func<MagnitudeDefDto, float>? magnitudeResolver = null)
    {
        ArgumentNullException.ThrowIfNull(def);
        Def = def;
        _magnitudeResolver = magnitudeResolver;
        Params = parameters is { Count: > 0 }
            ? new Dictionary<string, object>(parameters, StringComparer.Ordinal)
            : null;
        RemainingTurns = def.DurationType == EBuffDurationType.Turns
            ? Math.Max(1, def.Duration)
            : null;
        if (RemainingTurns is { } initial)
            _stackTurns.Add(initial);
        ChargeCounter = ChargeRequired;
    }

    /// <summary>
    /// 叠一层（不超过 <paramref name="maxStacks"/>）。<c>Turns</c> 型下新层拿到**完整时长**并独立计时，
    /// 因此先挂的层会先到期（"持续时间各自独立计算"）。
    /// </summary>
    public void AddStack(int maxStacks)
    {
        if (Stacks >= Math.Max(1, maxStacks))
            return;

        Stacks++;
        if (Def.DurationType != EBuffDurationType.Turns)
            return;

        _stackTurns.Add(Math.Max(1, Def.Duration));
        RemainingTurns = _stackTurns.Max();
    }

    /// <summary>StackRule.Refresh：重置时长（保持层数）——所有层的计时一起回到完整时长。</summary>
    public void RefreshDuration()
    {
        if (Def.DurationType != EBuffDurationType.Turns)
            return;

        var full = Math.Max(1, Def.Duration);
        if (_stackTurns.Count == 0)
            _stackTurns.Add(full);
        for (var i = 0; i < _stackTurns.Count; i++)
            _stackTurns[i] = full;
        RemainingTurns = full;
    }

    /// <summary>
    /// 回合结束：每层各自减 1，到期的层先掉（层数随之减少）；全部到期时 <see cref="IsExpired"/> 为真，
    /// 由 <see cref="BuffRuntime"/> 在本轮移除。
    /// </summary>
    public void TickTurnEnd()
    {
        if (RemainingTurns is not > 0)
            return;

        for (var i = _stackTurns.Count - 1; i >= 0; i--)
        {
            _stackTurns[i]--;
            if (_stackTurns[i] <= 0)
                _stackTurns.RemoveAt(i);
        }

        Stacks = _stackTurns.Count;
        RemainingTurns = _stackTurns.Count > 0 ? _stackTurns.Max() : 0;
    }

    /// <summary>消耗一层充能计数；归零返回 true（调用方触发载荷后 ResetCharge）。</summary>
    public bool TryConsumeCharge()
    {
        if (ChargeCounter <= 0)
            return false;
        ChargeCounter--;
        return ChargeCounter == 0;
    }

    public void ResetCharge() => ChargeCounter = ChargeRequired;

    public void SetDormant(bool dormant) => IsDormant = dormant;

    /// <summary>
    /// "本回合仅 1 次"门闩（钩子参数 <c>oncePerTurn: true</c>）：首次调用返回 true 并记账，
    /// 同回合内再次调用返回 false；<see cref="ResetTurnFlags"/> 在回合开始时清空。
    /// </summary>
    public bool TryMarkHookFiredThisTurn(string hookKey) => _firedThisTurn.Add(hookKey);

    /// <summary>回合开始：清空"本回合仅 1 次"记账。</summary>
    public void ResetTurnFlags() => _firedThisTurn.Clear();

    /// <summary>
    /// "本阶层（波次）仅 1 次"门闩（钩子参数 <c>oncePerWave: true</c>）：与
    /// <see cref="TryMarkHookFiredThisTurn"/> 同构，记账周期改为整个阶层；
    /// <see cref="ResetWaveFlags"/> 在阶层开始时清空（冯·诺依曼被动6）。
    /// </summary>
    public bool TryMarkHookFiredThisWave(string hookKey) => _firedThisWave.Add(hookKey);

    /// <summary>阶层（波次）开始：清空"本阶层仅 1 次"记账。</summary>
    public void ResetWaveFlags() => _firedThisWave.Clear();

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
            // 球伤害增加的「元素掩码」修正按元素拆分记账，球结算时只吃自己那一份。
            foreach (var attributeId in ResolveModifierAttributeIds(modifierDef))
            {
                if (!grouped.TryGetValue(attributeId, out var list))
                {
                    list = [];
                    grouped[attributeId] = list;
                }

                list.Add(new AttributeModifier(modifierDef.Operation, magnitude, sourceHandle: Handle));
            }
        }

        foreach (var pair in grouped)
            asc.Aggregator.SetModifiersForHandle(pair.Key, Handle, pair.Value);
    }

    /// <summary>
    /// 修正落到哪个（些）属性键上：普通修正是自身；<c>OrbDamageScale</c> + 非 0 元素掩码
    /// 展开为每个命中元素的 <c>OrbDamageScale:&lt;Element&gt;</c>。
    /// </summary>
    private static IEnumerable<string> ResolveModifierAttributeIds(AttributeModifierDefDto modifierDef)
    {
        if (!string.Equals(modifierDef.AttributeId, AttributeIds.OrbDamageScale, StringComparison.Ordinal) ||
            modifierDef.ElementMask == 0)
        {
            yield return modifierDef.AttributeId;
            yield break;
        }

        foreach (var element in Enum.GetValues<EElement>())
        {
            if (element != EElement.None && (modifierDef.ElementMask & (int)element) != 0)
                yield return AttributeIds.OrbDamageScaleFor(element);
        }
    }

    /// <summary>从持有者 ASC 聚合器撤销全部修正句柄（休眠或移除时调用）。</summary>
    public void UnregisterModifiers(AbilitySystemComponent? asc)
    {
        asc?.Aggregator.RemoveModifiersForHandle(Handle);
    }

    /// <summary>
    /// buff 修正幅度支持 Scalar / SetByCaller（取自挂载参数）/ PartyCountScaled（走容器的自定义取值，
    /// 需要队伍视角）/ TeamMaxHealthScaled（创建时取一次队伍生命上限并缓存，之后不随变）；
    /// AttributeBased 与其余 Custom 按 0 处理。
    /// </summary>
    private float EvaluateMagnitude(MagnitudeDefDto magnitudeDef)
    {
        switch (magnitudeDef.Kind)
        {
            case EMagnitudeKind.Scalar:
                return magnitudeDef.Scalar;
            case EMagnitudeKind.SetByCaller when Params is not null &&
                !string.IsNullOrWhiteSpace(magnitudeDef.CallerName) &&
                Params.TryGetValue(magnitudeDef.CallerName, out var value) &&
                float.TryParse(value.ToString(), out var parsed):
                return parsed;
            case EMagnitudeKind.PartyCountScaled:
                return _magnitudeResolver?.Invoke(magnitudeDef) ?? 0f;
            case EMagnitudeKind.TeamMaxHealthScaled:
                // 快照语义：第一次求值（挂载/结算当场）取值一次，之后队伍生命上限变化不影响本实例。
                if (!_snapshotMagnitudes.TryGetValue(magnitudeDef, out var snapshot))
                {
                    snapshot = _magnitudeResolver?.Invoke(magnitudeDef) ?? 0f;
                    _snapshotMagnitudes[magnitudeDef] = snapshot;
                }

                return snapshot;
            default:
                return 0f;
        }
    }

    private int? ReadIntParam(string key)
    {
        if (Params is null || !Params.TryGetValue(key, out var value) || value is null)
            return null;

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}