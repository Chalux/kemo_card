namespace KemoCard.Frame.Mvc;

/// <summary>
/// 功能内 Model：仅承担本功能数据持有；对外数据变更入口应由对应 Controller 调用。
/// 内部事件通过 <see cref="InternalBus"/> 在功能内通知（Model / Controller / 视图绑定等）。
/// </summary>
public abstract class BaseMod
{
    public string ModId { get; }

    protected BaseMod(string modId)
    {
        ModId = modId;
        Init();
    }

    public EventDispatcher InternalBus { get; } = new();

    public virtual void Init()
    {

    }

    public virtual void Dispose()
    {
        InternalBus.OffAll();
    }
}