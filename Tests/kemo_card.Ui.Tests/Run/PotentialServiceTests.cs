using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Combat;
using KemoCard.Mod.Run;
using KemoCard.Mod.Run.Potential;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

/// <summary>
/// 团体潜能：池/直充双来源消费、整笔（跨来源原子）返还与重锁、表决模式的提议限额、
/// 重复角色奖励入账、Run 存档 v1→v2 迁移。
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

    #region 解锁与消费

    [Test]
    public void Zero_cost_passive_is_unlocked_by_default()
    {
        var model = new RunMod();
        var character = NewCharacter();

        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p1", 0)), Is.True);
        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p2", 10)), Is.False);
    }

    [Test]
    public void Unlock_consumes_direct_credit_before_team_pool()
    {
        var model = new RunMod();
        var character = NewCharacter();
        model.PlayerStates[0].AddPotentialDirectCredit(6);
        model.TeamPotentialPool = 10;
        var service = NewService(model);

        var result = service.TryUnlock(0, character, Passive("p2", 10));

        Assert.That(result.Success, Is.True);
        Assert.That(model.PlayerStates[0].PotentialDirectCredit, Is.EqualTo(0), "先扣直充");
        Assert.That(model.TeamPotentialPool, Is.EqualTo(6), "再扣团队池");
        var entries = model.PlayerStates[0].PotentialSpent;
        Assert.That(entries.Count, Is.EqualTo(2), "跨来源拆两笔记账");
        Assert.That(entries.Sum(entry => entry.Amount), Is.EqualTo(10));
        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p2", 10)), Is.True);
    }

    [Test]
    public void Unlock_fails_when_funds_insufficient()
    {
        var model = new RunMod();
        var character = NewCharacter();
        model.TeamPotentialPool = 9;
        var service = NewService(model);

        var result = service.TryUnlock(0, character, Passive("p2", 10));

        Assert.That(result.Success, Is.False);
        Assert.That(model.TeamPotentialPool, Is.EqualTo(9), "失败不动账");
        Assert.That(model.PlayerStates[0].PotentialSpent, Is.Empty);
    }

    [Test]
    public void Refund_returns_amount_to_original_source_and_relocks()
    {
        var model = new RunMod();
        var character = NewCharacter();
        model.TeamPotentialPool = 30;
        var service = NewService(model);
        service.TryUnlock(0, character, Passive("p2", 30));
        Assert.That(model.TeamPotentialPool, Is.EqualTo(0));

        var entry = model.PlayerStates[0].PotentialSpent.Single();
        var refunded = service.Refund(0, entry.EntryId);

        Assert.That(refunded, Is.EqualTo(30), "返回实际返还总额");
        Assert.That(model.TeamPotentialPool, Is.EqualTo(30), "返还退回团队池");
        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p2", 30)), Is.False, "被动重新锁定");
    }

    /// <summary>
    /// 部分返还漏洞（2026-09-19 评审）：解锁按「存在匹配流水」判定，跨来源拆两笔的消费
    /// 只退池那笔时解锁仍在。返还必须按 (角色实例, 被动) 整组原子退回。
    /// </summary>
    [Test]
    public void Refund_is_atomic_for_split_purchase()
    {
        var model = new RunMod();
        var character = NewCharacter();
        model.PlayerStates[0].AddPotentialDirectCredit(6);
        model.TeamPotentialPool = 10;
        var service = NewService(model);
        Assert.That(service.TryUnlock(0, character, Passive("p2", 10)).Success, Is.True);
        var entries = model.PlayerStates[0].PotentialSpent;
        Assert.That(entries.Count, Is.EqualTo(2), "跨来源拆两笔");

        // 只点名池那笔，也必须整组退回。
        var poolEntry = entries.Single(entry => entry.Source != "credit");
        var refunded = service.Refund(0, poolEntry.EntryId);

        Assert.That(refunded, Is.EqualTo(10), "整笔 10 全额退回");
        Assert.That(model.PlayerStates[0].PotentialDirectCredit, Is.EqualTo(6), "直充部分回到槽位");
        Assert.That(model.TeamPotentialPool, Is.EqualTo(10), "池部分回到团队池");
        Assert.That(model.PlayerStates[0].PotentialSpent, Is.Empty, "该笔消费流水全部清除");
        Assert.That(PotentialService.IsPassiveUnlocked(model, character, Passive("p2", 10)), Is.False, "解锁随之撤销");
    }

    [Test]
    public void Refund_direct_credit_entry_restores_slot_credit()
    {
        var model = new RunMod();
        var character = NewCharacter();
        model.PlayerStates[1].AddPotentialDirectCredit(20);
        var service = NewService(model);
        Assert.That(service.TryUnlock(1, character, Passive("p2", 20)).Success, Is.True);
        Assert.That(model.PlayerStates[1].PotentialDirectCredit, Is.EqualTo(0));

        var entry = model.PlayerStates[1].PotentialSpent.Single();
        Assert.That(service.Refund(1, entry.EntryId), Is.EqualTo(20));
        Assert.That(model.PlayerStates[1].PotentialDirectCredit, Is.EqualTo(20), "直充来源返还到槽位直充");
    }

    [Test]
    public void Duplicate_unlock_is_idempotent_no_extra_charge()
    {
        var model = new RunMod();
        var character = NewCharacter();
        model.TeamPotentialPool = 30;
        var service = NewService(model);

        Assert.That(service.TryUnlock(0, character, Passive("p2", 10)).Success, Is.True);
        Assert.That(service.TryUnlock(0, character, Passive("p2", 10)).Success, Is.True, "重复解锁视为成功");

        Assert.That(model.TeamPotentialPool, Is.EqualTo(20), "只扣一次");
        Assert.That(model.PlayerStates[0].PotentialSpent.Count, Is.EqualTo(1));
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
    public void Vote_mode_requires_approval_for_pool_spend_but_not_credit_spend()
    {
        var model = new RunMod();
        var character = NewCharacter();
        model.PlayerStates[0].AddPotentialDirectCredit(10);
        model.TeamPotentialPool = 10;
        var approver = new RejectingApprover();
        var service = NewService(model, new PotentialPolicySettings(RequireVote: true, 2, false), approver);

        // 纯直充消费：无需表决。
        Assert.That(service.TryUnlock(0, character, Passive("p.credit", 10)).Success, Is.True);
        Assert.That(approver.Requests, Is.EqualTo(0));

        // 涉及团队池：表决被拒 → 失败且扣款被回滚（未入账）。
        Assert.That(service.TryUnlock(0, character, Passive("p.pool", 10)).Success, Is.False);
        Assert.That(approver.Requests, Is.EqualTo(1));
        Assert.That(model.TeamPotentialPool, Is.EqualTo(10));
        Assert.That(model.PlayerStates[0].PotentialSpent.Count, Is.EqualTo(1), "只有直充那笔记账");
    }

    [Test]
    public void Vote_mode_limits_proposals_per_ring()
    {
        var model = new RunMod();
        var character = NewCharacter();
        var characterB = NewCharacter("other");
        model.TeamPotentialPool = 100;
        var approver = new RejectingApprover();
        var service = NewService(model, new PotentialPolicySettings(RequireVote: true, 1, false), approver);

        // 第 1 次提议（被拒，但次数已消耗）。
        service.TryUnlock(0, character, Passive("p.a", 10));
        // 第 2 次提议：超过每环 1 次上限，直接拒绝。
        var second = service.TryUnlock(0, characterB, Passive("p.b", 10));

        Assert.That(second.Success, Is.False);
        Assert.That(approver.Requests, Is.EqualTo(1), "限额后不再发起表决");

        // 换环重置后可再次提议。
        service.ResetRingProposalCounters();
        Assert.That(service.ProposalsUsedThisRing(0), Is.EqualTo(0));
    }

    [Test]
    public void Vote_mode_unlimited_proposals_bypasses_the_cap()
    {
        var model = new RunMod();
        var character = NewCharacter();
        model.TeamPotentialPool = 100;
        var approver = new RejectingApprover();
        var service = NewService(model, new PotentialPolicySettings(true, 2, UnlimitedProposals: true), approver);

        for (var i = 0; i < 5; i++)
        {
            service.TryUnlock(0, character, Passive($"p.{i}", 10));
        }

        Assert.That(approver.Requests, Is.EqualTo(5), "无限制勾选生效，次数不封顶");
    }

    #endregion

    #region 奖励入账与存档

    [Test]
    public void Duplicate_reward_goes_to_slot_credit_or_team_pool()
    {
        var model = new RunMod();
        var service = NewService(model);

        service.GrantDuplicateReward();
        Assert.That(model.TeamPotentialPool, Is.EqualTo(20));

        service.GrantDuplicateReward(slotIndex: 2);
        Assert.That(model.TeamPotentialPool, Is.EqualTo(20), "槽位直充不动池");
        Assert.That(model.PlayerStates[2].PotentialDirectCredit, Is.EqualTo(20));
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
        Assert.That(model.PlayerStates[1].PotentialDirectCredit, Is.EqualTo(7));

        service.Grant(0);
        service.Grant(-3);
        Assert.That(model.TeamPotentialPool, Is.EqualTo(5), "非正数额是软失败");
    }

    [Test]
    public void Potential_ledger_round_trips_through_dto()
    {
        var model = new RunMod();
        var character = NewCharacter();
        model.TeamPotentialPool = 7;
        model.PlayerStates[3].AddPotentialDirectCredit(11);
        var service = NewService(model);
        Assert.That(service.TryUnlock(3, character, Passive("p2", 15)).Success, Is.True);

        var dto = model.ToDto();
        var restored = new RunMod();
        restored.RestoreFrom(dto);

        // 消费 15 = 直充 11 + 池 4，因此池剩 7 - 4 = 3。
        Assert.That(restored.TeamPotentialPool, Is.EqualTo(3));
        Assert.That(restored.PlayerStates[3].PotentialDirectCredit, Is.EqualTo(0));
        Assert.That(restored.PlayerStates[3].PotentialSpent.Count, Is.EqualTo(2));
        Assert.That(restored.PlayerStates[3].PotentialSpent.Sum(entry => entry.Amount), Is.EqualTo(15));
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
