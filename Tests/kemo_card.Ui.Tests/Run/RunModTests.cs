using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

[TestFixture]
public sealed class RunModTests
{
    [Test]
    public void ToDto_round_trips_shared_data()
    {
        var mod = new RunMod { RunId = "r1", RunSeed = 42, CurrentRing = 2 };
        var dto = mod.ToDto();

        Assert.That(dto.RunId, Is.EqualTo("r1"));
        Assert.That(dto.RunSeed, Is.EqualTo(42));
        Assert.That(dto.CurrentRing, Is.EqualTo(2));
        Assert.That(dto.Phase, Is.EqualTo(ERunPhase.Event));
    }

    [Test]
    public void ToDto_and_RestoreFrom_preserves_character_pool()
    {
        var mod = new RunMod();
        mod.AddToCharacterPool(new CharacterInstance(new CharacterDto { Id = "test", Cards = [] }));
        var dto = mod.ToDto();

        var restored = new RunMod();
        restored.RestoreFrom(dto);

        Assert.That(restored.CharacterPool, Has.Count.EqualTo(1));
    }

    [Test]
    public void ToDto_and_RestoreFrom_preserves_character_decks()
    {
        var mod = new RunMod();
        var character = new CharacterInstance(new CharacterDto { Id = "hero", Cards = ["c1", "c2"] }, "inst-1");
        var buildable = new HashSet<string>(StringComparer.Ordinal) { "c1", "c2", "card.x" };
        character.TryEditDeck(0, deck => deck.TryAddCard("card.x", buildable));
        character.TryCreateDeck();
        character.TrySetCurrentDeck(1);
        mod.AddToCharacterPool(character);

        var dto = mod.ToDto();
        var restored = new RunMod();
        restored.RestoreFrom(dto);

        Assert.That(restored.CharacterPool, Has.Count.EqualTo(1));
        var restoredCharacter = restored.CharacterPool[0];
        Assert.That(restoredCharacter.InstanceId, Is.EqualTo("inst-1"));
        Assert.That(restoredCharacter.Decks, Has.Count.EqualTo(2));
        Assert.That(restoredCharacter.Decks[0].CardIds, Is.EquivalentTo(new[] { "c1", "c2", "card.x" }));
        Assert.That(restoredCharacter.Decks[1].CardIds, Is.EquivalentTo(new[] { "c1", "c2" }));
        Assert.That(restoredCharacter.CurrentDeckIndex, Is.EqualTo(1));
    }

    [Test]
    public void ToDto_and_RestoreFrom_preserves_card_collection()
    {
        var mod = new RunMod();
        mod.AddCard("card.a");
        mod.AddCard("card.b");

        var dto = mod.ToDto();
        var restored = new RunMod();
        restored.RestoreFrom(dto);

        Assert.That(restored.CardCollection, Is.EquivalentTo(new[] { "card.a", "card.b" }));
    }

    [Test]
    public void ToDto_and_RestoreFrom_preserves_player_state_modifiers()
    {
        var mod = new RunMod();
        mod.PlayerStates[0].AddModifier(new RunModifierDto { ModifierId = "atk_up", Value = 5f, Source = "event.1" });

        var dto = mod.ToDto();
        var restored = new RunMod();
        restored.RestoreFrom(dto);

        Assert.That(restored.PlayerStates[0].Modifiers, Has.Count.EqualTo(1));
        Assert.That(restored.PlayerStates[0].Modifiers[0].ModifierId, Is.EqualTo("atk_up"));
    }

    [Test]
    public void ToDto_and_RestoreFrom_preserves_shared_gold_in_singleplayer()
    {
        var mod = new RunMod();
        mod.SharedGold = 999;

        var dto = mod.ToDto();
        var restored = new RunMod();
        restored.RestoreFrom(dto);

        Assert.That(restored.SharedGold, Is.EqualTo(999));
    }

    [Test]
    public void ActiveParty_is_computed_from_player_states()
    {
        var mod = new RunMod();
        var characters = new List<CharacterInstance>
        {
            new(new CharacterDto { Id = "c0", Cards = [] }),
            new(new CharacterDto { Id = "c1", Cards = [] }),
            new(new CharacterDto { Id = "c2", Cards = [] }),
            new(new CharacterDto { Id = "c3", Cards = [] }),
        };
        foreach (var c in characters)
            mod.AddToCharacterPool(c);
        for (var i = 0; i < RunConstants.SlotCount; i++)
            mod.PlayerStates[i].SetActiveCharacter(characters[i]);

        var party = mod.ActiveParty;

        Assert.That(party, Has.Length.EqualTo(RunConstants.SlotCount));
        Assert.That(party[0], Is.SameAs(characters[0]));
        Assert.That(party[^1], Is.SameAs(characters[RunConstants.SlotCount - 1]));
    }

    [Test]
    public void ValidateParty_fails_when_any_slot_is_null()
    {
        var mod = new RunMod();
        Assert.That(mod.ValidateParty(), Is.False);

        mod.PlayerStates[0].SetActiveCharacter(new CharacterInstance());
        mod.PlayerStates[1].SetActiveCharacter(new CharacterInstance());

        Assert.That(mod.ValidateParty(), Is.False);

        mod.PlayerStates[RunConstants.SlotCount - 1].SetActiveCharacter(new CharacterInstance());
        Assert.That(mod.ValidateParty(), Is.True);
    }

    [Test]
    public void CanUseCard_uses_card_collection()
    {
        var mod = new RunMod();
        mod.AddCard("card.x");
        mod.AddCard("card.y");

        Assert.That(mod.CanUseCard("card.x"), Is.True);
        Assert.That(mod.CanUseCard("card.y"), Is.True);
        Assert.That(mod.CanUseCard("card.z"), Is.False);
    }
}