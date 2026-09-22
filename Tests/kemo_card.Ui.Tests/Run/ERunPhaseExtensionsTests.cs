using KemoCard.Mod.Run;
using NUnit.Framework;

namespace KemoCard.Ui.Tests.Run;

/// <summary>
/// 战斗阶段判定：它同时是 RunMain 的保存按钮与 ESC 系统菜单「保存并退出」的禁用依据——
/// 战斗态不在 Run 存档模型里，中途落盘会得到读不回来的档，因此这条规则必须只有一处定义。
/// </summary>
[TestFixture]
public sealed class ERunPhaseExtensionsTests
{
    [Test]
    public void Only_battle_phases_count_as_combat()
    {
        Assert.That(ERunPhase.Battle.IsCombatPhase(), Is.True);
        Assert.That(ERunPhase.BattleEnd.IsCombatPhase(), Is.True);

        ERunPhase[] savable = [ERunPhase.Init, ERunPhase.Event, ERunPhase.Reward, ERunPhase.RingEnd, ERunPhase.Finished];
        foreach (var phase in savable)
        {
            Assert.That(phase.IsCombatPhase(), Is.False, $"{phase} 不属于战斗阶段，保存入口应保持可用");
        }
    }
}
