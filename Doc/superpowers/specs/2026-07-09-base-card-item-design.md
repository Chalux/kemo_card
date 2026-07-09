# BaseCardItem 卡牌基础组件设计

日期：2026-07-09

## 1. 目标

完成 `BaseCardItem` 的展示逻辑：根据 `CardDto` 刷新费用、类型、基础数值、元素色环、立绘与卡框；支持战斗中动态覆盖数值；并为每个展示子项提供独立修改接口。

布局仍在 `BaseCardItem.tscn`；C# 只负责数据绑定与刷新。

## 2. 范围

### 做

- `CardDto` 增加 `baseValue`
- `Definitions.cs` 扩展：卡类型本地化键、稀有度→卡框路径、元素→颜色辅助
- `strings.csv` 增加 `UI_CARD_TYPE_*` 短标签
- `BaseCardItem`：`SetData` / `SetDisplayValue` + 分项 Set API
- 场景 `node_paths` 绑定 Export
- `ModScriptCatalog`（及启动结果暴露）支持解析 Mod content 根，供立绘回退
- 纯逻辑单测（DTO、Definitions、元素拆分）

### 不做

- 不改 CodexDlg / 战斗特效系统（只提供 UI 覆盖接口）
- 不新增卡面 / 卡框美术资源文件（缺资源时隐藏）
- 不迁移 `CardDto.Element` 类型（仍为 `int`，按 `EElement` 位标志解释）

## 3. 数据与 Definitions

### 3.1 CardDto

新增字段：

| JSON | 属性 | 类型 | 说明 |
|------|------|------|------|
| `baseValue` | `BaseValue` | `int` | 卡牌基础展示数值；非战斗固定显示 |

示例卡 JSON（如 `strike.json`）按需补默认值；缺省为 `0`。

### 3.2 Definitions 扩展（`Src/mod/global/Def/Definitions.cs`）

在现有 `ColorDefinitions` 旁增加静态表 / 辅助方法（可同文件或同目录拆类，保持 mod 层）：

| 名称 | 作用 |
|------|------|
| `CardTypeLocaleKeys` | `ECardType` → 本地化键（如 `UI_CARD_TYPE_PHYSICS`） |
| `CardFramePaths` | `ERarity` → `res://Resource/Assets/CardFrame/{Rarity}.png` 约定路径 |
| `TryGetElementColor(EElement)` | 映射到 `ColorDefinitions.*` |
| `CollectElementColors(int flags, Span/List)` | 按 `EElement` 位顺序收集最多 3 色；0 个则回退 `NoneElement` |

卡类型键示例：

- `UI_CARD_TYPE_PHYSICS` → 物 / Phy
- `UI_CARD_TYPE_MAGICAL` → 魔 / Mag
- `UI_CARD_TYPE_SUPPORT` → 支 / Sup
- …（覆盖全部 `ECardType`）

写入 `Resource/Locale/strings.csv`（zh_CN / en）。

## 4. API

### 4.1 整体绑定

```csharp
void SetData(CardDto? card);
void SetDisplayValue(int? value); // null → 回退 BaseValue
```

`SetData`：

1. 缓存 `_card`、`_baseValue = card?.BaseValue ?? 0`，清除 `_displayOverride`
2. 刷新 Cost / Type / Value / Element / Art / Frame
3. `card == null`：清空文案、色环 None、隐藏纹理

### 4.2 分项接口

| 方法 | 行为 |
|------|------|
| `SetCost(int cost)` | 更新费用数字（不改 CostType 语义时由调用方保证） |
| `SetCostVisible(bool)` | 显示/隐藏费用 |
| `SetCardType(ECardType)` | Definitions 取键 → `Localization.Tr` |
| `SetBaseValue(int)` | 更新缓存基础值；无覆盖时刷新数值标签 |
| `SetElement(int elementFlags)` | 拆色写入 shader |
| `SetArt(Texture2D?)` | 直接设立绘；null 隐藏 |
| `SetArtFromPath(string artPath, string? cardId = null)` | Mod→Resource 回退 |
| `SetCardFrame(Texture2D?)` | 直接设卡框；null 隐藏 |
| `SetCardFrameFromRarity(ERarity)` | Definitions 路径加载 |

分项接口只改对应节点与相关缓存，不重跑整卡绑定。

### 4.3 费用显示规则

- `CostType == None`：隐藏费用
- `CostType == X`：显示 `"X"`
- 其他：显示 `Cost` 十进制数字

## 5. 立绘与卡框

### 5.1 立绘回退

`artPath` 非空时：

1. `AppRoot.Services.ContentModPipeline.Registry.TryGetOwnerModId(Card, cardId)`
2. Catalog 解析 content 根：`FolderPath + Manifest.ContentRoot`
3. 文件系统存在 `{contentRoot}/{artPath}` → 加载纹理
4. 否则 `res://Resource/Assets/{artPath}`（`ResourceLoader.Exists` + `Load`）
5. 都失败或路径空：隐藏 `_trArt`

### 5.2 Catalog / 启动暴露

- `ModScriptCatalog` 缓存 `modId → (FolderPath, ContentRoot)`，提供 `TryGetContentRootPath`
- `Rebuild(activeMods)` 从 `DiscoveredModEntry.Manifest` 写入
- 通过 `ContentModPipeline` 只读暴露 catalog，或写入 `ModStartupResult`，供 `AppRoot.Services` 访问

### 5.3 卡框

`Definitions.CardFramePaths[rarity]` → `ResourceLoader`；失败则隐藏 `_trCardFrame`。本轮不强制提交 png。

## 6. 元素色环

- 输入 `int` 按 `EElement` 标志位顺序收集（跳过 `None`）
- 最多 3 色写入 `CRAttr` shader：`colors`、`color_count`
- 0 个有效位：单色 `NoneElement`（保持色环可见）
- \>3：取前 3，`GD.PushWarning` 截断提示

## 7. 场景绑定

`BaseCardItem.tscn` 根节点配置 `node_paths`，对齐：

| Export | 节点 |
|--------|------|
| `_trArt` | `CArtRoot/TRArt` |
| `_trCardFrame` | `TRCardFrame` |
| `_txtCardCost` | `TCost` |
| `_txtCardType` | `TType` |
| `txtCardVal` | `TCardVal`（实现时统一为 `_txtCardVal`） |
| `_crAttr` | `CRAttr` |

清理占位硬编码文案（费用/类型/数值改为空或占位键，运行时由逻辑填充）。

## 8. 错误处理

- 缺资源 / 缺 owner Mod / AppRoot 未初始化：不向调用方抛异常；对应纹理隐藏，必要时 `PushWarning`
- Export 未绑定：对应刷新 no-op

## 9. 测试

- `CardDto` JSON 往返含 `baseValue`
- Definitions：类型键、稀有度路径、元素色映射
- `CollectElementColors`：0 / 1 / 3 / >3
- 不强制 Godot 场景集成测

## 10. 关键文件

| 文件 | 变更 |
|------|------|
| `Src/mod/global/Ui/Comp/BaseCardItem.cs` | 实现逻辑 |
| `Src/mod/global/Ui/Comp/BaseCardItem.tscn` | node_paths、清理占位 |
| `Src/mod/global/Def/Definitions.cs` | 键表 / 路径 / 颜色辅助 |
| `Src/frame/content/definitions/CardDto.cs` | `BaseValue` |
| `Src/frame/scripting/ModScriptCatalog.cs` | content 根解析 |
| `Src/frame/content/ContentModPipeline.cs` 或 `ModStartupResult` | 暴露 catalog |
| `Resource/Locale/strings.csv` | 卡类型键 |
| `Config/mods/base-game/content/cards/*.json` | 按需补 `baseValue` |
| `Tests/...` | DTO / Definitions / 元素辅助 |
