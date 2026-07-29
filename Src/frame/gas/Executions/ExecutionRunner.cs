using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas.Executions;

public sealed class ExecutionRunner
{
    private readonly Dictionary<string, IExecutionCalculation> _calculations = new(StringComparer.OrdinalIgnoreCase);

    public ExecutionRunner()
    {
        Register(new DamageExecution());
    }

    public void Run(GameplayEffectSpec spec, AbilitySystemComponent targetAsc)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(targetAsc);

        foreach (var executionDef in spec.Def.Executions)
        {
            if (string.IsNullOrWhiteSpace(executionDef.Kind))
                continue;
            if (!_calculations.TryGetValue(executionDef.Kind, out var calculation))
                continue;

            calculation.Execute(executionDef, spec, targetAsc);
        }
    }

    private void Register(IExecutionCalculation calculation)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        _calculations[calculation.Kind] = calculation;
    }
}