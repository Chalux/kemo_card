namespace KemoCard.Frame.Scripting;

public interface IModScriptLogger
{
	void Log(string message);
}

public sealed class NullModScriptLogger : IModScriptLogger
{
	public void Log(string message)
	{
	}
}
