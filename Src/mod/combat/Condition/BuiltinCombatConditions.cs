using System.Text.Json;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Mod.Combat.Condition;

/// <summary>
/// 战斗域内置条件类型（2026-09-21 启用；此前 v1 刻意留空）。
/// </summary>
/// <remarks>
/// 效果（<c>EffectDto.conditions</c>）在战斗内求值，用于「本回合打出过 N 张某属性卡」这类门闩——
/// 莱因哈特被动2 即 <c>CardPlayedThisTurn</c>。
/// 未知类型/参数非法一律视为<b>不通过</b>（运行期保守失败），内容准入阶段由
/// <c>ContentDefinitionValidator</c> 提前报错。
/// </remarks>
public static class BuiltinCombatConditions
{
    /// <summary>「本回合打出过 N 张指定属性的卡」：参数 <c>{ count, elementAny? }</c>。</summary>
    public const string CardPlayedThisTurn = "CardPlayedThisTurn";

    /// <summary>「自身当前标记队列有至少 N 张卡」：参数 <c>{ count, elementAny? }</c>。</summary>
    public const string CardsQueuedForExecution = "CardsQueuedForExecution";

    /// <summary>
    /// 「本回合连携达到 N 档」：参数 <c>{ tier, elementAny? }</c>。
    /// <c>tier</c> = 该属性的参与人数下限（2/3/4 = 二/三/四连携档）；
    /// <c>elementAny</c> 缺省时按<b>当前正在结算的卡牌</b>的属性判定（冯·诺依曼被动2）。
    /// </summary>
    public const string ChainTierAtLeast = "ChainTierAtLeast";

    /// <summary>
    /// 「属性/种族身份匹配」（2026-09-26 统一）：参数
    /// <c>{ elementAny?, raceAny?, raceAll?, matchAll?, partyMinCount?, partyElementAny?, partyRaceAny? }</c>。
    /// 判定主体 = <see cref="ICombatCondContext.SubjectElementFlags"/> /
    /// <see cref="ICombatCondContext.SubjectRaceFlags"/>（效果条件缺省为来源角色；目标筛选逐候选；
    /// buff 持有者条件为持有者）。队伍人数门闩与主体维度取"且"，只配人数时完全由人数决定。
    /// 所有「某元素/某种族特殊加成」一律走这条，不再为每个效果单写条件。
    /// </summary>
    public const string IdentityMatch = "IdentityMatch";

    /// <summary>
    /// 「当前充能球触发批次里包含指定球」（2026-09-26 新增）：参数
    /// <c>{ elementAny?: ["Yellow"], orbTypeId?: "yellow" }</c>（<b>至少一项</b>；两者都给时取"且"）。
    /// </summary>
    /// <remarks>
    /// 判定区间 = 一次触发（<c>OrbRuntime.Trigger</c>）清空队列后、<c>onOrbTriggered</c> 钩子的求值期间：
    /// 批次记录在钩子前后开关，因此只有挂在 <c>onOrbTriggered</c> 上的效果（含 <c>oncePerTurn</c>）读得到，
    /// 逐球触发效果与钩子之外一律不通过。「这次触发里有没有黄球 → 给护盾」这类效果用它表达
    /// （爱因斯坦凝析 buff 即 <c>elementAny: ["Yellow"]</c>）。
    /// </remarks>
    public const string OrbTriggered = "OrbTriggered";

    /// <summary>
    /// 「条件主体上一回合受到过魔法伤害」（2026-09-26 新增）：<b>无参数</b>。
    /// </summary>
    /// <remarks>
    /// 上下文保留主体的阵营与索引（效果条件 = 来源，目标筛选 = 候选，Buff 休眠 = 持有者）；
    /// 非玩家主体与无模拟上下文一律不通过。记账口径与 <c>onDamaged</c> 一致：
    /// 玩家槽位 + 敌方来源 + 非自我结算的魔法伤害（护盾完全抵消也算"挨了这一下"）。
    /// 账期由回合边界滚动——上一回合记的账在下一回合开始时转入可读账（爱因斯坦被动2 用它）。
    /// 目标筛选逐候选查询，敌方来源不能借用同索引的玩家账本。
    /// </remarks>
    public const string TookMagicDamageLastTurn = "TookMagicDamageLastTurn";

    /// <summary>存活敌人中是否存在指定 Buff 标签；params: { tag, exists? }。</summary>
    public const string EnemyHasBuffTag = "EnemyHasBuffTag";

    public static void RegisterAll(ConditionRegistry<ICombatCondContext> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(CondTypeHandler.Create<ICombatCondContext, EnemyBuffTagArgs>(
            EnemyHasBuffTag, "COND_ENEMY_BUFF_TAG_SHORT", "COND_ENEMY_BUFF_TAG_LONG",
            TryParseEnemyBuffTag, (args, context) => new LeafEvalData
            { Passed = context.AnyLivingEnemyHasBuffTag(args.Tag) == args.Exists, Fill = [args.Tag, args.Exists] }));

        registry.Register(CondTypeHandler.Create<ICombatCondContext, CardPlayedArgs>(
            CardPlayedThisTurn,
            "COND_CARD_PLAYED_THIS_TURN_SHORT",
            "COND_CARD_PLAYED_THIS_TURN_LONG",
            TryParseCardPlayed,
            CheckCardPlayed));

        registry.Register(CondTypeHandler.Create<ICombatCondContext, CardPlayedArgs>(
            CardsQueuedForExecution,
            "COND_CARDS_QUEUED_SHORT",
            "COND_CARDS_QUEUED_LONG",
            TryParseCardPlayed,
            CheckCardsQueued));

        registry.Register(CondTypeHandler.Create<ICombatCondContext, ChainTierArgs>(
            ChainTierAtLeast,
            "COND_CHAIN_TIER_SHORT",
            "COND_CHAIN_TIER_LONG",
            TryParseChainTier,
            CheckChainTier));

        registry.Register(CondTypeHandler.Create<ICombatCondContext, IdentityArgs>(
            IdentityMatch,
            "COND_IDENTITY_SHORT",
            "COND_IDENTITY_LONG",
            TryParseIdentity,
            CheckIdentity));

        registry.Register(CondTypeHandler.Create<ICombatCondContext, OrbTriggeredArgs>(
            OrbTriggered,
            "COND_ORB_TRIGGERED_SHORT",
            "COND_ORB_TRIGGERED_LONG",
            TryParseOrbTriggered,
            CheckOrbTriggered));

        registry.Register(CondTypeHandler.Create<ICombatCondContext, NoArgs>(
            TookMagicDamageLastTurn,
            "COND_TOOK_MAGIC_DAMAGE_SHORT",
            "COND_TOOK_MAGIC_DAMAGE_LONG",
            TryParseNoArgs,
            CheckTookMagicDamageLastTurn));
    }

    private sealed record CardPlayedArgs(int Count, int ElementFlags);

    private static bool TryParseCardPlayed(
        JsonElement args,
        string sourcePath,
        out CardPlayedArgs? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = $"{sourcePath}: 参数须为对象，例如 {{ \"count\": 2, \"elementAny\": [\"Yellow\"] }}";
            return false;
        }

        var count = 0;
        var elementFlags = 0;
        foreach (var property in args.EnumerateObject())
        {
            switch (property.Name)
            {
                case "count":
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out count) || count < 1)
                    {
                        error = $"{sourcePath}.count: 须为 ≥ 1 的整数";
                        return false;
                    }

                    break;
                case "elementAny":
                    if (!TryParseElementFlags(property.Value, sourcePath, out elementFlags, out error))
                        return false;
                    break;
            }
        }

        if (count == 0)
        {
            error = $"{sourcePath}.count: 缺失（须为 ≥ 1 的整数）";
            return false;
        }

        parsed = new CardPlayedArgs(count, elementFlags);
        return true;
    }

    private static bool TryParseElementFlags(
        JsonElement value,
        string sourcePath,
        out int flags,
        out string? error)
    {
        flags = 0;
        error = null;

        if (value.ValueKind != JsonValueKind.Array)
        {
            error = $"{sourcePath}.elementAny: 须为属性名数组";
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            var name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) ||
                !Enum.TryParse<EElement>(name, ignoreCase: true, out var element) ||
                element == EElement.None)
            {
                error = $"{sourcePath}.elementAny: 未知属性 '{name}'";
                return false;
            }

            flags |= (int)element;
        }

        return true;
    }

    private static LeafEvalData CheckCardPlayed(CardPlayedArgs args, ICombatCondContext context)
    {
        var played = context.CountCardsPlayedThisTurn(context.SourceCharacterIndex, args.ElementFlags);
        return new LeafEvalData
        {
            Passed = played >= args.Count,
            Fill = [played, args.Count],
            Progress = new ConditionProgress(Math.Min(played, args.Count), args.Count),
        };
    }

    private static LeafEvalData CheckCardsQueued(CardPlayedArgs args, ICombatCondContext context)
    {
        var queued = context.CountCardsQueuedForExecution(context.SourceCharacterIndex, args.ElementFlags);
        return new LeafEvalData
        {
            Passed = queued >= args.Count,
            Fill = [queued, args.Count],
            Progress = new ConditionProgress(Math.Min(queued, args.Count), args.Count),
        };
    }

    private sealed record ChainTierArgs(int Tier, int ElementFlags);

    private static bool TryParseChainTier(
        JsonElement args,
        string sourcePath,
        out ChainTierArgs? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = $"{sourcePath}: 参数须为对象，例如 {{ \"tier\": 2, \"elementAny\": [\"Blue\"] }}";
            return false;
        }

        var tier = 0;
        var elementFlags = 0;
        foreach (var property in args.EnumerateObject())
        {
            switch (property.Name)
            {
                case "tier":
                    if (property.Value.ValueKind != JsonValueKind.Number ||
                        !property.Value.TryGetInt32(out tier) ||
                        tier < 2)
                    {
                        error = $"{sourcePath}.tier: 须为 ≥ 2 的整数（连携档位由参与人数决定）";
                        return false;
                    }

                    break;
                case "elementAny":
                    if (!TryParseElementFlags(property.Value, sourcePath, out elementFlags, out error))
                        return false;
                    break;
            }
        }

        if (tier == 0)
        {
            error = $"{sourcePath}.tier: 缺失（须为 ≥ 2 的整数）";
            return false;
        }

        parsed = new ChainTierArgs(tier, elementFlags);
        return true;
    }

    private static LeafEvalData CheckChainTier(ChainTierArgs args, ICombatCondContext context)
    {
        var participants = context.CountChainParticipants(args.ElementFlags);
        return new LeafEvalData
        {
            Passed = participants >= args.Tier,
            Fill = [participants, args.Tier],
            Progress = new ConditionProgress(Math.Min(participants, args.Tier), args.Tier),
        };
    }

    #region EnemyHasBuffTag（存活敌人标签）

    private sealed record EnemyBuffTagArgs(string Tag, bool Exists);

    private static bool TryParseEnemyBuffTag(JsonElement args, string sourcePath,
        out EnemyBuffTagArgs? parsed, out string? error)
    {
        parsed = null;
        error = null;
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("tag", out var tag) ||
            tag.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tag.GetString()))
        {
            error = $"{sourcePath}.tag: 须为非空字符串";
            return false;
        }
        var exists = true;
        foreach (var property in args.EnumerateObject())
        {
            if (property.Name == "tag")
                continue;
            if (property.Name != "exists" || property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                error = $"{sourcePath}: 仅接受 tag 与布尔 exists";
                return false;
            }
            exists = property.Value.GetBoolean();
        }
        parsed = new(tag.GetString()!, exists);
        return true;
    }

    #endregion

    #region OrbTriggered（充能球触发批次）

    private sealed record OrbTriggeredArgs(int ElementFlags, string? OrbTypeId);

    private static bool TryParseOrbTriggered(
        JsonElement args,
        string sourcePath,
        out OrbTriggeredArgs? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = $"{sourcePath}: 参数须为对象，例如 {{ \"elementAny\": [\"Yellow\"] }} 或 {{ \"orbTypeId\": \"yellow\" }}";
            return false;
        }

        var elementFlags = 0;
        string? orbTypeId = null;
        foreach (var property in args.EnumerateObject())
        {
            switch (property.Name)
            {
                case "elementAny":
                    if (!TryParseElementFlags(property.Value, sourcePath, out elementFlags, out error))
                        return false;
                    break;
                case "orbTypeId":
                    var id = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        error = $"{sourcePath}.orbTypeId: 须为非空字符串（球类型 id，如 \"yellow\"）";
                        return false;
                    }

                    orbTypeId = id;
                    break;
                default:
                    error = $"{sourcePath}: 未知参数 '{property.Name}'（可用：elementAny / orbTypeId）";
                    return false;
            }
        }

        if (elementFlags == 0 && orbTypeId is null)
        {
            error = $"{sourcePath}: 至少配置 elementAny 或 orbTypeId 之一";
            return false;
        }

        parsed = new OrbTriggeredArgs(elementFlags, orbTypeId);
        return true;
    }

    private static LeafEvalData CheckOrbTriggered(OrbTriggeredArgs args, ICombatCondContext context) =>
        new() { Passed = context.OrbTriggeredInBatch(args.ElementFlags, args.OrbTypeId) };

    #endregion

    #region TookMagicDamageLastTurn（上一回合的魔法受击账）

    /// <summary>无参数条件的占位载荷（<see cref="CondTypeHandler"/> 需要一个 <c>TArgs</c>）。</summary>
    private sealed record NoArgs;

    private static bool TryParseNoArgs(
        JsonElement args,
        string sourcePath,
        out NoArgs? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = $"{sourcePath}: 该条件不接受参数（不写 params 即可）";
            return false;
        }

        foreach (var property in args.EnumerateObject())
        {
            error = $"{sourcePath}: 未知参数 '{property.Name}'（该条件无参数）";
            return false;
        }

        parsed = new NoArgs();
        return true;
    }

    /// <summary>
    /// 条件主体由上下文携带：效果读取来源，筛选读取候选，Buff 休眠读取持有者。
    /// 非玩家主体与无模拟上下文的容器恒为 false。
    /// </summary>
    private static LeafEvalData CheckTookMagicDamageLastTurn(NoArgs _, ICombatCondContext context) =>
        new() { Passed = context.SubjectTookMagicDamageLastTurn() };

    #endregion

    #region IdentityMatch（属性/种族身份 + 队伍人数门闩）

    private sealed record IdentityArgs(IdentityFilter Filter, int PartyMinCount, IdentityFilter PartyFilter);

    private static bool TryParseIdentity(
        JsonElement args,
        string sourcePath,
        out IdentityArgs? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        if (args.ValueKind != JsonValueKind.Object)
        {
            error = $"{sourcePath}: 参数须为对象，例如 {{ \"elementAny\": [\"Green\"], \"raceAny\": [\"Human\"] }}";
            return false;
        }

        List<EElement>? elementAny = null;
        List<ERace>? raceAny = null;
        List<ERace>? raceAll = null;
        var matchAll = false;
        var partyMinCount = 0;
        List<EElement>? partyElementAny = null;
        List<ERace>? partyRaceAny = null;

        foreach (var property in args.EnumerateObject())
        {
            switch (property.Name)
            {
                case "elementAny":
                    if (!TryParseEnumList<EElement>(property.Value, $"{sourcePath}.elementAny", out elementAny, out error))
                        return false;
                    break;
                case "raceAny":
                    if (!TryParseEnumList<ERace>(property.Value, $"{sourcePath}.raceAny", out raceAny, out error))
                        return false;
                    break;
                case "raceAll":
                    if (!TryParseEnumList<ERace>(property.Value, $"{sourcePath}.raceAll", out raceAll, out error))
                        return false;
                    break;
                case "matchAll":
                    if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        error = $"{sourcePath}.matchAll: 须为布尔值";
                        return false;
                    }

                    matchAll = property.Value.GetBoolean();
                    break;
                case "partyMinCount":
                    if (property.Value.ValueKind != JsonValueKind.Number ||
                        !property.Value.TryGetInt32(out partyMinCount) ||
                        partyMinCount < 1)
                    {
                        error = $"{sourcePath}.partyMinCount: 须为 ≥ 1 的整数";
                        return false;
                    }

                    break;
                case "partyElementAny":
                    if (!TryParseEnumList<EElement>(property.Value, $"{sourcePath}.partyElementAny", out partyElementAny, out error))
                        return false;
                    break;
                case "partyRaceAny":
                    if (!TryParseEnumList<ERace>(property.Value, $"{sourcePath}.partyRaceAny", out partyRaceAny, out error))
                        return false;
                    break;
                default:
                    error = $"{sourcePath}: 未知参数 '{property.Name}'"
                        + "（可用：elementAny / raceAny / raceAll / matchAll / partyMinCount / partyElementAny / partyRaceAny）";
                    return false;
            }
        }

        var filter = new IdentityFilter(elementAny ?? [], raceAny ?? [], raceAll ?? [], matchAll);
        if (filter.IsEmpty && partyMinCount <= 0)
        {
            error = $"{sourcePath}: 至少配置一个身份维度（elementAny / raceAny / raceAll）或队伍人数门闩（partyMinCount）";
            return false;
        }

        var partyFilter = new IdentityFilter(partyElementAny ?? [], partyRaceAny ?? [], [], matchAll);
        parsed = new IdentityArgs(filter, partyMinCount, partyFilter);
        return true;
    }

    private static LeafEvalData CheckIdentity(IdentityArgs args, ICombatCondContext context)
    {
        var element = (EElement)context.SubjectElementFlags;
        var race = (ERace)context.SubjectRaceFlags;
        var holderMatched = args.Filter.Matches(element, race);

        // 队伍人数门闩（2026-09-21 语义）：与主体维度取"且"；只配人数时完全由人数决定。
        if (args.PartyMinCount > 0)
        {
            var partyCount = context.CountPartyIdentityMatches(
                args.PartyFilter.ElementFlags,
                args.PartyFilter.RaceFlags,
                args.Filter.MatchAll);
            return new LeafEvalData
            {
                Passed = partyCount >= args.PartyMinCount && (args.Filter.IsEmpty || holderMatched),
                Fill = [partyCount, args.PartyMinCount],
                Progress = new ConditionProgress(Math.Min(partyCount, args.PartyMinCount), args.PartyMinCount),
            };
        }

        return new LeafEvalData { Passed = holderMatched };
    }

    /// <summary>解析枚举名数组（<c>["Green", "Blue"]</c>）；非数组/未知名/全空报错。</summary>
    private static bool TryParseEnumList<TEnum>(
        JsonElement value,
        string sourcePath,
        out List<TEnum>? parsed,
        out string? error)
        where TEnum : struct, Enum
    {
        parsed = null;
        error = null;

        if (value.ValueKind != JsonValueKind.Array)
        {
            error = $"{sourcePath}: 须为枚举名数组，例如 [\"Green\"]";
            return false;
        }

        var list = new List<TEnum>();
        foreach (var item in value.EnumerateArray())
        {
            var name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) ||
                !Enum.TryParse<TEnum>(name, ignoreCase: true, out var flag) ||
                Convert.ToInt64(flag) == 0)
            {
                error = $"{sourcePath}: 未知枚举名 '{name}'";
                return false;
            }

            list.Add(flag);
        }

        if (list.Count == 0)
        {
            error = $"{sourcePath}: 列表不能为空";
            return false;
        }

        parsed = list;
        return true;
    }

    #endregion
}