using KemoCard.Frame.Condition;
using KemoCard.Mod.Global;

namespace KemoCard.Mod.Global.Condition;

/// <summary>
/// Persistent 条件的生产侧只读上下文。v1：HasFlag → 全局存档 Unlocks（与 IsContentUnlocked 同表）；
/// 物品数量待商店/道具规格落地后接入。
/// </summary>
public sealed class GlobalPersistentCondContext(GlobalModController controller) : IPersistentCondContext
{
    private readonly GlobalModController _controller = controller ?? throw new ArgumentNullException(nameof(controller));

    public bool HasFlag(string flagId) => _controller.IsContentUnlocked(flagId);

    public int GetItemCount(string itemId) => 0;
}