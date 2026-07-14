using System;
using Godot;
using KemoCard.Frame.Logging;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Global.Ui.Comp;

public partial class SettingToggleRow : BaseCmp
{
    private static readonly Color OffTrackColor = new(0.32f, 0.32f, 0.36f, 1f);
    private static readonly Color OnTrackColor = new(0.28f, 0.52f, 0.88f, 1f);
    private const float KnobPad = 3f;
    private const float KnobSize = 22f;

    [Export] public Label? NameLabel { get; set; }
    [Export] public Control? Track { get; set; }
    [Export] public Control? Knob { get; set; }
    [Export] public bool DefaultValue { get; set; }
    [Export] public float AnimDuration { get; set; } = 0.12f;

    public event Action<bool>? ValueChanged;

    private bool _value;
    private Tween? _tween;
    private bool _suppress;

    public bool Value
    {
        get => _value;
        set => SetValue(value, animate: true, notify: true);
    }

    #region 公共 API

    public void SetValue(bool value, bool animate, bool notify)
    {
        _value = value;
        PlayVisual(animate);
        if (notify && !_suppress)
        {
            ValueChanged?.Invoke(_value);
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
        if (Track == null || Knob == null)
        {
            AppLog.Warning("SettingToggleRow: Track/Knob 未绑定，跳过事件。", nameof(SettingToggleRow));
        }

        _suppress = true;
        SetValue(DefaultValue, animate: false, notify: false);
        _suppress = false;

        if (Track != null)
        {
            Bind(
                () => Track.GuiInput += OnTrackGuiInput,
                () => Track.GuiInput -= OnTrackGuiInput);
        }
    }

    protected override void OnUnbind()
    {
        _tween?.Kill();
        _tween = null;
    }

    #endregion

    #region 交互与视觉

    private void OnTrackGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            SetValue(!_value, animate: true, notify: true);
            Track?.AcceptEvent();
        }
    }

    private void PlayVisual(bool animate)
    {
        if (Knob == null)
        {
            return;
        }

        var targetX = _value ? GetOnKnobX() : KnobPad;
        var targetPos = new Vector2(targetX, KnobPad);
        var targetColor = _value ? OnTrackColor : OffTrackColor;
        var trackRect = Track as ColorRect;

        _tween?.Kill();
        _tween = null;

        if (!animate || AnimDuration <= 0f)
        {
            Knob.Position = targetPos;
            if (trackRect != null)
            {
                trackRect.Color = targetColor;
            }

            return;
        }

        _tween = CreateTween();
        _tween.SetParallel(true);
        _tween.TweenProperty(Knob, "position:x", targetX, AnimDuration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        if (trackRect != null)
        {
            _tween.TweenProperty(trackRect, "color", targetColor, AnimDuration)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
        }
    }

    private float GetOnKnobX()
    {
        var trackWidth = Track?.Size.X ?? Track?.CustomMinimumSize.X ?? 56f;
        if (trackWidth <= 0f)
        {
            trackWidth = 56f;
        }

        return trackWidth - KnobSize - KnobPad;
    }

    #endregion
}
