using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Potential;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

/// <summary>
/// 团体潜能（2026-09-26 起的**进度值**模型）：槽位已分配 ≥ 门槛自动解锁、扣回低于门槛即重锁；
/// 分配 = 团队池 → 槽位（投票模式下需表决），扣除 = 槽位 → 团队池（无需表决）；
/// 重复角色奖励入账、Run 存档迁移与坏档容错。
/// </summary>
[TestFixture]
public sealed class PotentialServiceTests
{
    private static CharacterInstance NewCharacter(string definitionId = "chalux") =>
        new(new CharacterDto { Id = definitionId }, Guid.NewGuid().ToString("N"));

    private static PassiveRefDto Passive(string buffId, int cost) => new()
    {
        BuffId = buffId,
        RequiredPotential = cost,
    };

    private static PotentialService NewService(
        RunMod? model = null,
        PotentialPolicySettings? policy = null,
        IPotentialProposalApprover? approver = null)
    {
        var settings = policy ?? PotentialPolicySettings.Default;
        return new PotentialService(model ?? new RunMod(), () => settings, approver);
    }

    /// <summary>把角色上阵到指定槽位（解锁判定按「该槽位已分配潜能」）。</summary>
    private static void Deploy(RunMod model, int slotIndex, CharacterInstance character) =>
        model.PlayerStates[slotIndex].SetActiveCharacter(character);

    #region 自动解锁（进度值）

    [Test]
    public void Zero_cost_passive_is_unlocked_by_default()
    {
        var model = new RunMod();
        var character = NewCharacter();

        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p1", 0)), Is.True);
        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p2", 10)), Is.False);
    }

    [Test]
    public void Passives_unlock_automatically_when_slot_allocation_reaches_threshold()
    {
        var model = new RunMod();
        var character = NewCharacter();
        Deploy(model, 1, character);

        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p2", 10)), Is.False);

        model.PlayerStates[1].AllocatePotential(10);
        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p2", 10)), Is.True, "达到门槛自动解锁");
        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p3", 30)), Is.False, "更高门槛仍未解锁");
    }

    [Test]
    public void Deducting_below_threshold_relocks_the_passive()
    {
        var model = new RunMod();
        var character = NewCharacter();
        Deploy(model, 0, character);
        model.PlayerStates[0].AllocatePotential(30);
        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p3", 30)), Is.True);

        var result = NewService(model).TryDeduct(0, 25);

        Assert.That(result.Success, Is.True);
        Assert.That(model.PlayerStates[0].AllocatedPotential, Is.EqualTo(5));
        Assert.That(model.TeamPotentialPool, Is.EqualTo(25), "扣除退回团队池");
        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p3", 30)), Is.False, "30 门槛重锁");
        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p2", 10)), Is.False, "10 门槛也重锁");
    }

    [Test]
    public void Unassigned_character_only_unlocks_zero_cost_passives()
    {
        var model = new RunMod();
        var character = NewCharacter();
        model.PlayerStates[0].AllocatePotential(50);

        Assert.That(
            PotentialService.IsPassiveUnlocked(model, character, Passive("p2", 10)),
            Is.False,
            "未上阵没有槽位（视为 0）");
    }

    [Test]
    public void Allocation_belongs_to_the_slot_not_the_character_instance()
    {
        var model = new RunMod();
        var a = NewCharacter("a");
        var b = NewCharacter("b");
        Deploy(model, 2, a);
        model.PlayerStates[2].AllocatePotential(30);
        Assert.That(PotentialService.IsPassiveUnlocked(model, a, Passive("p2", 30)), Is.True);

        // 换人：进度留在槽位（槽位账本语义），新角色按同一进度解锁。
        Deploy(model, 2, b);
        Assert.That(PotentialService.IsPassiveUnlocked(model, b, Passive("p2", 30)), Is.True);
        Assert.That(PotentialService.IsPassiveUnlocked(model, a, Passive("p2", 30)), Is.False, "离开槽位即失去进度");
    }

    [Test]
    public void FindAssignedSlot_reports_the_deployed_slot()
    {
        var model = new RunMod();
        var character = NewCharacter();
        Assert.That(PotentialService.FindAssignedSlot(model, character), Is.Null);

        Deploy(model, 3, character);
        Assert.That(PotentialService.FindAssignedSlot(model, character), Is.EqualTo(3));
    }

    #endregion

    #region 分配 / 扣除

    [Test]
    public void Allocate_moves_pool_to_slot()
    {
        var model = new RunMod();
        model.TeamPotentialPool = 30;
        var service = NewService(model);

        var result = service.TryAllocate(1, 20);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Amount, Is.EqualTo(20));
        Assert.That(model.TeamPotentialPool, Is.EqualTo(10));
        Assert.That(model.PlayerStates[1].AllocatedPotential, Is.EqualTo(20));
        Assert.That(service.AllocatedFor(1), Is.EqualTo(20));
    }

    [Test]
    public void Allocate_fails_when_pool_is_short_and_does_not_move_anything()
    {
        var model = new RunMod();
        model.TeamPotentialPool = 9;
        var service = NewService(model);

        var result = service.TryAllocate(0, 10);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Failure, Is.EqualTo(EPotentialFailure.PoolShort));
        Assert.That(model.TeamPotentialPool, Is.EqualTo(9));
        Assert.That(model.PlayerStates[0].AllocatedPotential, Is.EqualTo(0));
    }

    [Test]
    public void Allocate_rejects_invalid_slot_and_amount()
    {
        var service = NewService();

        Assert.That(service.TryAllocate(-1, 10).Failure, Is.EqualTo(EPotentialFailure.InvalidSlot));
        Assert.That(service.TryAllocate(RunConstants.SlotCount, 10).Failure, Is.EqualTo(EPotentialFailure.InvalidSlot));
        Assert.That(service.TryAllocate(0, 0).Failure, Is.EqualTo(EPotentialFailure.InvalidAmount));
        Assert.That(service.TryAllocate(0, -5).Failure, Is.EqualTo(EPotentialFailure.InvalidAmount));
    }

    [Test]
    public void Deduct_fails_when_slot_allocation_is_short()
    {
        var model = new RunMod();
        model.PlayerStates[0].AllocatePotential(6);
        var service = NewService(model);

        var result = service.TryDeduct(0, 10);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Failure, Is.EqualTo(EPotentialFailure.SlotShort));
        Assert.That(model.PlayerStates[0].AllocatedPotential, Is.EqualTo(6));
        Assert.That(model.TeamPotentialPool, Is.EqualTo(0));
    }

    [Test]
    public void Deduct_rejects_invalid_slot_and_amount()
    {
        var service = NewService();

        Assert.That(service.TryDeduct(-1, 10).Failure, Is.EqualTo(EPotentialFailure.InvalidSlot));
        Assert.That(service.TryDeduct(0, 0).Failure, Is.EqualTo(EPotentialFailure.InvalidAmount));
    }

    [Test]
    public void AllocatedFor_is_zero_for_out_of_range_slot()
    {
        var service = NewService();

        Assert.That(service.AllocatedFor(-1), Is.Zero);
        Assert.That(service.AllocatedFor(RunConstants.SlotCount), Is.Zero);
    }

    #endregion

    #region 表决策略

    private sealed class RejectingApprover : IPotentialProposalApprover
    {
        public int Requests { get; private set; }

        public bool RequestApproval(int slotIndex, string description)
        {
            Requests++;
            return false;
        }
    }

    [Test]
    public void Vote_mode_requires_approval_for_allocation_but_not_for_deduction()
    {
        var model = new RunMod();
        model.TeamPotentialPool = 30;
        var approver = new RejectingApprover();
        var service = NewService(model, new PotentialPolicySettings(RequireVote: true, 2, false), approver);

        var rejected = service.TryAllocate(0, 10);
        Assert.That(rejected.Success, Is.False);
        Assert.That(rejected.Failure, Is.EqualTo(EPotentialFailure.VoteRejected));
        Assert.That(approver.Requests, Is.EqualTo(1));
        Assert.That(model.TeamPotentialPool, Is.EqualTo(30), "表决被拒不动账");
        Assert.That(model.PlayerStates[0].AllocatedPotential, Is.EqualTo(0));

        // 扣除（退回团队池）不需要表决：先手动摆好进度再扣（手动摆进度不动池）。
        model.PlayerStates[0].AllocatePotential(10);
        var deducted = service.TryDeduct(0, 10);

        Assert.That(deducted.Success, Is.True);
        Assert.That(approver.Requests, Is.EqualTo(1), "扣除不发起表决");
        Assert.That(model.TeamPotentialPool, Is.EqualTo(40), "扣除的 10 点回到团队池");
    }

    [Test]
    public void Vote_mode_limits_proposals_per_ring()
    {
        var model = new RunMod();
        model.TeamPotentialPool = 100;
        var approver = new RejectingApprover();
        var service = NewService(model, new PotentialPolicySettings(RequireVote: true, 1, false), approver);

        // 第 1 次提议（被拒，但次数已消耗）。
        service.TryAllocate(0, 10);
        // 第 2 次提议：超过每环 1 次上限，直接拒绝。
        var second = service.TryAllocate(0, 10);

        Assert.That(second.Success, Is.False);
        Assert.That(second.Failure, Is.EqualTo(EPotentialFailure.VoteRejected));
        Assert.That(approver.Requests, Is.EqualTo(1), "限额后不再发起表决");

        // 换环重置后可再次提议。
        service.ResetRingProposalCounters();
        Assert.That(service.ProposalsUsedThisRing(0), Is.EqualTo(0));
    }

    [Test]
    public void Vote_mode_unlimited_proposals_bypasses_the_cap()
    {
        var model = new RunMod();
        model.TeamPotentialPool = 100;
        var approver = new RejectingApprover();
        var service = NewService(model, new PotentialPolicySettings(true, 2, UnlimitedProposals: true), approver);

        for (var i = 0; i < 5; i++)
        {
            service.TryAllocate(0, 10);
        }

        Assert.That(approver.Requests, Is.EqualTo(5), "无限制勾选生效，次数不封顶");
    }

    #endregion

    #region 奖励入账与存档

    [Test]
    public void Duplicate_reward_goes_to_slot_allocation_or_team_pool()
    {
        var model = new RunMod();
        var service = NewService(model);

        service.GrantDuplicateReward();
        Assert.That(model.TeamPotentialPool, Is.EqualTo(20));

        service.GrantDuplicateReward(slotIndex: 2);
        Assert.That(model.TeamPotentialPool, Is.EqualTo(20), "直接分配到槽位不动池");
        Assert.That(model.PlayerStates[2].AllocatedPotential, Is.EqualTo(20));
    }

    /// <summary>调试/奖励管线的任意数额入账：按申请数额精确入账（不再按 20 取整）。</summary>
    [Test]
    public void Grant_supports_arbitrary_amounts()
    {
        var model = new RunMod();
        var service = NewService(model);

        service.Grant(5);
        Assert.That(model.TeamPotentialPool, Is.EqualTo(5));

        service.Grant(7, slotIndex: 1);
        Assert.That(model.TeamPotentialPool, Is.EqualTo(5));
        Assert.That(model.PlayerStates[1].AllocatedPotential, Is.EqualTo(7));

        service.Grant(0);
        service.Grant(-3);
        Assert.That(model.TeamPotentialPool, Is.EqualTo(5), "非正数额是软失败");
    }

    [Test]
    public void Potential_round_trips_through_dto()
    {
        var model = new RunMod();
        model.TeamPotentialPool = 7;
        model.PlayerStates[3].AllocatePotential(11);

        var dto = model.ToDto();
        var restored = new RunMod();
        restored.RestoreFrom(dto);

        Assert.That(restored.TeamPotentialPool, Is.EqualTo(7));
        Assert.That(restored.PlayerStates[3].AllocatedPotential, Is.EqualTo(11));
    }

    /// <summary>
    /// 老档（消费流水模型）仍能反序列化并恢复：流水字段保留但**不参与**解锁判定，
    /// 只有 `PotentialDirectCredit`（现为「已分配潜能」）决定门槛。
    /// </summary>
    [Test]
    public void Legacy_spend_ledger_is_tolerated_but_unused()
    {
        var json = """
        {
          "runId": "r1",
          "storyId": "s1",
          "schemaVersion": 2,
          "playerStates": [
            {
              "gold": 5,
              "potentialDirectCredit": 0,
              "potentialSpent": [
                { "entryId": "e1", "source": "pool", "amount": 10, "characterInstanceId": "inst-1", "buffId": "p2" }
              ]
            }
          ]
        }
        """;

        var dto = System.Text.Json.JsonSerializer.Deserialize<RunDto>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var normalized = dto.Normalize();

        Assert.That(normalized.PlayerStates[0].PotentialSpent, Has.Count.EqualTo(1), "老档流水能被读入");

        var model = new RunMod();
        model.RestoreFrom(normalized);
        var character = new CharacterInstance(new CharacterDto { Id = "chalux" }, "inst-1");
        Deploy(model, 0, character);

        Assert.That(model.PlayerStates[0].AllocatedPotential, Is.EqualTo(0));
        Assert.That(
            PotentialService.IsPassiveUnlocked(model, character, Passive("p2", 10)),
            Is.False,
            "流水不再解锁被动；要解锁得把潜能分配回槽位");
    }

    [Test]
    public void V1_save_migrates_to_v2_with_defaults()
    {
        var v1Json = """
        {
          "runId": "r1",
          "storyId": "s1",
          "schemaVersion": 1,
          "currentRing": 2,
          "phase": 3,
          "playerStates": [
            { "activeCharacterIndex": null, "gold": 5 }
          ]
        }
        """;

        var dto = System.Text.Json.JsonSerializer.Deserialize<RunDto>(v1Json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(dto, Is.Not.Null);
        var normalized = dto!.Normalize();
        Assert.That(normalized.SchemaVersion, Is.EqualTo(RunDto.CurrentSchemaVersion));
        Assert.That(normalized.TeamPotentialPool, Is.EqualTo(0));
        Assert.That(normalized.PlayerStates[0].PotentialSpent, Is.Empty);
        Assert.That(normalized.PlayerStates[0].PotentialDirectCredit, Is.EqualTo(0));
    }

    #endregion

    #region 坏档容错

    /// <summary>
    /// 显式 null 的集合（手写 / 外部工具改写的存档）不能让 Normalize 抛异常：
    /// 它在 <c>RunSaveService.TryRead</c> 的 try 之外执行，一旦抛出，坏档既不会被归档，
    /// 异常还会直接冒到上层。Normalize 负责把 null 归一成空集。
    /// </summary>
    [Test]
    public void Normalize_tolerates_explicit_null_collections()
    {
        var json = """
        {
          "runId": "r1",
          "storyId": "s1",
          "schemaVersion": 1,
          "playerStates": null,
          "cardCollection": null,
          "battleHistory": null
        }
        """;

        var dto = System.Text.Json.JsonSerializer.Deserialize<RunDto>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(dto, Is.Not.Null);
        Assert.That(dto!.PlayerStates, Is.Null, "前提：显式 null 会被反序列化成 null");

        var normalized = dto.Normalize();

        Assert.That(normalized.PlayerStates, Is.Empty);
        Assert.That(normalized.CardCollection, Is.Empty);
        Assert.That(normalized.BattleHistory, Is.Empty);
        Assert.That(normalized.SchemaVersion, Is.EqualTo(RunDto.CurrentSchemaVersion));
    }

    /// <summary>数组里的 null 元素同样要被归一成默认状态，而不是让 Normalize 抛 NRE。</summary>
    [Test]
    public void Normalize_tolerates_null_player_state_entries()
    {
        var json = """
        {
          "runId": "r1",
          "storyId": "s1",
          "schemaVersion": 2,
          "playerStates": [ null, { "gold": 3 } ]
        }
        """;

        var dto = System.Text.Json.JsonSerializer.Deserialize<RunDto>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var normalized = dto.Normalize();

        Assert.That(normalized.PlayerStates, Has.Count.EqualTo(2));
        Assert.That(normalized.PlayerStates[0].PotentialSpent, Is.Empty);
        Assert.That(normalized.PlayerStates[1].Gold, Is.EqualTo(3));
    }

    #endregion
}
