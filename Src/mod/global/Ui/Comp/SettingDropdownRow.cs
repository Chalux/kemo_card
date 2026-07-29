using System;
using System.Collections.Generic;
using Godot;
using KemoCard.Frame.Logging;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Global.Ui.Comp;

public partial class SettingDropdownRow : BaseCmp
{
    [Export] public Label? NameLabel { get; set; }
    [Export] public OptionButton? OptionButton { get; set; }
    [Export] public string DefaultValue { get; set; } = string.Empty;

    public event Action<string>? ValueChanged;

    private readonly List<string> _ids = [];
    private bool _suppress;

    public string SelectedId
    {
        get => GetSelectedId();
        set => SetSelectedId(value, notify: true);
    }

    #region 公共 API

    public void SetOptions(IReadOnlyList<(string id, string label)> options)
    {
        _ids.Clear();
        OptionButton?.Clear();
        if (options == null)
        {
            return;
        }

        foreach (var (id, label) in options)
        {
            _ids.Add(id);
            OptionButton?.AddItem(label);
        }
    }

    public void SetNameKey(string key)
    {
        if (NameLabel != null)
        {
            NameLabel.Text = key;
        }
    }

    public void SetSelectedId(string id, bool notify)
    {
        _suppress = true;
        var ok = TrySelectById(id);
        _suppress = false;
        if (ok && notify)
        {
            ValueChanged?.Invoke(GetSelectedId());
        }
    }

    #endregion

    #region 生命周期

    protected override void InitEvent()
    {
        if (OptionButton == null)
        {
            AppLog.Warning("SettingDropdownRow: OptionButton 未绑定，跳过事件。", nameof(SettingDropdownRow));
            return;
        }

        Bind(
            () => OptionButton.ItemSelected += OnItemSelected,
            () => OptionButton.ItemSelected -= OnItemSelected);

        _suppress = true;
        SelectDefaultOrFirst();
        _suppress = false;
    }

    #endregion

    #region 选中与事件

    private void OnItemSelected(long index)
    {
        if (_suppress)
        {
            return;
        }

        ValueChanged?.Invoke(GetIdAt((int)index));
    }

    private void SelectDefaultOrFirst()
    {
        if (OptionButton == null || OptionButton.ItemCount <= 0)
        {
            return;
        }

        if (!string.IsNullOrEmpty(DefaultValue) && TrySelectById(DefaultValue))
        {
            return;
        }

        OptionButton.Selected = 0;
    }

    private bool TrySelectById(string id)
    {
        if (OptionButton == null)
        {
            return false;
        }

        var index = _ids.IndexOf(id);
        if (index < 0 || index >= OptionButton.ItemCount)
        {
            return false;
        }

        OptionButton.Selected = index;
        return true;
    }

    private string GetSelectedId()
    {
        if (OptionButton == null)
        {
            return string.Empty;
        }

        return GetIdAt(OptionButton.Selected);
    }

    private string GetIdAt(int index)
    {
        if (index < 0 || index >= _ids.Count)
        {
            return string.Empty;
        }

        return _ids[index];
    }

    #endregion
}