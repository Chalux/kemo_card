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
/// <remarks>
/// 标量字段一律可空：<c>null</c> 表示「本次未指定」，<see cref="MergeFrom"/> 只覆盖显式指定的字段。
/// 读取方一律用 <c>Effective*</c> 取值（把「未指定」收敛为框架默认），不要在调用点散写 <c>??</c>。
/// </remarks>
public class UIOpenOpt
{
    /// <summary>未指定缓存时间时的默认值（毫秒）。</summary>
    public const int DefaultCacheTime = 30000;

    /// <summary>
    /// 打开的层级
    /// </summary>
    public EUILayer? Layer { get; set; }
    /// <summary>
    /// 打开时添加到的父节点
    /// </summary>
    public Control? Parent { get; set; }

    /// <summary>
    /// 缓存时间,单位:毫秒, -1->永久 0->不缓存 null->未指定
    /// </summary>
    public int? CacheTime { get; set; }

    /// <summary>
    /// 动画类型
    /// None：不播动画
    /// Always：始终播放动画
    /// SkipReOpen：如果界面已打开则不播
    /// null：未指定
    /// </summary>
    public EAnimType? AnimType { get; set; }

    /// <summary>
    /// 是否隐藏层级低于自己的界面（null：未指定）
    /// </summary>
    public bool? HideBelow { get; set; }

    /// <summary>
    /// 是否不遮挡下面的界面（null：未指定）
    /// </summary>
    public bool? NoCover { get; set; }
    /// <summary>
    /// 界面对齐方式（null：未指定）
    /// </summary>
    public EUIAlign? Align { get; set; }
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

    #region 生效值（未指定 → 框架默认）

    /// <summary>生效缓存时间（毫秒）。</summary>
    public int EffectiveCacheTime => CacheTime ?? DefaultCacheTime;

    /// <summary>生效动画类型。</summary>
    public EAnimType EffectiveAnimType => AnimType ?? EAnimType.SkipReOpen;

    /// <summary>生效的「隐藏下层」开关。</summary>
    public bool EffectiveHideBelow => HideBelow ?? false;

    /// <summary>生效的「不遮挡下层」开关。</summary>
    public bool EffectiveNoCover => NoCover ?? false;

    /// <summary>生效的对齐方式。</summary>
    public EUIAlign EffectiveAlign => Align ?? EUIAlign.Center;

    #endregion

    /// <summary>
    /// 把 <paramref name="source"/> 中<b>显式指定</b>的字段合并进本实例。
    /// </summary>
    /// <remarks>
    /// <para><b>只有非 null 字段才覆盖。</b>此前实现对本类的 5 个标量字段（CacheTime / AnimType /
    /// HideBelow / NoCover / Align）是无条件赋值，于是「只想覆写 CacheTime」的调用会把
    /// <c>BaseOpenOpt</c> 提供的 <c>HideBelow = true</c> / <c>Align = Full</c> 一并静默改掉
    /// （见 ui-mod-binding 规格 §2.6）。</para>
    /// <para>合并顺序为 <c>Default → BaseOpenOpt → 层级 → 注册项 → 调用点</c>，后者覆盖前者。</para>
    /// </remarks>
    public void MergeFrom(UIOpenOpt? source)
    {
        if (source is null)
        {
            return;
        }

        if (source.Layer is { } layer) Layer = layer;
        if (source.Parent is { } parent) Parent = parent;
        if (source.CacheTime is { } cacheTime) CacheTime = cacheTime;
        if (source.AnimType is { } animType) AnimType = animType;
        if (source.HideBelow is { } hideBelow) HideBelow = hideBelow;
        if (source.NoCover is { } noCover) NoCover = noCover;
        if (source.Align is { } align) Align = align;
        if (source.Mask is not null) Mask = source.Mask;
        if (source.Pop is not null) Pop = source.Pop;
        if (source.SkipOpenCheck is not null) SkipOpenCheck = source.SkipOpenCheck;
        if (source.OnOpenBefore is not null) OnOpenBefore = source.OnOpenBefore;
        if (source.OnOpen is not null) OnOpen = source.OnOpen;
        if (source.OnFail is not null) OnFail = source.OnFail;
        if (source.PreLoadResList is not null) PreLoadResList = source.PreLoadResList;
    }

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