using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui.Comp;
using KemoCard.Mod.Global.Ui.Tip;

namespace KemoCard.Mod.Global.Ui;

public record struct CodexDlgPayload;

public partial class CodexDlg : BaseDlg
{
	private enum CodexTab
	{
		Card,
		Character,
	}

	[Export] private TabContainer? _tabContainer;
	[Export] private OptionButton? _obField;
	[Export] private OptionButton? _obOp;
	[Export] private OptionButton? _obVal;
	[Export] private ItemList? _itemListConditions;
	[Export] private Button? _btnAdd;
	[Export] private Button? _btnSearch;
	[Export] private LineEdit? _iptTxtFilter;
	[Export] private GridContainer? _gridCardList;
	[Export] private BasePager? _pager;

	[Export] private OptionButton? _obCharField;
	[Export] private OptionButton? _obCharOp;
	[Export] private OptionButton? _obCharVal;
	[Export] private ItemList? _itemListCharConditions;
	[Export] private Button? _btnCharAdd;
	[Export] private Button? _btnCharSearch;
	[Export] private LineEdit? _iptCharTxtFilter;
	[Export] private GridContainer? _gridCharList;
	[Export] private BasePager? _charPager;

	private CodexTab _tab;
	private readonly List<CardFilterCondition> _conditions = [];
	private readonly List<CharFilterCondition> _charConditions = [];
	private IReadOnlyList<CardDto> _filteredCards = [];
	private IReadOnlyList<CharacterDto> _filteredCharacters = [];
	private List<BaseCardItem> _cardSlots = [];
	private List<BaseCharacterItem> _charSlots = [];
	private bool _eventsBound;

	public override string UIId => GlobalUiIds.Codex;
	public override string UIDir => "Src/mod/global/Ui";

	protected override void InitEvent()
	{
		if (_eventsBound)
		{
			return;
		}

		_eventsBound = true;

		if (_tabContainer != null)
		{
			_tabContainer.TabChanged += OnTabChanged;
		}

		if (_obField != null)
		{
			_obField.ItemSelected += OnFieldSelected;
		}

		if (_obCharField != null)
		{
			_obCharField.ItemSelected += OnCharFieldSelected;
		}

		if (_btnAdd != null)
		{
			OnClicks(_btnAdd, OnAddPressed);
		}

		if (_btnCharAdd != null)
		{
			OnClicks(_btnCharAdd, OnCharAddPressed);
		}

		if (_btnSearch != null)
		{
			OnClicks(_btnSearch, OnSearchPressed);
		}

		if (_btnCharSearch != null)
		{
			OnClicks(_btnCharSearch, OnCharSearchPressed);
		}

		if (_itemListConditions != null)
		{
			_itemListConditions.ItemClicked += OnConditionItemClicked;
		}

		if (_itemListCharConditions != null)
		{
			_itemListCharConditions.ItemClicked += OnCharConditionItemClicked;
		}

		if (_iptTxtFilter != null)
		{
			_iptTxtFilter.TextSubmitted += OnTextSubmitted;
		}

		if (_iptCharTxtFilter != null)
		{
			_iptCharTxtFilter.TextSubmitted += OnCharTextSubmitted;
		}

		if (_pager != null)
		{
			_pager.OnPageChanged = OnPagerPageChanged;
		}

		if (_charPager != null)
		{
			_charPager.OnPageChanged = OnCharPagerPageChanged;
		}
	}

	protected override void OnOpen()
	{
		_conditions.Clear();
		_charConditions.Clear();
		_itemListConditions?.Clear();
		_itemListCharConditions?.Clear();

		if (_iptTxtFilter != null)
		{
			_iptTxtFilter.Text = "";
		}

		if (_iptCharTxtFilter != null)
		{
			_iptCharTxtFilter.Text = "";
		}

		CacheCardSlots();
		CacheCharSlots();

		_tab = CodexTab.Card;
		if (_tabContainer != null)
		{
			_tabContainer.CurrentTab = 0;
		}

		PopulateFieldOptions();
		RebuildOpAndValOptions();
		PopulateCharFieldOptions();
		RebuildCharOpAndValOptions();
		RefreshFilteredList(resetPage: true);
	}

	protected override void UpdateView()
	{
		RefreshFilteredList(resetPage: false);
	}

	#region Tab 切换

	private void OnTabChanged(long tabIndex)
	{
		KeywordTipService.Current?.HideTips();
		_tab = tabIndex == (long)CodexTab.Character ? CodexTab.Character : CodexTab.Card;

		if (_tab == CodexTab.Character)
		{
			PopulateCharFieldOptions();
			RebuildCharOpAndValOptions();
			if (_iptCharTxtFilter != null)
			{
				_iptCharTxtFilter.PlaceholderText = "UI_CODEX_CHAR_TXT_FILTER";
			}
		}
		else
		{
			PopulateFieldOptions();
			RebuildOpAndValOptions();
			if (_iptTxtFilter != null)
			{
				_iptTxtFilter.PlaceholderText = "UI_CODEX_TXT_FILTER";
			}
		}

		RefreshFilteredList(resetPage: true);
	}

	#endregion

	#region 卡牌图鉴过滤 UI

	private void CacheCardSlots()
	{
		_cardSlots = [];
		if (_gridCardList == null)
		{
			return;
		}

		foreach (var child in _gridCardList.GetChildren())
		{
			if (child is BaseCardItem item)
			{
				_cardSlots.Add(item);
			}
		}

		foreach (var item in _cardSlots)
		{
			item.EnableHoverTip = true;
		}
	}

	private void PopulateFieldOptions()
	{
		if (_obField == null)
		{
			return;
		}

		_obField.Clear();
		foreach (ECardFilterField field in Enum.GetValues<ECardFilterField>())
		{
			_obField.AddItem(Localization.Tr(CodexFilterDefinitions.GetFieldLocaleKey(field)));
			_obField.SetItemMetadata(_obField.ItemCount - 1, (int)field);
		}

		_obField.Selected = 0;
	}

	private void OnFieldSelected(long _)
	{
		if (_tab != CodexTab.Card)
		{
			return;
		}

		RebuildOpAndValOptions();
	}

	private void RebuildOpAndValOptions()
	{
		var field = GetSelectedField();
		PopulateOpOptions(field);
		PopulateValOptions(field);
	}

	private ECardFilterField GetSelectedField()
	{
		if (_obField == null || _obField.Selected < 0)
		{
			return ECardFilterField.CardType;
		}

		return (ECardFilterField)(int)_obField.GetItemMetadata(_obField.Selected);
	}

	private void PopulateOpOptions(ECardFilterField field)
	{
		if (_obOp == null)
		{
			return;
		}

		_obOp.Clear();
		foreach (var op in CardCodexQuery.OpsForField(field))
		{
			_obOp.AddItem(Localization.Tr(CodexFilterDefinitions.GetOpLocaleKey(op)));
			_obOp.SetItemMetadata(_obOp.ItemCount - 1, (int)op);
		}

		_obOp.Selected = 0;
	}

	private void PopulateValOptions(ECardFilterField field)
	{
		if (_obVal == null)
		{
			return;
		}

		_obVal.Clear();
		switch (field)
		{
			case ECardFilterField.CardType:
				foreach (ECardType v in Enum.GetValues<ECardType>())
				{
					var text = CardUiDefinitions.TryGetCardTypeLocaleKey(v, out var key)
						? Localization.Tr(key)
						: v.ToString();
					_obVal.AddItem(text);
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECardFilterField.Cost:
				for (var i = 0; i <= CardCodexQuery.MaxCostOption; i++)
				{
					_obVal.AddItem(i.ToString());
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, i.ToString());
				}
				break;
			case ECardFilterField.Element:
				foreach (EElement v in Enum.GetValues<EElement>())
				{
					if (v == EElement.None || !CodexFilterDefinitions.TryGetElementLocaleKey(v, out var key))
					{
						continue;
					}

					_obVal.AddItem(Localization.Tr(key));
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECardFilterField.Role:
				foreach (ERole v in Enum.GetValues<ERole>())
				{
					var text = CodexFilterDefinitions.TryGetRoleLocaleKey(v, out var key)
						? Localization.Tr(key)
						: v.ToString();
					_obVal.AddItem(text);
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECardFilterField.CostType:
				foreach (ECostType v in Enum.GetValues<ECostType>())
				{
					var text = CodexFilterDefinitions.TryGetCostTypeLocaleKey(v, out var key)
						? Localization.Tr(key)
						: v.ToString();
					_obVal.AddItem(text);
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECardFilterField.Tag:
				var store = AppRoot.Services.ContentModPipeline.Registry.Store;
				foreach (var tag in CardCodexQuery.CollectTags(store.Cards.Values))
				{
					_obVal.AddItem(tag);
					_obVal.SetItemMetadata(_obVal.ItemCount - 1, tag);
				}
				break;
		}

		_obVal.Selected = _obVal.ItemCount > 0 ? 0 : -1;
	}

	private void OnAddPressed()
	{
		if (_obOp == null || _obVal == null || _obVal.Selected < 0 || _obOp.Selected < 0)
		{
			return;
		}

		var field = GetSelectedField();
		var op = (ECardFilterOp)(int)_obOp.GetItemMetadata(_obOp.Selected);
		var valueId = _obVal.GetItemMetadata(_obVal.Selected).AsString();
		if (string.IsNullOrEmpty(valueId))
		{
			return;
		}

		var display = $"{Localization.Tr(CodexFilterDefinitions.GetFieldLocaleKey(field))} {Localization.Tr(CodexFilterDefinitions.GetOpLocaleKey(op))} {_obVal.GetItemText(_obVal.Selected)}";
		_conditions.Add(new CardFilterCondition(field, op, valueId, display));
		_itemListConditions?.AddItem(display);
	}

	private void OnConditionItemClicked(long index, Vector2 _, long mouseButtonIndex)
	{
		if (mouseButtonIndex != (long)MouseButton.Left)
		{
			return;
		}

		var i = (int)index;
		if (i < 0 || i >= _conditions.Count)
		{
			return;
		}

		_conditions.RemoveAt(i);
		_itemListConditions?.RemoveItem(i);
	}

	private void OnSearchPressed() => RefreshCardFilteredList(resetPage: true);

	private void OnTextSubmitted(string _) => RefreshCardFilteredList(resetPage: true);

	private void OnPagerPageChanged(int _) => FillCardCurrentPage();

	private void RefreshCardFilteredList(bool resetPage)
	{
		var store = AppRoot.Services.ContentModPipeline.Registry.Store;
		var text = _iptTxtFilter?.Text ?? "";
		_filteredCards = CardCodexQuery.Filter(
			store.Cards.Values,
			_conditions,
			text,
			Localization.Tr,
			id => store.TryGetSkill(id, out var skill) ? skill : null);

		if (_pager != null)
		{
			var pages = CardCodexQuery.TotalPages(_filteredCards.Count, CardCodexQuery.PageSize);
			_pager.TotalPages = pages;
			if (resetPage)
			{
				_pager.SetPage(0);
			}
		}

		FillCardCurrentPage();
	}

	private void FillCardCurrentPage()
	{
		var page = _pager?.CurrentPage ?? 0;
		var slice = CardCodexQuery.SlicePage(_filteredCards, page, CardCodexQuery.PageSize);
		for (var i = 0; i < _cardSlots.Count; i++)
		{
			if (i >= slice.Count)
			{
				_cardSlots[i].SetData(null);
				_cardSlots[i].Visible = false;
				continue;
			}

			_cardSlots[i].SetData(slice[i]);
			_cardSlots[i].Visible = true;
		}
	}

	#endregion

	#region 角色图鉴过滤 UI

	private void CacheCharSlots()
	{
		_charSlots = [];
		if (_gridCharList == null)
		{
			return;
		}

		foreach (var child in _gridCharList.GetChildren())
		{
			if (child is BaseCharacterItem item)
			{
				_charSlots.Add(item);
			}
		}

		foreach (var item in _charSlots)
		{
			item.EnableHoverTip = true;
		}
	}

	private void PopulateCharFieldOptions()
	{
		if (_obCharField == null)
		{
			return;
		}

		_obCharField.Clear();
		foreach (ECharFilterField field in Enum.GetValues<ECharFilterField>())
		{
			_obCharField.AddItem(Localization.Tr(CodexFilterDefinitions.GetCharFieldLocaleKey(field)));
			_obCharField.SetItemMetadata(_obCharField.ItemCount - 1, (int)field);
		}

		_obCharField.Selected = 0;
	}

	private void OnCharFieldSelected(long _)
	{
		if (_tab != CodexTab.Character)
		{
			return;
		}

		RebuildCharOpAndValOptions();
	}

	private void RebuildCharOpAndValOptions()
	{
		var field = GetSelectedCharField();
		PopulateCharOpOptions(field);
		PopulateCharValOptions(field);
	}

	private ECharFilterField GetSelectedCharField()
	{
		if (_obCharField == null || _obCharField.Selected < 0)
		{
			return ECharFilterField.Element;
		}

		return (ECharFilterField)(int)_obCharField.GetItemMetadata(_obCharField.Selected);
	}

	private void PopulateCharOpOptions(ECharFilterField field)
	{
		if (_obCharOp == null)
		{
			return;
		}

		_obCharOp.Clear();
		foreach (var op in CharacterCodexQuery.OpsForField(field))
		{
			_obCharOp.AddItem(Localization.Tr(CodexFilterDefinitions.GetOpLocaleKey(op)));
			_obCharOp.SetItemMetadata(_obCharOp.ItemCount - 1, (int)op);
		}

		_obCharOp.Selected = 0;
	}

	private void PopulateCharValOptions(ECharFilterField field)
	{
		if (_obCharVal == null)
		{
			return;
		}

		_obCharVal.Clear();
		switch (field)
		{
			case ECharFilterField.Element:
				foreach (EElement v in Enum.GetValues<EElement>())
				{
					if (v == EElement.None || !CodexFilterDefinitions.TryGetElementLocaleKey(v, out var key))
					{
						continue;
					}

					_obCharVal.AddItem(Localization.Tr(key));
					_obCharVal.SetItemMetadata(_obCharVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECharFilterField.Role:
				foreach (ERole v in Enum.GetValues<ERole>())
				{
					var text = CodexFilterDefinitions.TryGetRoleLocaleKey(v, out var key)
						? Localization.Tr(key)
						: v.ToString();
					_obCharVal.AddItem(text);
					_obCharVal.SetItemMetadata(_obCharVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECharFilterField.Race:
				foreach (ERace v in Enum.GetValues<ERace>())
				{
					if (v == ERace.None || !CodexFilterDefinitions.TryGetRaceLocaleKey(v, out var key))
					{
						continue;
					}

					_obCharVal.AddItem(Localization.Tr(key));
					_obCharVal.SetItemMetadata(_obCharVal.ItemCount - 1, v.ToString());
				}
				break;
			case ECharFilterField.Tag:
				var store = AppRoot.Services.ContentModPipeline.Registry.Store;
				foreach (var tag in CharacterCodexQuery.CollectTags(store.Characters.Values))
				{
					_obCharVal.AddItem(tag);
					_obCharVal.SetItemMetadata(_obCharVal.ItemCount - 1, tag);
				}
				break;
		}

		_obCharVal.Selected = _obCharVal.ItemCount > 0 ? 0 : -1;
	}

	private void OnCharAddPressed()
	{
		if (_obCharOp == null || _obCharVal == null || _obCharVal.Selected < 0 || _obCharOp.Selected < 0)
		{
			return;
		}

		var field = GetSelectedCharField();
		var op = (ECardFilterOp)(int)_obCharOp.GetItemMetadata(_obCharOp.Selected);
		var valueId = _obCharVal.GetItemMetadata(_obCharVal.Selected).AsString();
		if (string.IsNullOrEmpty(valueId))
		{
			return;
		}

		var display =
			$"{Localization.Tr(CodexFilterDefinitions.GetCharFieldLocaleKey(field))} {Localization.Tr(CodexFilterDefinitions.GetOpLocaleKey(op))} {_obCharVal.GetItemText(_obCharVal.Selected)}";
		_charConditions.Add(new CharFilterCondition(field, op, valueId, display));
		_itemListCharConditions?.AddItem(display);
	}

	private void OnCharConditionItemClicked(long index, Vector2 _, long mouseButtonIndex)
	{
		if (mouseButtonIndex != (long)MouseButton.Left)
		{
			return;
		}

		var i = (int)index;
		if (i < 0 || i >= _charConditions.Count)
		{
			return;
		}

		_charConditions.RemoveAt(i);
		_itemListCharConditions?.RemoveItem(i);
	}

	private void OnCharSearchPressed() => RefreshCharacterFilteredList(resetPage: true);

	private void OnCharTextSubmitted(string _) => RefreshCharacterFilteredList(resetPage: true);

	private void OnCharPagerPageChanged(int _) => FillCharacterCurrentPage();

	private void RefreshCharacterFilteredList(bool resetPage)
	{
		var store = AppRoot.Services.ContentModPipeline.Registry.Store;
		var text = _iptCharTxtFilter?.Text ?? "";
		_filteredCharacters = CharacterCodexQuery.Filter(
			store.Characters.Values,
			_charConditions,
			text,
			Localization.Tr,
			id => store.TryGetSkill(id, out var skill) ? skill : null);

		if (_charPager != null)
		{
			var pages = CharacterCodexQuery.TotalPages(_filteredCharacters.Count, CharacterCodexQuery.PageSize);
			_charPager.TotalPages = pages;
			if (resetPage)
			{
				_charPager.SetPage(0);
			}
		}

		FillCharacterCurrentPage();
	}

	private void FillCharacterCurrentPage()
	{
		var page = _charPager?.CurrentPage ?? 0;
		var slice = CharacterCodexQuery.SlicePage(_filteredCharacters, page, CharacterCodexQuery.PageSize);
		for (var i = 0; i < _charSlots.Count; i++)
		{
			if (i >= slice.Count)
			{
				_charSlots[i].SetData(null);
				_charSlots[i].Visible = false;
				continue;
			}

			_charSlots[i].SetData(slice[i]);
			_charSlots[i].Visible = true;
		}
	}

	#endregion

	#region 当前 Tab 刷新

	private void RefreshFilteredList(bool resetPage)
	{
		if (_tab == CodexTab.Character)
		{
			RefreshCharacterFilteredList(resetPage);
		}
		else
		{
			RefreshCardFilteredList(resetPage);
		}
	}

	#endregion
}
