using System;
using Godot;
using KemoCard.Frame.Logging;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Global.Ui.Comp;

public partial class SettingSliderRow : BaseCmp
{
    [Export] public Label? NameLabel { get; set; }
    [Export] public HSlider? Slider { get; set; }
    [Export] public Label? ValueLabel { get; set; }
    [Export] public float MinValue { get; set; }
    [Export] public float MaxValue { get; set; } = 100f;
    [Export] public float Step { get; set; } = 1f;
    [Export] public float DefaultValue { get; set; } = 100f;

    public event Action<double>? ValueChanged;

    private bool _suppress;

    public double Value
    {
        get => Slider?.Value ?? DefaultValue;
        set => SetValue(value, notify: true);
    }

    #region 公共 API

    public void SetValue(double value, bool notify)
    {
        if (Slider != null)
        {
            _suppress = true;
            Slider.Value = value;
            _suppress = false;
        }

        RefreshValueLabel(value);
        if (notify && !_suppress)
        {
            ValueChanged?.Invoke(value);
        }
    }

    public void SetNameKey(string key)
    {
        if (NameLabel != null)
        {
            NameLabel.Text = key;
        }
    }

    #endregion

    #region 生命周期

    protected override void InitEvent()
    {
        if (Slider == null)
        {
            AppLog.Warning("SettingSliderRow: Slider 未绑定，跳过事件。", nameof(SettingSliderRow));
            return;
        }

        Slider.MinValue = MinValue;
        Slider.MaxValue = MaxValue;
        Slider.Step = Step;

        Bind(
            () => Slider.ValueChanged += OnSliderValueChanged,
            () => Slider.ValueChanged -= OnSliderValueChanged);

        _suppress = true;
        Slider.Value = DefaultValue;
        RefreshValueLabel(DefaultValue);
        _suppress = false;
    }

    #endregion

    #region 交互

    private void OnSliderValueChanged(double value)
    {
        RefreshValueLabel(value);
        if (!_suppress)
        {
            ValueChanged?.Invoke(value);
        }
    }

    private void RefreshValueLabel(double value)
    {
        if (ValueLabel != null)
        {
            ValueLabel.Text = ((int)Math.Round(value)).ToString();
        }
    }

    #endregion
}