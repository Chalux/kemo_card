using System.Text.Json;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Condition;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Gas.Executions;

namespace KemoCard.Frame.Content;

public sealed class ContentDefinitionValidationError
{
    public ContentDefinitionValidationError(
        EContentCategory category,
        string definitionId,
        string message)
    {
        Category = category;
        DefinitionId = definitionId;
        Message = message;
    }

    public EContentCategory Category { get; }

    public string DefinitionId { get; }

    public string Message { get; }
}

public sealed class ContentDefinitionValidator
{
    public List<ContentDefinitionValidationError> Validate(GameDefinitionStore store)
    {
        var errors = new List<ContentDefinitionValidationError>();
        ValidateCardGroups(store, errors);
        ValidateCharacters(store, errors);
        ValidateEnemies(store, errors);
        ValidateBattles(store, errors);
        ValidateEvents(store, errors);
        ValidateItems(store, errors);
        ValidateCards(store, errors);
        ValidateSkills(store, errors);
        ValidateBuffs(store, errors);
        ValidateEffects(store, errors);
        ValidateSkillActions(store, errors);
        ValidateReferenceCycles(store, errors);
        ValidateGameplayTags(store, errors);
        ValidateGameplayEffects(store, errors);
        ValidateStories(store, errors);
        ValidateOrbTypes(store, errors);
        return errors;
    }

    /// <summary>
    /// 充能球类型：悬空效果引用会静默失效（触发时无收益），伤害与元素声明必须自洽
    /// （元素球必须有元素、纯效果球必须有触发效果），因此与其它引用类定义同标准校验。
    /// </summary>
    private static void ValidateOrbTypes(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var orb in store.OrbTypes.Values)
        {
            ValidateEffectRefs(EContentCategory.OrbType, orb.Id, orb.TriggerEffects, store, errors);

            if (orb.PerOrbAmount < 0f)
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.OrbType,
                    orb.Id,
                    "perOrbAmount must not be negative."));
            }

            if (orb.AttackBonusScale < 0f)
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.OrbType,
                    orb.Id,
                    "attackBonusScale must not be negative."));
            }

            // 纯效果球（dealsDamage: false）只跑 triggerEffects：两者皆空等于"空球"，必然是配置失误；
            // 元素球必须声明元素，否则伤害无可归属的属性（物理/魔法球不需要元素）。
            if (!orb.DealsDamage && orb.TriggerEffects.Count == 0)
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.OrbType,
                    orb.Id,
                    "Orb type must deal damage or declare at least one triggerEffect."));
            }

            if (orb.DealsDamage &&
                orb.DamageKind == EDamageKind.Elemental &&
                orb.Element == EElement.None)
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.OrbType,
                    orb.Id,
                    "Elemental orb kind requires a non-empty element."));
            }
        }
    }

    private static void ValidateStories(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var story in store.Stories.Values)
        {
            if (story.Unlock is not { } unlock || unlock.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var sourcePath = $"content/stories/{story.Id}.json:unlock";
            if (!ConditionParser.TryParse<IPersistentCondContext>(
                    unlock,
                    ConditionDomains.Persistent,
                    sourcePath,
                    out _,
                    out var error))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Story,
                    story.Id,
                    error ?? "unlock 条件解析失败。"));
            }
        }
    }

    private static void ValidateCardGroups(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        var tierByGroup = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var card in store.Cards.Values)
        {
            if (string.IsNullOrWhiteSpace(card.CardGroupId))
            {
                continue;
            }

            var tiers = tierByGroup.GetValueOrDefault(card.CardGroupId)
                ?? new HashSet<int>();
            if (tiers.Contains(card.UpgradeTier))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Card,
                    card.Id,
                    $"Duplicate upgradeTier {card.UpgradeTier} in cardGroup '{card.CardGroupId}'."));
            }

            tiers.Add(card.UpgradeTier);
            tierByGroup[card.CardGroupId] = tiers;
        }
    }

    private static void ValidateCharacters(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var character in store.Characters.Values)
        {
            ValidateSkillRefs(EContentCategory.Character, character.Id, character.SkillRefs, store, errors);
            ValidatePassives(character, store, errors);
            foreach (var cardId in character.Cards)
            {
                if (!store.TryGetCard(cardId, out _))
                {
                    errors.Add(new ContentDefinitionValidationError(
                        EContentCategory.Character,
                        character.Id,
                        $"Unknown cardId '{cardId}'."));
                }
            }
        }
    }

    private static void ValidatePassives(
        CharacterDto character,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        foreach (var passive in character.Passives)
        {
            if (!store.TryGetBuff(passive.BuffId, out _))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Character,
                    character.Id,
                    $"Unknown passive buffId '{passive.BuffId}'."));
            }

            if (passive.RequiredPotential < 0)
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Character,
                    character.Id,
                    $"Passive '{passive.BuffId}' requires non-negative requiredPotential."));
            }
        }
    }

    private static void ValidateEnemies(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var enemy in store.Enemies.Values)
        {
            var maxHealth = enemy.BaseAttributes.TryGetValue(AttributeIds.MaxHealth, out var value)
                ? value
                : enemy.MaxHp;
            if (maxHealth <= 0f)
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Enemy,
                    enemy.Id,
                    "baseAttributes.MaxHealth/maxHp must be greater than 0."));
            }

            ValidateSkillRefs(EContentCategory.Enemy, enemy.Id, enemy.SkillRefs, store, errors);
            ValidateBuffRefs(EContentCategory.Enemy, enemy.Id, enemy.BuffRefs, store, errors);
        }
    }

    private static void ValidateBattles(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var battle in store.Battles.Values)
        {
            if (battle.Waves.Count == 0)
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Battle,
                    battle.Id,
                    "waves must not be empty."));
            }

            for (var waveIndex = 0; waveIndex < battle.Waves.Count; waveIndex++)
            {
                var wave = battle.Waves[waveIndex];
                if (wave.EnemySpawns.Count == 0)
                {
                    errors.Add(new ContentDefinitionValidationError(
                        EContentCategory.Battle,
                        battle.Id,
                        $"waves[{waveIndex}].enemySpawns must not be empty."));
                }

                foreach (var spawn in wave.EnemySpawns)
                {
                    if (!store.TryGetEnemy(spawn.EnemyId, out _))
                    {
                        errors.Add(new ContentDefinitionValidationError(
                            EContentCategory.Battle,
                            battle.Id,
                            $"Unknown enemyId '{spawn.EnemyId}'."));
                    }

                    if (spawn.SkillOverrides is not null)
                    {
                        ValidateSkillRefs(
                            EContentCategory.Battle,
                            battle.Id,
                            spawn.SkillOverrides,
                            store,
                            errors);
                    }
                }
            }

            ValidateBattleRewards(battle, store, errors);
        }
    }

    private static void ValidateBattleRewards(
        BattleDto battle,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        foreach (var entry in battle.Rewards.Entries)
        {
            switch (entry.Kind)
            {
                case ERewardKind.ItemGrant:
                    ValidateRewardItemId(battle.Id, entry, store, errors);
                    break;
                case ERewardKind.Effect:
                    ValidateRewardEffectId(battle.Id, entry, store, errors);
                    break;
                case ERewardKind.CardChoice:
                    ValidateRewardCardIds(battle.Id, entry, store, errors);
                    break;
            }
        }
    }

    private static void ValidateRewardItemId(
        string battleId,
        RewardEntryDto entry,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        var itemId = GetStringParam(entry.Params, "itemId");
        if (itemId is null)
        {
            errors.Add(new ContentDefinitionValidationError(
                EContentCategory.Battle,
                battleId,
                "ItemGrant reward requires params.itemId."));
            return;
        }

        if (!store.TryGetItem(itemId, out _))
        {
            errors.Add(new ContentDefinitionValidationError(
                EContentCategory.Battle,
                battleId,
                $"Unknown itemId '{itemId}' in reward."));
        }
    }

    private static void ValidateRewardEffectId(
        string battleId,
        RewardEntryDto entry,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        var effectId = GetStringParam(entry.Params, "effectId");
        if (effectId is null)
        {
            errors.Add(new ContentDefinitionValidationError(
                EContentCategory.Battle,
                battleId,
                "Effect reward requires params.effectId."));
            return;
        }

        if (!store.TryGetEffect(effectId, out _))
        {
            errors.Add(new ContentDefinitionValidationError(
                EContentCategory.Battle,
                battleId,
                $"Unknown effectId '{effectId}' in reward."));
        }
    }

    private static void ValidateRewardCardIds(
        string battleId,
        RewardEntryDto entry,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        var cardIds = GetStringListParam(entry.Params, "cardIds");
        if (cardIds is null)
        {
            return;
        }

        foreach (var cardId in cardIds)
        {
            if (!store.TryGetCard(cardId, out _))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Battle,
                    battleId,
                    $"Unknown cardId '{cardId}' in reward."));
            }
        }
    }

    private static void ValidateEvents(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var eventDef in store.Events.Values)
        {
            switch (eventDef.EventKind)
            {
                case EEventKind.Data:
                    if (eventDef.Options.Count == 0)
                    {
                        errors.Add(new ContentDefinitionValidationError(
                            EContentCategory.Event,
                            eventDef.Id,
                            "Data event requires at least one option."));
                    }

                    foreach (var option in eventDef.Options)
                    {
                        ValidateEffectRefs(
                            EContentCategory.Event,
                            eventDef.Id,
                            option.EffectRefs,
                            store,
                            errors);
                    }

                    break;
                case EEventKind.Script:
                    if (string.IsNullOrWhiteSpace(eventDef.ScriptPath))
                    {
                        errors.Add(new ContentDefinitionValidationError(
                            EContentCategory.Event,
                            eventDef.Id,
                            "Script event requires scriptPath."));
                    }

                    break;
            }
        }
    }

    private static void ValidateItems(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var item in store.Items.Values)
        {
            if (item.UseSkillRefs.Count == 0)
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Item,
                    item.Id,
                    "useSkillRefs must not be empty."));
            }

            ValidateSkillRefs(EContentCategory.Item, item.Id, item.UseSkillRefs, store, errors);
        }
    }

    private static void ValidateCards(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var card in store.Cards.Values)
        {
            ValidateSkillRefs(EContentCategory.Card, card.Id, card.SkillRefs, store, errors);
        }
    }

    private static void ValidateSkills(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var skill in store.Skills.Values)
        {
            if (skill.ActionRefs.Count > 0)
            {
                ValidateActionRefs(EContentCategory.Skill, skill.Id, skill.ActionRefs, store, errors);
            }
            else
            {
                ValidateEffectRefs(
                    EContentCategory.Skill,
                    skill.Id,
                    skill.EffectRefs,
                    store,
                    errors);
            }
        }
    }

    private static void ValidateBuffs(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var buff in store.Buffs.Values)
        {
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnApply, store, errors);
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnTurnStart, store, errors);
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnTurnEnd, store, errors);
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnStackChanged, store, errors);
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnRemove, store, errors);
            // 2026-09-19 buff 运行时新增的三条钩子（chalux 被动全靠它们接线）：
            // 悬空 effectId 会静默失效，必须与旧钩子同标准校验。
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnWaveStart, store, errors);
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnActiveSkillCast, store, errors);
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnSlotCardPlayed, store, errors);
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnCardSettled, store, errors);
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnCardExecutionEnd, store, errors);
            ValidateEffectRefs(EContentCategory.Buff, buff.Id, buff.Hooks.OnOrbTriggered, store, errors);
        }
    }

    private static void ValidateEffects(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var effect in store.Effects.Values)
        {
            if (effect.Kind == EEffectKind.ExecuteScript && string.IsNullOrWhiteSpace(effect.ScriptPath))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Effect,
                    effect.Id,
                    "ExecuteScript requires scriptPath."));
            }

            if (effect.Kind == EEffectKind.ChainEffects)
            {
                ValidateEffectRefs(EContentCategory.Effect, effect.Id, effect.EffectRefs, store, errors);
            }

            if (effect.Kind == EEffectKind.ApplyBuff)
            {
                ValidateBuffIdInParams(effect, store, errors);
            }

            if (effect.Kind == EEffectKind.RemoveBuff)
            {
                ValidateBuffRemovalParams(EContentCategory.Effect, effect.Id, effect.Params, store, errors);
            }

            if (effect.Kind is EEffectKind.GainOrb or EEffectKind.GainOrbPerPlayedCard or EEffectKind.GainOrbByDeckCount)
            {
                ValidateOrbTypeIdInParams(EContentCategory.Effect, effect.Id, effect.Params, store, errors);
            }

            if (effect.Kind == EEffectKind.AttachSlotBuff)
            {
                ValidateAttachSlotParams(EContentCategory.Effect, effect.Id, effect.Params, store, errors);
            }

            ValidateEffectConditions(effect, errors);
            ValidateScaledRuntimeParams(EContentCategory.Effect, effect.Id, effect.Params, errors);
        }
    }

    /// <summary>
    /// 效果级条件（<c>EffectDto.conditions</c>）必须能在 Combat 域解析：运行期对未知 CondType / 非法参数
    /// 是"条件不通过"的静默失败，写错类型键会让效果永远不触发，必须在内容准入阶段拦下。
    /// </summary>
    private static void ValidateEffectConditions(EffectDto effect, List<ContentDefinitionValidationError> errors)
    {
        foreach (var condition in effect.Conditions)
        {
            var sourcePath = $"effect:{effect.Id}:conditions.{condition.Kind}";
            if (!ConditionDomains.Combat.TryGet(condition.Kind, out var handler) || handler is null)
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Effect,
                    effect.Id,
                    $"Unknown condition kind '{condition.Kind}'."));
                continue;
            }

            var args = JsonSerializer.SerializeToElement(
                condition.Params ?? new Dictionary<string, object>(StringComparer.Ordinal));
            if (!handler.TryParse(args, sourcePath, out _, out var conditionError))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Effect,
                    effect.Id,
                    conditionError ?? $"Invalid params for condition '{condition.Kind}'."));
            }
        }
    }

    private static void ValidateSkillActions(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var action in store.SkillActions.Values)
        {
            if (action.Kind == ESkillActionKind.ExecuteScript && string.IsNullOrWhiteSpace(action.ScriptPath))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.SkillAction,
                    action.Id,
                    "ExecuteScript requires scriptPath."));
            }

            if (action.Kind == ESkillActionKind.ChainActions)
            {
                ValidateActionRefs(EContentCategory.SkillAction, action.Id, action.ActionRefs, store, errors);
            }

            if (action.Kind is ESkillActionKind.ApplyGameplayEffect or ESkillActionKind.RemoveGameplayEffect)
            {
                var gameplayEffectId = GetStringParam(action.Params, "gameplayEffectId");
                if (string.IsNullOrWhiteSpace(gameplayEffectId))
                {
                    errors.Add(new ContentDefinitionValidationError(
                        EContentCategory.SkillAction,
                        action.Id,
                        $"{action.Kind} requires params.gameplayEffectId."));
                }
            }

            // buff 类技能动作（2026-09-19 buff 运行时启用）：buffId 悬空 / 槽位索引非法
            // 在运行期都是静默失败，必须在内容准入阶段拒绝。
            if (action.Kind is ESkillActionKind.ApplyBuff or ESkillActionKind.AttachSlotBuff)
            {
                var buffId = GetStringParam(action.Params, "buffId");
                if (string.IsNullOrWhiteSpace(buffId))
                {
                    errors.Add(new ContentDefinitionValidationError(
                        EContentCategory.SkillAction,
                        action.Id,
                        $"{action.Kind} requires params.buffId."));
                }
                else if (!store.TryGetBuff(buffId, out _))
                {
                    errors.Add(new ContentDefinitionValidationError(
                        EContentCategory.SkillAction,
                        action.Id,
                        $"Unknown buffId '{buffId}'."));
                }
            }

            if (action.Kind == ESkillActionKind.RemoveBuff)
            {
                ValidateBuffRemovalParams(EContentCategory.SkillAction, action.Id, action.Params, store, errors);
            }

            if (action.Kind == ESkillActionKind.AttachSlotBuff)
            {
                ValidateAttachSlotParams(EContentCategory.SkillAction, action.Id, action.Params, store, errors);
            }

            if (action.Kind == ESkillActionKind.GainOrb)
            {
                ValidateOrbTypeIdInParams(EContentCategory.SkillAction, action.Id, action.Params, store, errors);
            }

            ValidateScaledRuntimeParams(EContentCategory.SkillAction, action.Id, action.Params, errors);
        }
    }

    /// <summary>
    /// 2026-09-21 新增的"参数化机制"取值校验。这些参数在运行期对非法 / 缺失一律<b>静默退化</b>：
    /// <list type="bullet">
    /// <item><c>attackScaleFromOrbs</c> 漏写或写错 <c>maxBonus</c> ⇒ <c>attackScale</c> 恒为 1（参数完全无效，卡面伤害静默变低）；</item>
    /// <item><c>tiers</c> 结构写错 ⇒ 一个球都不发；</item>
    /// <item><c>perCard</c> / <c>offset</c> / <c>elementMask</c> 非数字 ⇒ 回落缺省；</item>
    /// <item><c>oncePerTurn</c> 非布尔 ⇒ 恒为 false（"每回合仅 1 次"失效，被动每张牌都触发）。</item>
    /// <item><c>oncePerWave</c> 非布尔 ⇒ 恒为 false（"每阶层仅 1 次"失效）。</item>
    /// <item><c>count</c>（SetActionCount）非整数 ⇒ 回落 2；<c>turns</c>（SetDomain）非整数 ⇒ 领域不自动收起。</item>
    /// <item><c>slotIndex</c>（DiscardSlot / 暴风载荷）非整数 ⇒ 弃不掉任何牌；<c>adjacentSlots</c> 同理。</item>
    /// </list>
    /// 与 <c>damageType</c> / 效果条件的校验同口径：这类"静默失败"必须在内容准入阶段拦下。
    /// </summary>
    private static void ValidateScaledRuntimeParams(
        EContentCategory category,
        string definitionId,
        Dictionary<string, object>? parameters,
        List<ContentDefinitionValidationError> errors)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return;
        }

        ValidateIntParam(category, definitionId, parameters, "perCard", errors, allowNegative: true);
        ValidateIntParam(category, definitionId, parameters, "offset", errors, allowNegative: true);
        ValidateIntParam(category, definitionId, parameters, "elementMask", errors, allowNegative: false);
        ValidateIntParam(category, definitionId, parameters, "count", errors, allowNegative: false);
        ValidateIntParam(category, definitionId, parameters, "turns", errors, allowNegative: false);
        ValidateIntParam(category, definitionId, parameters, "slotIndex", errors, allowNegative: false);
        ValidateIntParam(category, definitionId, parameters, "adjacentSlots", errors, allowNegative: false);
        ValidateBoolParam(category, definitionId, parameters, "oncePerTurn", errors);
        ValidateBoolParam(category, definitionId, parameters, "oncePerWave", errors);
        ValidateOrbTiers(category, definitionId, parameters, errors);
        ValidateAttackScaleFromOrbs(category, definitionId, parameters, errors);
    }

    private static void ValidateIntParam(
        EContentCategory category,
        string definitionId,
        Dictionary<string, object> parameters,
        string key,
        List<ContentDefinitionValidationError> errors,
        bool allowNegative)
    {
        if (!parameters.ContainsKey(key) || parameters[key] is null)
        {
            return;
        }

        if (GetIntParam(parameters, key) is not { } parsed)
        {
            errors.Add(new ContentDefinitionValidationError(
                category, definitionId, $"params.{key} must be an integer."));
            return;
        }

        if (!allowNegative && parsed < 0)
        {
            errors.Add(new ContentDefinitionValidationError(
                category, definitionId, $"params.{key} must be >= 0."));
        }
    }

    private static void ValidateBoolParam(
        EContentCategory category,
        string definitionId,
        Dictionary<string, object> parameters,
        string key,
        List<ContentDefinitionValidationError> errors)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
        {
            return;
        }

        var valid = value switch
        {
            bool => true,
            JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } => true,
            JsonElement { ValueKind: JsonValueKind.String } element =>
                bool.TryParse(element.GetString(), out _),
            string text => bool.TryParse(text, out _),
            _ => false,
        };
        if (!valid)
        {
            errors.Add(new ContentDefinitionValidationError(
                category, definitionId, $"params.{key} must be a boolean."));
        }
    }

    /// <summary><c>params.tiers</c>：非空的 <c>[最小张数, 球数]</c> 整数对列表（写成别的一律不发球）。</summary>
    private static void ValidateOrbTiers(
        EContentCategory category,
        string definitionId,
        Dictionary<string, object> parameters,
        List<ContentDefinitionValidationError> errors)
    {
        if (!parameters.TryGetValue("tiers", out var value) || value is null)
        {
            return;
        }

        if (!TryGetParamItems(value, out var tiers) || tiers.Count == 0)
        {
            errors.Add(new ContentDefinitionValidationError(
                category,
                definitionId,
                "params.tiers must be a non-empty array of [minCount, orbCount] pairs."));
            return;
        }

        foreach (var tier in tiers)
        {
            if (!TryGetParamItems(tier, out var pair) || pair.Count != 2 ||
                !TryGetIntValue(pair[0], out var minCount) ||
                !TryGetIntValue(pair[1], out var orbCount))
            {
                errors.Add(new ContentDefinitionValidationError(
                    category,
                    definitionId,
                    "each params.tiers entry must be [minCount, orbCount] with integers."));
                return;
            }

            if (minCount < 0 || orbCount < 0)
            {
                errors.Add(new ContentDefinitionValidationError(
                    category, definitionId, "params.tiers values must be >= 0."));
                return;
            }
        }
    }

    /// <summary>
    /// <c>params.attackScaleFromOrbs</c>：<c>{ elementMask, perOrb, maxBonus }</c>。
    /// <c>maxBonus</c> 必须为正——它同时是"是否启用"的开关（<c>min(maxBonus, perOrb × 球数)</c>），
    /// 缺省 0 会让这个参数静默变成「永远 +0%」。
    /// </summary>
    private static void ValidateAttackScaleFromOrbs(
        EContentCategory category,
        string definitionId,
        Dictionary<string, object> parameters,
        List<ContentDefinitionValidationError> errors)
    {
        if (!parameters.TryGetValue("attackScaleFromOrbs", out var value) || value is null)
        {
            return;
        }

        if (!TryGetParamMembers(value, out var members))
        {
            errors.Add(new ContentDefinitionValidationError(
                category,
                definitionId,
                "params.attackScaleFromOrbs must be an object { elementMask, perOrb, maxBonus }."));
            return;
        }

        if (!TryGetFloatMember(members, "maxBonus", out var maxBonus) || maxBonus <= 0f)
        {
            errors.Add(new ContentDefinitionValidationError(
                category,
                definitionId,
                "params.attackScaleFromOrbs.maxBonus must be a positive number"
                + " (missing/0 makes attackScale stay 1, i.e. the parameter does nothing)."));
        }

        if (members.ContainsKey("perOrb") && !TryGetFloatMember(members, "perOrb", out _))
        {
            errors.Add(new ContentDefinitionValidationError(
                category, definitionId, "params.attackScaleFromOrbs.perOrb must be a number."));
        }

        if (members.ContainsKey("elementMask") && !TryGetIntValue(members["elementMask"], out _))
        {
            errors.Add(new ContentDefinitionValidationError(
                category, definitionId, "params.attackScaleFromOrbs.elementMask must be an integer."));
        }
    }

    /// <summary>参数值 → 数组元素（接受 JSON 反序列化来的 <see cref="JsonElement"/> 与程序化构造的列表）。</summary>
    private static bool TryGetParamItems(object value, out IReadOnlyList<object> items)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            items = element.EnumerateArray().Select(item => (object)item).ToArray();
            return true;
        }

        if (value is IEnumerable<object> list)
        {
            items = list.ToArray();
            return true;
        }

        items = [];
        return false;
    }

    /// <summary>参数值 → 对象成员（同样的两种来源）。</summary>
    private static bool TryGetParamMembers(object value, out IReadOnlyDictionary<string, object> members)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            members = element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => (object)property.Value,
                StringComparer.Ordinal);
            return true;
        }

        if (value is IReadOnlyDictionary<string, object> readOnly)
        {
            members = readOnly;
            return true;
        }

        if (value is IDictionary<string, object> dictionary)
        {
            members = new Dictionary<string, object>(dictionary, StringComparer.Ordinal);
            return true;
        }

        members = new Dictionary<string, object>(StringComparer.Ordinal);
        return false;
    }

    private static bool TryGetIntValue(object value, out int parsed)
    {
        switch (value)
        {
            case int i:
                parsed = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                parsed = (int)l;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                return element.TryGetInt32(out parsed);
            case string text:
                return int.TryParse(text, out parsed);
            default:
                parsed = 0;
                return false;
        }
    }

    private static bool TryGetFloatMember(IReadOnlyDictionary<string, object> members, string key, out float parsed)
    {
        parsed = 0f;
        if (!members.TryGetValue(key, out var value) || value is null)
        {
            return false;
        }

        switch (value)
        {
            case float f:
                parsed = f;
                return true;
            case double d:
                parsed = (float)d;
                return true;
            case int i:
                parsed = i;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                return element.TryGetSingle(out parsed);
            default:
                return false;
        }
    }

    /// <summary>
    /// 槽位 buff 挂载（同名技能动作与效果共用）：<c>buffId</c> 必填且必须存在；
    /// 槽位三选一——非负 <c>slotIndex</c>、<c>slotSelection: "randomNonEmpty"</c>（随机一张手牌）、
    /// <c>slotSelection: "all"</c>（全部手牌槽）。
    /// </summary>
    private static void ValidateAttachSlotParams(
        EContentCategory category,
        string definitionId,
        Dictionary<string, object>? parameters,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        var buffId = GetStringParam(parameters, "buffId");
        if (string.IsNullOrWhiteSpace(buffId))
        {
            errors.Add(new ContentDefinitionValidationError(
                category,
                definitionId,
                "AttachSlotBuff requires params.buffId."));
        }
        else if (!store.TryGetBuff(buffId, out _))
        {
            errors.Add(new ContentDefinitionValidationError(
                category,
                definitionId,
                $"Unknown buffId '{buffId}'."));
        }

        var hasExplicitSlot = GetIntParam(parameters, "slotIndex") is >= 0;
        if (!hasExplicitSlot && !IsKnownSlotSelection(parameters))
        {
            errors.Add(new ContentDefinitionValidationError(
                category,
                definitionId,
                "AttachSlotBuff requires a non-negative params.slotIndex or params.slotSelection = \"randomNonEmpty\"/\"all\"."));
        }
    }

    /// <summary><c>params.slotSelection</c> 取值判定（AttachSlotBuff 的槽位选择写法）。</summary>
    private static bool IsKnownSlotSelection(Dictionary<string, object>? parameters)
    {
        var selection = GetStringParam(parameters, "slotSelection");
        return string.Equals(selection, "randomNonEmpty", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(selection, "all", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>充能球授予（效果 / 技能动作）：<c>orbTypeId</c> 必填且必须存在，否则静默不发球。</summary>
    private static void ValidateOrbTypeIdInParams(
        EContentCategory category,
        string definitionId,
        Dictionary<string, object>? parameters,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        var orbTypeId = GetStringParam(parameters, "orbTypeId");
        if (string.IsNullOrWhiteSpace(orbTypeId))
        {
            errors.Add(new ContentDefinitionValidationError(
                category,
                definitionId,
                "GainOrb requires params.orbTypeId."));
            return;
        }

        if (!store.TryGetOrbType(orbTypeId, out _))
        {
            errors.Add(new ContentDefinitionValidationError(
                category,
                definitionId,
                $"Unknown orbTypeId '{orbTypeId}'."));
        }
    }

    /// <summary>驱散类参数校验：<c>buffId</c> 或非空 <c>withTags</c> 至少其一，且 buffId 必须存在。</summary>
    private static void ValidateBuffRemovalParams(
        EContentCategory category,
        string definitionId,
        Dictionary<string, object>? parameters,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        var buffId = GetStringParam(parameters, "buffId");
        var hasTags = GetStringListParam(parameters, "withTags") is { Count: > 0 };

        if (string.IsNullOrWhiteSpace(buffId) && !hasTags)
        {
            errors.Add(new ContentDefinitionValidationError(
                category,
                definitionId,
                "RemoveBuff requires params.buffId or a non-empty params.withTags."));
            return;
        }

        if (!string.IsNullOrWhiteSpace(buffId) && !store.TryGetBuff(buffId, out _))
        {
            errors.Add(new ContentDefinitionValidationError(
                category,
                definitionId,
                $"Unknown buffId '{buffId}'."));
        }
    }

    private static void ValidateGameplayTags(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var gameplayTag in store.GameplayTags.Values)
        {
            if (!IsValidGameplayTagString(gameplayTag.Id))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.GameplayTag,
                    gameplayTag.Id,
                    $"Invalid gameplay tag id '{gameplayTag.Id}'."));
            }

            if (!string.IsNullOrWhiteSpace(gameplayTag.Parent))
            {
                if (!IsValidGameplayTagString(gameplayTag.Parent!))
                {
                    errors.Add(new ContentDefinitionValidationError(
                        EContentCategory.GameplayTag,
                        gameplayTag.Id,
                        $"Invalid gameplay tag parent '{gameplayTag.Parent}'."));
                }
                else if (!store.TryGetGameplayTag(gameplayTag.Parent!, out _))
                {
                    errors.Add(new ContentDefinitionValidationError(
                        EContentCategory.GameplayTag,
                        gameplayTag.Id,
                        $"Unknown gameplay tag parent '{gameplayTag.Parent}'."));
                }
            }
        }
    }

    private static void ValidateGameplayEffects(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var gameplayEffect in store.GameplayEffects.Values)
        {
            ValidateDamageExecutions(gameplayEffect, errors);

            foreach (var modifier in gameplayEffect.Modifiers)
            {
                if (!store.TryGetAttribute(modifier.AttributeId, out _))
                {
                    errors.Add(new ContentDefinitionValidationError(
                        EContentCategory.GameplayEffect,
                        gameplayEffect.Id,
                        $"Unknown attributeId '{modifier.AttributeId}' in modifier."));
                }
            }

            // Hooks 里的 actionId 此前完全没有校验：悬空引用会静默入库，直到运行期才无声失效。
            ValidateActionRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.Hooks.OnApply, store, errors);
            ValidateActionRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.Hooks.OnTurnStart, store, errors);
            ValidateActionRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.Hooks.OnTurnEnd, store, errors);
            ValidateActionRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.Hooks.OnStackChanged, store, errors);
            ValidateActionRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.Hooks.OnRemove, store, errors);

            // 注意：GameplayTags.Count > 0 这个守卫是「出货内容尚未提供 tags 定义」的临时兼容，
            // 不是正确语义（tag 表为空时任何 tag 引用必然非法）。补齐 tags 内容后才可去掉，
            // 否则会立刻把现有带 grantedTags 的定义全部判非法并剔除。
            if (store.GameplayTags.Count > 0)
            {
                ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.GrantedTags, store, errors);
                ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.ApplicationRequiredTags, store, errors);
                ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.ApplicationBlockedTags, store, errors);
                ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.OngoingRequiredTags, store, errors);
                ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.ImmunityTags, store, errors);
                ValidateGameplayTagRefs(EContentCategory.GameplayEffect, gameplayEffect.Id, gameplayEffect.RemoveEffectsWithTags, store, errors);
            }
        }
    }

    /// <summary>
    /// 伤害执行的 <c>damageType</c> / <c>element</c> 必须可解析：运行期 <c>DamageTypeParser</c> 对未知取值
    /// 是"回落物理无属性"的静默行为，写错一个字母就会让法术卡按物防结算，必须在内容准入阶段拦下。
    /// </summary>
    private static void ValidateDamageExecutions(
        GameplayEffectDefDto gameplayEffect,
        List<ContentDefinitionValidationError> errors)
    {
        foreach (var execution in gameplayEffect.Executions)
        {
            if (!string.Equals(execution.Kind, DamageExecution.DamageKind, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (DamageTypeParser.TryParse(execution.DamageType, execution.Element, out _, out var error))
            {
                continue;
            }

            errors.Add(new ContentDefinitionValidationError(
                EContentCategory.GameplayEffect,
                gameplayEffect.Id,
                error ?? "Invalid damageType/element on Damage execution."));
        }
    }

    private static void ValidateSkillRefs(
        EContentCategory category,
        string definitionId,
        IReadOnlyList<SkillRefDto> skillRefs,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        foreach (var skillRef in skillRefs)
        {
            if (!store.TryGetSkill(skillRef.SkillId, out _))
            {
                errors.Add(new ContentDefinitionValidationError(
                    category,
                    definitionId,
                    $"Unknown skillId '{skillRef.SkillId}'."));
            }
        }
    }

    private static void ValidateBuffRefs(
        EContentCategory category,
        string definitionId,
        IReadOnlyList<BuffRefDto> buffRefs,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        foreach (var buffRef in buffRefs)
        {
            if (!store.TryGetBuff(buffRef.BuffId, out _))
            {
                errors.Add(new ContentDefinitionValidationError(
                    category,
                    definitionId,
                    $"Unknown buffId '{buffRef.BuffId}'."));
            }
        }
    }

    private static void ValidateBuffIdInParams(
        EffectDto effect,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        var buffId = GetStringParam(effect.Params, "buffId");
        if (buffId is null)
        {
            errors.Add(new ContentDefinitionValidationError(
                EContentCategory.Effect,
                effect.Id,
                "ApplyBuff requires params.buffId."));
            return;
        }

        if (!store.TryGetBuff(buffId, out _))
        {
            errors.Add(new ContentDefinitionValidationError(
                EContentCategory.Effect,
                effect.Id,
                $"Unknown buffId '{buffId}'."));
        }
    }

    private static void ValidateEffectRefs(
        EContentCategory category,
        string definitionId,
        IReadOnlyList<EffectRefDto> effectRefs,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        foreach (var effectRef in effectRefs)
        {
            if (!store.TryGetEffect(effectRef.EffectId, out _))
            {
                errors.Add(new ContentDefinitionValidationError(
                    category,
                    definitionId,
                    $"Unknown effectId '{effectRef.EffectId}'."));
            }
        }
    }

    /// <summary>
    /// 链式引用环检测：<c>ChainEffects</c> / <c>ChainActions</c> 在执行侧是直接递归
    /// （mod 层的 <c>CombatEffectExecutor</c> / <c>SkillActionExecutor</c>），且没有深度守卫，
    /// a↔b 互引会让游戏进程 StackOverflow（.NET 不可捕获）。
    /// 必须在内容准入阶段拒绝：这里报出的 id 会被 <c>GameDefinitionRegistry.RemoveInvalidDefinitions</c> 剔除。
    /// </summary>
    private static void ValidateReferenceCycles(GameDefinitionStore store, List<ContentDefinitionValidationError> errors)
    {
        foreach (var effect in store.Effects.Values)
        {
            if (effect.Kind != EEffectKind.ChainEffects)
            {
                continue;
            }

            var path = new List<string>();
            if (FindEffectCycle(effect.Id, store, [], path))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.Effect,
                    effect.Id,
                    $"ChainEffects contains a reference cycle: {string.Join(" -> ", path)}."));
            }
        }

        foreach (var action in store.SkillActions.Values)
        {
            if (action.Kind != ESkillActionKind.ChainActions)
            {
                continue;
            }

            var path = new List<string>();
            if (FindActionCycle(action.Id, store, [], path))
            {
                errors.Add(new ContentDefinitionValidationError(
                    EContentCategory.SkillAction,
                    action.Id,
                    $"ChainActions contains a reference cycle: {string.Join(" -> ", path)}."));
            }
        }
    }

    private static bool FindEffectCycle(
        string effectId,
        GameDefinitionStore store,
        HashSet<string> visiting,
        List<string> path)
    {
        path.Add(effectId);
        if (!visiting.Add(effectId))
        {
            return true;
        }

        var hasCycle = false;
        if (store.TryGetEffect(effectId, out var effect) && effect.Kind == EEffectKind.ChainEffects)
        {
            foreach (var child in effect.EffectRefs)
            {
                if (FindEffectCycle(child.EffectId, store, visiting, path))
                {
                    hasCycle = true;
                    break;
                }
            }
        }

        if (!hasCycle)
        {
            visiting.Remove(effectId);
            path.RemoveAt(path.Count - 1);
        }

        return hasCycle;
    }

    private static bool FindActionCycle(
        string actionId,
        GameDefinitionStore store,
        HashSet<string> visiting,
        List<string> path)
    {
        path.Add(actionId);
        if (!visiting.Add(actionId))
        {
            return true;
        }

        var hasCycle = false;
        if (store.TryGetSkillAction(actionId, out var action) && action.Kind == ESkillActionKind.ChainActions)
        {
            foreach (var child in action.ActionRefs)
            {
                if (FindActionCycle(child.ActionId, store, visiting, path))
                {
                    hasCycle = true;
                    break;
                }
            }
        }

        if (!hasCycle)
        {
            visiting.Remove(actionId);
            path.RemoveAt(path.Count - 1);
        }

        return hasCycle;
    }

    private static void ValidateActionRefs(
        EContentCategory category,
        string definitionId,
        IReadOnlyList<SkillActionRefDto> actionRefs,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        foreach (var actionRef in actionRefs)
        {
            if (!store.TryGetSkillAction(actionRef.ActionId, out _))
            {
                errors.Add(new ContentDefinitionValidationError(
                    category,
                    definitionId,
                    $"Unknown actionId '{actionRef.ActionId}'."));
            }
        }
    }

    private static void ValidateGameplayTagRefs(
        EContentCategory category,
        string definitionId,
        IReadOnlyList<string> tags,
        GameDefinitionStore store,
        List<ContentDefinitionValidationError> errors)
    {
        foreach (var gameplayTag in tags)
        {
            if (!IsValidGameplayTagString(gameplayTag))
            {
                errors.Add(new ContentDefinitionValidationError(
                    category,
                    definitionId,
                    $"Invalid gameplay tag '{gameplayTag}'."));
                continue;
            }

            if (!store.TryGetGameplayTag(gameplayTag, out _))
            {
                errors.Add(new ContentDefinitionValidationError(
                    category,
                    definitionId,
                    $"Unknown gameplay tag '{gameplayTag}'."));
            }
        }
    }

    private static bool IsValidGameplayTagString(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        if (tag.StartsWith(".", StringComparison.Ordinal) || tag.EndsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        if (tag.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var ch in tag)
        {
            if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string? GetStringParam(Dictionary<string, object>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return element.GetString();
        }

        return value.ToString();
    }

    private static int? GetIntParam(Dictionary<string, object>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l => l is >= int.MinValue and <= int.MaxValue ? (int)l : null,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Number } element
                when element.TryGetInt32(out var parsed) => parsed,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : null,
        };
    }

    private static IReadOnlyList<string>? GetStringListParam(Dictionary<string, object>? parameters, string key)
    {
        if (parameters is null || !parameters.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var result = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result.Add(text);
                    }
                }
            }

            return result;
        }

        // 程序化构造的 store（测试 / 工具）传内存列表，与 JSON 反序列化路径同语义。
        if (value is IEnumerable<object> list)
        {
            var result = list
                .Select(item => item.ToString() ?? string.Empty)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
            return result.Count > 0 ? result : null;
        }

        return null;
    }
}