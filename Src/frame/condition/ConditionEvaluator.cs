namespace KemoCard.Frame.Condition;

public static class ConditionEvaluator
{
    public static ConditionEvalResult Evaluate<TContext>(
        ConditionNode root,
        TContext context,
        ConditionRegistry<TContext> registry)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(registry);

        var leaves = new List<LeafResult>();
        var passed = EvalNode(root, context, registry, leaves);
        return new ConditionEvalResult { Passed = passed, Leaves = leaves };
    }

    private static bool EvalNode<TContext>(
        ConditionNode node,
        TContext context,
        ConditionRegistry<TContext> registry,
        List<LeafResult> leaves)
    {
        switch (node)
        {
            case AndNode and:
            {
                var ok = true;
                foreach (var child in and.Children)
                {
                    if (!EvalNode(child, context, registry, leaves))
                    {
                        ok = false;
                    }
                }

                return ok;
            }
            case OrNode or:
            {
                var ok = false;
                foreach (var child in or.Children)
                {
                    if (EvalNode(child, context, registry, leaves))
                    {
                        ok = true;
                    }
                }

                return ok;
            }
            case LeafNode leaf:
            {
                if (!registry.TryGet(leaf.CondType, out var handler) || handler is null)
                {
                    throw new InvalidOperationException($"未注册的 CondType: {leaf.CondType}");
                }

                var data = handler.Check(leaf.Args, context);
                leaves.Add(new LeafResult
                {
                    CondType = handler.Id,
                    Passed = data.Passed,
                    ShortTipKey = handler.ShortTipKey,
                    LongTipKey = handler.LongTipKey,
                    Fill = data.Fill,
                    Progress = data.Progress,
                    Refs = data.Refs,
                });
                return data.Passed;
            }
            default:
                throw new InvalidOperationException($"未知条件节点: {node.GetType().Name}");
        }
    }
}
