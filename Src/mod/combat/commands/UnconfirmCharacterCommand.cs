namespace KemoCard.Mod.Combat.Commands;

/// <summary>显式取消确认：仅解锁该角色的标记编辑，不清除已标记项（规格 §2.2）。</summary>
public sealed record UnconfirmCharacterCommand(int CharacterIndex) : ICombatCommand;