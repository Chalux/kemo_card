namespace KemoCard.Frame.Scripting;

public interface IScriptRuntimeResetter
{
    void BeginRebuild();

    void Recreate();

    void EndRebuild();
}