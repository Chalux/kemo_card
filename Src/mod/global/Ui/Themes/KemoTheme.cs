using Godot;

namespace KemoCard.Mod.Global.Ui.Themes;

/// <summary>
/// 全局调色板：羊皮纸卡桌——暖米白纸面 + 墨色描边，墨绿做主操作、酒红做危险/强调。
/// 所有 UI 颜色只允许从这里取，禁止在场景/代码里内联魔法色值；
/// <b>场景侧</b>的颜色一律写在主题资源里（见 <see cref="KemoTheme.ResourcePath"/>），本类供
/// 代码侧动态着色（开关轨道、列表锁定项底色、状态文案等）与资源维护时对照使用。
/// 权威见 <c>Doc/superpowers/specs/2026-09-21-ui-parchment-redesign-design.md</c> §2。
/// </summary>
public static class KemoPalette
{
    // 纸面
    public static readonly Color WindowBg = new(0.945f, 0.906f, 0.827f);        // #F1E7D3 Paper（project.godot 的默认清屏色）
    public static readonly Color PanelBg = new(0.980f, 0.953f, 0.890f);         // #FAF3E3 PaperLight（卡片/按钮/输入框面）
    public static readonly Color RailBg = new(0.894f, 0.839f, 0.737f);          // #E4D6BC PaperDark（左栏、悬停底）
    public static readonly Color SurfaceSunken = new(0.827f, 0.757f, 0.627f);   // #D3C1A0 PaperDeep（下沉区、锁定项底）
    public static readonly Color PanelBorder = new(0.227f, 0.180f, 0.145f);     // #3A2E25 Ink（描边）

    // 普通按钮
    public static readonly Color ButtonNormal = new(0.980f, 0.953f, 0.890f);    // #FAF3E3
    public static readonly Color ButtonBorder = new(0.227f, 0.180f, 0.145f);    // #3A2E25
    public static readonly Color ButtonHover = new(1.000f, 0.976f, 0.925f);     // #FFF9EC
    public static readonly Color ButtonPressed = new(0.827f, 0.757f, 0.627f);   // #D3C1A0
    public static readonly Color ButtonDisabled = new(0.894f, 0.839f, 0.737f);  // #E4D6BC
    public static readonly Color ButtonDisabledBorder = new(0.659f, 0.588f, 0.502f); // #A89680

    // 强调色（墨绿：主操作 / 选中 / 开关开启）
    public static readonly Color Accent = new(0.184f, 0.365f, 0.314f);          // #2F5D50
    public static readonly Color AccentHover = new(0.231f, 0.435f, 0.376f);     // #3B6F60
    public static readonly Color AccentPressed = new(0.145f, 0.286f, 0.247f);   // #25493F
    public static readonly Color AccentDim = new(0.561f, 0.639f, 0.612f);       // #8FA39C
    public static readonly Color AccentDisabled = new(0.561f, 0.639f, 0.612f);  // #8FA39C

    // 警示色（酒红：危险按钮 / 警示文案 / 焦点环）
    public static readonly Color Danger = new(0.545f, 0.180f, 0.227f);          // #8B2E3A
    public static readonly Color DangerHover = new(0.639f, 0.220f, 0.271f);     // #A33845
    public static readonly Color DangerPressed = new(0.435f, 0.141f, 0.180f);   // #6F242E

    // 文本
    public static readonly Color TextPrimary = new(0.227f, 0.180f, 0.145f);     // #3A2E25 Ink
    public static readonly Color TextSecondary = new(0.431f, 0.369f, 0.314f);   // #6E5E50 InkSoft
    public static readonly Color TextDanger = new(0.545f, 0.180f, 0.227f);      // #8B2E3A Wine
    public static readonly Color TextOnAccent = new(0.961f, 0.933f, 0.867f);    // #F5EEDD
    public static readonly Color TextDisabled = new(0.659f, 0.588f, 0.502f);    // #A89680 InkFaint

    // 输入与选中
    public static readonly Color InputBg = new(1.000f, 0.992f, 0.969f);         // #FFFDF7
    public static readonly Color Selection = new(0.184f, 0.365f, 0.314f, 0.3f); // 墨绿半透明
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