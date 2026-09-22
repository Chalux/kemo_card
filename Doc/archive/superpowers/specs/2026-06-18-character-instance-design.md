# 角色实例 / 战斗实例 / 卡组预设 / 手牌槽位 设计规格

**日期**：2026-06-18  
**状态**：已确认（首期实现范围）  
**范围**：`DeckPreset`、`CharacterInstance`、`HandSlot`、`CharacterBattleInstance` 及直接依赖的类型（`CharacterAttributes`、`CardStatBlockDto`）

---

## 1. 首期实现边界

### 本期实现

| 类型 | 路径 |
|------|------|
| `DeckPreset` | `Src/mod/combat/DeckPreset.cs` |
| `CharacterInstance` | `Src/mod/combat/CharacterInstance.cs` |
| `HandSlot` | `Src/mod/combat/HandSlot.cs` |
| `CharacterBattleInstance` | `Src/mod/combat/CharacterBattleInstance.cs` |
| `CharacterAttributes` | `Src/mod/combat/CharacterAttributes.cs` |
| `CardStatBlockDto` | `Src/frame/content/definitions/CardStatBlockDto.cs`（扩展 `CardDto`） |

### 后续单独文档实现（本期不做）

- `BuffInstance` 及 Buff 结算管线
- 局内存档（`RunSaveDto`、`RunSaveService`、`ToSaveDto` / `FromSaveDto`）
- `ObtainedCardPool` 类型（本期构筑校验通过方法参数传入已获得卡 id 集合）
- `TeamBattleState`、队伍共用 HP
- `CharacterBattleFactory` 独立类（本期用 `CharacterBattleInstance.TryCreate` 静态方法）
- 战斗阶段机、`CombatTurnController`

---

## 2. 已确认玩法规则

| 决策 | 结论 |
|------|------|
| `CharacterDto` | 无战斗属性；`cards` 为角色专属构筑池 |
| 角色属性 | 当前卡组内卡牌 `stats` 字段 **逐项求和** |
| 卡组数量 | 初始 **1 套**；`TryCreateDeck()` 最多 **10 套**；**不可删除** |
| 新建卡组默认 | 填入专属卡；超过 10 张取 `CharacterDto.Cards` **前 10 张** |
| 每套卡组 | 最多 10 张、不可重复 |
| 构筑卡来源 | 已获得卡 id 集合（调用方传入）∪ 角色专属卡 |
| 构筑时机 | 战斗外；`IsDeckLocked == true` 时禁止编辑 |
| 手牌 | 固定 5 槽；槽位效果本期用 `HandSlotEffectRef`（仅 `buffId` + `params`）占位，后续替换为 `BuffInstance` |
| 战斗实例 | 不参与存档；由 `CharacterInstance` 当前卡组生成牌库并洗牌 |

---

## 3. 类型职责

### `DeckPreset`

- `DeckId`、`DisplayName?`、`CardIds`（≤10，无重复）
- `CreateWithExclusiveCards(CharacterDto)`
- `TryAddCard` / `TryRemoveCard`
- `Validate(IReadOnlySet<string> buildableCardIds)`

### `CharacterInstance`

- 构造：`CharacterInstance(CharacterDto)` 与无参 `CharacterInstance()`
- `TryCreateDeck()`、`TryEditDeck`、`TrySetCurrentDeck`
- `GetBuildableCardIds(IReadOnlySet<string> obtainedCardIds)`
- `ComputeAttributes(GameDefinitionRegistry)` — 基于当前卡组
- `SetDeckLocked(bool)` — 由上层战斗流程调用

### `HandSlot`

- `PlaceCard` / `ClearCard`
- `SlotEffects`：`List<HandSlotEffectRef>`（后续对接 Buff 系统）

### `CharacterBattleInstance`

- `TryCreate(CharacterInstance, GameDefinitionRegistry, HostRng, out error)`
- 牌库 / 5 手牌槽 / 墓地、`CardRuntimeEntry`
- 能量三元组与 `BaseAttributes` 快照
- `HasActed`

---

## 4. 自检

- 无局内存档、无 BuffInstance、无队伍 HP — 与首期范围一致  
- 手牌槽位效果可扩展 — 通过 `HandSlotEffectRef` 预留  
- 属性唯一定义在 `CardDto.stats` — 与「CharacterDto 无属性」一致
