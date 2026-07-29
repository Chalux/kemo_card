# 卡牌详情对话框（CardDetailsDlg）设计

**日期：** 2026-07-13  
**状态：** 已确认

## 1. 目标

完成卡牌详情对话框：从 `BaseCardItem` 点击打开；展示卡面、卡名、所属 Mod、画师与技能描述；描述内 BBCode 关键词可悬停弹出词条 tip。详情页内的卡牌必须关闭点击打开，避免递归套娃。

## 2. 范围

### 做

- `CardDetailsDlg`：继承 `BaseDlg`，绑定已有场景节点并按 payload 刷新
- UI 注册：`GlobalUiIds.CardDetails` + `GlobalMod.GetUIRegistrations`
- `BaseCardItem`：`ECardClickAction`（`None` / `OpenDetails`，可扩展）；默认 `OpenDetails`
- `CardDto` 增加 `artistNameId`（本地化键）
- 描述：按 `skillRefs` → `SkillDto.DescId` 本地化后换行拼接
- 描述词条 tip：BBCode `[url=kw:{id}]...[/url]` + `meta_hover_*` → `KeywordTipService`
- 可选纯逻辑 `CardDescBuilder` / meta 解析 helper，便于单测
- OwnerMod 显示名解析（见 §4.3）；示例内容补 `artistNameId` 与关键词标记
- 单测：描述拼接、meta 解析、相关默认值

### 不做

- 不按 Catalog 标题自动扫词高亮
- 不做战斗专用复杂详情（payload 仅留可选 `DisplayValue`）
- 不改图鉴过滤 / 其它 Tab
- 本轮 meta 协议先只支持 `kw:{keywordId}`（命名参数扩展可后续加）

## 3. 架构

采用方案 A：对话框自洽；点击打开内聚在 `BaseCardItem`。

```
BaseCardItem (ClickAction.OpenDetails)
        │ 左键点击
        ▼
UIManager.OpenAsync(CardDetailsDlg, CardDetailsDlgPayload)
        │
        ▼
CardDetailsDlg : BaseDlg
  ├─ BaseCardItem (ClickAction.None)  ← 防递归
  ├─ TxtCardName / TxtModName / TxtArtistName
  └─ RTCardDesc ── meta_hover ──► KeywordTipService
```

依赖方向：`mod` → `frame`（DTO / Catalog / UI 基类）；Tip 仍用现有 `KeywordTipService`。

## 4. 数据模型与 API

### 4.1 CardDto

| JSON | 属性 | 类型 | 说明 |
|------|------|------|------|
| `artistNameId` | `ArtistNameId` | `string` | 画师本地化键；空则详情页隐藏画师 Label |

### 4.2 Payload

```csharp
CardDetailsDlgPayload {
  CardId: string       // 必填
  DisplayValue: int?   // 可选；有则覆盖卡面展示数值
}
```

打开后按 `CardId` 从 `GameDefinitionStore` 取 `CardDto`；找不到则 Warning 并关闭对话框。

### 4.3 字段绑定

| UI | 来源 |
|----|------|
| BaseCardItem | `SetData(card)`；若 payload 有 `DisplayValue` 再 `SetDisplayValue` |
| TxtCardName | `Localization.Tr(card.DisplayNameId)` |
| TxtModName | `TryGetOwnerModId(Card, id)` → 查 Mod 显示名键 → `Tr`；失败则显示 modId 或清空 |
| TxtArtistName | `Tr(artistNameId)`；空则 `Visible = false` |
| RTCardDesc | 技能描述拼接结果（BBCode 已启用） |

**Mod 显示名：** `mod.json` 的 `displayName` 已是本地化键（如 `MOD_BASE_GAME_DISPLAY_NAME`）。本轮在 `ModScriptCatalog`（或等价薄层）于 Rebuild 时缓存 `modId → displayNameKey`，供详情页查询；禁止在 UI 里硬编码明文 Mod 名。

### 4.4 描述拼接

```csharp
// CardDescBuilder（建议）
string Build(CardDto card, Func<string, SkillDto?> resolveSkill, Func<string, string> translate)
```

- 按 `skillRefs` 顺序解析技能
- 缺技能或 `DescId` 为空：跳过
- 多段用换行拼接（`\n`）
- 翻译文案可含 BBCode，例如：`造成 6 点伤害。获得 [url=kw:exhaust]消耗[/url]。`

### 4.5 Meta 词条协议

| meta 字符串 | 行为 |
|-------------|------|
| `kw:{keywordId}` | 解析 id，构造 `KeywordTipRequest`，相对 `RTCardDesc` ShowTips |
| 其它 / 空 | Warning，不弹 |

本轮不做 query 参数（如 `?amount=3`）。

## 5. ClickAction

```csharp
enum ECardClickAction {
  None,         // 不响应（详情页内）
  OpenDetails,  // 默认：打开 CardDetailsDlg
}
```

- `[Export] ClickAction`，默认 `OpenDetails`
- 左键且 `OpenDetails` 且已有 `_card` → `OpenAsync`
- `None` 或无数据 → 忽略
- 详情场景中的 `BaseCardItem`：Export 设为 `None`（OnOpen 可再强制一次）
- 再次打开同一 UI：刷新 payload（沿用 UIManager），不叠多层
- `UIManager` 未就绪：Warning，不抛异常

Payload 中 `DisplayValue` 传当前 `_displayOverride`（可为 null）。

## 6. CardDetailsDlg 生命周期

| 时机 | 行为 |
|------|------|
| InitEvent | 绑定 `RTCardDesc` 的 `meta_hover_started` / `meta_hover_ended`（只绑一次） |
| OnOpen / UpdateView | 读 payload → 查卡 → 刷新全部控件 |
| meta hover start | 解析 `kw:` → ShowTips |
| meta hover end / OnClose | HideTips |

关闭按钮继续由已有 `BaseDlgComp` 处理。

## 7. 文件落点

| 内容 | 路径 |
|------|------|
| 对话框逻辑 | `Src/mod/global/Ui/CardDetailsDlg.cs`（场景已存在） |
| 描述/meta helper | `Src/mod/global/Ui/CardDescBuilder.cs`（或同目录小类） |
| 点击枚举 + 打开 | `Src/mod/global/Ui/Comp/BaseCardItem.cs`（枚举可同文件或 `Def/`） |
| DTO | `Src/frame/content/definitions/CardDto.cs` |
| Mod 显示名缓存 | `Src/frame/scripting/ModScriptCatalog.cs`（或紧邻扩展） |
| UI Id / 注册 | `GlobalUiIds.cs`、`GlobalMod.cs` |
| 示例 JSON / 翻译 | `Config/mods/base-game/...`、`strings.csv` |
| 单测 | `Tests/kemo_card.Ui.Tests/` |

## 8. 错误与边界

| 情况 | 行为 |
|------|------|
| 未知 CardId | Warning 并关闭对话框 |
| 技能缺失 / DescId 空 | 跳过该段 |
| 未知 keyword id | TipService / 调用方 Warning，不弹 |
| 非法 meta | Warning，不弹 |
| 无 OwnerMod | Mod 名清空或显示 modId |
| UIManager 为空 | Warning，不打开 |

## 9. 验收

1. 图鉴（或任意 `OpenDetails` 卡牌）左键可打开详情，卡面与文案正确。
2. 详情页内卡牌再点不会再次打开 / 套娃。
3. 描述中 `[url=kw:...]` 悬停弹出 tip，离开消失。
4. 关闭详情时 tip 一并关闭。
5. 画师键为空时画师 Label 隐藏；有键时显示本地化文案。
6. 相关单元测试通过。
