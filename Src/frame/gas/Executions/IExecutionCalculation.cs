using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas.Executions;

public interface IExecutionCalculation
{
    string Kind { get; }

    void Execute(ExecutionDefDto executionDef, GameplayEffectSpec spec, AbilitySystemComponent targetAsc);
}