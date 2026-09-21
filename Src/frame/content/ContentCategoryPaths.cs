namespace KemoCard.Frame.Content;

public static class ContentCategoryPaths
{
    public static string Folder(EContentCategory category) => category switch
    {
        EContentCategory.Character => "characters",
        EContentCategory.Enemy => "enemies",
        EContentCategory.Battle => "battles",
        EContentCategory.Event => "events",
        EContentCategory.Card => "cards",
        EContentCategory.Item => "items",
        EContentCategory.Skill => "skills",
        EContentCategory.Buff => "buffs",
        EContentCategory.Effect => "effects",
        EContentCategory.SkillAction => "skill_actions",
        EContentCategory.Attribute => "attributes",
        EContentCategory.GameplayEffect => "gameplay_effects",
        EContentCategory.GameplayTag => "tags",
        EContentCategory.Story => "stories",
        EContentCategory.OrbType => "orbs",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };
}