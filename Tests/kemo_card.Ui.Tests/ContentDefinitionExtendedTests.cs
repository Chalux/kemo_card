using System.Text.Json;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class ContentDefinitionExtendedTests
{
    [Test]
    public void Load_deserializes_character_enemy_battle_event_item_chain()
    {
        var root = Path.Combine(Path.GetTempPath(), "kemo_mod_tests", Guid.NewGuid().ToString("N"));
        var modDir = ContentModTestHelper.CreateModFolder(root, "base", "base.game");
        ContentModTestHelper.AddEffect(modDir, "slime_damage", """
			{ "kind": "Damage", "params": { "amount": 5 } }
			""");
        ContentModTestHelper.AddEffect(modDir, "potion_heal", """
			{ "kind": "Heal", "params": { "amount": 20 } }
			""");
        ContentModTestHelper.AddEffect(modDir, "shrine_gold", """
			{ "kind": "GainResource", "params": { "resource": "Gold", "amount": 50 } }
			""");
        ContentModTestHelper.AddSkill(modDir, "slime_tackle", """
			{
			  "displayNameId": "skill.slime_tackle.name",
			  "descId": "skill.slime_tackle.desc",
			  "effectRefs": [{ "effectId": "slime_damage" }]
			}
			""");
        ContentModTestHelper.AddSkill(modDir, "potion_use", """
			{
			  "displayNameId": "skill.potion_use.name",
			  "descId": "skill.potion_use.desc",
			  "effectRefs": [{ "effectId": "potion_heal" }]
			}
			""");
        ContentModTestHelper.AddBuff(modDir, "kemo_talent", """
			{
			  "displayNameId": "buff.kemo_talent.name",
			  "descId": "buff.kemo_talent.desc",
			  "iconPath": "buffs/kemo_talent.png",
			  "maxStacks": 1,
			  "stackRule": "Replace",
			  "durationType": "Permanent",
			  "dispellable": false
			}
			""");
        ContentModTestHelper.AddSkill(modDir, "kemo_dash", """
			{
			  "displayNameId": "skill.kemo_dash.name",
			  "descId": "skill.kemo_dash.desc",
			  "effectRefs": [{ "effectId": "slime_damage" }]
			}
			""");
        ContentModTestHelper.AddCard(modDir, "strike", """
			{
			  "displayNameId": "card.strike.name",
			  "costType": "Energy",
			  "cost": 1,
			  "skillRefs": [{ "skillId": "slime_tackle" }],
			  "cardType": "Physics",
			  "targetSide": "Enemy",
			  "targetScope": "Single",
			  "rarity": "Common"
			}
			""");
        ContentModTestHelper.AddCharacter(modDir, "kemo", """
			{
			  "displayNameId": "char.kemo.name",
			  "descId": "char.kemo.desc",
			  "element": "Red",
			  "role": "Warrior",
			  "skillRefs": [{ "skillId": "kemo_dash" }],
			  "buffRefs": [{ "buffId": "kemo_talent" }],
			  "cards": ["strike"],
			  "artPath": "chars/kemo.png"
			}
			""");
        ContentModTestHelper.AddEnemy(modDir, "slime", """
			{
			  "displayNameId": "enemy.slime.name",
			  "maxHp": 30,
			  "skillRefs": [{ "skillId": "slime_tackle" }],
			  "artPath": "enemies/slime.png"
			}
			""");
        ContentModTestHelper.AddEnemy(modDir, "slime_elite", """
			{
			  "displayNameId": "enemy.slime_elite.name",
			  "maxHp": 45,
			  "skillRefs": [{ "skillId": "slime_tackle" }],
			  "artPath": "enemies/slime_elite.png"
			}
			""");
        ContentModTestHelper.AddBattle(modDir, "forest_ambush", """
			{
			  "displayNameId": "battle.forest_ambush.name",
			  "waves": [
			    { "enemySpawns": [{ "enemyId": "slime", "count": 2 }] },
			    { "enemySpawns": [{ "enemyId": "slime_elite", "count": 1, "hpScale": 1.5 }] }
			  ],
			  "rewards": {
			    "entries": [
			      { "kind": "Gold", "params": { "min": 15, "max": 25 } },
			      { "kind": "CardChoice", "params": { "count": 3, "pool": "common_attack" } }
			    ]
			  }
			}
			""");
        ContentModTestHelper.AddEvent(modDir, "shrine", """
			{
			  "displayNameId": "event.shrine.name",
			  "descId": "event.shrine.desc",
			  "eventKind": "Data",
			  "artPath": "events/shrine.png",
			  "options": [
			    {
			      "optionId": "take_gold",
			      "labelId": "event.shrine.option.gold",
			      "effectRefs": [{ "effectId": "shrine_gold" }]
			    }
			  ]
			}
			""");
        ContentModTestHelper.AddItem(modDir, "health_potion", """
			{
			  "displayNameId": "item.health_potion.name",
			  "descId": "item.health_potion.desc",
			  "rarity": "Common",
			  "useSkillRefs": [{ "skillId": "potion_use" }],
			  "targetSpec": { "side": "Ally", "scope": "Single" },
			  "maxStack": 3,
			  "artPath": "items/health_potion.png"
			}
			""");

        var discovery = new ContentModDiscovery();
        var bundle = ContentModLoader.Load(discovery.Scan(root).ValidMods[0]);
        var registry = new GameDefinitionRegistry();
        registry.Rebuild(new[] { bundle }, out var report);

        Assert.That(report.ValidationErrors, Is.Empty);
        Assert.That(registry.Contains(EContentCategory.Character, "kemo"), Is.True);
        Assert.That(registry.Contains(EContentCategory.Enemy, "slime"), Is.True);
        Assert.That(registry.Contains(EContentCategory.Battle, "forest_ambush"), Is.True);
        Assert.That(registry.Contains(EContentCategory.Event, "shrine"), Is.True);
        Assert.That(registry.Contains(EContentCategory.Item, "health_potion"), Is.True);
        Assert.That(registry.Store.TryGetCharacter("kemo", out var character), Is.True);
        Assert.That(character.Cards[0], Is.EqualTo("strike"));
        Assert.That(registry.Store.TryGetBattle("forest_ambush", out var battle), Is.True);
        Assert.That(battle.Waves, Has.Count.EqualTo(2));
    }

    [Test]
    public void CharacterDto_round_trips_through_json()
    {
        var original = new CharacterDto
        {
            Id = "kemo",
            DisplayNameId = "char.kemo.name",
            DescId = "char.kemo.desc",
            Element = EElement.Red,
            Role = ERole.Warrior,
            SkillRefs = [new SkillRefDto { SkillId = "kemo_dash" }],
            BuffRefs = [new BuffRefDto { BuffId = "kemo_talent" }],
            Cards = ["strike"],
        };

        var json = JsonSerializer.Serialize(original, ContentDefinitionJson.Options);
        var restored = JsonSerializer.Deserialize<CharacterDto>(json, ContentDefinitionJson.Options);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored!.Element, Is.EqualTo(EElement.Red));
        Assert.That(restored.BuffRefs[0].BuffId, Is.EqualTo("kemo_talent"));
    }

    [Test]
    public void Validation_rejects_unknown_enemy_in_battle()
    {
        var bundle = ContentModTestHelper.Bundle(
            "base",
            ModDefinitionsBundle.Empty with
            {
                Battles = new Dictionary<string, BattleDto>
                {
                    ["bad_battle"] = new()
                    {
                        Id = "bad_battle",
                        Waves =
                        [
                            new BattleWaveDto
                            {
                                EnemySpawns = [new EnemySpawnDto { EnemyId = "missing_enemy" }],
                            },
                        ],
                    },
                },
            });

        var registry = new GameDefinitionRegistry();
        registry.Rebuild(new[] { bundle }, out var report);

        Assert.That(report.ValidationErrors, Is.Empty);
        Assert.That(report.RemovedValidationErrors, Has.Count.EqualTo(1));
        Assert.That(registry.Contains(EContentCategory.Battle, "bad_battle"), Is.False);
    }

    [Test]
    public void Validation_rejects_script_event_without_script_path()
    {
        var bundle = ContentModTestHelper.Bundle(
            "base",
            ModDefinitionsBundle.Empty with
            {
                Events = new Dictionary<string, EventDto>
                {
                    ["script_event"] = new()
                    {
                        Id = "script_event",
                        EventKind = EEventKind.Script,
                    },
                },
            });

        var registry = new GameDefinitionRegistry();
        registry.Rebuild(new[] { bundle }, out var report);

        Assert.That(report.ValidationErrors, Is.Empty);
        Assert.That(report.RemovedValidationErrors, Has.Count.EqualTo(1));
        Assert.That(registry.Contains(EContentCategory.Event, "script_event"), Is.False);
    }

    [Test]
    public void Validation_rejects_item_without_use_skill_refs()
    {
        var bundle = ContentModTestHelper.Bundle(
            "base",
            ModDefinitionsBundle.Empty with
            {
                Items = new Dictionary<string, ItemDto>
                {
                    ["empty_potion"] = new() { Id = "empty_potion" },
                },
            });

        var registry = new GameDefinitionRegistry();
        registry.Rebuild(new[] { bundle }, out var report);

        Assert.That(report.ValidationErrors, Is.Empty);
        Assert.That(report.RemovedValidationErrors, Has.Count.EqualTo(1));
        Assert.That(registry.Contains(EContentCategory.Item, "empty_potion"), Is.False);
    }
}