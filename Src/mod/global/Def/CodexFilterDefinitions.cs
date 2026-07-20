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

	public static string GetCharFieldLocaleKey(ECharFilterField field) => field switch
	{
		ECharFilterField.Element => "UI_CODEX_FILTER_ELEMENT",
		ECharFilterField.Role => "UI_CODEX_FILTER_ROLE",
		ECharFilterField.Race => "UI_CODEX_FILTER_RACE",
		ECharFilterField.Tag => "UI_CODEX_FILTER_TAG",
		_ => field.ToString(),
	};

	public static bool TryGetRaceLocaleKey(ERace race, out string key)
	{
		key = race switch
		{
			ERace.Human => "UI_RACE_HUMAN",
			ERace.Canine => "UI_RACE_CANINE",
			ERace.Feline => "UI_RACE_FELINE",
			ERace.Bird => "UI_RACE_BIRD",
			ERace.Insect => "UI_RACE_INSECT",
			ERace.Beast => "UI_RACE_BEAST",
			ERace.Fish => "UI_RACE_FISH",
			ERace.Reptile => "UI_RACE_REPTILE",
			ERace.Plant => "UI_RACE_PLANT",
			ERace.Machine => "UI_RACE_MACHINE",
			ERace.Demonic => "UI_RACE_DEMONIC",
			ERace.Angel => "UI_RACE_ANGEL",
			ERace.Dragon => "UI_RACE_DRAGON",
			ERace.God => "UI_RACE_GOD",
			ERace.Devil => "UI_RACE_DEVIL",
			ERace.Undead => "UI_RACE_UNDEAD",
			ERace.UnKnown => "UI_RACE_UNKNOWN",
			_ => "",
		};
		return key.Length > 0;
	}
}
