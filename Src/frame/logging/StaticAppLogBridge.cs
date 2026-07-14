namespace KemoCard.Frame.Logging;

/// <summary>
/// 将调用转发到静态 <see cref="AppLog"/> 门面，供无法持有具体 IAppLog 实例的工厂使用。
/// </summary>
public sealed class StaticAppLogBridge : IAppLog
{
	public void Debug(string message, string? category = null) => AppLog.Debug(message, category);

	public void Info(string message, string? category = null) => AppLog.Info(message, category);

	public void Warning(string message, string? category = null) => AppLog.Warning(message, category);

	public void Error(string message, string? category = null) => AppLog.Error(message, category);
}
