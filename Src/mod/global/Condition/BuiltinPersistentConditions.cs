using System.Text.Json;
using KemoCard.Frame.Condition;

namespace KemoCard.Mod.Global.Condition;

public static class BuiltinPersistentConditions
{
    public static void RegisterAll(ConditionRegistry<IPersistentCondContext> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(CondTypeHandler.Create<IPersistentCondContext, string>(
            "HasFlag",
            "COND_HAS_FLAG_SHORT",
            "COND_HAS_FLAG_LONG",
            TryParseFlag,
            (flagId, context) => CheckFlag(flagId, context, shouldHaveFlag: true)));
        registry.Register(CondTypeHandler.Create<IPersistentCondContext, string>(
            "NotHasFlag",
            "COND_NOT_HAS_FLAG_SHORT",
            "COND_NOT_HAS_FLAG_LONG",
            TryParseFlag,
            (flagId, context) => CheckFlag(flagId, context, shouldHaveFlag: false)));
        registry.Register(CondTypeHandler.Create<IPersistentCondContext, ItemRequirement[]>(
            "HasAllItems",
            "COND_HAS_ALL_ITEMS_SHORT",
            "COND_HAS_ALL_ITEMS_LONG",
            TryParseItemRequirements,
            CheckAllItems));
        registry.Register(CondTypeHandler.Create<IPersistentCondContext, ItemRequirement[]>(
            "HasAnyItem",
            "COND_HAS_ANY_ITEM_SHORT",
            "COND_HAS_ANY_ITEM_LONG",
            TryParseItemRequirements,
            CheckAnyItem));
    }

    private static bool TryParseFlag(
        JsonElement args,
        string sourcePath,
        out string? flagId,
        out string? error)
    {
        if (args.ValueKind != JsonValueKind.Array
            || args.GetArrayLength() != 1
            || args[0].ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(args[0].GetString()))
        {
            flagId = null;
            error = $"{sourcePath}: 参数须为仅含一个非空旗标 ID 的数组";
            return false;
        }

        flagId = args[0].GetString();
        error = null;
        return true;
    }

    private static bool TryParseItemRequirements(
        JsonElement args,
        string sourcePath,
        out ItemRequirement[]? requirements,
        out string? error)
    {
        if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() == 0)
        {
            requirements = null;
            error = $"{sourcePath}: 参数须为非空物品需求数组";
            return false;
        }

        requirements = new ItemRequirement[args.GetArrayLength()];
        for (var index = 0; index < requirements.Length; index++)
        {
            var item = args[index];
            if (item.ValueKind != JsonValueKind.Array
                || item.GetArrayLength() != 2
                || item[0].ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item[0].GetString())
                || item[1].ValueKind != JsonValueKind.Number
                || !item[1].TryGetInt32(out var need)
                || need < 1)
            {
                requirements = null;
                error = $"{sourcePath}[{index}]: 物品需求须为 [非空 ID, 正整数数量]";
                return false;
            }

            requirements[index] = new ItemRequirement(item[0].GetString()!, need);
        }

        error = null;
        return true;
    }

    private static LeafEvalData CheckFlag(
        string flagId,
        IPersistentCondContext context,
        bool shouldHaveFlag)
    {
        var passed = context.HasFlag(flagId) == shouldHaveFlag;
        return new LeafEvalData
        {
            Passed = passed,
            Fill = [flagId],
            Progress = new ConditionProgress(passed ? 1 : 0, 1),
            Refs = new ConditionRefs { FlagIds = [flagId] },
        };
    }

    private static LeafEvalData CheckAllItems(
        ItemRequirement[] requirements,
        IPersistentCondContext context)
    {
        var satisfied = requirements.Count(requirement =>
            context.GetItemCount(requirement.ItemId) >= requirement.Need);
        return new LeafEvalData
        {
            Passed = satisfied == requirements.Length,
            Progress = new ConditionProgress(satisfied, requirements.Length),
            Refs = new ConditionRefs
            {
                ItemIds = requirements.Select(requirement => requirement.ItemId).ToArray(),
            },
        };
    }

    private static LeafEvalData CheckAnyItem(
        ItemRequirement[] requirements,
        IPersistentCondContext context)
    {
        var passed = requirements.Any(requirement =>
            context.GetItemCount(requirement.ItemId) >= requirement.Need);
        return new LeafEvalData
        {
            Passed = passed,
            Progress = new ConditionProgress(passed ? 1 : 0, 1),
            Refs = new ConditionRefs
            {
                ItemIds = requirements.Select(requirement => requirement.ItemId).ToArray(),
            },
        };
    }

    private readonly record struct ItemRequirement(string ItemId, int Need);
}