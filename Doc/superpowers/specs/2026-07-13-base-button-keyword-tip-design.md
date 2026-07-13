# BaseButton 与词条提示系统设计

**日期：** 2026-07-13  
**状态：** 已确认

## 1. 目标

提供可复用的按钮基类 `BaseButton`（可配置悬停/聚焦缩放与按下回弹动画），以及与按钮解耦的**词条提示（Keyword Tip）**能力：在鼠标悬停或手柄/键盘 focus 时，于锚点附近弹出一条或多条说明（类似杀戮尖塔词条提示）。提示逻辑抽成独立服务，供 `BaseButton`、`BaseCardItem` 等组件共同调用。词条支持命名参数，并可被本体与玩家 mod **动态注册**。

## 2. 范围

### 做

- `KeywordCatalog`：词条 id → 标题/说明本地化键；`Register` / `Unregister` / `TryGet`；冲突策略为后写覆盖 + Warning
- `KeywordTipService`：相对锚点 Show/Hide；多条纵向堆叠；左右偏好（默认右）+ 贴边翻到对侧
- Tip 场景：`TipLayer`（顶层）+ `KeywordTipPanel`（单条标题+说明）
- `BaseButton`：可单独开关的悬停/聚焦缩放与按下回弹；hover/focus 时委托 TipService
- `BaseCardItem`：预留同一套 Show/Hide Tips 调用入口（不强制接完所有卡牌交互）
- 少量内置示例词条 + `strings.csv` 本地化键
- 单元测试：Catalog 注册/覆盖/Unregister、命名参数替换、未知 id 行为

### 不做

- 接入完整 `EContentCategory` 内容管线（后续可加 JSON 批量导入）
- 像素级美术定稿（Panel 用简洁可用样式）
- 多锚点同时显示提示（本轮同一时间只服务一个锚点）
- 上下方向的智能避让（本轮仅左右偏好与左右翻转）

## 3. 架构

组合优先：提示与动画分离；卡牌不继承按钮。

```
KeywordCatalog（frame/content，可动态注册）
        │ 查找 + 参数替换
        ▼
KeywordTipService（mod/global/Ui/Tip）
        │ 实例化 / 定位
        ▼
TipLayer + KeywordTipPanel × N

调用方（互不继承）：
  BaseButton   ── hover / focus ──► TipService
  BaseCardItem ── hover / focus ──► TipService
  （缩放/按下动画仅在 BaseButton 内）
```

依赖方向：`mod` → `frame`；Catalog 放在 `frame` 以便 mod 脚本/内容注册且不反向依赖 UI。

## 4. 数据模型与 API

### 4.1 词条定义

```csharp
KeywordEntry {
  Id: string
  TitleKey: string   // 本地化键
  DescKey: string    // 本地化键，可含 {amount} 等命名占位符
}
```

### 4.2 展示请求

```csharp
KeywordTipRequest {
  KeywordId: string
  Params: IReadOnlyDictionary<string, string>  // 命名参数，缺省为空字典
}

enum TipSide { Left, Right }  // 默认 Right
```

### 4.3 KeywordCatalog

| API | 行为 |
|-----|------|
| `Register(entry)` / `TryRegister` | 注册或覆盖；已存在同 id 时覆盖并 `PushWarning` |
| `Unregister(id)` | 移除；不存在则 no-op |
| `TryGet(id, out entry)` | 查找 |
| `Clear()` | 清空（测试 / 全量重载） |

- 本体内置词条在启动时注册一批
- 玩家 mod：运行时 `Register`（后续可再加 JSON 批量导入）；文案走本地化键，mod 翻译 CSV 与现有 `IContentModTranslationLoader` 一致

### 4.4 参数替换

- 说明文案：`Localization.Tr(DescKey)` 后，将 `{name}` 替换为 `Params[name]`
- **缺参：保留 `{name}` 原文**，便于玩家识别为程序/配置错误，而非显示异常
- 多余参数忽略

### 4.5 KeywordTipService

```csharp
Show(Control anchor, IReadOnlyList<KeywordTipRequest> tips, TipSide preferSide = TipSide.Right)
Hide()
// 可选：Hide(Control anchor) — 仅当当前锚点匹配时关闭
```

- 解析 Catalog → 标题/说明本地化 + 参数替换 → 生成 Panel 列表
- 未知 keyword id：跳过该条并 Warning，其余照常显示
- `tips` 为空：不显示（或等价 Hide）
- 新 `Show` 替换当前展示（单锚点）
- 锚点释放 / 控件销毁：自动 Hide

### 4.6 BaseButton

继承 `Godot.Button`，命名空间 `KemoCard.Mod.Global.Ui.Comp`。

| Export / API | 说明 |
|--------------|------|
| 启用悬停缩放 | 默认开；悬停或 focus 时缩放到约 1.05 |
| 启用按下回弹 | 默认开；按下约 0.95 后恢复 |
| Tween 时长 / 缩放倍率 | 可配置 |
| 词条列表 / preferSide | 可配置或代码设置；空则不弹 tip |
| 弹出延迟 | 建议默认约 0.15s，可 Export |

触发：`mouse_entered` / `focus_entered` →（延迟后）Show；`mouse_exited` / `focus_exited` → Hide。

### 4.7 BaseCardItem

增加委托 TipService 的 `ShowTips` / `HideTips`（或等价）入口，词条列表由调用方或后续卡牌数据提供；本轮不强制改 Codex/战斗卡牌交互接线。

## 5. 定位规则

- 提示相对锚点全局矩形放置，挂在顶层 `TipLayer`，避免父控件裁剪
- `preferSide`：Left / Right，默认 Right
- 首选侧放不下整列 tip（超出视口）时，翻到对侧
- 多条纵向堆叠，固定间距
- 本轮不做上/下翻转

## 6. 文件落点

| 内容 | 路径 |
|------|------|
| KeywordEntry / TipRequest / TipSide / Catalog | `Src/frame/content/keywords/` |
| KeywordTipService + Tip 场景 | `Src/mod/global/Ui/Tip/` |
| BaseButton | `Src/mod/global/Ui/Comp/BaseButton.cs`（+ 可选 `.tscn`） |
| BaseCardItem 调用入口 | `Src/mod/global/Ui/Comp/BaseCardItem.cs` |
| 内置词条与本地化 | `Resource/` + `Resource/Locale/strings.csv` |
| 单元测试 | `Tests/kemo_card.Ui.Tests/KeywordCatalogTests.cs` 等 |

## 7. 错误与边界

| 情况 | 行为 |
|------|------|
| 未知词条 id | 跳过 + Warning |
| 缺命名参数 | 保留 `{name}` 原文 |
| id 冲突注册 | 后写覆盖 + Warning |
| 无父级 / 无 TipLayer | Warning，不抛异常 |
| 快速划过 | 延迟取消，不误弹 |

## 8. 验收

1. `BaseButton` 悬停/聚焦可缩放、按下可回弹，且可单独关闭任一项。
2. 配置多条词条后，悬停或 focus 时在优先侧弹出堆叠提示；贴边时翻到对侧。
3. 词条说明中 `{amount}` 等按命名参数替换；缺参时仍显示 `{amount}`。
4. mod / 代码可 `Register` 新词条；覆盖同 id 时打 Warning 且新定义生效。
5. `BaseCardItem` 可通过同一 TipService API 弹出提示。
6. Catalog 相关单元测试通过。
