using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run.Potential;

namespace KemoCard.Mod.Run.Team;

/// <summary>队伍编辑的服务层结果：<see cref="MessageKey"/> 是本地化键，界面负责翻译与展示。</summary>
public readonly record struct TeamEditResult(bool Ok, string MessageKey)
{
    public static TeamEditResult Success(string messageKey = "UI_TEAM_OK") => new(true, messageKey);

    public static TeamEditResult Failure(string messageKey) => new(false, messageKey);
}

/// <summary>槽位视图（当前上阵角色；空槽为 null）。</summary>
public readonly record struct TeamSlotView(
    int SlotIndex,
    string? InstanceId,
    string? CharacterId,
    string? DisplayNameId,
    int DeckSize);

/// <summary>角色池条目视图：<see cref="AssignedSlotIndex"/> 非 null 表示已在其它槽上阵。</summary>
public readonly record struct TeamPoolEntryView(
    string InstanceId,
    string CharacterId,
    string DisplayNameId,
    int? AssignedSlotIndex,
    int DeckSize);

/// <summary>卡组页签视图（多套卡组）。</summary>
public readonly record struct DeckTabView(int DeckIndex, bool IsCurrent, int CardCount);

/// <summary>
/// "可加入卡组"列表的条目：<see cref="InDeck"/> 为 true 表示该牌已在当前卡组内，
/// 界面必须加遮罩与"已在卡组内"提示，并且不再响应加入操作。
/// </summary>
public readonly record struct DeckPoolCardView(string CardId, bool InDeck);

/// <summary>单个卡组的编辑视图。</summary>
public sealed record DeckEditView(
    string InstanceId,
    int DeckIndex,
    IReadOnlyList<string> CardIds,
    IReadOnlyList<string> BuildableCardIds,
    IReadOnlyList<string> InvalidCardIds,
    int MaxCards,
    bool IsLocked);

/// <summary>
/// 角色被动视图：门槛、当前 Run 的解锁状态、描述键（buff 的 descId；缺失为空串）。
/// 界面只负责把 <see cref="DescriptionId"/> 交给 <c>Localization.Tr</c> 与展示。
/// </summary>
public readonly record struct PassiveView(int RequiredPotential, bool Unlocked, string DescriptionId);

/// <summary>
/// 队伍编辑的语义层（刻意不依赖 Godot）：槽位/角色池视图、上阵与下阵、卡组增删与校验、
/// 以及"何时允许编辑"的门闩。界面（<c>RunTeamEditDlg</c> / <c>RunCharacterDeckDlg</c>）
/// 只做取值、显示与把用户操作转成这里的方法调用，因此全部规则都能被 NUnit 直接覆盖。
/// </summary>
/// <remarks>
/// 规则要点（2026-09-21 规格）：
/// <list type="bullet">
/// <item>战斗中（<see cref="ERunPhase.Battle"/> / <see cref="ERunPhase.BattleEnd"/>）与已结束禁止编辑；</item>
/// <item>角色定义池内实例唯一：把已在其它槽上阵的角色迁到当前槽时，原槽自动清空（同一实例不占多槽）；</item>
/// <item>卡组增删受 <see cref="CharacterInstance.IsDeckLocked"/> 与 <see cref="DeckPreset"/> 自身规则约束
/// （上限 10 张、不可重复、必须属于"收藏 ∪ 角色专属卡"的可构筑集合）；</item>
/// <item>任何写操作都会置 <see cref="IsDirty"/>，由界面在关闭时统一保存。</item>
/// </list>
/// </remarks>
public sealed class RunTeamEditService
{
    private readonly RunController _run;
    private readonly GameDefinitionRegistry _registry;

    public RunTeamEditService(RunController run, GameDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(registry);
        _run = run;
        _registry = registry;
    }

    /// <summary>本次编辑会话是否有未保存的改动（界面在关闭时据此决定是否落盘）。</summary>
    public bool IsDirty { get; private set; }

    /// <summary>是否允许编辑队伍/卡组（战斗中与结束后禁止）。</summary>
    public bool CanEdit =>
        _run.State.Phase is not (ERunPhase.Battle or ERunPhase.BattleEnd or ERunPhase.Finished);

    /// <summary>禁止编辑时的本地化键（允许编辑时为 null）。</summary>
    public string? EditBlockReasonKey =>
        CanEdit ? null : "UI_TEAM_EDIT_BLOCKED";

    public int MaxCardsPerDeck => CombatConstants.MaxCardsPerDeck;

    #region 视图

    public IReadOnlyList<TeamSlotView> GetSlots()
    {
        var states = _run.State.PlayerStates;
        var slots = new List<TeamSlotView>(RunConstants.SlotCount);
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            var character = states[i].ActiveCharacter;
            slots.Add(character is null
                ? new TeamSlotView(i, null, null, null, 0)
                : new TeamSlotView(
                    i,
                    character.InstanceId,
                    character.DefinitionId,
                    character.Definition?.DisplayNameId,
                    GetCurrentDeckSize(character)));
        }

        return slots;
    }

    /// <summary>角色池全量条目（含已被其它槽上阵的角色，用 <c>AssignedSlotIndex</c> 标记）。</summary>
    public IReadOnlyList<TeamPoolEntryView> GetPoolEntries()
    {
        var pool = _run.State.CharacterPool;
        var entries = new List<TeamPoolEntryView>(pool.Count);
        foreach (var character in pool)
        {
            entries.Add(new TeamPoolEntryView(
                character.InstanceId,
                character.DefinitionId,
                character.Definition?.DisplayNameId ?? string.Empty,
                FindAssignedSlot(character.InstanceId),
                GetCurrentDeckSize(character)));
        }

        return entries;
    }

    public TeamPoolEntryView? FindPoolEntry(string instanceId)
    {
        foreach (var entry in GetPoolEntries())
        {
            if (string.Equals(entry.InstanceId, instanceId, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    public IReadOnlyList<DeckTabView> GetDecks(string instanceId)
    {
        if (FindCharacter(instanceId) is not { } character)
            return [];

        var tabs = new List<DeckTabView>(character.Decks.Count);
        for (var i = 0; i < character.Decks.Count; i++)
        {
            tabs.Add(new DeckTabView(
                i,
                i == character.CurrentDeckIndex,
                character.Decks[i].CardIds.Count));
        }

        return tabs;
    }

    public DeckEditView? GetDeck(string instanceId, int deckIndex)
    {
        if (FindCharacter(instanceId) is not { } character)
            return null;
        if (deckIndex < 0 || deckIndex >= character.Decks.Count)
            return null;

        var deck = character.Decks[deckIndex];
        var buildable = character.GetBuildableCardIds(_run.State.CardCollection).ToList();
        buildable.Sort(StringComparer.Ordinal);
        var validation = deck.Validate(character.GetBuildableCardIds(_run.State.CardCollection));

        return new DeckEditView(
            instanceId,
            deckIndex,
            [.. deck.CardIds],
            buildable,
            [.. validation.InvalidCardIds],
            CombatConstants.MaxCardsPerDeck,
            character.IsDeckLocked);
    }

    public CardDto? GetCard(string cardId) =>
        _registry.Store.TryGetCard(cardId, out var card) ? card : null;

    /// <summary>
    /// "可加入卡组"列表：可构筑卡牌（收藏 ∪ 角色专属卡）按 id 排序，并标出**已在当前卡组内**的牌。
    /// </summary>
    /// <remarks>
    /// 卡组不可重复（<see cref="DeckPreset"/> 规则），因此列表里必须把已在卡组内的牌显式标出来，
    /// 否则玩家点下去只会得到一句失败提示。角色或卡组不存在时返回空列表（界面显示空态）。
    /// </remarks>
    public IReadOnlyList<DeckPoolCardView> GetPoolCards(string instanceId, int deckIndex)
    {
        if (FindCharacter(instanceId) is not { } character)
        {
            return [];
        }

        if (TryGetDeck(character, deckIndex) is not { } deck)
        {
            return [];
        }

        var inDeck = new HashSet<string>(deck.CardIds, StringComparer.Ordinal);
        var buildable = character.GetBuildableCardIds(_run.State.CardCollection).ToList();
        buildable.Sort(StringComparer.Ordinal);

        return [.. buildable.Select(cardId => new DeckPoolCardView(cardId, inDeck.Contains(cardId)))];
    }

    public CharacterInstance? FindCharacter(string instanceId) =>
        _run.State.CharacterPool.FirstOrDefault(character =>
            string.Equals(character.InstanceId, instanceId, StringComparison.Ordinal));

    /// <summary>
    /// 角色被动视图（展示顺序 = 定义声明顺序）：门槛 + 当前 Run 的解锁状态 + 描述键。
    /// 解锁判定与战斗开战挂载共用 <see cref="PotentialService.IsPassiveUnlocked"/>，界面不自行推断。
    /// </summary>
    public IReadOnlyList<PassiveView> GetPassives(string instanceId)
    {
        if (FindCharacter(instanceId) is not { Definition: { } definition } character)
        {
            return [];
        }

        return
        [
            .. definition.Passives.Select(passive => new PassiveView(
                passive.RequiredPotential,
                PotentialService.IsPassiveUnlocked(_run.State, character, passive),
                _registry.Store.TryGetBuff(passive.BuffId, out var buff) ? buff.DescId : "")),
        ];
    }

    #endregion

    #region 上阵 / 下阵

    /// <summary>
    /// 把角色池中的实例上阵到指定槽位；若它已在其它槽上阵，原槽自动清空（自动迁移）。
    /// </summary>
    public TeamEditResult AssignToSlot(int slotIndex, string instanceId)
    {
        if (!CanEdit)
            return TeamEditResult.Failure("UI_TEAM_EDIT_BLOCKED");
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return TeamEditResult.Failure("UI_TEAM_SLOT_INVALID");

        var character = FindCharacter(instanceId);
        if (character is null)
            return TeamEditResult.Failure("UI_TEAM_CHARACTER_MISSING");

        var currentSlot = FindAssignedSlot(instanceId);
        if (currentSlot == slotIndex)
            return TeamEditResult.Success("UI_TEAM_ALREADY_DEPLOYED");

        if (currentSlot is { } source)
            _run.UnsetActiveCharacter(source);

        var poolIndex = _run.State.CharacterPool.ToList().FindIndex(candidate =>
            string.Equals(candidate.InstanceId, instanceId, StringComparison.Ordinal));
        if (poolIndex < 0 || !_run.SetActiveCharacter(slotIndex, poolIndex))
            return TeamEditResult.Failure("UI_TEAM_DEPLOY_FAILED");

        IsDirty = true;
        return TeamEditResult.Success("UI_TEAM_DEPLOYED");
    }

    /// <summary>清空指定槽位（允许临时空槽；开战前由 <c>ValidateParty</c> 校验满编）。</summary>
    public TeamEditResult UnassignSlot(int slotIndex)
    {
        if (!CanEdit)
            return TeamEditResult.Failure("UI_TEAM_EDIT_BLOCKED");
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return TeamEditResult.Failure("UI_TEAM_SLOT_INVALID");
        if (_run.State.PlayerStates[slotIndex].ActiveCharacter is null)
            return TeamEditResult.Success("UI_TEAM_SLOT_ALREADY_EMPTY");
        if (!_run.UnsetActiveCharacter(slotIndex))
            return TeamEditResult.Failure("UI_TEAM_DEPLOY_FAILED");

        IsDirty = true;
        return TeamEditResult.Success("UI_TEAM_UNASSIGNED");
    }

    #endregion

    #region 潜能

    /// <summary>团队池可用潜能（尚未分配到槽位的部分）。</summary>
    public int AvailablePotential => _run.Potential.TeamPool;

    /// <summary>指定槽位的已分配潜能（进度值；≥ 被动门槛即自动解锁）。</summary>
    public int SlotAllocatedPotential(int slotIndex) => _run.Potential.AllocatedFor(slotIndex);

    /// <summary>把 <paramref name="amount"/> 点可用潜能分配到槽位（投票模式下需表决）。</summary>
    public TeamEditResult AllocatePotential(int slotIndex, int amount)
    {
        if (!CanEdit)
            return TeamEditResult.Failure("UI_TEAM_EDIT_BLOCKED");
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return TeamEditResult.Failure("UI_TEAM_SLOT_INVALID");
        if (amount <= 0)
            return TeamEditResult.Failure("UI_TEAM_POTENTIAL_INVALID");

        var result = _run.Potential.TryAllocate(slotIndex, amount);
        if (!result.Success)
            return TeamEditResult.Failure(PotentialFailureKey(result.Failure));

        IsDirty = true;
        return TeamEditResult.Success("UI_TEAM_POTENTIAL_ALLOCATED");
    }

    /// <summary>从槽位扣除 <paramref name="amount"/> 点已分配潜能退回团队池（被动随门槛自动重锁）。</summary>
    public TeamEditResult DeductPotential(int slotIndex, int amount)
    {
        if (!CanEdit)
            return TeamEditResult.Failure("UI_TEAM_EDIT_BLOCKED");
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
            return TeamEditResult.Failure("UI_TEAM_SLOT_INVALID");
        if (amount <= 0)
            return TeamEditResult.Failure("UI_TEAM_POTENTIAL_INVALID");

        var result = _run.Potential.TryDeduct(slotIndex, amount);
        if (!result.Success)
            return TeamEditResult.Failure(PotentialFailureKey(result.Failure));

        IsDirty = true;
        return TeamEditResult.Success("UI_TEAM_POTENTIAL_DEDUCTED");
    }

    private static string PotentialFailureKey(EPotentialFailure failure) => failure switch
    {
        EPotentialFailure.InvalidAmount => "UI_TEAM_POTENTIAL_INVALID",
        EPotentialFailure.PoolShort => "UI_TEAM_POTENTIAL_POOL_SHORT",
        EPotentialFailure.SlotShort => "UI_TEAM_POTENTIAL_SLOT_SHORT",
        EPotentialFailure.VoteRejected => "UI_TEAM_POTENTIAL_VOTE_FAILED",
        _ => "UI_TEAM_SLOT_INVALID",
    };

    #endregion

    #region 卡组

    /// <summary>新建一套卡组（用角色定义卡初始化），并切为当前卡组。</summary>
    public TeamEditResult CreateDeck(string instanceId)
    {
        if (!CanEdit)
            return TeamEditResult.Failure("UI_TEAM_EDIT_BLOCKED");
        if (FindCharacter(instanceId) is not { } character)
            return TeamEditResult.Failure("UI_TEAM_CHARACTER_MISSING");
        if (character.IsDeckLocked)
            return TeamEditResult.Failure("UI_TEAM_DECK_LOCKED");
        if (character.Decks.Count >= CombatConstants.MaxDecksPerCharacter)
            return TeamEditResult.Failure("UI_TEAM_DECK_LIMIT");
        if (!character.TryCreateDeck())
            return TeamEditResult.Failure("UI_TEAM_DECK_CREATE_FAILED");

        character.TrySetCurrentDeck(character.Decks.Count - 1);
        _run.NotifyDeckChanged(instanceId, character.CurrentDeckIndex);
        IsDirty = true;
        return TeamEditResult.Success("UI_TEAM_DECK_CREATED");
    }

    public TeamEditResult SetCurrentDeck(string instanceId, int deckIndex)
    {
        if (!CanEdit)
            return TeamEditResult.Failure("UI_TEAM_EDIT_BLOCKED");
        if (FindCharacter(instanceId) is not { } character)
            return TeamEditResult.Failure("UI_TEAM_CHARACTER_MISSING");
        if (!character.TrySetCurrentDeck(deckIndex))
            return TeamEditResult.Failure("UI_TEAM_DECK_LOCKED");

        _run.NotifyDeckChanged(instanceId, deckIndex);
        IsDirty = true;
        return TeamEditResult.Success("UI_TEAM_DECK_SWITCHED");
    }

    public TeamEditResult AddCard(string instanceId, int deckIndex, string cardId)
    {
        if (!CanEdit)
            return TeamEditResult.Failure("UI_TEAM_EDIT_BLOCKED");
        if (FindCharacter(instanceId) is not { } character)
            return TeamEditResult.Failure("UI_TEAM_CHARACTER_MISSING");

        var deck = TryGetDeck(character, deckIndex);
        if (deck is null)
            return TeamEditResult.Failure("UI_TEAM_DECK_MISSING");
        if (character.IsDeckLocked)
            return TeamEditResult.Failure("UI_TEAM_DECK_LOCKED");
        if (deck.CardIds.Count >= CombatConstants.MaxCardsPerDeck)
            return TeamEditResult.Failure("UI_TEAM_DECK_FULL");
        if (deck.CardIds.Contains(cardId, StringComparer.Ordinal))
            return TeamEditResult.Failure("UI_TEAM_DECK_DUPLICATE");

        var buildable = character.GetBuildableCardIds(_run.State.CardCollection);
        if (!buildable.Contains(cardId))
            return TeamEditResult.Failure("UI_TEAM_DECK_NOT_BUILDABLE");

        var edited = character.TryEditDeck(deckIndex, target => target.TryAddCard(cardId, buildable));
        if (!edited)
            return TeamEditResult.Failure("UI_TEAM_DECK_EDIT_FAILED");

        _run.NotifyDeckChanged(instanceId, deckIndex);
        IsDirty = true;
        return TeamEditResult.Success("UI_TEAM_DECK_CARD_ADDED");
    }

    public TeamEditResult RemoveCard(string instanceId, int deckIndex, string cardId)
    {
        if (!CanEdit)
            return TeamEditResult.Failure("UI_TEAM_EDIT_BLOCKED");
        if (FindCharacter(instanceId) is not { } character)
            return TeamEditResult.Failure("UI_TEAM_CHARACTER_MISSING");

        var deck = TryGetDeck(character, deckIndex);
        if (deck is null)
            return TeamEditResult.Failure("UI_TEAM_DECK_MISSING");
        if (character.IsDeckLocked)
            return TeamEditResult.Failure("UI_TEAM_DECK_LOCKED");
        if (!deck.CardIds.Contains(cardId, StringComparer.Ordinal))
            return TeamEditResult.Failure("UI_TEAM_DECK_CARD_MISSING");
        if (deck.CardIds.Count <= CombatConstants.MinCardsPerDeck)
            return TeamEditResult.Failure("UI_TEAM_DECK_EMPTY");

        character.TryEditDeck(deckIndex, target => target.TryRemoveCard(cardId));
        _run.NotifyDeckChanged(instanceId, deckIndex);
        IsDirty = true;
        return TeamEditResult.Success("UI_TEAM_DECK_CARD_REMOVED");
    }

    private static DeckPreset? TryGetDeck(CharacterInstance character, int deckIndex) =>
        deckIndex >= 0 && deckIndex < character.Decks.Count ? character.Decks[deckIndex] : null;

    #endregion

    private int? FindAssignedSlot(string instanceId)
    {
        var states = _run.State.PlayerStates;
        for (var i = 0; i < RunConstants.SlotCount; i++)
        {
            var character = states[i].ActiveCharacter;
            if (character is not null &&
                string.Equals(character.InstanceId, instanceId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return null;
    }

    private static int GetCurrentDeckSize(CharacterInstance character) =>
        character.GetCurrentDeck()?.CardIds.Count ?? 0;
}