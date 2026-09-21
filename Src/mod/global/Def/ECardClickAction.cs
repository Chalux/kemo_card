namespace KemoCard.Mod.Global.Def;

public enum ECardClickAction
{
    None = 0,

    /// <summary>单击打开卡牌详情（默认）。</summary>
    OpenDetails = 1,

    /// <summary>单击只触发 <c>BaseCardItem.CardClicked</c>，由宿主界面决定行为（队伍编辑的加/移卡用）。</summary>
    Emit = 2,
}
