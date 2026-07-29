using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Gas;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Gas;

namespace KemoCard.Mod.Combat;

public sealed class CharacterBattleInstance
{
    private readonly List<CardRuntimeEntry> _drawPile = [];
    private readonly List<CardRuntimeEntry> _graveyard = [];
    private readonly HandSlot[] _handSlots;
    private readonly List<int> _drawModifiers = [];
    private readonly ActiveSkillTier[] _activeSkillChain;
    private bool _phaseShuffleUsed;

    public string SourceInstanceId { get; }
    public string DefinitionId { get; }
    public IReadOnlyList<CardRuntimeEntry> DrawPile => _drawPile;
    public IReadOnlyList<CardRuntimeEntry> Graveyard => _graveyard;
    public IReadOnlyList<HandSlot> HandSlots => _handSlots;
    public IReadOnlyDictionary<string, float> BaseAttributes { get; }
    public AbilitySystemComponent Asc { get; }

    /// <summary>每回合用来灌入「当前可用能量」的额度，恒被 clamp 在 [0, <see cref="MaxEnergy"/>]。</summary>
    public int CurrentEnergy { get; private set; }

    /// <summary>出牌扣费池，可被技能/效果抬高到超过 <see cref="MaxEnergy"/>。</summary>
    public int AvailableEnergy { get; private set; }

    public int MaxEnergy => ReadAscEnergy(AttributeIds.MaxEnergy);
    public bool HasActed { get; private set; }

    /// <summary>规格 §2.5：是否持有约定封印标签 <see cref="CombatConstants.SealedTag"/>。</summary>
    public bool IsSealed => Asc.Tags.HasTag(CombatConstants.SealedTag);

    /// <summary>技能计数器 <c>S</c>（规格 §5.2）。</summary>
    public int SkillCounter { get; private set; }

    /// <summary><c>S</c> 的上限 <c>Cap</c> = 主动技链各档 cooldown 之和；无主动链时为 0。</summary>
    public int SkillCounterCap { get; }

    /// <summary>主动技蓄力链快照（规格 §5.1），开战时从角色定义读入，战斗内不再变化。</summary>
    public IReadOnlyList<ActiveSkillTier> ActiveSkillChain => _activeSkillChain;

    private CharacterBattleInstance(
        string sourceInstanceId,
        string definitionId,
        IReadOnlyDictionary<string, float> baseAttributes,
        IEnumerable<CardRuntimeEntry> drawPile,
        AbilitySystemComponent asc,
        int currentEnergy,
        int skillCounterCap,
        IEnumerable<ActiveSkillTier>? activeSkillChain)
    {
        SourceInstanceId = sourceInstanceId;
        DefinitionId = definitionId;
        BaseAttributes = new Dictionary<string, float>(baseAttributes, StringComparer.Ordinal);
        Asc = asc;
        _drawPile.AddRange(drawPile);
        CurrentEnergy = currentEnergy;
        _activeSkillChain = activeSkillChain?.ToArray() ?? [];
        // 有主动链时 Cap 恒为各档 cooldown 之和（规格 §5.1）；无链时才回落到显式传入值（仅测试用）。
        SkillCounterCap = _activeSkillChain.Length > 0
            ? _activeSkillChain.Sum(tier => tier.Cooldown)
            : Math.Max(0, skillCounterCap);
        _handSlots = Enumerable.Range(0, CombatConstants.HandSlotCount)
            .Select(index => new HandSlot(index))
            .ToArray();
    }

    public static CharacterBattleInstance? TryCreate(
        CharacterInstance source,
        GameDefinitionRegistry definitions,
        HostRng rng,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(rng);

        var deck = source.GetCurrentDeck();
        if (deck is null)
        {
            error = "当前角色没有可用卡组。";
            return null;
        }

        var validation = deck.Validate(source.GetBuildableCardIds(new HashSet<string>(StringComparer.Ordinal)));
        if (!validation.IsValid)
        {
            error = "当前卡组构筑非法。";
            return null;
        }

        if (source.Definition is not null &&
            !CombatContentValidator.TryValidateActiveSkillChain(source.Definition, out var chainError))
        {
            error = chainError;
            return null;
        }

        var baseAttributes = source.ComputeAttributeMap(definitions);
        var ascFactory = new CombatAscFactory();
        var asc = ascFactory.CreateCharacterAsc(definitions.Store.Attributes, baseAttributes);
        var drawPile = deck.CardIds
            .Select(cardId => new CardRuntimeEntry(cardId, Guid.NewGuid().ToString("N")))
            .ToList();

        Shuffle(drawPile, rng);

        error = null;
        return new CharacterBattleInstance(
            source.InstanceId,
            source.DefinitionId,
            baseAttributes,
            drawPile,
            asc,
            currentEnergy: Math.Clamp(
                (int)MathF.Round(asc.GetCurrentValue(AttributeIds.InitialEnergy)),
                0,
                Math.Max(0, (int)MathF.Round(asc.GetCurrentValue(AttributeIds.MaxEnergy)))),
            skillCounterCap: 0,
            activeSkillChain: SnapshotActiveSkillChain(source.Definition));
    }

    /// <summary>
    /// 读取角色定义的主动链。内容校验（<see cref="CombatContentValidator"/>）已保证各档 cooldown ≥ 1，
    /// 这里仍对非法档位做兜底：跳过空 skillId，并把 cooldown 抬到至少 1 以保持累计阈值单调递增。
    /// </summary>
    private static IEnumerable<ActiveSkillTier> SnapshotActiveSkillChain(CharacterDto? definition)
    {
        if (definition is null)
            return [];

        return definition.ActiveSkillChain
            .Where(entry => !string.IsNullOrWhiteSpace(entry.SkillId))
            .Select(entry => new ActiveSkillTier(entry.SkillId, Math.Max(1, entry.Cooldown)))
            .ToArray();
    }

    public void SetHasActed(bool hasActed) => HasActed = hasActed;

    #region 能量（当前能量 / 当前可用能量）

    public void RegenCurrentEnergy() => CurrentEnergy = Math.Min(CurrentEnergy + 1, MaxEnergy);

    public void RefillAvailableEnergy() => AvailableEnergy = CurrentEnergy;

    public bool TryConsumeAvailableEnergy(int amount)
    {
        if (amount <= 0)
            return true;
        if (AvailableEnergy < amount)
            return false;
        AvailableEnergy -= amount;
        return true;
    }

    public void RefundAvailableEnergy(int amount)
    {
        if (amount <= 0)
            return;
        AvailableEnergy += amount;
    }

    public void GainAvailableEnergy(int amount)
    {
        if (amount <= 0)
            return;
        AvailableEnergy += amount;
    }

    #endregion

    #region 技能计数器（S）与主动链档位

    public void TickSkillCounter() => SkillCounter = Math.Min(SkillCounter + 1, SkillCounterCap);

    /// <summary>技能效果显式加 <c>S</c>（规格 §5.3 连发），上限仍为 <c>Cap</c>。</summary>
    public void GainSkillCounter(int amount)
    {
        if (amount <= 0)
            return;
        SkillCounter = Math.Min(SkillCounter + amount, SkillCounterCap);
    }

    /// <summary>释放第 k 档后扣除累计阈值 <c>T_k</c>；溢出资保留（规格 §5.3）。</summary>
    public void PaySkillCounter(int threshold)
    {
        if (threshold <= 0)
            return;
        SkillCounter = Math.Max(0, SkillCounter - threshold);
    }

    /// <summary>累计阈值 <c>T_k = C_0 + … + C_k</c>；k 越界返回 0。</summary>
    public int GetTierThreshold(int tierIndex)
    {
        if (tierIndex < 0 || tierIndex >= _activeSkillChain.Length)
            return 0;

        var threshold = 0;
        for (var i = 0; i <= tierIndex; i++)
            threshold += _activeSkillChain[i].Cooldown;
        return threshold;
    }

    /// <summary>
    /// 当前 <c>S</c> 达标的最高档（规格 §5.3，玩家不可故意降档）；不可释放时返回 <c>-1</c>。
    /// </summary>
    public int ResolveCastableTier()
    {
        var resolved = -1;
        var threshold = 0;
        for (var i = 0; i < _activeSkillChain.Length; i++)
        {
            threshold += _activeSkillChain[i].Cooldown;
            if (SkillCounter < threshold)
                break;
            resolved = i;
        }

        return resolved;
    }

    #endregion

    #region 抽牌数量修正与洗牌抽牌

    /// <summary>投放一次抽牌数量修正（正=增益，负=减益）。修正在本玩家阶段的抽牌步骤后失效。</summary>
    public void AddDrawModifier(int delta)
    {
        if (delta != 0)
            _drawModifiers.Add(delta);
    }

    public void ClearDrawModifiers() => _drawModifiers.Clear();

    /// <summary>规格 §4.2：<c>max(0, 1 + 最大增益 − 最大减益)</c>，同向修正不叠多段。</summary>
    public int ComputeDrawCount()
    {
        var largestBonus = 0;
        var largestPenalty = 0;
        foreach (var modifier in _drawModifiers)
        {
            if (modifier > largestBonus)
                largestBonus = modifier;
            else if (-modifier > largestPenalty)
                largestPenalty = -modifier;
        }

        return Math.Max(0, 1 + largestBonus - largestPenalty);
    }

    public void ResetPhaseShuffleBudget() => _phaseShuffleUsed = false;

    /// <summary>
    /// 抽牌，抽牌堆空且本阶段尚未洗牌时把弃牌堆洗回再抽（规格 §4.4，每阶段至多洗 1 次）。
    /// </summary>
    /// <returns>实际抽到的张数。</returns>
    public int DrawWithReshuffle(int count, HostRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (count <= 0)
            return 0;

        var drawn = 0;
        while (drawn < count)
        {
            var slot = FindFirstEmptyHandSlot();
            if (slot is null)
                break;

            if (_drawPile.Count == 0 && !TryReshuffleGraveyardIntoDrawPile(rng))
                break;

            var entry = _drawPile[^1];
            _drawPile.RemoveAt(_drawPile.Count - 1);
            slot.PlaceCard(entry.CardId, entry.RuntimeInstanceId);
            drawn++;
        }

        return drawn;
    }

    private bool TryReshuffleGraveyardIntoDrawPile(HostRng rng)
    {
        if (_phaseShuffleUsed || _graveyard.Count == 0)
            return false;

        _phaseShuffleUsed = true;
        _drawPile.AddRange(_graveyard);
        _graveyard.Clear();
        Shuffle(_drawPile, rng);
        return _drawPile.Count > 0;
    }

    #endregion

    /// <summary>
    /// 规格 §2.1：卡牌结算完毕（含空放）后从手牌槽移入弃牌堆并空出槽位。
    /// </summary>
    /// <returns>找到并移动了对应手牌时返回 <c>true</c>。</returns>
    public bool MoveHandCardToGraveyard(string runtimeInstanceId)
    {
        if (string.IsNullOrWhiteSpace(runtimeInstanceId))
            return false;

        foreach (var slot in _handSlots)
        {
            if (slot.IsEmpty || slot.CardId is null ||
                !string.Equals(slot.RuntimeInstanceId, runtimeInstanceId, StringComparison.Ordinal))
            {
                continue;
            }

            _graveyard.Add(new CardRuntimeEntry(slot.CardId, runtimeInstanceId));
            slot.ClearCard();
            return true;
        }

        return false;
    }

    public void MoveTopDrawToGraveyard()
    {
        if (_drawPile.Count == 0)
            return;
        var entry = _drawPile[^1];
        _drawPile.RemoveAt(_drawPile.Count - 1);
        _graveyard.Add(entry);
    }

    public int DrawCards(int count)
    {
        if (count <= 0)
            return 0;

        var drawn = 0;
        while (drawn < count && _drawPile.Count > 0)
        {
            var slot = FindFirstEmptyHandSlot();
            if (slot is null)
                break;

            var entry = _drawPile[^1];
            _drawPile.RemoveAt(_drawPile.Count - 1);
            slot.PlaceCard(entry.CardId, entry.RuntimeInstanceId);
            drawn++;
        }

        return drawn;
    }

    /// <summary>
    /// 规格 §4.6：从<strong>未标记</strong>手牌中用 <paramref name="rng"/> 均匀随机弃置最多 <paramref name="count"/> 张。
    /// 未标记池空时弃 0 张（软失败），不改动已标记牌。
    /// </summary>
    public int DiscardRandomUnmarked(int count, HostRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (count <= 0)
            return 0;

        var discarded = 0;
        while (discarded < count)
        {
            var unmarked = CollectUnmarkedOccupiedSlots();
            if (unmarked.Count == 0)
                break;

            var pick = unmarked[rng.NextInt(0, unmarked.Count)];
            _graveyard.Add(new CardRuntimeEntry(pick.CardId!, pick.RuntimeInstanceId!));
            pick.ClearCard();
            discarded++;
        }

        return discarded;
    }

    /// <summary>
    /// 规格 §4.6 ActiveSkill 通道：从全部非空手牌均匀随机弃一张，返回被弃槽位；池空返回 <c>null</c>。
    /// </summary>
    public HandSlot? PickRandomOccupiedSlot(HostRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var occupied = new List<HandSlot>();
        foreach (var slot in _handSlots)
        {
            if (!slot.IsEmpty)
                occupied.Add(slot);
        }

        if (occupied.Count == 0)
            return null;

        return occupied[rng.NextInt(0, occupied.Count)];
    }

    private List<HandSlot> CollectUnmarkedOccupiedSlots()
    {
        var unmarked = new List<HandSlot>();
        foreach (var slot in _handSlots)
        {
            if (!slot.IsEmpty && !slot.IsMarked)
                unmarked.Add(slot);
        }

        return unmarked;
    }

    private int ReadAscEnergy(string attributeId) =>
        Math.Max(0, (int)MathF.Round(Asc.GetCurrentValue(attributeId)));

    private HandSlot? FindFirstEmptyHandSlot()
    {
        foreach (var slot in _handSlots)
        {
            if (slot.IsEmpty)
                return slot;
        }

        return null;
    }

    private static void Shuffle(List<CardRuntimeEntry> entries, HostRng rng)
    {
        for (var i = entries.Count - 1; i > 0; i--)
        {
            var j = rng.NextInt(0, i + 1);
            (entries[i], entries[j]) = (entries[j], entries[i]);
        }
    }

    internal static CharacterBattleInstance CreateForTests(
        string definitionId,
        IReadOnlyDictionary<string, float> baseAttributes,
        IEnumerable<CardRuntimeEntry>? drawPile = null,
        int skillCounterCap = 0,
        IReadOnlyList<ActiveSkillChainEntryDto>? activeSkillChain = null)
    {
        ArgumentNullException.ThrowIfNull(baseAttributes);
        var copiedAttributes = new Dictionary<string, float>(baseAttributes, StringComparer.Ordinal);
        var asc = new CombatAscFactory().CreateCharacterAsc(
            new Dictionary<string, AttributeDefDto>(StringComparer.Ordinal),
            copiedAttributes);
        var initialEnergy = copiedAttributes.TryGetValue(AttributeIds.InitialEnergy, out var initial)
            ? (int)MathF.Round(initial)
            : 0;
        var energyCap = copiedAttributes.TryGetValue(AttributeIds.MaxEnergy, out var maxEnergy)
            ? (int)MathF.Round(maxEnergy)
            : 0;

        return new CharacterBattleInstance(
            sourceInstanceId: Guid.NewGuid().ToString("N"),
            definitionId,
            copiedAttributes,
            drawPile: drawPile ?? [],
            asc: asc,
            currentEnergy: Math.Clamp(initialEnergy, 0, Math.Max(0, energyCap)),
            skillCounterCap: skillCounterCap,
            activeSkillChain: SnapshotActiveSkillChain(new CharacterDto
            {
                Id = definitionId,
                ActiveSkillChain = [.. activeSkillChain ?? []],
            }));
    }

    internal static CharacterBattleInstance CreateForTests(
        string definitionId,
        CharacterAttributes baseAttributes,
        IEnumerable<CardRuntimeEntry>? drawPile = null,
        int skillCounterCap = 0,
        IReadOnlyList<ActiveSkillChainEntryDto>? activeSkillChain = null) =>
        CreateForTests(definitionId, baseAttributes.Values, drawPile, skillCounterCap, activeSkillChain);
}