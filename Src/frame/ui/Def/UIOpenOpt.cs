using Godot;

namespace KemoCard.Frame.UI.Def;

/// <summary>
/// 遮罩选项
/// </summary>
public class UIMaskOpt
{
    public string? Runtime { get; set; }

    public float Alpha { get; set; } = .6f;
    public bool ClickClose { get; set; } = true;
    public bool HideTipText { get; set; } = false;
    public int CloseMinTime { get; set; } = 0;
    public Color Color { get; set; } = Colors.Black;
    public object? Payload { get; set; }
}

/// <summary>
/// 气泡选项
/// </summary>
public class UIPopOpt
{
    public Control? Target { get; set; }
    public UIPopPosOpt? Pos { get; set; }
}

/// <summary>
/// 气泡位置参数
/// </summary>
public class UIPopPosOpt
{
    public Vector2 TargetAnchor { get; set; } = new(0, 0);
    public Vector2 Anchor { get; set; } = new(0, 0);
    public Vector2 Offset { get; set; } = new(0, 0);
    public Control? Target { get; set; }
    public bool LimitInScreen { get; set; } = true;
}

/// <summary>
/// 界面打开参数
/// </summary>
public class UIOpenOpt
{
    /// <summary>
    /// 打开的层级
    /// </summary>
    public EUILayer? Layer { get; set; }
    /// <summary>
    /// 打开时添加到的父节点
    /// </summary>
    public Control? Parent { get; set; }

    /// <summary>
    /// 缓存时间,单位:毫秒, -1->永久 0->不缓存
    /// </summary>
    public int CacheTime { get; set; } = 30000;

    /// <summary>
    /// 动画类型
    /// None：不播动画
    /// Always：始终播放动画
    /// SkipReOpen：如果界面已打开则不播
    /// </summary>
    public EAnimType AnimType { get; set; } = EAnimType.SkipReOpen;

    /// <summary>
    /// 是否隐藏层级低于自己的界面
    /// </summary>
    public bool HideBelow { get; set; } = false;

    /// <summary>
    /// 是否不遮挡下面的界面
    /// </summary>
    public bool NoCover { get; set; } = false;
    /// <summary>
    /// 界面对齐方式
    /// </summary>
    public EUIAlign Align { get; set; } = EUIAlign.Center;
    /// <summary>
    /// 遮罩选项
    /// </summary>
    public UIMaskOpt? Mask { get; set; }
    /// <summary>
    /// 气泡选项
    /// </summary>
    public UIPopOpt? Pop { get; set; }

    /// <summary>
    /// 跳过打开检查
    /// 如果返回 true，则跳过打开检查
    /// 如果返回 false，则继续打开
    /// </summary>
    public Func<object?, bool>? SkipOpenCheck { get; set; }
    /// <summary>
    /// 打开前回调
    /// </summary>
    public Action<IUIVoHandle>? OnOpenBefore { get; set; }
    /// <summary>
    /// 打开成功回调
    /// </summary>
    public Action<IUIVoHandle>? OnOpen { get; set; }
    /// <summary>
    /// 打开失败回调
    /// </summary>
    public Action? OnFail { get; set; }
    /// <summary>
    /// 预加载资源列表
    /// 返回需要预加载的资源列表
    /// </summary>
    public Func<IUIVoHandle, string[]>? PreLoadResList { get; set; }

    public UIOpenOpt Clone()
    {
        return new UIOpenOpt
        {
            Layer = Layer,
            Parent = Parent,
            CacheTime = CacheTime,
            AnimType = AnimType,
            HideBelow = HideBelow,
            NoCover = NoCover,
            Align = Align,
            Mask = Mask == null ? null : new UIMaskOpt
            {
                Runtime = Mask.Runtime,
                Alpha = Mask.Alpha,
                ClickClose = Mask.ClickClose,
                HideTipText = Mask.HideTipText,
                CloseMinTime = Mask.CloseMinTime,
                Color = Mask.Color,
                Payload = Mask.Payload,
            },
            Pop = Pop == null ? null : new UIPopOpt
            {
                Target = Pop.Target,
                Pos = Pop.Pos
            },
            SkipOpenCheck = SkipOpenCheck,
            OnOpenBefore = OnOpenBefore,
            OnOpen = OnOpen,
            OnFail = OnFail,
            PreLoadResList = PreLoadResList,
        };
    }
}