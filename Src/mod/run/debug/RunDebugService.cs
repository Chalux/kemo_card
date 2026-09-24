using System.Text;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Buffs;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Run.Potential;

namespace KemoCard.Mod.Run.Debug;

/// <summary>
/// 调试操作结果。<see cref="Message"/> 是<b>面向调试者</b>的明文说明（不走翻译键：
/// 调试面板只在开发期使用，且需要精确报出 id / 数量这类不该进本地化表的信息）。
/// </summary>
public readonly record struct RunDebugResult(bool Ok, string Message)
{
    public static RunDebugResult Success(string message) => new(true, message);

    public static RunDebugResult Failure(string message) => new(false, message);
}

/// <summary>调试面板的候选项（内容 id 与显示名）。</summary>
public sealed record RunDebugOption(string Id, string Label);

/// <summary>
/// Run 调试面板的全部操作。
/// </summary>
/// <remarks>
/// <para><b>刻意不引用 Godot</b>：这样每条调试操作的语义（给了什么、改了什么、失败在哪一步）
/// 都能被现有「不依赖场景树」的测试直接覆盖，界面层只剩取值与显示。</para>
/// <para><b>这是调试工具，允许绕过玩法前置条件</b>：例如 <see cref="StartBattle"/> 会先把阶段改成
/// Reward、补齐控制权与上阵，再调用正式的 <see cref="RunController.StartBattle"/>。
/// 绕过的是「怎么走到这一步」，进入战斗后走的仍是生产管线（内容定义 + BattleStart 管线）。</para>
/// </remarks>
public sealed class RunDebugService
{
    private const string DebugScriptEntry = "execute";
    private const string LastEventFlagKey = "debug.lastTriggeredEventId";

    private readonly RunController _run;
    private readonly GameDefinitionRegistry _definitions;
    private readonly IContentEffectScriptHost _scriptHost;

    public RunDebugService(
        RunController run,
        GameDefinitionRegistry definitions,
        IContentEffectScriptHost? scriptHost = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(definitions);

        _run = run;
        _definitions = definitions;
        _scriptHost = scriptHost ?? new NullContentEffectScriptHost();
    }

    public RunMod State => _run.State;

    /// <summary>当前金币（单机为共享金币）。</summary>
    public int Gold => _run.GetGold();

    private GameDefinitionStore Store => _definitions.Store;

    #region 候选项

    public IReadOnlyList<RunDebugOption> ListCards() =>
        [.. Store.Cards.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => ToOption(kv.Key, kv.Value.DisplayNameId))];

    public IReadOnlyList<RunDebugOption> ListCharacters() =>
        [.. Store.Characters.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => ToOption(kv.Key, kv.Value.DisplayNameId))];

    public IReadOnlyList<RunDebugOption> ListBattles() =>
        [.. Store.Battles.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new RunDebugOption(kv.Key, $"{kv.Key}（{kv.Value.Waves.Count} 波）"))];

    public IReadOnlyList<RunDebugOption> ListEvents() =>
        [.. Store.Events.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new RunDebugOption(kv.Key, $"{kv.Key}（{kv.Value.Pages.Count} 页 / {kv.Value.Options.Count} 选项）"))];

    private static RunDebugOption ToOption(string id, string displayNameId) =>
        new(id, string.IsNullOrWhiteSpace(displayNameId) ? id : $"{id}（{displayNameId}）");

    #endregion

    #region 卡牌

    /// <summary>把卡牌加入本局的可用卡池（<c>RunMod.CardCollection</c>）。</summary>
    public RunDebugResult GrantCard(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return RunDebugResult.Failure("未选择卡牌。");
        }

        if (!Store.TryGetCard(cardId, out _))
        {
            return RunDebugResult.Failure($"内容中不存在卡牌 '{cardId}'。");
        }

        if (!_run.AddCard(cardId))
        {
            return RunDebugResult.Failure($"卡牌 '{cardId}' 加入失败。");
        }

        return RunDebugResult.Success(State.CanUseCard(cardId)
            ? $"已加入卡牌收藏：{cardId}（收藏共 {State.CardCollection.Count} 张）"
            : $"卡牌 '{cardId}' 未能进入收藏。");
    }

    public RunDebugResult RevokeCard(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return RunDebugResult.Failure("未选择卡牌。");
        }

        return _run.RemoveCard(cardId)
            ? RunDebugResult.Success($"已移出卡牌收藏：{cardId}（收藏共 {State.CardCollection.Count} 张）")
            : RunDebugResult.Failure($"卡牌 '{cardId}' 不在收藏中。");
    }

    /// <summary>
    /// 把卡牌写进指定槽位角色的当前卡组。
    /// </summary>
    /// <remarks>
    /// 只进收藏是打不出来的：战斗抽牌读的是角色卡组（<c>CharacterInstance.Decks[CurrentDeckIndex]</c>），
    /// 所以这里会先确保收藏里有这张牌，再写卡组——这正是「调试要用某张牌打一场」的最小闭环。
    /// </remarks>
    public RunDebugResult AddCardToDeck(int slotIndex, string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
        {
            return RunDebugResult.Failure("未选择卡牌。");
        }

        if (!TryGetSlotCharacter(slotIndex, out var character, out var error))
        {
            return RunDebugResult.Failure(error);
        }

        var grant = GrantCard(cardId);
        if (!grant.Ok)
        {
            return grant;
        }

        if (character!.Decks.Count == 0)
        {
            return RunDebugResult.Failure($"槽位 {slotIndex + 1} 的角色没有卡组。");
        }

        var deck = character.Decks[character.CurrentDeckIndex];
        if (deck.CardIds.Contains(cardId, StringComparer.Ordinal))
        {
            return RunDebugResult.Success($"槽位 {slotIndex + 1} 的卡组里已有 '{cardId}'。");
        }

        if (!character.TryEditDeck(character.CurrentDeckIndex, d => d.TryAddCard(cardId, State.CardCollection)))
        {
            return RunDebugResult.Failure($"槽位 {slotIndex + 1} 的卡组不可编辑（可能已锁定）。");
        }

        _run.NotifyDeckChanged(character.InstanceId, character.CurrentDeckIndex);
        return deck.CardIds.Contains(cardId, StringComparer.Ordinal)
            ? RunDebugResult.Success(
                $"槽位 {slotIndex + 1} 卡组已加入 '{cardId}'（{deck.CardIds.Count} 张）。")
            : RunDebugResult.Failure(
                $"'{cardId}' 未能写入槽位 {slotIndex + 1} 的卡组（可能已达卡组上限 {CombatConstants.MaxCardsPerDeck}）。");
    }

    #endregion

    #region 角色

    /// <summary>把角色加入角色池；<paramref name="deploySlot"/> 非负时同时上阵到该槽位。</summary>
    public RunDebugResult GrantCharacter(string characterId, int deploySlot = -1)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return RunDebugResult.Failure("未选择角色。");
        }

        if (!Store.TryGetCharacter(characterId, out var definition))
        {
            return RunDebugResult.Failure($"内容中不存在角色 '{characterId}'。");
        }

        var instance = new CharacterInstance(definition);
        if (!_run.AddToCharacterPool(instance, sourceSlotIndex: deploySlot))
        {
            // 重复获得（角色定义唯一）：已转化为 +20 潜能。目标槽位有效**且**该槽上阵的正是这个
            // 角色定义时才是"直充该槽位"，否则进的是团队池——提示必须与真实去向一致。
            var creditedToSlot = deploySlot >= 0 &&
                deploySlot < RunConstants.SlotCount &&
                string.Equals(
                    _run.State.PlayerStates[deploySlot].ActiveCharacter?.DefinitionId,
                    characterId,
                    StringComparison.Ordinal);
            return RunDebugResult.Success(creditedToSlot
                ? $"重复获得角色 '{characterId}'：不入池，直充槽位 {deploySlot + 1} 潜能 +{PotentialService.DuplicateReward}"
                    + $"（该槽位直充余额 {_run.State.PlayerStates[deploySlot].PotentialDirectCredit}）。"
                : $"重复获得角色 '{characterId}'：不入池，转化为团队潜能 +{PotentialService.DuplicateReward}"
                    + $"（团队池 {_run.Potential.TeamPool}）。");
        }

        var poolIndex = State.CharacterPool.Count - 1;
        var suffix = $"角色池共 {State.CharacterPool.Count} 个";

        if (deploySlot < 0)
        {
            return RunDebugResult.Success($"已加入角色池：{characterId}（索引 {poolIndex}，{suffix}）");
        }

        var deploy = DeployPoolCharacter(poolIndex, deploySlot);
        return deploy.Ok
            ? RunDebugResult.Success($"已加入角色池并上阵：{characterId} → 槽位 {deploySlot + 1}（{suffix}）")
            : RunDebugResult.Failure($"角色已入池（索引 {poolIndex}），但上阵失败：{deploy.Message}");
    }

    /// <summary>把角色池中的某个实例上阵到指定槽位。</summary>
    public RunDebugResult DeployPoolCharacter(int poolIndex, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
        {
            return RunDebugResult.Failure($"槽位 {slotIndex} 越界（0..{RunConstants.SlotCount - 1}）。");
        }

        if (poolIndex < 0 || poolIndex >= State.CharacterPool.Count)
        {
            return RunDebugResult.Failure($"角色池索引 {poolIndex} 越界（0..{State.CharacterPool.Count - 1}）。");
        }

        if (!_run.SetActiveCharacter(slotIndex, poolIndex))
        {
            return RunDebugResult.Failure($"上阵失败：槽位 {slotIndex + 1} ← 角色池 {poolIndex}。");
        }

        return RunDebugResult.Success(
            $"槽位 {slotIndex + 1} 上阵 {State.CharacterPool[poolIndex].DefinitionId}。");
    }

    /// <summary>
    /// 按角色定义 id 找到角色池中的实例并上阵（界面按内容 id 选择，池里存的是实例）。
    /// </summary>
    public RunDebugResult DeployCharacter(string characterId, int slotIndex)
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return RunDebugResult.Failure("未选择角色。");
        }

        for (var i = 0; i < State.CharacterPool.Count; i++)
        {
            if (string.Equals(State.CharacterPool[i].DefinitionId, characterId, StringComparison.Ordinal))
            {
                return DeployPoolCharacter(i, slotIndex);
            }
        }

        return RunDebugResult.Failure($"角色池里没有 '{characterId}'，请先「加入角色池」。");
    }

    /// <summary>
    /// 用角色池里的空闲角色补满空槽位，让 <see cref="StartBattle"/> 的队伍前置条件成立。
    /// </summary>
    public RunDebugResult FillPartyFromPool()
    {
        var inUse = new HashSet<string>(StringComparer.Ordinal);
        foreach (var active in State.ActiveParty)
        {
            if (active != null)
            {
                inUse.Add(active.InstanceId);
            }
        }

        var assigned = 0;
        for (var slot = 0; slot < RunConstants.SlotCount; slot++)
        {
            if (State.ActiveParty[slot] != null)
            {
                continue;
            }

            var poolIndex = -1;
            for (var i = 0; i < State.CharacterPool.Count; i++)
            {
                if (!inUse.Contains(State.CharacterPool[i].InstanceId))
                {
                    poolIndex = i;
                    break;
                }
            }

            if (poolIndex < 0)
            {
                break;
            }

            if (!_run.SetActiveCharacter(slot, poolIndex))
            {
                break;
            }

            inUse.Add(State.CharacterPool[poolIndex].InstanceId);
            assigned++;
        }

        if (assigned == 0)
        {
            return State.ValidateParty()
                ? RunDebugResult.Success("全部槽位已上阵，无需补位。")
                : RunDebugResult.Failure("角色池里没有空闲角色可补位，请先添加角色。");
        }

        return RunDebugResult.Success($"已自动补位 {assigned} 个槽位（角色池 {State.CharacterPool.Count} 个）。");
    }

    /// <summary>补齐控制权分配（单机演示时把全部槽位交给第一个玩家控制器）。</summary>
    public RunDebugResult EnsureSlotsAssigned()
    {
        if (State.AllSlotsAssigned())
        {
            return RunDebugResult.Success("全部槽位已有控制权。");
        }

        if (State.PlayerControllers.Count == 0)
        {
            return RunDebugResult.Failure("本局没有任何玩家控制器，无法分配控制权。");
        }

        var playerId = State.PlayerControllers[0].PlayerId;
        var assigned = 0;
        for (var slot = 0; slot < RunConstants.SlotCount; slot++)
        {
            if (_run.AssignSlot(slot, playerId))
            {
                assigned++;
            }
        }

        return State.AllSlotsAssigned()
            ? RunDebugResult.Success($"已把 {assigned} 个槽位的控制权交给 '{playerId}'。")
            : RunDebugResult.Failure("控制权分配未完成（当前阶段可能不允许改控制权）。");
    }

    #endregion

    #region 战斗

    /// <summary>
    /// 用指定战斗定义开一场战斗。
    /// </summary>
    /// <param name="battleId">战斗定义 id。</param>
    /// <param name="seed">
    /// 指定战斗随机种子（同时作为洗牌流与战斗内随机流的种子）；<c>null</c> 时看
    /// <paramref name="useRandomSeed"/>：勾选取随机值，否则用 RunSeed（默认 <c>run_debug</c> 流）。
    /// </param>
    /// <param name="useRandomSeed">种子留空时的兜底：勾选"随机种子"即每次开战换一局。</param>
    /// <remarks>
    /// 无战斗界面：这里只把战斗跑起来（<see cref="RunController.StartBattle"/> 会走内容定义 +
    /// BattleStart 管线并停在首个玩家阶段），调试者据此确认战斗接线是否成立，
    /// 再用 <see cref="EndBattle"/> 判定胜负退出战斗阶段。
    /// </remarks>
    public RunDebugResult StartBattle(string battleId, int? seed = null, bool useRandomSeed = false)
    {
        if (string.IsNullOrWhiteSpace(battleId))
        {
            return RunDebugResult.Failure("未选择战斗。");
        }

        if (!Store.TryGetBattle(battleId, out var battle))
        {
            return RunDebugResult.Failure($"内容中不存在战斗 '{battleId}'。");
        }

        var slots = EnsureSlotsAssigned();
        if (!slots.Ok)
        {
            return slots;
        }

        var party = FillPartyFromPool();
        if (!State.ValidateParty())
        {
            return RunDebugResult.Failure($"队伍未齐，无法开战：{party.Message}");
        }

        // StartBattle 只接受 Reward 阶段；调试面板显式前置到这里，绕过的是流程而不是战斗管线。
        State.Phase = ERunPhase.Reward;

        try
        {
            // 种子：显式传入优先；留空时勾选「随机种子」→ 随机，否则默认 RunSeed + "run_debug" 流。
            var runSeed = State.RunSeed == 0 ? 1 : State.RunSeed;
            var battleSeed = seed ?? (useRandomSeed ? Random.Shared.Next(1, int.MaxValue) : runSeed);
            var simulation = _run.StartBattle(
                _definitions,
                new HostRng(battleSeed, "run_debug"),
                battleSeed,
                battleId);

            return RunDebugResult.Success(
                $"已进入战斗 '{simulation.Battle?.Id ?? battleId}'：{simulation.WaveCount} 波、"
                + $"{simulation.EnemyTeam.Enemies.Count} 名敌人，阶段 {simulation.Phase}。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return RunDebugResult.Failure($"开战失败：{ex.Message}");
        }
    }

    /// <summary>判定当前战斗胜负并退出战斗阶段（<c>true</c> 走胜利结算，<c>false</c> 回滚到战前快照）。</summary>
    public RunDebugResult EndBattle(bool won)
    {
        if (State.Phase is not (ERunPhase.Battle or ERunPhase.BattleEnd))
        {
            return RunDebugResult.Failure($"当前不在战斗中（阶段 {State.Phase}），无需结算。");
        }

        try
        {
            _run.EndBattle(won);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return RunDebugResult.Failure($"结算失败：{ex.Message}");
        }

        return RunDebugResult.Success(won
            ? $"已判定胜利，阶段 → {State.Phase}。"
            : $"已判定失败（回滚到战前快照），阶段 → {State.Phase}。");
    }

    #endregion

    #region 事件

    /// <summary>
    /// 触发一次指定事件。
    /// </summary>
    /// <remarks>
    /// <para><b>事件运行时尚未实现</b>（环内容后置，见 run-mod-design §5）：本方法能做的只有
    /// ① 把阶段拨到 <see cref="ERunPhase.Event"/>；② 在全部槽位记下 <c>debug.lastTriggeredEventId</c>
    /// 事件标记（随存档往返）；③ 若事件声明了 <c>ScriptPath</c>，按归属 mod 执行该脚本并回报产出。</para>
    /// <para>返回值里会明确写出「页面/选项未呈现」，避免调试者把「没弹窗」误判成脚本没跑。</para>
    /// </remarks>
    public RunDebugResult TriggerEvent(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return RunDebugResult.Failure("未选择事件。");
        }

        if (!Store.TryGetEvent(eventId, out var definition))
        {
            return RunDebugResult.Failure($"内容中不存在事件 '{eventId}'。");
        }

        State.Phase = ERunPhase.Event;
        foreach (var state in State.PlayerStates)
        {
            state.SetEventFlag(LastEventFlagKey, eventId);
        }

        var scriptNote = RunEventScript(definition);
        return RunDebugResult.Success(
            $"已触发事件 '{eventId}'（{definition.EventKind}，{definition.Pages.Count} 页 / "
            + $"{definition.Options.Count} 选项）：阶段 → Event，已写入标记 {LastEventFlagKey}。"
            + $"事件运行时未实现，页面/选项不会呈现。{scriptNote}");
    }

    private string RunEventScript(EventDto definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ScriptPath))
        {
            return " 该事件未声明 ScriptPath，无脚本可执行。";
        }

        if (!_definitions.TryGetOwnerModId(EContentCategory.Event, definition.Id, out var modId)
            || string.IsNullOrWhiteSpace(modId))
        {
            return $" 无法确定事件 '{definition.Id}' 的归属 mod，脚本 '{definition.ScriptPath}' 未执行。";
        }

        var context = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["eventId"] = definition.Id,
            ["runId"] = State.RunId,
            ["ring"] = State.CurrentRing,
        };

        try
        {
            if (!_scriptHost.TryExecute(modId, definition.ScriptPath!, DebugScriptEntry, context, out var effects))
            {
                return $" 脚本 '{definition.ScriptPath}'（mod {modId}）未执行或执行失败。";
            }

            return $" 脚本 '{definition.ScriptPath}' 已执行，产出 {effects.Count} 条效果。";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return $" 脚本 '{definition.ScriptPath}' 执行异常：{ex.Message}";
        }
    }

    #endregion

    #region 通用

    public RunDebugResult AddGold(int amount)
    {
        if (amount <= 0)
        {
            return RunDebugResult.Failure("金币数量必须为正数。");
        }

        if (!_run.AddGold(0, amount))
        {
            return RunDebugResult.Failure($"加金币失败：{amount}。");
        }

        return RunDebugResult.Success($"金币 +{amount} → {_run.GetGold()}");
    }

    public RunDebugResult SetRing(int ring)
    {
        if (ring < 1 || ring > State.MaxRing)
        {
            return RunDebugResult.Failure($"环数必须在 1..{State.MaxRing} 之间。");
        }

        State.CurrentRing = ring;
        return RunDebugResult.Success($"当前环 → {State.CurrentRing}/{State.MaxRing}");
    }

    public RunDebugResult SetPhase(ERunPhase phase)
    {
        State.Phase = phase;
        return RunDebugResult.Success($"阶段 → {State.Phase}");
    }

    #endregion

    #region 潜能

    public RunDebugResult GrantPotential(int amount, int slotIndex = -1)
    {
        if (amount <= 0)
        {
            return RunDebugResult.Failure("潜能数量必须为正数。");
        }

        // 调试通道按申请数额精确入账（语义与重复角色奖励一致：槽位直充或团队池）。
        _run.Potential.Grant(amount, slotIndex);

        return RunDebugResult.Success(slotIndex >= 0
            ? $"槽位 {slotIndex} 潜能直充 +{amount} → 现有 {_run.State.PlayerStates[slotIndex].PotentialDirectCredit}"
            : $"团队潜能池 +{amount} → 现有 {_run.Potential.TeamPool}");
    }

    /// <summary>解锁选中槽位上阵角色的下一条未解锁被动（走正式潜能消费管线）。</summary>
    public RunDebugResult UnlockNextPassive(int slotIndex)
    {
        if (!TryGetSlotCharacter(slotIndex, out var character, out var error) || character?.Definition is null)
        {
            return RunDebugResult.Failure(error);
        }

        var next = character.Definition.Passives
            .Where(passive => !PotentialService.IsPassiveUnlocked(State, character, passive))
            .OrderBy(passive => passive.RequiredPotential)
            .FirstOrDefault();
        if (next is null)
        {
            return RunDebugResult.Success($"{character.DefinitionId} 的被动已全部解锁。");
        }

        var result = _run.Potential.TryUnlock(slotIndex, character, next);
        return result.Success
            ? RunDebugResult.Success($"已解锁 {character.DefinitionId} 的 {next.BuffId}（成本 {next.RequiredPotential}）。")
            : RunDebugResult.Failure($"解锁失败：{result.Error}");
    }

    /// <summary>返还选中槽位最近一笔消费（同一笔解锁拆出的直充/池流水整组退回）。</summary>
    public RunDebugResult RefundLatestPotential(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
        {
            return RunDebugResult.Failure($"槽位 {slotIndex} 越界。");
        }

        var latest = State.PlayerStates[slotIndex].PotentialSpent.LastOrDefault();
        if (latest is null)
        {
            return RunDebugResult.Failure($"槽位 {slotIndex} 没有可返还的消费记录。");
        }

        var refunded = _run.Potential.Refund(slotIndex, latest.EntryId);
        return refunded > 0
            ? RunDebugResult.Success($"已返还 {latest.BuffId} 的整笔消费 {refunded}，对应被动重新锁定。")
            : RunDebugResult.Failure("返还失败：流水不存在。");
    }

    /// <summary>战斗检查快照：波次/回合/连携档位、充能球、各角色（S/能量/buff/手牌槽与槽位buff）、敌人状态。</summary>
    public string InspectBattle()
    {
        var simulation = _run.Simulation;
        if (simulation is null)
        {
            return "当前没有进行中的战斗。";
        }

        var builder = new StringBuilder();
        builder.Append($"战斗 {simulation.Battle?.Id ?? "-"}：波 {simulation.CurrentWaveIndex + 1}/{simulation.WaveCount}，"
            + $"回合 {simulation.TurnNumber}（波内 {simulation.TurnsIntoWave}），阶段 {simulation.Phase}。\n");

        var chainCounts = ChainCalculator.CountDistinctCharacters(simulation);
        var chainText = string.Join("、", chainCounts
            .Where(pair => ChainCalculator.TierScale(pair.Value) > 0)
            .Select(pair => $"{pair.Key}×{pair.Value}({ChainCalculator.TierScale(pair.Value):P0})"));
        builder.Append($"连携（按当前出牌队列）：{(chainText.Length > 0 ? chainText : "无")}\n");

        var orbText = string.Join("、", simulation.Orbs.Queue.Orbs
            .GroupBy(orb => orb.OrbTypeId, StringComparer.Ordinal)
            .Select(group => $"{group.Key}×{group.Count()}"));
        builder.Append($"充能球 {simulation.Orbs.Queue.Count}/{OrbQueue.Capacity}"
            + $"（{(orbText.Length > 0 ? orbText : "空")}；"
            + $"{(simulation.Orbs.Queue.CanTriggerManually ? "可主动触发" : $"满 {OrbQueue.ManualTriggerThreshold} 可主动触发")}）\n");

        var normalAttackSlot = simulation.NormalAttacks.ResolveSlotIndex(simulation);
        var lastNormalAttack = simulation.NormalAttacks.LastResult;
        builder.Append($"普通攻击：本回合归槽位 {(normalAttackSlot >= 0 ? (normalAttackSlot + 1).ToString() : "-")}"
            + $"（累计 {simulation.NormalAttacks.ExecutionCount} 次）"
            + (lastNormalAttack is null
                ? "；尚未执行"
                : $";上次 槽位 {lastNormalAttack.SlotIndex + 1} {lastNormalAttack.CharacterDefinitionId}"
                    + $" {lastNormalAttack.Kind} 元素={lastNormalAttack.Element}"
                    + $" 命中 {lastNormalAttack.TargetCount} 总伤 {lastNormalAttack.TotalDamage:0.##}")
            + "\n");

        for (var i = 0; i < simulation.PlayerTeam.Characters.Count; i++)
        {
            var character = simulation.PlayerTeam.Characters[i];
            builder.Append($"[{i}] {character.DefinitionId}（{character.Element}/{character.Race}）"
                + $" S={character.SkillCounter}/{character.SkillCounterCap}"
                + $" 能量={character.AvailableEnergy}/{character.CurrentEnergy}\n");

            foreach (var instance in character.Buffs.All)
            {
                builder.Append($"    buff {instance.Def.Id} ×{instance.Stacks}"
                    + (instance.RemainingTurns.HasValue ? $" 剩{instance.RemainingTurns}回合" : "")
                    + (instance.IsDormant ? " [休眠]" : "") + "\n");
            }

            foreach (var slot in character.HandSlots)
            {
                var slotBuffText = string.Join("、", slot.Buffs.All.Select(instance =>
                    $"{instance.Def.Id}(充能{instance.ChargeCounter}, 剩{instance.RemainingTurns}回合)"));
                if (slot.CardId is not null || slotBuffText.Length > 0)
                {
                    builder.Append($"    槽{slot.SlotIndex + 1}: {slot.CardId ?? "-"}"
                        + (slotBuffText.Length > 0 ? $" | {slotBuffText}" : "") + "\n");
                }
            }
        }

        for (var i = 0; i < simulation.EnemyTeam.Enemies.Count; i++)
        {
            var enemy = simulation.EnemyTeam.Enemies[i];
            if (!enemy.IsAlive)
            {
                continue;
            }

            var enemyBuffText = string.Join("、", enemy.Buffs.All.Select(instance =>
                $"{instance.Def.Id}(剩{instance.RemainingTurns}回合)"));
            builder.Append($"敌[{i}] {enemy.DefinitionId} HP={enemy.CurrentHp}/{enemy.MaxHp}"
                + (enemyBuffText.Length > 0 ? $" | {enemyBuffText}" : "") + "\n");
        }

        return builder.ToString().TrimEnd();
    }

    #endregion

    #region 充能球

    /// <summary>内容中的充能球类型（调试面板下拉用，按 id 排序）。</summary>
    public IReadOnlyList<OrbTypeDto> ListOrbTypes() =>
        [.. Store.OrbTypes.Values.OrderBy(orb => orb.Id, StringComparer.Ordinal)];

    /// <summary>
    /// 授予充能球（调试通道）：产球者 = <paramref name="producerSlot"/> 槽位的上阵角色。
    /// 未知球类型 / 无战斗 / 槽位越界都是软失败，并说明失败在哪一步。
    /// </summary>
    public RunDebugResult GrantOrb(string orbTypeId, int count = 1, int producerSlot = 0)
    {
        if (!Store.TryGetOrbType(orbTypeId, out var orbType))
        {
            return RunDebugResult.Failure($"内容中不存在充能球 '{orbTypeId}'。");
        }

        if (count <= 0)
        {
            return RunDebugResult.Failure("充能球数量必须为正数。");
        }

        var simulation = _run.Simulation;
        if (simulation is null)
        {
            return RunDebugResult.Failure("当前没有进行中的战斗。");
        }

        if (producerSlot < 0 || producerSlot >= simulation.PlayerTeam.Characters.Count)
        {
            return RunDebugResult.Failure($"产球者槽位 {producerSlot} 越界（0..{simulation.PlayerTeam.Characters.Count - 1}）。");
        }

        simulation.Orbs.Grant(simulation, orbTypeId, producerSlot, count);
        return RunDebugResult.Success(
            $"已授予 {orbType.DisplayNameId} ×{count}（产球者 槽位 {producerSlot + 1}；"
            + $"当前 {simulation.Orbs.Queue.Count}/{OrbQueue.Capacity}）");
    }

    /// <summary>手动触发充能球（走正式触发管线：清空队列 + 逐球结算）。</summary>
    public RunDebugResult TriggerOrbs()
    {
        var simulation = _run.Simulation;
        if (simulation is null)
        {
            return RunDebugResult.Failure("当前没有进行中的战斗。");
        }

        var result = simulation.Orbs.TriggerManual(simulation);
        if (!result.Triggered)
        {
            return RunDebugResult.Failure(result.Error ?? "充能球触发失败。");
        }

        var cleared = string.Join(
            "、",
            result.ClearedByType.Select(pair => $"{pair.Key}×{pair.Value}"));
        return RunDebugResult.Success($"已触发充能球：{cleared}");
    }

    #endregion

    private bool TryGetSlotCharacter(int slotIndex, out CharacterInstance? character, out string error)
    {
        character = null;
        if (slotIndex < 0 || slotIndex >= RunConstants.SlotCount)
        {
            error = $"槽位 {slotIndex} 越界（0..{RunConstants.SlotCount - 1}）。";
            return false;
        }

        character = State.ActiveParty[slotIndex];
        if (character == null)
        {
            error = $"槽位 {slotIndex + 1} 没有上阵角色。";
            return false;
        }

        error = "";
        return true;
    }
}