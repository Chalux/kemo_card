using Godot;
using KemoCard.Fixed.Godot;
using KemoCard.Frame.Content.Definitions;
using KemoCard.Frame.UI.Base;
using KemoCard.Mod.Combat.Orbs;
using KemoCard.Mod.Global.Def;
using KemoCard.Mod.Global.Ui.Themes;
using KemoCard.Mod.Run.Ui.CombatUi.Presentation;

namespace KemoCard.Mod.Run.Ui.CombatUi;

/// <summary>
/// 充能球指示器（战斗规格 §11.6）：7 个球位按 FIFO 顺序上色（元素球取元素色，物理球墨色、魔法球墨绿）
/// + <c>n/7</c> + 提示 + 触发按钮（不足 3 个时禁用）。
/// </summary>
public partial class OrbQueueCmp : BaseCmp
{
    [Export] private Container? _slots;
    [Export] private Label? _lblCount;
    [Export] private Label? _lblHint;
    [Export] private Button? _btnTrigger;
    [Export] private Control? _margin;

    /// <summary>触发按钮回调。</summary>
    public Action? TriggerRequested { get; set; }

    private readonly List<ColorRect> _orbRects = [];
    private bool _inputLocked;

    /// <summary>
    /// 高度跟随内容：右栏是 VBoxContainer，按子节点最小尺寸排版；根是普通 <see cref="Control"/>，
    /// 不聚合子节点最小尺寸，不上报就会拿到 0 高度、内容（grow=both）向上溢出压到暂停按钮上。
    /// </summary>
    public override Vector2 _GetMinimumSize() => _margin?.GetCombinedMinimumSize() ?? base._GetMinimumSize();

    protected override void OnReady()
    {
        _orbRects.Clear();
        if (_slots != null)
        {
            foreach (var child in _slots.GetChildren())
            {
                if (child is ColorRect rect)
                    _orbRects.Add(rect);
            }
        }

        if (_lblHint != null)
        {
            _lblHint.Text = string.Format(
                Localization.Tr("UI_ORB_HINT"),
                OrbQueue.Capacity,
                OrbQueue.ManualTriggerThreshold);
        }
    }

    protected override void InitEvent()
    {
        if (_btnTrigger != null)
            OnClicks(_btnTrigger, () => TriggerRequested?.Invoke());
    }

    public void Bind(OrbQueue queue, Func<string, OrbTypeDto?> resolveOrbType)
    {
        for (var i = 0; i < _orbRects.Count; i++)
        {
            var rect = _orbRects[i];
            if (i < queue.Count)
            {
                rect.Color = ResolveColor(resolveOrbType(queue.Orbs[i].OrbTypeId));
                rect.TooltipText = ResolveName(queue.Orbs[i].OrbTypeId, resolveOrbType);
            }
            else
            {
                rect.Color = KemoPalette.SurfaceSunken;
                rect.TooltipText = "";
            }
        }

        if (_lblCount != null)
            _lblCount.Text = $"{queue.Count} / {OrbQueue.Capacity}";

        if (_btnTrigger != null)
            _btnTrigger.Disabled = _inputLocked || !queue.CanTriggerManually;
    }

    public void SetInputLocked(bool locked)
    {
        _inputLocked = locked;
        if (_btnTrigger != null && locked)
            _btnTrigger.Disabled = true;
    }

    /// <summary>
    /// 按事件载荷单独上色一格并更新计数（入队事件专用）：满员自动触发会把队列立即清空，
    /// 此时读状态的 <see cref="Bind"/> 只能画出空队列，刚入队的球必须由载荷提供。
    /// 触发 / 批次结束时仍由 <see cref="Bind"/> 全量对账。
    /// </summary>
    public void PaintOrb(int index, string orbTypeId, int count, Func<string, OrbTypeDto?> resolveOrbType)
    {
        if (index >= 0 && index < _orbRects.Count)
        {
            var rect = _orbRects[index];
            rect.Color = ResolveColor(resolveOrbType(orbTypeId));
            rect.TooltipText = ResolveName(orbTypeId, resolveOrbType);
        }

        if (_lblCount != null)
            _lblCount.Text = $"{count} / {OrbQueue.Capacity}";
    }

    /// <summary>第 <paramref name="index"/> 个球位亮起（入队）。</summary>
    public Task FlashSlotAsync(int index, float duration)
    {
        if (index < 0 || index >= _orbRects.Count)
            return Task.CompletedTask;

        return UnitTweens.FlashAsync(_orbRects[index], Colors.White, duration);
    }

    /// <summary>整排闪光（触发）。</summary>
    public Task FlashAllAsync(float duration)
    {
        if (_slots is null)
            return Task.CompletedTask;

        return UnitTweens.FlashAsync(_slots, Colors.White, duration);
    }

    private static Color ResolveColor(OrbTypeDto? orbType)
    {
        if (orbType is null)
            return KemoPalette.TextDisabled;

        if (orbType.Element != EElement.None && CardUiDefinitions.TryGetElementColor(orbType.Element, out var color))
            return color;

        return orbType.DamageKind == EDamageKind.Magical ? KemoPalette.Accent : KemoPalette.PanelBorder;
    }

    private static string ResolveName(string orbTypeId, Func<string, OrbTypeDto?> resolveOrbType)
    {
        var orbType = resolveOrbType(orbTypeId);
        return orbType is null || string.IsNullOrWhiteSpace(orbType.DisplayNameId)
            ? orbTypeId
            : Localization.Tr(orbType.DisplayNameId);
    }
}