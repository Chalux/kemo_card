using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Ui;

namespace KemoCard.Mod.Global.Def;

public static class CodexFilterDefinitions
{
	public static string GetFieldLocaleKey(ECardFilterField field) => field switch
	{
		ECardFilterField.CardType => "UI_CODEX_FILTER_CARD_TYPE",
		ECardFilterField.Cost => "UI_CODEX_FILTER_COST",
		ECardFilterField.Element => "UI_CODEX_FILTER_ELEMENT",
		ECardFilterField.Role => "UI_CODEX_FILTER_ROLE",
		ECardFilterField.CostType => "UI_CODEX_FILTER_COST_TYPE",
		ECardFilterField.Tag => "UI_CODEX_FILTER_TAG",
		_ => field.ToString(),
	};

	public static string GetOpLocaleKey(ECardFilterOp op) => op switch
	{
		ECardFilterOp.Equal => "UI_CODEX_OP_EQ",
		ECardFilterOp.NotEqual => "UI_CODEX_OP_NE",
		ECardFilterOp.LessOrEqual => "UI_CODEX_OP_LE",
		ECardFilterOp.GreaterOrEqual => "UI_CODEX_OP_GE",
		ECardFilterOp.Contains => "UI_CODEX_OP_CONTAINS",
		ECardFilterOp.Exact => "UI_CODEX_OP_EXACT",
		_ => op.ToString(),
	};

	public static bool TryGetElementLocaleKey(EElement element, out string key)
	{
		key = element switch
		{
			EElement.Red => "UI_ELEMENT_RED",
			EElement.Blue => "UI_ELEMENT_BLUE",
			EElement.Green => "UI_ELEMENT_GREEN",
			EElement.Yellow => "UI_ELEMENT_YELLOW",
			EElement.Yin => "UI_ELEMENT_YIN",
			EElement.Yang => "UI_ELEMENT_YANG",
			_ => "",
		};
		return key.Length > 0;
	}

	public static bool TryGetCostTypeLocaleKey(ECostType costType, out string key)
	{
		key = costType switch
		{
			ECostType.None => "UI_COST_TYPE_NONE",
			ECostType.Energy => "UI_COST_TYPE_ENERGY",
			ECostType.Health => "UI_COST_TYPE_HEALTH",
			ECostType.Gold => "UI_COST_TYPE_GOLD",
			ECostType.Discard => "UI_COST_TYPE_DISCARD",
			ECostType.X => "UI_COST_TYPE_X",
			_ => "",
		};
		return key.Length > 0;
	}

	public static bool TryGetRoleLocaleKey(ERole role, out string key)
	{
		key = role switch
		{
			ERole.None => "UI_ROLE_NONE",
			ERole.Warrior => "UI_ROLE_WARRIOR",
			ERole.Wizard => "UI_ROLE_WIZARD",
			ERole.Healer => "UI_ROLE_HEALER",
			ERole.Guard => "UI_ROLE_GUARD",
			ERole.Shield => "UI_ROLE_SHIELD",
			ERole.Controller => "UI_ROLE_CONTROLLER",
			ERole.Support => "UI_ROLE_SUPPORT",
			ERole.CardPlayer => "UI_ROLE_CARD_PLAYER",
			ERole.SwordMan => "UI_ROLE_SWORD_MAN",
			ERole.Mage => "UI_ROLE_MAGE",
			ERole.Alchemist => "UI_ROLE_ALCHEMIST",
			ERole.Elementist => "UI_ROLE_ELEMENTIST",
			_ => "",
		};
		return key.Length > 0;
	}
}
