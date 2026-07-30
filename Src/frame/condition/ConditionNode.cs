namespace KemoCard.Frame.Condition;

public abstract record ConditionNode;

public sealed record AndNode(IReadOnlyList<ConditionNode> Children) : ConditionNode;

public sealed record OrNode(IReadOnlyList<ConditionNode> Children) : ConditionNode;

public sealed record LeafNode(string CondType, object Args) : ConditionNode;