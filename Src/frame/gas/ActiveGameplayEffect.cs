using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Frame.Gas;

public sealed class ActiveGameplayEffect
{
    private int _periodCounter;

    public Guid Handle { get; }
    public GameplayEffectDefDto Def { get; }
    public GameplayEffectSpec Spec { get; }
    public int Stacks { get; private set; }
    public int RemainingTurns { get; private set; }
    public bool IsExpired { get; private set; }
    public bool IsSuspended { get; set; }
    public int PeriodCounter => _periodCounter;

    public ActiveGameplayEffect(GameplayEffectSpec spec, int initialStacks = 1)
    {
        Spec = spec ?? throw new ArgumentNullException(nameof(spec));
        Def = spec.Def;
        Handle = spec.Handle ?? Guid.NewGuid();
        Stacks = Math.Max(1, Math.Min(initialStacks, GetMaxStacks()));
        RemainingTurns = Def.DurationPolicy == EDurationPolicy.HasDuration ? Math.Max(0, Def.DurationTurns) : 0;
        IsExpired = Def.DurationPolicy == EDurationPolicy.HasDuration && RemainingTurns == 0;
        // Suspension is managed by AbilitySystemComponent based on OngoingRequiredTags only.
        IsSuspended = false;
    }

    public bool AddStack()
    {
        var maxStacks = GetMaxStacks();
        if (Stacks >= maxStacks)
            return false;
        Stacks++;
        RefreshDurationIfNeeded();
        return true;
    }

    public bool OnTurnStart()
    {
        if (IsExpired || Def.PeriodTurns <= 0)
            return false;

        _periodCounter++;
        if (_periodCounter < Def.PeriodTurns)
            return false;

        _periodCounter = 0;
        return true;
    }

    public void OnTurnEnd()
    {
        if (IsExpired)
            return;

        if (Def.DurationPolicy == EDurationPolicy.HasDuration && RemainingTurns > 0)
        {
            RemainingTurns--;
            if (RemainingTurns <= 0)
                IsExpired = true;
        }
    }

    private int GetMaxStacks() => Math.Max(1, Def.MaxStacks);

    private void RefreshDurationIfNeeded()
    {
        if (Def.DurationPolicy != EDurationPolicy.HasDuration)
            return;
        RemainingTurns = Math.Max(0, Def.DurationTurns);
        IsExpired = RemainingTurns == 0;
    }
}