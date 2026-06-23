using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Ui.Tests.Combat;

namespace KemoCard.Ui.Tests.Run;

internal static class RunTestHelper
{
    public static GameDefinitionRegistry CreateRegistryWithHpCard()
    {
        return CombatTestHelper.CreateFullRegistry(
            cards: new Dictionary<string, CardDto>
            {
                [CombatSimulationTestBuilder.PartyHpCardId] = new()
                {
                    Id = CombatSimulationTestBuilder.PartyHpCardId,
                    Stats = new CardStatBlockDto { HpCap = 10 },
                },
            });
    }

    public static CharacterInstance CreateCharacterWithHp(int index)
    {
        return new CharacterInstance(
            new CharacterDto
            {
                Id = $"hero_{index}",
                Cards = [CombatSimulationTestBuilder.PartyHpCardId],
            },
            $"inst-{index}");
    }
}
