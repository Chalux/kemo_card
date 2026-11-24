using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;
using KemoCard.Mod.Run.Adventure;

namespace KemoCard.Mod.Run.Ui.Items;

public record struct RunConsumableDlgPayload
{
    public int Slot { get; init; }
    public int CharacterIndex { get; init; }
    public bool CanUse { get; init; }
    public Action<IReadOnlyList<CombatTargetRef>>? UseRequested { get; init; }
    public Action? Changed { get; init; }
    public string? ReplacementItemId { get; init; }
    /// <summary>已经在上一轮替换中用过的槽位；再次打开时不再列出，避免重复替换同一格。</summary>
    public IReadOnlyCollection<int>? ExcludedSlots { get; init; }
    public Action<int?>? ReplacementChosen { get; init; }
}

public partial class RunConsumableDlg : BaseDlg
{
    public override string UIId => RunUiIds.Consumable;
    public override string UIDir => "Src/mod/run/Ui/Items";
    [Export] private Label? _title;
    [Export] private Label? _description;
    [Export] private Button? _use;
    [Export] private Button? _discard;
    [Export] private Button? _cancel;
    [Export] private Container? _options;
    [Export] private PackedScene? _optionScene;
    private RunConsumableDlgPayload _payload;
    private string? _id;
    private ItemDto? _item;

    #region 生命周期与操作

    protected override void UpdateView() { }

    protected override void InitEvent()
    {
        if (_use is not null) Binder.OnPressed(_use, Use);
        if (_discard is not null) Binder.OnPressed(_discard, Discard);
        if (_cancel is not null) Binder.OnPressed(_cancel, Close);
    }

    protected override void OnOpen()
    {
        // 已打开的界面会被原地重开（双击 / 替换流程连开）：先清掉上一轮追加的选项行。
        ClearOptions();
        _payload = GetTypedPayload<RunConsumableDlgPayload>();
        var service = RunRuntime.CreateAdventureService();
        if (service is null) { Close(); return; }
        var replacement = _payload.ReplacementItemId is not null;
        _id = replacement ? _payload.ReplacementItemId : _payload.Slot is >= 0 and < CombatItemInventory.ConsumableCapacity
            ? service.State.Items.Consumables[_payload.Slot] : null;
        _item = _id is not null && service.Store.TryGetItem(_id, out var item) ? item : null;
        if (_id is null) { Close(); return; }
        if (_title is not null) _title.Text = Localization.Tr(_item?.DisplayNameId ?? _id);
        if (_description is not null) _description.Text = Localization.Tr(_item?.DescId ?? "UI_ITEM_MISSING")
            + (replacement ? "\n" + Localization.Tr("UI_ITEM_REPLACE_HINT") : "");
        _use!.Visible = !replacement;
        _use.Disabled = !CanUse(RunRuntime.Current?.Simulation);
        _discard!.Visible = !replacement;
        // 弹珠阶段服务层不允许丢弃：按钮跟着禁用，避免"点了没反应"。
        _discard.Disabled = !CanDiscardNow();
        if (replacement)
        {
            for (var i = 0; i < CombatItemInventory.ConsumableCapacity; i++)
            {
                if (_payload.ExcludedSlots?.Contains(i) == true)
                    continue;

                var slot = i;
                var oldId = service.State.Items.Consumables[i];
                var name = oldId is not null && service.Store.TryGetItem(oldId, out var old) ? Localization.Tr(old.DisplayNameId) : Localization.Tr("UI_ITEM_EMPTY_SLOT");
                AddOption(string.Format(Localization.Tr("UI_ITEM_REPLACE_SLOT"), i + 1, name), () =>
                { var chosen = _payload.ReplacementChosen; Close(); chosen?.Invoke(slot); });
            }
            AddOption(Localization.Tr("UI_ITEM_GIVE_UP"), () =>
            { var chosen = _payload.ReplacementChosen; Close(); chosen?.Invoke(null); });
        }
    }

    private void Use()
    {
        var sim = RunRuntime.Current?.Simulation;
        if (sim is null || _item is null || !CanUse(sim)) return;
        var spec = _item.TargetSpec;
        if (spec is { Scope: ETargetScope.Single, Side: not ETargetSide.Self })
        {
            _use!.Disabled = true;
            ClearOptions();
            foreach (var target in CombatTargeting.CollectLegalTargetsForScope(sim, spec.Side, spec.Scope, _payload.CharacterIndex))
            {
                var name = target.Side == ECombatSide.Player ? sim.PlayerTeam.Characters[target.Index].DefinitionId
                    : sim.EnemyTeam.Enemies[target.Index].DefinitionId;
                if (target.Side == ECombatSide.Player && sim.Definitions.Store.TryGetCharacter(name, out var character)) name = Localization.Tr(character.DisplayNameId);
                else if (target.Side == ECombatSide.Enemy && sim.Definitions.Store.TryGetEnemy(name, out var enemy)) name = Localization.Tr(enemy.DisplayNameId);
                AddOption(name, () => Submit([target]));
            }
        }
        else Submit([]);
    }

    private void Submit(IReadOnlyList<CombatTargetRef> targets)
    {
        var callback = _payload.UseRequested;
        Close();
        callback?.Invoke(targets);
    }

    private bool CanUse(CombatSimulation? sim) =>
        _payload.CanUse && sim is not null && _item is not null && sim.CanUseItemInCurrentPhase(_item);

    private void Discard()
    {
        if (_id is not null && RunRuntime.CreateAdventureService()?.DiscardConsumable(_payload.Slot, _id) == true)
        { _payload.Changed?.Invoke(); Close(); }
    }

    /// <summary>与 <see cref="RunAdventureService.DiscardConsumable"/> 的守卫保持一致：
    /// 战斗内只有玩家阶段允许丢弃；弹珠阶段按钮直接禁用，避免静默失败。</summary>
    private static bool CanDiscardNow()
    {
        var run = RunRuntime.Current;
        if (run is null || run.State.Phase == ERunPhase.Finished)
            return false;
        return !run.State.Phase.IsCombatPhase() ||
            run.Simulation?.Phase == ECombatPhase.Player;
    }

    /// <summary>清掉选项容器里的旧行；界面原地重开时 OnOpen 会再次追加选项。</summary>
    private void ClearOptions()
    {
        if (_options is null)
            return;
        foreach (var child in _options.GetChildren())
        {
            _options.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void AddOption(string text, Action action)
    {
        if (_optionScene is null || _options is null) return;
        var row = _optionScene.Instantiate<RunOptionCmp>();
        _options.AddChild(row);
        row.Bind(text, "", true, action);
    }

    #endregion
}