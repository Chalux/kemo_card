using System;
using KemoCard.Frame.Logging;

namespace KemoCard.Frame.Scripting;

public sealed class AppLogModScriptLogger : IModScriptLogger
{
	private readonly IAppLog _log;

	public AppLogModScriptLogger(IAppLog log)
	{
		_log = log ?? throw new ArgumentNullException(nameof(log));
	}

	public void Log(string message) => _log.Info(message, "Script");
}
