using Godot;
using KemoCard.Fixed.Godot;

namespace KemoCard.Mod.Global.Ui.Toast;

/// <summary>
/// Toast 静态门面：对象池 + 纵向堆叠 + 生命周期调度。
/// 挂载于 Notice 层（TopLayer，不受 hideBelow 影响），不经 UIManager 状态机
/// ——UIVoRegistry/UILayer 按 UIId 单实例，与堆叠多实例冲突。
/// </summary>
public static class ToastService
{
    private const string ScenePath = "res://Src/mod/global/Ui/Toast/ToastItem.tscn";
    private const int MaxPoolSize = 4;
    private const float StayMs = 1000f;
    private const float FadeMs = 2000f;
    private const float RisePx = 100f;

    private static readonly Stack<ToastItem> Pool = [];
    private static readonly List<ToastItem> Active = [];
    private static VBoxContainer? _container;
    private static PackedScene? _scene;
    private static bool _configured;

    /// <summary>
    /// 启动时注入 Notice 层；重复调用幂等。
    /// </summary>
    public static void Configure(Control? noticeLayer)
    {
        if (_configured || noticeLayer == null)
        {
            return;
        }

        _configured = true;
        var container = new VBoxContainer
        {
            Name = "ToastContainer",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Begin,
        };
        // 顶部 25% 处水平居中；容器由运行时创建，故锚点在此设置。
        container.AnchorLeft = 0f;
        container.AnchorRight = 1f;
        container.AnchorTop = 0.25f;
        container.AnchorBottom = 1f;
        container.OffsetTop = 0f;
        container.GrowHorizontal = Control.GrowDirection.Both;
        container.GrowVertical = Control.GrowDirection.End;
        noticeLayer.AddChild(container);
        _container = container;
    }

    /// <summary>
    /// 拉起一条 Toast。文案为翻译键（内部 Localization.Tr）；
    /// <paramref name="args"/> 传入时用 string.Format 格式化翻译串的占位符（如 "{0} {1}"）。
    /// </summary>
    public static void Show(string textKey, params string?[]? args)
    {
        var container = _container;
        if (container == null)
        {
            return;
        }

        var text = args is { Length: > 0 }
            ? string.Format(Localization.Tr(textKey), args)
            : Localization.Tr(textKey);

        var item = Acquire();
        item.ShowText(text);
        container.AddChild(item);
        Active.Add(item);
        item.PlayRecycle(StayMs, FadeMs, RisePx, () => Recycle(item));
    }

    private static ToastItem Acquire()
    {
        if (Pool.Count > 0)
        {
            return Pool.Pop();
        }

        _scene ??= ResourceLoader.Load<PackedScene>(ScenePath);
        var node = _scene.Instantiate<ToastItem>();
        return node;
    }

    private static void Recycle(ToastItem item)
    {
        Active.Remove(item);
        if (GodotObject.IsInstanceValid(item))
        {
            item.GetParent()?.RemoveChild(item);
            item.Visible = false;
            if (Pool.Count < MaxPoolSize)
            {
                Pool.Push(item);
                return;
            }

            item.QueueFree();
        }
    }
}