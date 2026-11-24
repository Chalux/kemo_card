namespace KemoCard.Mod.Combat;

internal sealed record DiscardPoolRequirement(object Reference, int[] Owners, int Count, bool Required);

internal sealed record AssignedDiscard(DiscardPoolRequirement Requirement, IReadOnlyList<HandCardChoice> Cards);

internal sealed class DiscardPlan
{
    private readonly IReadOnlyList<DiscardPoolRequirement> _groups;
    private readonly HandCardChoice[] _available;
    private readonly int[] _required;
    private readonly int[] _capacity;

    public HashSet<int> Owners { get; }
    public DiscardSelection Limits { get; }

    public DiscardPlan(IReadOnlyList<DiscardPoolRequirement> groups, IReadOnlyList<CharacterBattleInstance> characters)
    {
        _groups = groups;
        Owners = groups.SelectMany(group => group.Owners).ToHashSet();
        _available = Owners.Order().SelectMany(owner => characters[owner].HandSlots
            .Where(slot => !slot.IsEmpty).Select(slot => new HandCardChoice(owner, slot.RuntimeInstanceId!))).ToArray();
        _capacity = groups.Select(group => Math.Min(group.Count,
            _available.Count(card => group.Owners.Contains(card.CharacterIndex)))).ToArray();

        var mandatory = groups.Select((group, index) => group.Required ? _capacity[index] : 0).ToArray();
        var slots = Expand(mandatory);
        var assigned = Match(_available, slots);
        _required = new int[groups.Count];
        foreach (var index in assigned.Where(index => index >= 0))
            _required[slots[index]]++;

        slots = Expand(_capacity);
        Limits = new DiscardSelection(_required.Sum(), Match(_available, slots).Count(index => index >= 0));
    }

    public bool TryAssign(IReadOnlyList<HandCardChoice> cards, out List<AssignedDiscard> selections)
    {
        selections = new();
        if (cards.Count < Limits.Minimum || cards.Count > Limits.Maximum ||
            cards.Distinct().Count() != cards.Count || cards.Any(card => !_available.Contains(card)))
            return false;

        var mandatory = Expand(_required);
        var slots = mandatory.Concat(Expand(_capacity.Select((capacity, index) => capacity - _required[index]).ToArray())).ToArray();
        var assigned = Match(cards, slots);
        if (assigned.Any(index => index < 0) || assigned.Count(index => index < mandatory.Length) != mandatory.Length)
            return false;

        var byGroup = _groups.Select(_ => new List<HandCardChoice>()).ToArray();
        for (var i = 0; i < cards.Count; i++)
            byGroup[slots[assigned[i]]].Add(cards[i]);
        selections = _groups.Select((group, index) => new AssignedDiscard(group, byGroup[index])).ToList();
        return true;
    }

    private static int[] Expand(IReadOnlyList<int> counts) =>
        counts.SelectMany((count, index) => Enumerable.Repeat(index, count)).ToArray();

    // Match quota slots to distinct cards. Reassign earlier matches when a later pool has fewer eligible cards.
    private int[] Match(IReadOnlyList<HandCardChoice> cards, int[] slots)
    {
        var assignment = Enumerable.Repeat(-1, cards.Count).ToArray();
        for (var slot = 0; slot < slots.Length; slot++)
            TryMatch(slot, new bool[cards.Count]);
        return assignment;

        bool TryMatch(int slot, bool[] visited)
        {
            var owners = _groups[slots[slot]].Owners;
            for (var card = 0; card < cards.Count; card++)
            {
                if (visited[card] || !owners.Contains(cards[card].CharacterIndex))
                    continue;

                visited[card] = true;
                if (assignment[card] < 0 || TryMatch(assignment[card], visited))
                {
                    assignment[card] = slot;
                    return true;
                }
            }
            return false;
        }
    }
}
