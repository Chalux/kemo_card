using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.Scripting;
using KemoCard.Mod.Combat.Runtime;

namespace KemoCard.Mod.Run.Adventure;

public sealed record RunRequirement(bool Passed, string TextKey, object[] Args);

/// <summary>地图、事件、商店和共享库存的宿主入口；所有随机结果与购买状态进入 Run 快照。</summary>
public sealed class RunAdventureService(RunController run, GameDefinitionRegistry definitions)
{
    public RunMod State => run.State;
    public GameDefinitionStore Store => definitions.Store;
    public bool CanNavigate => !State.Phase.IsCombatPhase() && State.Phase != ERunPhase.Finished && State.Phase != ERunPhase.RingEnd;

    #region 地图与流程

    public void EnsureMap()
    {
        if (State.Phase.IsCombatPhase())
            return;
        if (State.Map?.Ring == State.CurrentRing)
        {
            UpgradeVisibleSlots();
            return;
        }
        Store.TryGetStory(State.StoryId, out var story);
        var pool = (story?.RingEventPools.FirstOrDefault(entry => entry.Ring == State.CurrentRing)?.Events ?? [])
            .Where(entry => entry.Weight > 0 && !State.ReservedEventIds.Contains(entry.EventId)
                && Store.TryGetEvent(entry.EventId, out var evt) && evt.EventKind == EEventKind.Data)
            .GroupBy(entry => entry.EventId, StringComparer.Ordinal).Select(group => group.First())
            .OrderBy(entry => entry.EventId, StringComparer.Ordinal).ToList();
        var rng = new HostRng(State.RunSeed, $"map:{State.StoryId}:{State.CurrentRing}");
        var nodes = new List<RunMapNode>();
        for (var branch = 0; branch < 2; branch++)
            for (var step = 0; step < 3; step++)
            {
                string? eventId = null;
                if (step == 0 && pool.Count > 0)
                {
                    var total = pool.Sum(entry => (long)Math.Min(entry.Weight, 1_000_000));
                    var roll = rng.NextInt(0, (int)total);
                    var index = 0;
                    while (roll >= Math.Min(pool[index].Weight, 1_000_000))
                        roll -= Math.Min(pool[index++].Weight, 1_000_000);
                    eventId = pool[index].EventId;
                    pool.RemoveAt(index);
                    State.ReservedEventIds.Add(eventId);
                }
                nodes.Add(new RunMapNode
                {
                    Branch = branch, Step = step, Kind = step == 1 ? ERunNodeKind.Shop : step == 2 ? ERunNodeKind.VisibleEvent : ERunNodeKind.Event,
                    EventId = eventId,
                    VisibleReward = step == 2 ? GenerateVisibleReward(branch) : null,
                    Offers = step == 1 ? (story?.ShopItemIds ?? []).Distinct(StringComparer.Ordinal)
                        .Where(id => Store.TryGetItem(id, out var item) && item.ShopPrice is >= 0)
                        .Select(id => new RunShopOffer { ItemId = id, Price = Store.Items[id].ShopPrice!.Value }).ToList() : [],
                });
            }
        State.Map = new RunMapState { Ring = State.CurrentRing, Nodes = nodes };
        run.NotifyAdventureChanged();
    }

    public bool SelectBranch(int branch)
    {
        var map = State.Map;
        if (!CanNavigate || map is null || map.Ring != State.CurrentRing || map.NodeOpen
            || map.SelectedBranch != -1 || branch is < 0 or > 1)
            return false;
        map.SelectedBranch = branch;
        run.NotifyAdventureChanged();
        return true;
    }

    public bool OpenNode(int branch, int step)
    {
        var map = State.Map;
        if (!CanNavigate || map is null || map.Ring != State.CurrentRing || map.NodeOpen
            || branch is < 0 or > 1 || map.SelectedBranch != branch
            || !map.Nodes.Any(node => node.Branch == branch && node.Step == step
                && (node.Kind == ERunNodeKind.Shop || !node.Completed)))
            return false;
        map.Position = step;
        map.NodeOpen = true;
        PrepareVisibleReward();
        State.Phase = ERunPhase.Event;
        run.NotifyAdventureChanged();
        return true;
    }

    public bool LeaveNode()
    {
        var node = State.Map?.CurrentNode;
        if (!CanNavigate || State.Map?.NodeOpen != true || node is null ||
            node.Kind == ERunNodeKind.Event && node.EventId is not null ||
            node.Kind == ERunNodeKind.VisibleEvent && !CanSkipVisibleReward)
            return false;
        CompleteNode();
        return true;
    }

    private void CompleteNode()
    {
        var map = State.Map!;
        if (map.CurrentNode!.Kind != ERunNodeKind.Shop)
            map.CurrentNode.Completed = true;
        map.NodeOpen = false;
        State.Phase = ERunPhase.Event;
        run.NotifyAdventureChanged();
    }

    public bool CanStartBoss => CanNavigate && State.Map is { NodeOpen: false, SelectedBranch: >= 0 } map
        && map.Ring == State.CurrentRing
        && map.Nodes.Count(node => node.Branch == map.SelectedBranch && node.Kind != ERunNodeKind.Shop && node.Completed) == 2;

    public bool StartBoss(out string? error)
    {
        error = null;
        if (!CanStartBoss)
            return false;
        var previous = State.Phase;
        try
        {
            State.Phase = ERunPhase.Reward;
            run.StartBattle(definitions, new HostRng(State.RunSeed, $"battle:{State.CurrentRing}"), State.RunSeed);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            State.Phase = previous;
            error = ex.Message;
            return false;
        }
    }

    #endregion

    #region 明文事件

    private static ERole[] CardRoles => Enum.GetValues<ERole>().Where(role => role != ERole.None).ToArray();

    private RunVisibleReward GenerateVisibleReward(int branch)
    {
        var rng = new HostRng(State.RunSeed, $"visible:{State.StoryId}:{State.CurrentRing}:{branch}");
        var kind = (EVisibleEventKind)rng.NextInt(0, 4);
        var roles = CardRoles;
        return new RunVisibleReward { Kind = kind, Role = kind == EVisibleEventKind.RolePack ? roles[rng.NextInt(0, roles.Length)] : ERole.None };
    }

    private void UpgradeVisibleSlots()
    {
        var map = State.Map!;
        var changed = false;
        for (var index = 0; index < map.Nodes.Count; index++)
        {
            var node = map.Nodes[index];
            // 已完成或已打开的旧随机事件保持原样，避免替换已提交奖励或中断待选选项。
            if (node.Step != 2 || node.Kind != ERunNodeKind.Event || node.Completed ||
                map.NodeOpen && map.SelectedBranch == node.Branch && map.Position == node.Step)
                continue;
            map.Nodes[index] = node with { Kind = ERunNodeKind.VisibleEvent, EventId = null, VisibleReward = GenerateVisibleReward(node.Branch) };
            changed = true;
        }
        if (changed) run.NotifyAdventureChanged();
    }

    public RunVisibleReward? CurrentVisibleReward => State.Map is { NodeOpen: true } map &&
        map.CurrentNode is { Kind: ERunNodeKind.VisibleEvent, Completed: false } node ? node.VisibleReward : null;

    private void PrepareVisibleReward()
    {
        var reward = CurrentVisibleReward;
        if (reward is null || reward.Prepared) return;
        var node = State.Map!.CurrentNode!;
        var rng = new HostRng(State.RunSeed, $"visible.contents:{State.StoryId}:{State.CurrentRing}:{node.Branch}");
        var cards = new RunCardPackRewards(run, Store).EligibleCards();
        if (reward.Kind == EVisibleEventKind.RolePack)
            reward.CardIds = Draw(cards.Where(card => card.Role == reward.Role).Select(card => card.Id), 5, rng);
        else if (reward.Kind == EVisibleEventKind.AllRolesPack)
            reward.CardIds = CardRoles.SelectMany(role => Draw(cards.Where(card => card.Role == role).Select(card => card.Id), 1, rng)).ToList();
        else if (reward.Kind == EVisibleEventKind.RelicChoice)
            reward.RelicIds = Draw(Store.Items.Values.Where(item => item.Kind == EItemKind.Relic && !State.Items.Relics.Contains(item.Id))
                .OrderBy(item => item.Id, StringComparer.Ordinal).Select(item => item.Id), 3, rng);
        reward.Prepared = true;
    }

    private static List<string> Draw(IEnumerable<string> ids, int count, HostRng rng)
    {
        var pool = ids.Distinct(StringComparer.Ordinal).ToList();
        var drawn = new List<string>();
        while (drawn.Count < count && pool.Count > 0)
        {
            var index = rng.NextInt(0, pool.Count);
            drawn.Add(pool[index]);
            pool.RemoveAt(index);
        }
        return drawn;
    }

    public bool CanClaimVisibleReward(string? relicId = null)
    {
        if (!CanNavigate || CurrentVisibleReward is not { Prepared: true } reward) return false;
        return reward.Kind switch
        {
            EVisibleEventKind.Gold => (long)run.GetGold() + 150 <= int.MaxValue,
            EVisibleEventKind.RolePack => reward.CardIds.Count == 5 && reward.CardIds.Distinct().Count() == 5 &&
                reward.CardIds.All(id => Store.TryGetCard(id, out var card) && RunCardPackRewards.IsPackCard(card) && card.Role == reward.Role)
                && new RunCardPackRewards(run, Store).CanGrant(reward.CardIds),
            EVisibleEventKind.AllRolesPack => reward.CardIds.Count == CardRoles.Length && reward.CardIds.Distinct().Count() == CardRoles.Length &&
                reward.CardIds.All(id => Store.TryGetCard(id, out var card) && RunCardPackRewards.IsPackCard(card)) &&
                reward.CardIds.Select(id => Store.Cards[id].Role).Distinct().Count() == CardRoles.Length
                && new RunCardPackRewards(run, Store).CanGrant(reward.CardIds),
            EVisibleEventKind.RelicChoice => relicId is not null && reward.RelicIds.Contains(relicId) &&
                Store.TryGetItem(relicId, out var item) && item.Kind == EItemKind.Relic && State.Items.CanAdd(item),
            _ => false,
        };
    }

    public bool CanSkipVisibleReward => CurrentVisibleReward is { } reward && (reward.Kind == EVisibleEventKind.RelicChoice
        ? !reward.RelicIds.Any(id => CanClaimVisibleReward(id)) : !CanClaimVisibleReward());

    public bool ClaimVisibleReward(string? relicId = null)
    {
        if (!CanClaimVisibleReward(relicId)) return false;
        var reward = CurrentVisibleReward!;
        if (reward.Kind == EVisibleEventKind.Gold) run.AddGold(0, 150);
        else if (reward.Kind == EVisibleEventKind.RelicChoice) State.Items.TryAdd(Store.Items[relicId!]);
        else if (!new RunCardPackRewards(run, Store).Grant(reward.CardIds)) return false;
        CompleteNode();
        return true;
    }

    #endregion

    #region 事件条件与提交

    public EventDto? CurrentEvent => State.Map is { NodeOpen: true } map && map.CurrentNode is
    { Kind: ERunNodeKind.Event, EventId: { } id } && Store.TryGetEvent(id, out var evt) ? evt : null;

    public IReadOnlyList<RunRequirement> Requirements(EventOptionDto option) => option.Conditions.Select(condition =>
    {
        var args = condition.Params ?? [];
        var amount = ContentParameters.ReadInt(args, "amount", -1);
        var id = args.TryGetValue("itemId", out var item) ? item.ToString() ?? "" : "";
        var flag = args.TryGetValue("flag", out var value) ? value.ToString() ?? "" : "";
        return condition.Kind switch
        {
            "GoldAtLeast" => new RunRequirement(amount >= 0 && run.GetGold() >= amount, "UI_EVENT_NEED_GOLD", [amount, run.GetGold()]),
            "HasItem" => new RunRequirement(id.Length > 0 && (State.Items.Relics.Contains(id) || State.Items.Potions.Contains(id)),
                "UI_EVENT_NEED_ITEM", [Store.TryGetItem(id, out var dto) ? dto.DisplayNameId : id]),
            "HasFlag" => new RunRequirement(flag.Length > 0 && State.PlayerStates.Any(player =>
                player.EventFlags.TryGetValue(flag, out var present) && string.Equals(present?.ToString(), "true", StringComparison.OrdinalIgnoreCase)),
                "UI_EVENT_NEED_FLAG", [flag]),
            "RingAtLeast" => new RunRequirement(amount > 0 && State.CurrentRing >= amount, "UI_EVENT_NEED_RING", [amount]),
            _ => new RunRequirement(false, "UI_EVENT_UNKNOWN_CONDITION", [condition.Kind]),
        };
    }).ToArray();

    private bool TryPlan(EventOptionDto option, out int gold)
    {
        gold = 0;
        if (option.NextPageIndex is not null || Requirements(option).Any(requirement => !requirement.Passed))
            return false;
        foreach (var reference in option.EffectRefs)
        {
            if (!Store.TryGetEffect(reference.EffectId, out var effect) || effect.Kind != EEffectKind.GainResource ||
                effect.Conditions.Count != 0 || effect.EffectRefs.Count != 0)
                return false;
            var args = ContentParameters.Merge(effect.Params, reference.Params);
            if (!args.TryGetValue("resource", out var resource) || resource.ToString() != "Gold" ||
                !args.TryGetValue("amount", out var value) || !ContentParameters.TryInt(value, out var amount))
                return false;
            var sum = (long)gold + amount;
            if (sum is < int.MinValue or > int.MaxValue)
                return false;
            gold = (int)sum;
        }
        return (long)run.GetGold() + gold is >= 0 and <= int.MaxValue;
    }

    public bool CanChoose(EventOptionDto option) => TryPlan(option, out _) &&
        option.ItemRewards.All(id => Store.TryGetItem(id, out var item) &&
            (item.Kind != EItemKind.Relic || State.Items.CanAdd(item)));

    public bool ChooseOption(string optionId, int? replaceSlot = null, bool declinePotions = false)
    {
        var evt = CurrentEvent;
        var option = evt?.Options.FirstOrDefault(entry => entry.OptionId == optionId);
        if (!CanNavigate || option is null || !TryPlan(option, out var gold))
            return false;
        var scratch = new CombatItemInventory();
        scratch.Restore(State.Items.Relics, State.Items.Potions);
        foreach (var id in option.ItemRewards)
        {
            if (!Store.TryGetItem(id, out var item))
                return false;
            if (item.Kind == EItemKind.Potion && declinePotions) continue;
            if (!scratch.TryAdd(item, replaceSlot)) return false;
            if (item.Kind == EItemKind.Potion)
                replaceSlot = null;
        }
        State.Items.Restore(scratch.Relics, scratch.Potions);
        if (gold > 0) run.AddGold(0, gold);
        else if (gold < 0) run.SpendGold(0, -gold);
        foreach (var player in State.PlayerStates)
            player.EventFlags[$"event.completed:{evt!.Id}"] = true;
        CompleteNode();
        return true;
    }

    #endregion

    #region 库存与商店

    public bool GrantItem(string id, int? replaceSlot = null)
    {
        if (State.Phase.IsCombatPhase() || State.Phase == ERunPhase.Finished ||
            !Store.TryGetItem(id, out var item) || !State.Items.TryAdd(item, replaceSlot))
            return false;
        run.NotifyAdventureChanged();
        return true;
    }

    public bool DiscardPotion(int slot, string id)
    {
        if (State.Phase == ERunPhase.Finished || State.Phase.IsCombatPhase() && run.Simulation?.Phase != Combat.StateMachine.ECombatPhase.Player
            || !State.Items.RemovePotion(slot, id))
            return false;
        run.NotifyAdventureChanged();
        return true;
    }

    public bool Buy(int offerIndex, int? replaceSlot = null)
    {
        var node = State.Map?.CurrentNode;
        if (!CanNavigate || State.Map?.NodeOpen != true || node?.Kind != ERunNodeKind.Shop ||
            offerIndex < 0 || offerIndex >= node.Offers.Count)
            return false;
        var offer = node.Offers[offerIndex];
        if (offer.Sold || offer.Price < 0 || run.GetGold() < offer.Price ||
            !Store.TryGetItem(offer.ItemId, out var item) || !State.Items.CanAdd(item, replaceSlot))
            return false;
        if (!run.SpendGold(0, offer.Price))
            return false;
        State.Items.TryAdd(item, replaceSlot);
        offer.Sold = true;
        run.NotifyAdventureChanged();
        return true;
    }

    #endregion
}