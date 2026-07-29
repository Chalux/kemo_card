using KemoCard.Mod.Global.Save;

namespace KemoCard.Mod.Global.Events;

public sealed class GlobalSaveChangedEvent
{
    public GlobalSaveChangedEvent(GlobalSaveDto snapshot)
    {
        Snapshot = snapshot;
    }

    public GlobalSaveDto Snapshot { get; }
}