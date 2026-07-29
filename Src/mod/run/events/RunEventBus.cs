using KemoCard.Frame.Mvc;

namespace KemoCard.Mod.Run.Events;

public enum ERunEvent
{
    [EventPayload(typeof(RunPhaseChangedPayload))]
    RunPhaseChanged,

    [EventPayload(typeof(RunSlotOwnershipChangedPayload))]
    RunSlotOwnershipChanged,

    [EventPayload(typeof(RunGoldChangedPayload))]
    RunGoldChanged,
}

public readonly struct RunPhaseChangedPayload
{
    public ERunPhase PreviousPhase { get; init; }
    public ERunPhase CurrentPhase { get; init; }
}

public readonly struct RunSlotOwnershipChangedPayload
{
    public int SlotIndex { get; init; }
    public string? PreviousPlayerId { get; init; }
    public string? CurrentPlayerId { get; init; }
}

public readonly struct RunGoldChangedPayload
{
    public int? SlotIndex { get; init; }
    public int PreviousAmount { get; init; }
    public int CurrentAmount { get; init; }
}

[EventTable(typeof(ERunEvent), typeof(RunMod))]
public static partial class RunModEventTable
{
}