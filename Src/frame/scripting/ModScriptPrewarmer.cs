using KemoCard.Frame.Content;

namespace KemoCard.Frame.Scripting;

public sealed class ModScriptPrewarmer
{
    private readonly ModScriptRuntime _runtime;
    private readonly GameDefinitionRegistry _registry;

    public ModScriptPrewarmer(ModScriptRuntime runtime, GameDefinitionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(registry);
        _runtime = runtime;
        _registry = registry;
    }

    public IReadOnlyList<ScriptLoadError> Warm()
    {
        var errors = new List<ScriptLoadError>();
        foreach (var (modId, scriptPath) in ModScriptPathCollector.Collect(_registry))
        {
            if (_runtime.TryLoadModule(modId, scriptPath))
            {
                continue;
            }

            errors.Add(new ScriptLoadError(modId, scriptPath, "Failed to import script module."));
        }

        return errors;
    }
}