using KemoCard.Frame.Mvc;

namespace KemoCard.Mod.Run.Events;

public enum ERunEvent
{
    [EventPayload(typeof(RunAdventureChangedPayload))]
    RunAdventureChanged,
    [EventPayload(typeof(RunPhaseChangedPayload))]
    RunPhaseChanged,

    [EventPayload(typeof(RunGoldChangedPayload))]
    RunGoldChanged,

    [EventPayload(typeof(RunCharacterAssignedPayload))]
    RunCharacterAssigned,

    [EventPayload(typeof(RunDeckChangedPayload))]
    RunDeckChanged,
}

public readonly struct RunAdventureChangedPayload { }

public readonly struct RunPhaseChangedPayload
{
    public ERunPhase PreviousPhase { get; init; }
    public ERunPhase CurrentPhase { get; init; }
}

/// <summary>
/// 槽位上阵角色发生变化。
/// </summary>
public readonly struct RunCharacterAssignedPayload
{
    public int SlotIndex { get; init; }
    public string? PreviousInstanceId { get; init; }
    public string? CurrentInstanceId { get; init; }
}

/// <summary>
/// 某角色的卡组发生变更（新建 / 切换当前卡组 / 增删卡）。
/// </summary>
/// <remarks>
/// <see cref="DeckIndex"/> 为发生变更的卡组下标；<see cref="InstanceId"/> 为角色实例 id。
/// 卡组编辑入口不止一处（队伍编辑、调试面板），因此由写入方在写成功后广播。
/// </remarks>
public readonly struct RunDeckChangedPayload
{
    public string? InstanceId { get; init; }
    public int DeckIndex { get; init; }
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