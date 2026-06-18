using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;

namespace KemoCard.Ui.Tests.Combat;

internal static class CombatTestHelper
{
    public static GameDefinitionRegistry CreateRegistry(params CardDto[] cards)
    {
        var cardDict = cards.ToDictionary(card => card.Id, StringComparer.Ordinal);
        var definitions = new ModDefinitionsBundle(
            ModDefinitionsBundle.Empty.Characters,
            ModDefinitionsBundle.Empty.Enemies,
            ModDefinitionsBundle.Empty.Battles,
            ModDefinitionsBundle.Empty.Events,
            ModDefinitionsBundle.Empty.Items,
            cardDict,
            ModDefinitionsBundle.Empty.Skills,
            ModDefinitionsBundle.Empty.Buffs,
            ModDefinitionsBundle.Empty.Effects);

        var bundle = new ModContentBundle(
            ModId: "test.mod",
            Characters: [],
            Enemies: [],
            Battles: [],
            Events: [],
            Cards: cardDict.Keys.ToList(),
            Items: [],
            Skills: [],
            Buffs: [],
            Effects: [],
            Definitions: definitions);

        var registry = new GameDefinitionRegistry();
        registry.Rebuild([bundle], out _);
        return registry;
    }
}
