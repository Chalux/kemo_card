namespace KemoCard.Frame.Notification;

/// <summary>
/// 红点节点的激活策略重载。
/// </summary>
public enum RedDotOverride
{
    /// <summary>不重载，按 checkFunc 或子节点聚合判定</summary>
    None,

    /// <summary>强制激活</summary>
    ForceActive,

    /// <summary>强制不激活</summary>
    ForceInactive,
}