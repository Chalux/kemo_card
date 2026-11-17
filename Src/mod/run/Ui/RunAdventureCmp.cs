using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Run.Adventure;
using KemoCard.Mod.Run.Ui.Items;

namespace KemoCard.Mod.Run.Ui;

public partial class RunAdventureCmp : BaseCmp
{
    [Export] private Label? _nodeTitle;
    [Export] private Label? _text;
    [Export] private Container? _options;
    [Export] private PackedScene? _optionScene;
    [Export] private Button? _leave;
    [Export] private Button? _boss;
    [Export] private Button? _next;
    private RunAdventureService? _service;

    #region 接线与地图

    protected override void InitEvent()
    {
        for (var branch = 0; branch < 2; branch++)
        {
            var selected = branch;
            Binder.OnPressed(BranchButton(selected), () => _service?.SelectBranch(selected));
            for (var step = 0; step < 3; step++)
            {
                var b = branch; var s = step;
                Binder.OnPressed(NodeButton(b, s), () => _service?.OpenNode(b, s));
            }
        }
        if (_leave is not null) Binder.OnPressed(_leave, () => _service?.LeaveNode());
        if (_boss is not null) Binder.OnPressed(_boss, () =>
        {
            if (_service is not null && !_service.StartBoss(out var error) && _text is not null)
                _text.Text = error ?? Localization.Tr("UI_ACTION_BLOCKED");
        });
        if (_next is not null) Binder.OnPressed(_next, () =>
        {
            RunRuntime.Current?.NextRing();
            if (_service is not null) { _service.EnsureMap(); Bind(_service); }
        });
    }

    private Button NodeButton(int branch, int step) => GetNode<Button>($"Map/Routes/Lanes/Branch{branch}/Node{step}");
    private Button BranchButton(int branch) => GetNode<Button>($"Map/Routes/Lanes/Branch{branch}/Select");

    public void Bind(RunAdventureService service)
    {
        _service = service;
        service.EnsureMap();
        var map = service.State.Map;
        if (map is null) return;
        for (var branch = 0; branch < 2; branch++)
        {
            var button = BranchButton(branch);
            button.Text = string.Format(Localization.Tr(map.SelectedBranch == branch ? "UI_MAP_ROUTE_SELECTED" : "UI_MAP_SELECT_ROUTE"), branch + 1);
            button.Disabled = !service.CanNavigate || map.SelectedBranch != -1 || map.NodeOpen;
        }
        foreach (var node in map.Nodes)
        {
            var button = NodeButton(node.Branch, node.Step);
            button.Text = (node.Kind != ERunNodeKind.Shop && node.Completed ? "✓ " : "") + NodeName(node);
            button.Disabled = !service.CanNavigate || map.NodeOpen || node.Kind != ERunNodeKind.Shop && node.Completed ||
                node.Branch != map.SelectedBranch;
            button.TooltipText = NodeName(node);
        }
        _boss!.Disabled = !service.CanStartBoss;
        _next!.Visible = service.State.Phase == ERunPhase.RingEnd;
        _next.Disabled = RunRuntime.Current?.HasNextRing() != true;
        if (_next.Visible && _next.Disabled) _next.Text = Localization.Tr("UI_MAP_FINISHED");
        else _next.Text = Localization.Tr("UI_MAP_NEXT_RING");
        foreach (var child in _options!.GetChildren()) { _options.RemoveChild(child); child.QueueFree(); }
        var active = map.NodeOpen && service.CanNavigate;
        _leave!.Visible = active && (map.CurrentNode?.Kind == ERunNodeKind.Shop ||
            map.CurrentNode?.Kind == ERunNodeKind.Event && map.CurrentNode.EventId is null || service.CanSkipVisibleReward);
        _leave.Text = Localization.Tr(map.CurrentNode?.Kind == ERunNodeKind.Shop ? "UI_MAP_RETURN" :
            map.CurrentNode?.Kind == ERunNodeKind.VisibleEvent ? "UI_VISIBLE_SKIP" : "UI_MAP_CONTINUE");
        if (!active)
        {
            _nodeTitle!.Text = Localization.Tr("UI_MAP_TITLE");
            _text!.Text = Localization.Tr(service.State.Phase == ERunPhase.RingEnd ? "UI_MAP_RING_COMPLETE" : "UI_MAP_ROUTE_HINT");
            return;
        }
        if (map.CurrentNode?.Kind == ERunNodeKind.Shop) PaintShop();
        else if (map.CurrentNode?.Kind == ERunNodeKind.VisibleEvent) PaintVisibleReward();
        else PaintEvent();
    }

    private static string NodeName(RunMapNode node) => node.Kind == ERunNodeKind.Shop ? Localization.Tr("UI_MAP_SHOP") :
        node.VisibleReward is { } reward ? reward.Kind switch
        {
            EVisibleEventKind.Gold => Localization.Tr("UI_VISIBLE_GOLD"),
            EVisibleEventKind.RolePack => string.Format(Localization.Tr("UI_VISIBLE_ROLE_PACK"),
                KemoCard.Mod.Global.Ui.CharacterIdentityLabels.Role(reward.Role, Localization.Tr)),
            EVisibleEventKind.AllRolesPack => Localization.Tr("UI_VISIBLE_ALL_PACK"),
            EVisibleEventKind.RelicChoice => Localization.Tr("UI_VISIBLE_RELIC"),
            _ => Localization.Tr("UI_MAP_VISIBLE_EVENT"),
        } : Localization.Tr("UI_MAP_EVENT");

    private void PaintVisibleReward()
    {
        var reward = _service!.CurrentVisibleReward;
        _nodeTitle!.Text = NodeName(_service.State.Map!.CurrentNode!);
        if (reward is null) { _text!.Text = Localization.Tr("UI_VISIBLE_UNAVAILABLE"); return; }
        if (reward.Kind == EVisibleEventKind.RelicChoice)
        {
            _text!.Text = Localization.Tr(_service.CanSkipVisibleReward ? "UI_VISIBLE_UNAVAILABLE" : "UI_VISIBLE_RELIC_HINT");
            foreach (var id in reward.RelicIds)
            {
                _service.Store.TryGetItem(id, out var item);
                AddOption(Localization.Tr(item?.DisplayNameId ?? id), Localization.Tr(item?.DescId ?? "UI_ITEM_MISSING"),
                    _service.CanClaimVisibleReward(id), () => _service.ClaimVisibleReward(id));
            }
            return;
        }
        var ready = _service.CanClaimVisibleReward();
        _text!.Text = Localization.Tr(!ready ? "UI_VISIBLE_UNAVAILABLE" : reward.Kind == EVisibleEventKind.Gold
            ? "UI_VISIBLE_GOLD_HINT" : "UI_VISIBLE_PACK_HINT");
        var preview = string.Join("\n", reward.CardIds.Select(id => Localization.Tr(_service.Store.TryGetCard(id, out var card) ? card.DisplayNameId : id)));
        AddOption(Localization.Tr("UI_VISIBLE_CLAIM"), preview, ready, () => _service.ClaimVisibleReward());
    }

    #endregion

    #region 事件与商店

    private void PaintEvent()
    {
        var evt = _service!.CurrentEvent;
        _nodeTitle!.Text = Localization.Tr(evt?.DisplayNameId ?? "UI_MAP_EVENT");
        _text!.Text = Localization.Tr(evt?.Pages.FirstOrDefault()?.TextId ?? evt?.DescId ?? "UI_EVENT_EMPTY");
        if (evt is null) return;
        foreach (var option in evt.Options)
        {
            var requirements = _service.Requirements(option);
            var details = new List<string>();
            if (option.DescId is not null) details.Add(Localization.Tr(option.DescId));
            details.AddRange(requirements.Select(requirement =>
                (requirement.Passed ? "✓ " : "○ ") + string.Format(Localization.Tr(requirement.TextKey),
                    requirement.Args.Select(arg => arg is string key ? (object)Localization.Tr(key) : arg).ToArray())));
            var enabled = _service.CanChoose(option);
            if (!enabled && requirements.All(requirement => requirement.Passed)) details.Add(Localization.Tr("UI_EVENT_UNSUPPORTED"));
            AddOption(Localization.Tr(option.LabelId), string.Join("\n", details), enabled, () => Choose(option));
        }
    }

    private void Choose(EventOptionDto option)
    {
        if (_service is null) return;
        if (_service.ChooseOption(option.OptionId)) return;
        var potionId = option.ItemRewards.FirstOrDefault(id => _service.Store.TryGetItem(id, out var item) && item.Kind == EItemKind.Potion);
        if (potionId is not null && _service.State.Items.Potions.All(id => id is not null))
            _ = RunUiController.OpenPotionAsync(new RunPotionDlgPayload
            {
                ReplacementItemId = potionId,
                ReplacementChosen = slot =>
                {
                    if (_service.ChooseOption(option.OptionId, slot, declinePotions: slot is null)) return;
                    if (_text is not null) _text.Text = Localization.Tr("UI_ACTION_BLOCKED");
                },
            });
        else _text!.Text = Localization.Tr("UI_ACTION_BLOCKED");
    }

    private void PaintShop()
    {
        var offers = _service!.State.Map!.CurrentNode!.Offers;
        _nodeTitle!.Text = Localization.Tr("UI_MAP_SHOP");
        _text!.Text = Localization.Tr(offers.Count == 0 ? "UI_SHOP_EMPTY" : "UI_SHOP_HINT");
        for (var i = 0; i < offers.Count; i++)
        {
            var index = i;
            var offer = offers[i];
            _service.Store.TryGetItem(offer.ItemId, out var item);
            var name = Localization.Tr(item?.DisplayNameId ?? offer.ItemId);
            var label = offer.Sold ? string.Format(Localization.Tr("UI_SHOP_SOLD"), name)
                : string.Format(Localization.Tr("UI_SHOP_PRICE"), name, offer.Price);
            var enabled = !offer.Sold && item is not null && _service.State.SharedGold >= offer.Price
                && (item.Kind != EItemKind.Relic || !_service.State.Items.Relics.Contains(offer.ItemId));
            AddOption(label, Localization.Tr(item?.DescId ?? "UI_ITEM_MISSING"), enabled, () => Buy(index));
        }
    }

    private void Buy(int index)
    {
        if (_service is null || _service.Buy(index)) return;
        var offer = _service.State.Map!.CurrentNode!.Offers[index];
        if (_service.Store.TryGetItem(offer.ItemId, out var item) && item.Kind == EItemKind.Potion &&
            _service.State.Items.Potions.All(id => id is not null))
            _ = RunUiController.OpenPotionAsync(new RunPotionDlgPayload
            {
                ReplacementItemId = offer.ItemId,
                ReplacementChosen = slot =>
                {
                    if (slot.HasValue && !_service.Buy(index, slot) && _text is not null)
                        _text.Text = Localization.Tr("UI_ACTION_BLOCKED");
                },
            });
    }

    private void AddOption(string text, string details, bool enabled, Action action)
    {
        if (_optionScene is null || _options is null) return;
        var row = _optionScene.Instantiate<RunOptionCmp>();
        _options.AddChild(row);
        row.Bind(text, details, enabled, action);
    }

    #endregion
}