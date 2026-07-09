using Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Mod.Global.Def;
using NUnit.Framework;

namespace KemoCard.Ui.Tests;

[TestFixture]
public sealed class CardUiDefinitionsTests
{
	[Test]
	public void CardTypeLocaleKeys_cover_all_card_types()
	{
		foreach (ECardType type in Enum.GetValues<ECardType>())
		{
			Assert.That(CardUiDefinitions.TryGetCardTypeLocaleKey(type, out var key), Is.True, type.ToString());
			Assert.That(key, Does.StartWith("UI_CARD_TYPE_"));
		}
	}

	[Test]
	public void CardFramePaths_cover_all_rarities()
	{
		foreach (ERarity rarity in Enum.GetValues<ERarity>())
		{
			Assert.That(CardUiDefinitions.TryGetCardFramePath(rarity, out var path), Is.True, rarity.ToString());
			Assert.That(path, Does.StartWith("res://Resource/Assets/CardFrame/"));
			Assert.That(path, Does.EndWith(".png"));
		}
	}

	[Test]
	public void CollectElementColors_none_flags_returns_none_color()
	{
		var colors = CardUiDefinitions.CollectElementColors(0);
		Assert.That(colors, Has.Length.EqualTo(1));
		Assert.That(colors[0], Is.EqualTo(ColorDefinitions.NoneElement));
	}

	[Test]
	public void CollectElementColors_single_red()
	{
		var colors = CardUiDefinitions.CollectElementColors((int)EElement.Red);
		Assert.That(colors, Has.Length.EqualTo(1));
		Assert.That(colors[0], Is.EqualTo(ColorDefinitions.RedElement));
	}

	[Test]
	public void CollectElementColors_three_elements_in_flag_order()
	{
		var flags = (int)(EElement.Red | EElement.Blue | EElement.Green);
		var colors = CardUiDefinitions.CollectElementColors(flags);
		Assert.That(colors, Has.Length.EqualTo(3));
		Assert.That(colors[0], Is.EqualTo(ColorDefinitions.RedElement));
		Assert.That(colors[1], Is.EqualTo(ColorDefinitions.BlueElement));
		Assert.That(colors[2], Is.EqualTo(ColorDefinitions.GreenElement));
	}

	[Test]
	public void CollectElementColors_more_than_three_truncates()
	{
		var flags = (int)(EElement.Red | EElement.Blue | EElement.Green | EElement.Yellow);
		var colors = CardUiDefinitions.CollectElementColors(flags);
		Assert.That(colors, Has.Length.EqualTo(3));
		Assert.That(colors[0], Is.EqualTo(ColorDefinitions.RedElement));
		Assert.That(colors[1], Is.EqualTo(ColorDefinitions.BlueElement));
		Assert.That(colors[2], Is.EqualTo(ColorDefinitions.GreenElement));
	}

	[Test]
	public void TryGetElementColor_maps_known_elements()
	{
		Assert.That(CardUiDefinitions.TryGetElementColor(EElement.Yellow, out var c), Is.True);
		Assert.That(c, Is.EqualTo(ColorDefinitions.YellowElement));
		Assert.That(CardUiDefinitions.TryGetElementColor(EElement.None, out _), Is.False);
	}

	[Test]
	public void FormatCost_rules()
	{
		Assert.That(CardUiDefinitions.FormatCost(ECostType.None, 3), Is.EqualTo(""));
		Assert.That(CardUiDefinitions.FormatCost(ECostType.X, 0), Is.EqualTo("X"));
		Assert.That(CardUiDefinitions.FormatCost(ECostType.Energy, 2), Is.EqualTo("2"));
	}
}
