using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Runtime;
using KemoCard.Mod.Combat.StateMachine;

namespace KemoCard.Mod.Run.Ui.Items;

public record struct RunPotionDlgPayload
{
    public int Slot { get; init; }
    public int CharacterIndex { get; init; }
    public bool CanUse { get; init; }
    public Action<IReadOnlyList<CombatTargetRef>>? UseRequested { get; init; }
    public Action? Changed { get; init; }
    public string? ReplacementItemId { get; init; }
    public Action<int?>? ReplacementChosen { get; init; }
}

public partial class RunPotionDlg : BaseDlg
{
    public override string UIId => RunUiIds.Potion;
    public override string UIDir => "Src/mod/run/Ui/Items";
    [Export] private Label? _title;
    [Export] private Label? _description;
    [Export] private Button? _use;
    [Export] private Button? _discard;
    [Export] private Button? _cancel;
    [Export] private Container? _options;
    [Export] private PackedScene? _optionScene;
    private RunPotionDlgPayload _payload;
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
        _payload = GetTypedPayload<RunPotionDlgPayload>();
        var service = RunRuntime.CreateAdventureService();
        if (service is null) { Close(); return; }
        var replacement = _payload.ReplacementItemId is not null;
        _id = replacement ? _payload.ReplacementItemId : _payload.Slot is >= 0 and < CombatItemInventory.PotionCapacity
            ? service.State.Items.Potions[_payload.Slot] : null;
        _item = _id is not null && service.Store.TryGetItem(_id, out var item) ? item : null;
        if (_id is null) { Close(); return; }
        if (_title is not null) _title.Text = Localization.Tr(_item?.DisplayNameId ?? _id);
        if (_description is not null) _description.Text = Localization.Tr(_item?.DescId ?? "UI_ITEM_MISSING")
            + (replacement ? "\n" + Localization.Tr("UI_ITEM_REPLACE_HINT") : "");
        _use!.Visible = !replacement;
        _use.Disabled = !_payload.CanUse || RunRuntime.Current?.Simulation?.Phase != ECombatPhase.Player;
        _discard!.Visible = !replacement;
        if (replacement)
        {
            for (var i = 0; i < CombatItemInventory.PotionCapacity; i++)
            {
                var slot = i;
                var oldId = service.State.Items.Potions[i];
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
        if (!_payload.CanUse || sim?.Phase != ECombatPhase.Player || _item is null) return;
        var spec = _item.TargetSpec;
        if (spec is { Scope: ETargetScope.Single, Side: not ETargetSide.Self })
        {
            _use!.Disabled = true;
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

    private void Discard()
    {
        if (_id is not null && RunRuntime.CreateAdventureService()?.DiscardPotion(_payload.Slot, _id) == true)
        { _payload.Changed?.Invoke(); Close(); }
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