using KemoCard.Frame.Condition;
using KemoCard.Frame.Content;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Combat.Condition;
using KemoCard.Mod.Global.Condition;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Combat;

/// <summary>
/// 针对 **出货内容** 的冒烟测试:其余用例都用合成 DTO,真实的
/// <c>Config/mods/base-game</c> 内容此前没有任何覆盖,主动链配置写错不会被发现。
/// </summary>
[TestFixture]
public sealed class BaseGameActiveSkillChainContentTests
{
    private static ModDefinitionsBundle LoadBaseGame() => BaseGameContent.Load();

    [Test]
    public void Every_shipped_character_has_a_valid_active_skill_chain()
    {
        var definitions = LoadBaseGame();

        Assert.That(definitions.Characters, Is.Not.Empty, "base-game 至少应有一个角色");
        foreach (var (characterId, character) in definitions.Characters)
        {
            Assert.That(
                CombatContentValidator.TryValidateActiveSkillChain(character, out var error),
                Is.True,
                $"角色 {characterId} 的 activeSkillChain 非法:{error}");
        }
    }

    [Test]
    public void Every_active_skill_chain_tier_resolves_to_a_shipped_skill()
    {
        var definitions = LoadBaseGame();

        foreach (var (characterId, character) in definitions.Characters)
        {
            foreach (var tier in character.ActiveSkillChain)
            {
                Assert.That(
                    definitions.Skills.ContainsKey(tier.SkillId),
                    Is.True,
                    $"角色 {characterId} 的档位技能 {tier.SkillId} 在内容中不存在");
            }
        }
    }

    /// <summary>
    /// 档位技能若缺 <c>targetOverride</c>,按 §5.4 决议会被当成 Self 单体,
    /// 伤害类主动技会打到施法者自己身上（迁移主动技链时踩过的坑）。
    /// </summary>
    [Test]
    public void Every_active_skill_chain_tier_declares_an_explicit_target_override()
    {
        var definitions = LoadBaseGame();

        foreach (var (characterId, character) in definitions.Characters)
        {
            foreach (var tier in character.ActiveSkillChain)
            {
                Assert.That(definitions.Skills.TryGetValue(tier.SkillId, out var skill), Is.True);
                Assert.That(
                    skill!.TargetOverride,
                    Is.Not.Null,
                    $"角色 {characterId} 的档位技能 {tier.SkillId} 未声明 targetOverride");
            }
        }
    }

    /// <summary>
    /// 出货角色的档位顺序与冷却下限：cooldown 必须 ≥ 1，否则累计阈值不单调递增
    /// （要么永远放不出主动技，要么每回合都满档）。
    /// </summary>
    [Test]
    public void Chalux_chain_tiers_declare_positive_cooldowns()
    {
        var definitions = LoadBaseGame();

        Assert.That(definitions.Characters.TryGetValue("chalux", out var chalux), Is.True);
        Assert.That(chalux!.ActiveSkillChain, Is.Not.Empty, "chalux 至少应有一档主动技");
        Assert.That(chalux.ActiveSkillChain[0].SkillId, Is.EqualTo("chalux_glacial_overflow"));
        Assert.That(
            chalux.ActiveSkillChain.Select(tier => tier.Cooldown),
            Is.All.GreaterThanOrEqualTo(1),
            "每档 cooldown 必须 ≥ 1，否则累计阈值不单调递增");
    }

    /// <summary>
    /// 潜能被动统一为每个角色 <b>4 条、档位 0 / 10 / 30 / 50</b>（2026-09-24：原 6 档的
    /// <c>70</c> 与 <c>99</c> 两档连同其载荷一并删除，不做合并）。新增角色或调整档位时这里会立刻报错。
    /// </summary>
    [Test]
    public void Every_shipped_character_has_four_potential_passives()
    {
        var definitions = LoadBaseGame();
        Assert.That(definitions.Characters, Is.Not.Empty);

        foreach (var (characterId, character) in definitions.Characters)
        {
            Assert.That(
                character.Passives,
                Has.Count.EqualTo(4),
                $"角色 {characterId} 的潜能被动应为 4 条");

            Assert.That(
                character.Passives.Select(passive => passive.RequiredPotential),
                Is.EqualTo(new[] { 0, 10, 30, 50 }),
                $"角色 {characterId} 的档位必须是 0 / 10 / 30 / 50");

            foreach (var passive in character.Passives)
                Assert.That(definitions.Buffs.ContainsKey(passive.BuffId), Is.True, $"被动 {passive.BuffId} 不存在");
        }
    }

    /// <summary>
    /// 出货内容必须整体通过内容校验,不允许任何定义被剔除。
    /// 新增校验规则（例如链式引用环检测）若对真实内容误报,会在这里立刻暴露。
    /// </summary>
    [Test]
    public void Shipped_content_passes_validation_without_removals()
    {
        RegisterBuiltinConditions();

        var definitions = LoadBaseGame();
        var registry = new GameDefinitionRegistry();

        registry.Rebuild([new ModContentBundle("base.game", definitions)], out var report);

        Assert.That(
            report.RemovedValidationErrors,
            Is.Empty,
            "出货内容不应有任何定义被校验剔除:"
                + string.Join(
                    "; ",
                    report.RemovedValidationErrors.Select(e => $"{e.Category}/{e.DefinitionId}: {e.Message}")));
    }

    /// <summary>
    /// 出货内容里的技能定义（含 ChainActions）必须全部保留，防止环检测误伤真实链路。
    /// </summary>
    [Test]
    public void Shipped_content_keeps_all_skill_actions()
    {
        RegisterBuiltinConditions();

        var definitions = LoadBaseGame();
        var registry = new GameDefinitionRegistry();

        registry.Rebuild([new ModContentBundle("base.game", definitions)], out _);

        foreach (var actionId in definitions.SkillActions.Keys)
        {
            Assert.That(
                registry.Store.SkillActions.ContainsKey(actionId),
                Is.True,
                $"技能动作 {actionId} 被校验剔除");
        }
    }

    /// <summary>
    /// 复刻 <c>ModFactory.Bootstrap</c> 的注册顺序：条件域必须在内容 Rebuild 之前填好，
    /// 否则 <c>Story.unlock</c> 的 CondType 校验会因注册表为空而误报未知名。
    /// </summary>
    private static void RegisterBuiltinConditions() => BaseGameContent.RegisterBuiltinConditions();
}