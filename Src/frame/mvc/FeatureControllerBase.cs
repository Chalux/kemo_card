namespace KemoCard.Frame.Mvc;

/// <summary>
/// 功能 Controller：封装对 Model 的读写与加工；业务与界面逻辑应通过本类公开 API 访问，避免直接操作 Model 字段。
/// </summary>
/// <typeparam name="TModel">与本功能绑定的 Model 类型。</typeparam>
public abstract class FeatureControllerBase<TModel> where TModel : FeatureModelBase
{
	protected FeatureControllerBase(TModel model)
	{
		Model = model;
	}

	protected TModel Model { get; }

	protected EventDispatcher InternalBus => Model.InternalBus;
}
