using Godot;

namespace KemoCard.Mod.Global.Ui.Themes;

/// <summary>
/// 全局调色板：深蓝夜色底 + 琥珀金点缀。
/// 所有 UI 颜色只允许从这里取，禁止在场景/代码里内联魔法色值；
/// <b>场景侧</b>的颜色一律写在主题资源里（见 <see cref="KemoTheme.ResourcePath"/>），本类供
/// 代码侧动态着色（开关轨道、调试面板状态色等）与资源维护时对照使用。
/// </summary>
public static class KemoPalette
{
    // 底色与面板
    public static readonly Color WindowBg = new(0.055f, 0.075f, 0.133f);        // #0E1322（project.godot 的默认清屏色）
    public static readonly Color PanelBg = new(0.086f, 0.110f, 0.180f);         // #161C2E
    public static readonly Color PanelBorder = new(0.165f, 0.204f, 0.314f);     // #2A3450

    // 普通按钮
    public static readonly Color ButtonNormal = new(0.137f, 0.173f, 0.275f);    // #232C46
    public static readonly Color ButtonBorder = new(0.224f, 0.271f, 0.420f);    // #39456B
    public static readonly Color ButtonHover = new(0.180f, 0.227f, 0.369f);     // #2E3A5E
    public static readonly Color ButtonPressed = new(0.102f, 0.129f, 0.220f);   // #1A2138
    public static readonly Color ButtonDisabled = new(0.102f, 0.125f, 0.200f);  // #1A2033
    public static readonly Color ButtonDisabledBorder = new(0.149f, 0.184f, 0.286f); // #262F49

    // 强调色（琥珀金）
    public static readonly Color Accent = new(0.878f, 0.710f, 0.361f);          // #E0B55C
    public static readonly Color AccentHover = new(0.941f, 0.784f, 0.431f);     // #F0C86E
    public static readonly Color AccentPressed = new(0.780f, 0.604f, 0.259f);   // #C79A42
    public static readonly Color AccentDim = new(0.541f, 0.431f, 0.200f);       // #8A6E33
    public static readonly Color AccentDisabled = new(0.290f, 0.271f, 0.212f);  // #4A4536

    // 文本
    public static readonly Color TextPrimary = new(0.914f, 0.925f, 0.957f);     // #E9ECF4
    public static readonly Color TextSecondary = new(0.604f, 0.639f, 0.722f);   // #9AA3B8
    public static readonly Color TextDanger = new(0.894f, 0.459f, 0.420f);      // #E4756B
    public static readonly Color TextOnAccent = new(0.141f, 0.106f, 0.031f);    // #241B08
    public static readonly Color TextDisabled = new(0.353f, 0.388f, 0.475f);    // #5A6379

    // 输入与选中
    public static readonly Color InputBg = new(0.063f, 0.086f, 0.145f);         // #101625
    public static readonly Color Selection = new(0.541f, 0.431f, 0.200f, 0.35f); // 琥珀半透明
}

/// <summary>
/// 全局主题引用与约定常量。
/// </summary>
/// <remarks>
/// <para><b>主题本体是资源文件</b>：<c>Resource/Asset/Theme/kemo_theme.tres</c>，由
/// <c>project.godot</c> 的 <c>gui/theme/custom</c> 在引擎启动时挂载到所有界面（含弹窗与 Tooltip），
/// <b>不再在运行时用代码动态构建</b>——改样式只改资源，改色板时两侧一起对照。</para>
/// <para>新增控件样式必须写进该资源；代码里只保留"动态着色 / 变体名"这类常量。</para>
/// </remarks>
public static class KemoTheme
{
    /// <summary>全局主题资源路径（样式唯一事实源）。</summary>
    public const string ResourcePath = "res://Resource/Asset/Theme/kemo_theme.tres";

    /// <summary>主按钮的 TypeVariation 名；资源里同名变体的 <c>base_type</c> 为 <c>Button</c>。</summary>
    public const string PrimaryVariation = "Primary";
}
