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

namespace KemoCard.Mod.Global.Ui;

public record struct CodexDlgPayload;

public partial class CodexDlg : BaseDlg
{
	[Export] private OptionButton? _obField;
	[Export] private OptionButton? _obOp;
	[Export] private OptionButton? _obVal;
	[Export] private ItemList? _itemListConditions;
	[Export] private Button? _btnAdd;
	[Export] private LineEdit? _iptTxtFilter;
	[Export] private GridContainer? _gridCardList;
	[Export] private BasePager? _pager;

	private readonly List<CardFilterCondition> _conditions = [];
	private IReadOnlyList<CardDto> _filteredCards = [];
	private List<BaseCardItem> _cardSlots = [];
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

		if (_obField != null)
		{
			_obField.ItemSelected += OnFieldSelected;
		}

		if (_btnAdd != null)
		{
			OnClicks(_btnAdd, OnAddPressed);
		}

		if (_itemListConditions != null)
		{
			_itemListConditions.ItemClicked += OnConditionItemClicked;
		}

		if (_iptTxtFilter != null)
		{
			_iptTxtFilter.TextSubmitted += OnTextSubmitted;
			_iptTxtFilter.FocusExited += OnTextFocusExited;
		}

		if (_pager != null)
		{
			_pager.OnPageChanged = OnPagerPageChanged;
		}
	}

	protected override void OnOpen()
	{
		_conditions.Clear();
		_itemListConditions?.Clear();
		if (_iptTxtFilter != null)
		{
			_iptTxtFilter.Text = "";
		}

		CacheCardSlots();
		PopulateFieldOptions();
		RebuildOpAndValOptions();
		RefreshFilteredList(resetPage: true);
	}

	protected override void UpdateView()
	{
		RefreshFilteredList(resetPage: false);
	}

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
		RefreshFilteredList(resetPage: true);
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
		RefreshFilteredList(resetPage: true);
	}

	private void OnTextSubmitted(string _) => RefreshFilteredList(resetPage: true);

	private void OnTextFocusExited() => RefreshFilteredList(resetPage: true);

	private void OnPagerPageChanged(int _) => FillCurrentPage();

	private void RefreshFilteredList(bool resetPage)
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

		FillCurrentPage();
	}

	private void FillCurrentPage()
	{
		var page = _pager?.CurrentPage ?? 0;
		var slice = CardCodexQuery.SlicePage(_filteredCards, page, CardCodexQuery.PageSize);
		for (var i = 0; i < _cardSlots.Count; i++)
		{
			_cardSlots[i].SetData(i < slice.Count ? slice[i] : null);
		}
	}

	#endregion
}
