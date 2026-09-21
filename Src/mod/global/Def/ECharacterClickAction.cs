namespace KemoCard.Mod.Global.Def;

public enum ECharacterClickAction
{
    None = 0,

    /// <summary>单击打开角色详情（默认）。</summary>
    OpenDetails = 1,

    /// <summary>单击只触发 <c>BaseCharacterItem.CharacterClicked</c>，由宿主界面决定行为（队伍编辑用）。</summary>
    Emit = 2,
}
