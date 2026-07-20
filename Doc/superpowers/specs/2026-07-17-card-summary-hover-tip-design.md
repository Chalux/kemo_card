# 卡牌摘要悬停 Tip 设计

**日期：** 2026-07-17  
**状态：** 已确认

## 1. 目标

提供由 `CardDto` 生成卡牌摘要文案的工具函数，并在 `BaseCardItem` 上支持可选的悬停 tip；本轮仅在图鉴（`CodexDlg`）中开启。Tip 复用现有 `KeywordTipService` 的面板与定位，扩展自由文案入口。

## 2. 范围

### 做

- `CardSummaryBuilder`：由 `CardDto` 生成 `(Title, Body)` 本地化摘要
- `KeywordTipService.ShowCustomTips`：不经 `KeywordCatalog`，直接显示 title/desc
- `BaseCardItem`：`EnableHoverTip`（默认 `false`）+ 延迟悬停显示/离开隐藏
- `CodexDlg`：对卡槽启用 `EnableHoverTip`
- 费用 tip 展示的可扩展后缀接口（本轮落地正常费用与 X 费）
- 本地化键：费用后缀、专属前缀
- 单测：摘要拼接、费用后缀、BBCode 剥离、专属行有无

### 不做

- 不为详情弹窗内嵌卡面开启悬停 tip（默认关即可）
- 不新增专用 tip 面板场景
- 不改词条 Catalog / 内置 Keyword 定义
- 不实现生命/金币等其它费用后缀文案（接口预留，回退到正常后缀）

## 3. 架构

```
CardDto
   │
   ▼
CardSummaryBuilder.Build ──► CardSummaryTip(Title, Body)
   │
   ▼
BaseCardItem (EnableHoverTip) ── MouseEntered(delay) ──► KeywordTipService.ShowCustomTips
                                                      └── KeywordTipPanel（复用）
CodexDlg 仅打开 EnableHoverTip
```

依赖方向：`mod` → `frame`；Builder 放在 `Src/mod/global/Ui/`（与 `CardDescBuilder` 同层）。

## 4. 文案格式

### 4.1 Tip 结构

- **Title**：卡名（`Localization.Tr(card.DisplayNameId)`）
- **Body** 多行：

| 行 | 内容 | 规则 |
|----|------|------|
| 1 | `费用 属性 职业` | 空格连接；缺项跳过，不留多余空格 |
| 2 | 效果描述 | 复用 `CardDescBuilder`，再剥 BBCode/`[url=...]` 为纯文本 |
| 3 | 专属 | 仅当有专属角色名时输出 |

示例：

```text
打击
3费 红 战士
造成 6 点伤害。
专属于此角色：可萝
```

无专属时不输出第 3 行。

### 4.2 费用

- 正常费用（`Energy` 等非 X、非 None）：`{cost}{suffix}` → 中文 `3费`，英文 `3 Cost`
- X 费：`X{suffix}` → 中文 `X费`，英文 `X Cost`
- `None`：费用段省略

可扩展接口（建议放在 `CardUiDefinitions` 或 Builder 旁）：

```csharp
// 按 CostType 取 tip 用后缀本地化键；未知类型回退到正常后缀键
bool TryGetCostTipSuffixKey(ECostType costType, out string key);

// 组合数值/符号 + 后缀，得到 tip 用费用段
string FormatCostForTip(ECostType costType, int cost);
```

本轮映射：

| CostType | 行为 |
|----------|------|
| `None` | 返回空 |
| `X` | `X` + `UI_CARD_TIP_COST_SUFFIX_X` |
| 其它（含 Energy） | `cost.ToString()` + `UI_CARD_TIP_COST_SUFFIX` |

### 4.3 属性 / 职业

- 属性 = 元素（`CardDto.Element` flags），用已有 `UI_ELEMENT_*`；多元素用 `、`；无则跳过
- 职业 = `ERole`，用已有 `UI_ROLE_*`；`None` 跳过

### 4.4 专属角色

- 卡牌侧仅有 `IsExclusive`；角色名由调用方注入：`Func<string /*cardId*/, string? /*displayName*/>`
- 解析约定：遍历角色表，找 `Cards` 含该卡 id 的角色，取其本地化显示名；找不到或非专属 → `null`，不输出专属行
- 有值时：`translate(UI_CARD_TIP_EXCLUSIVE_PREFIX) + characterName`  
  中文前缀：`专属于此角色：`（**不含** `【】`）

### 4.5 本地化键（`Resource/Locale/strings.csv`）

| 键 | zh | en |
|----|----|----|
| `UI_CARD_TIP_COST_SUFFIX` | 费 |  Cost |
| `UI_CARD_TIP_COST_SUFFIX_X` | 费 |  Cost |
| `UI_CARD_TIP_EXCLUSIVE_PREFIX` | 专属于此角色： | Exclusive to: |

说明：英文后缀前带空格，使 `3 Cost` / `X Cost` 自然断词；中文无空格。

## 5. API

### 5.1 CardSummaryBuilder

```csharp
public readonly record struct CardSummaryTip(string Title, string Body);

public static class CardSummaryBuilder
{
    public static CardSummaryTip Build(
        CardDto card,
        Func<string, SkillDto?> resolveSkill,
        Func<string, string> translate,
        Func<string, string?> resolveExclusiveCharacterName);
}
```

### 5.2 KeywordTipService

```csharp
public void ShowCustomTips(
    Control anchor,
    IReadOnlyList<(string Title, string Desc)> tips,
    TipSide preferSide = TipSide.Right);
```

- 复用 `_panelScene` / 定位 / 单锚点语义
- 不查 `KeywordCatalog`；空列表则 Hide

### 5.3 BaseCardItem

- `[Export] bool EnableHoverTip` 默认 `false`
- `[Export] float TipDelaySec` 默认 `0.15f`
- `[Export] TipSide PreferTipSide` 默认 `Right`
- 悬停逻辑对齐 `BaseButton` 的 tip 延迟模式；仅 `EnableHoverTip && _card != null` 时生效
- 显示时内部解析 Store（skill / 专属角色）并调用 Builder → `ShowCustomTips`
- `_ExitTree` / `MouseExited` 时 `HideTips(this)`

### 5.4 CodexDlg

- `CacheCardSlots` 后：对各 `_cardSlots[i].EnableHoverTip = true`

## 6. 错误与边界

- Builder 得到 Title、Body 皆空 → 不 Show
- 效果 BBCode 剥离失败时尽量保留可见文本，不抛异常
- `KeywordTipService.Current` 为空 → Warning，不崩溃
- 与词条 tip 共用服务：后调用覆盖前锚点显示

## 7. 测试要点

- 正常费用 / X 费后缀格式
- 费用+元素+职业同行空格拼接与缺项省略
- 效果多 skill 换行；BBCode 剥离
- 有/无专属角色行
- `EnableHoverTip` 默认关闭（逻辑层可测 Builder；UI 交互以图鉴手动验证为主）
