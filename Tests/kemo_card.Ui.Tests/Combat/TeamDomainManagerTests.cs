using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Rules;
using KemoCard.Mod.Combat.Runtime;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

[TestFixture]
public sealed class TeamDomainManagerTests
{
    [Test]
    public void SetDomain_replaces_existing_domain_on_same_team()
    {
        var gameplayEffects = new Dictionary<string, GameplayEffectDefDto>
        {
            ["domain_a"] = new()
            {
                Id = "domain_a",
                DurationPolicy = EDurationPolicy.Infinite,
                StackingPolicy = EStackingPolicy.None,
            },
            ["domain_b"] = new()
            {
                Id = "domain_b",
                DurationPolicy = EDurationPolicy.Infinite,
                StackingPolicy = EStackingPolicy.None,
            },
        };
        var registry = CombatTestHelper.CreateFullRegistry(gameplayEffects: gameplayEffects);
        var enemy = new EnemyUnit("e", "slime", 10);
        var sim = CombatSimulationTestBuilder.Minimal(enemy, registry);
        var manager = new TeamDomainManager(sim);

        Assert.That(manager.TrySetPlayerDomain("domain_a"), Is.True);
        Assert.That(sim.PlayerTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo("domain_a"));
        Assert.That(sim.PlayerTeam.Asc.ActiveEffects, Has.Count.EqualTo(1));

        Assert.That(manager.TrySetPlayerDomain("domain_b"), Is.True);
        Assert.That(sim.PlayerTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo("domain_b"));
        Assert.That(sim.PlayerTeam.Asc.ActiveEffects, Has.Count.EqualTo(1));
    }

    [Test]
    public void Player_and_enemy_domains_do_not_conflict()
    {
        var registry = CombatTestHelper.CreateFullRegistry(gameplayEffects: new Dictionary<string, GameplayEffectDefDto>
        {
            ["d1"] = new() { Id = "d1", DurationPolicy = EDurationPolicy.Infinite, StackingPolicy = EStackingPolicy.None },
            ["d2"] = new() { Id = "d2", DurationPolicy = EDurationPolicy.Infinite, StackingPolicy = EStackingPolicy.None },
        });
        var sim = CombatSimulationTestBuilder.Minimal(new EnemyUnit("e", "slime", 10), registry);
        var manager = new TeamDomainManager(sim);

        manager.TrySetPlayerDomain("d1");
        manager.TrySetEnemyDomain("d2");

        Assert.That(sim.PlayerTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo("d1"));
        Assert.That(sim.EnemyTeam.ActiveDomain?.GameplayEffectId, Is.EqualTo("d2"));
    }

    #region P1：域 GE 的 Hooks 必须真的派发

    [Test]
    public void Domain_manager_installs_hook_dispatcher_on_team_ascs()
    {
        var sim = CombatSimulationTestBuilder.Minimal(
            new EnemyUnit("e", "slime", 10),
            CombatTestHelper.CreateFullRegistry());

        var manager = new TeamDomainManager(sim);

        Assert.That(sim.PlayerTeam.Asc.HookDispatcher, Is.SameAs(manager), "队伍 ASC 未挂 HookDispatcher 会让 hooks 静默失效");
        Assert.That(sim.EnemyTeam.Asc.HookDispatcher, Is.SameAs(manager));
    }

    [Test]
    public void Turn_start_hook_executes_declared_skill_action()
    {
        var host = new RecordingScriptHost();
        var registry = CombatTestHelper.CreateFullRegistry(
            skillActions: new Dictionary<string, SkillActionDto>
            {
                ["action.marker"] = new()
                {
                    Id = "action.marker",
                    Kind = ESkillActionKind.ExecuteScript,
                    ScriptPath = "effects/marker.js",
                    ScriptEntry = "execute",
                },
            },
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto>
            {
                ["domain_hooked"] = new()
                {
                    Id = "domain_hooked",
                    DurationPolicy = EDurationPolicy.Infinite,
                    StackingPolicy = EStackingPolicy.None,
                    Hooks = new GameplayEffectHooksDto
                    {
                        OnTurnStart = [new SkillActionRefDto { ActionId = "action.marker" }],
                    },
                },
            });
        var sim = BuildSimulation(registry, host);
        var manager = new TeamDomainManager(sim);

        Assert.That(manager.TrySetPlayerDomain("domain_hooked"), Is.True);
        manager.FireTurnStartHooks();

        Assert.That(host.Invocations, Does.Contain("effects/marker.js"), "域 GE 的 onTurnStart 钩子必须被执行");
    }

    [Test]
    public void Domain_without_hooks_does_not_invoke_script_host()
    {
        var host = new RecordingScriptHost();
        var registry = CombatTestHelper.CreateFullRegistry(
            gameplayEffects: new Dictionary<string, GameplayEffectDefDto>
            {
                ["domain_plain"] = new()
                {
                    Id = "domain_plain",
                    DurationPolicy = EDurationPolicy.Infinite,
                    StackingPolicy = EStackingPolicy.None,
                },
            });
        var sim = BuildSimulation(registry, host);
        var manager = new TeamDomainManager(sim);

        manager.TrySetPlayerDomain("domain_plain");
        manager.FireTurnStartHooks();

        Assert.That(host.Invocations, Is.Empty);
    }

    private static CombatSimulation BuildSimulation(
        GameDefinitionRegistry registry,
        IContentEffectScriptHost host)
    {
        var character = CharacterBattleInstance.CreateForTests(
            "c0",
            new CharacterAttributes(10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var player = new PlayerTeamState([character], sharedMaxHp: 10);
        var enemyTeam = new EnemyTeamState([new EnemyUnit("e", "slime", 10)]);
        return new CombatSimulation(
            player,
            enemyTeam,
            new CombatRuleEngine([]),
            registry,
            scriptHost: host,
            modId: "test.mod");
    }

    private sealed class RecordingScriptHost : IContentEffectScriptHost
    {
        public List<string> Invocations { get; } = [];

        public bool TryExecute(
            string modId,
            string scriptPath,
            string scriptEntry,
            IReadOnlyDictionary<string, object>? context,
            out IReadOnlyList<Dictionary<string, object>> proposedEffects)
        {
            Invocations.Add(scriptPath);
            proposedEffects = [];
            return false;
        }
    }

    #endregion
}