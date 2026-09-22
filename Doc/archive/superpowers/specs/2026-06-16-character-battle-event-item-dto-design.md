# 角色 / 敌人 / 战斗 / 事件 / 道具 内容 DTO 设计规格

**日期**：2026-06-16  
**最后同步**：2026-06-17（与代码 `Src/frame/content/definitions/` 对齐）  
**状态**：已实现  
**范围**：Character / Enemy / Battle / Event / Item 五类 Mod JSON DTO、枚举、加载与校验

---

## 1. 已确认边界

| 决策点 | 结论 |
|--------|------|
| CharacterDto | 仅可操控友方角色（4 人小队） |
| 敌人 | 独立 `EnemyDto`（`content/enemies/*.json`），`BattleDto` 引用 |
| ItemDto | 仅消耗品（使用后消失） |
| EventDto | 混合：`eventKind` 区分数据驱动 vs 脚本驱动 |
| Battle | 多波次 + 可选 `scriptPath` + 战后奖励 |

---

## 2. 引用链

- Character：`cards` → Card；`skillRefs` → Skill；`activeSkillChain[].skillId` → Skill；`passives[].buffId` → Buff
- Enemy：`skillRefs` → Skill；`buffRefs` → Buff
- Battle：`waves[].enemySpawns[].enemyId` → Enemy；`enemySpawns[].skillOverrides` → Skill；奖励 `ItemGrant`/`Effect`/`CardChoice` 引用 Item/Effect/Card
- Event（Data）：`options[].effectRefs` → Effect
- Item：`useSkillRefs` → Skill

---

## 3. Mod 目录与加载

```
content/characters/*.json
content/enemies/*.json
content/battles/*.json
content/events/*.json
content/items/*.json
```

- `id` = 文件名（无扩展名）；`ContentModLoader.LoadDefinitions<T>()` 加载时注入 DTO `Id`
- `EContentCategory` 新增 `Enemy`，与 Character / Battle / Event / Item 并列
- 合并后由 `GameDefinitionRegistry.Rebuild()` 调用 `ContentDefinitionValidator`，非法定义从 `GameDefinitionStore` 剔除

---

## 4. 枚举（`ContentEnums.cs`）

### 本规格直接使用

| 枚举 | 说明 |
|------|------|
| `EElement` | `[Flags]`：None, Fire, Water, Wind, Earth, Dark, Light |
| `ERole` | None, Warrior, Wizard, Healer, Guard, Shield, Controller, Support, CardPlayer, SwordMan, Mage, Alchemist |
| `ERace` | `[Flags]`：None, Human, Animal（动物：原犬/猫/鸟/兽/爬行合并）, Insect, Fish, Plant, Machine, Demon（恶魔族：原 Demonic + Devil）, Angel, Dragon, God, Undead, Academic（学术）, Fantasy（幻想）, Astronomy（天文）, Hero（英雄）, Calamity（灾厄）, Unknown（未知，2026-09-21 由 UnKnown 更名） |
| `EEventKind` | Data, Script |
| `ERewardKind` | Gold, CardChoice, ItemGrant, Effect |
| `ERarity` | ItemDto 稀有度：Common, Uncommon, Rare, Epic, Legendary |

### 共用（定义于同一文件，被嵌套 DTO 引用）

`ETargetSide`, `ETargetScope`, `ERetargetPolicy`（ItemDto `targetSpec` 与 CardDto 目标字段共用）。

### 与 CardDto 的差异

- Character / Enemy 的 `element`、`role` 已使用 `EElement` / `ERole`
- CardDto 的 `element` 仍为 `int`（位标志，见 `ElementFlags`）；`role` 已迁移为 `ERole`

---

## 5. DTO 字段

### CharacterDto（`CharacterDto.cs`）

| JSON 字段 | 类型 | 说明 |
|-----------|------|------|
| `id` | string | 加载时注入 |
| `displayNameId` | string | 本地化键 |
| `element` | EElement | 元素 |
| `role` | ERole | 职业/定位 |
| `race` | ERace | 种族（可组合 Flags） |
| `skillRefs` | SkillRefDto[] | 角色技能池 |
| `activeSkillChain` | ActiveSkillChainEntryDto[] | 主动技蓄力链（战斗主动按钮只读本字段） |
| `passives` | PassiveRefDto[] | 潜能门闩被动：解锁后开战挂对应 buff（**角色唯一的"常驻增益"通道**，2026-09-21 起取代已移除的 `buffRefs`） |
| `cards` | string[] | 角色专属构筑池（非 Run 初始牌库） |
| `maxEnergy` | int | 能量上限 |
| `initialEnergy` | int | 初始能量 |
| `artPath` | string | 立绘路径 |
| `tags` | string[] | 标签 |

### EnemyDto（`EnemyDto.cs`）

| JSON 字段 | 类型 | 说明 |
|-----------|------|------|
| `id` | string | 加载时注入 |
| `displayNameId` | string | |
| `descId` | string | |
| `maxHp` | int | 必须 > 0 |
| `element` | EElement | |
| `role` | ERole | |
| `skillRefs` | SkillRefDto[] | AI 技能池（运行时规范见开放项） |
| `buffRefs` | BuffRefDto[] | |
| `artPath` | string | |
| `scriptPath` | string? | 可选自定义 AI 脚本 |
| `tags` | string[] | |

### BattleDto（`BattleDto.cs`）

| JSON 字段 | 类型 | 说明 |
|-----------|------|------|
| `id` | string | |
| `displayNameId` | string | |
| `descId` | string | |
| `waves` | BattleWaveDto[] | 至少一波 |
| `scriptPath` | string? | 可选战斗脚本 |
| `rewards` | BattleRewardDto | 战后奖励 |
| `backgroundPath` | string? | |
| `musicId` | string? | |
| `tags` | string[] | |

**嵌套类型**（`SharedDefinitionDtos.cs`）：

- `BattleWaveDto`：`enemySpawns`（非空）、`waveScriptPath?`
- `EnemySpawnDto`：`enemyId`、`count`（默认 1）、`hpScale?`、`skillOverrides?`
- `BattleRewardDto`：`entries`
- `RewardEntryDto`：`kind`、`weight`（默认 1）、`params?`

**奖励 `params` 约定**（首版 `Dictionary<string, object>`，加载期部分校验）：

| kind | params 键 | 校验 |
|------|-----------|------|
| Gold | `min`, `max` | 无引用校验 |
| CardChoice | `count`, `pool`；可选 `cardIds` | 若提供 `cardIds`，逐项校验 Card 存在 |
| ItemGrant | `itemId` | 必填，校验 Item 存在 |
| Effect | `effectId` | 必填，校验 Effect 存在 |

### EventDto（`EventDto.cs`）

| JSON 字段 | 类型 | 说明 |
|-----------|------|------|
| `id` | string | |
| `displayNameId` | string | |
| `descId` | string | |
| `eventKind` | EEventKind | Data 或 Script |
| `artPath` | string | |
| `pages` | EventPageDto[] | 叙事页 |
| `options` | EventOptionDto[] | 选项（Data 事件必填） |
| `scriptPath` | string? | Script 事件必填 |
| `tags` | string[] | |

**嵌套类型**：

- `EventPageDto`：`textId`、`imagePath?`
- `EventOptionDto`：`optionId`、`labelId`、`descId?`、`conditions`、`effectRefs`、`nextPageIndex?`

**eventKind 约束**：

- `Data`：`options` 至少一项；各 `effectRefs` 校验 Effect 存在
- `Script`：`scriptPath` 非空

### ItemDto（`ItemDto.cs`）

| JSON 字段 | 类型 | 说明 |
|-----------|------|------|
| `id` | string | |
| `displayNameId` | string | |
| `descId` | string | |
| `rarity` | ERarity | |
| `useSkillRefs` | SkillRefDto[] | 非空；使用后触发技能链 |
| `targetSpec` | TargetSpecDto? | 目标规格 |
| `maxStack` | int | 默认 1 |
| `artPath` | string | |
| `shopPrice` | int? | 商店价格 |
| `tags` | string[] | |

### 共用嵌套 DTO（`SharedDefinitionDtos.cs`）

| 类型 | 字段 |
|------|------|
| `SkillRefDto` | `skillId`, `params?` |
| `BuffRefDto` | `buffId`, `params?` |
| `EffectRefDto` | `effectId`, `params?` |
| `TargetSpecDto` | `side`, `scope`, `targetCount`（默认 1）, `retargetPolicy` |
| `ConditionRefDto` | `kind`, `params?`（Event 选项条件） |

---

## 6. 校验规则（`ContentDefinitionValidator`）

加载合并后执行；失败项从 `GameDefinitionStore` 与 `_tables` 同时移除，错误写入 `ContentLoadReport.ValidationErrors`。

| 类别 | 规则 |
|------|------|
| Character | `skillRefs` / `cards` / `passives[].buffId` 引用存在；`requiredPotential` 非负 |
| Enemy | `maxHp > 0`；`skillRefs` / `buffRefs` 引用存在 |
| Battle | `waves` 非空；每波 `enemySpawns` 非空；`enemyId` 存在；`skillOverrides` 技能存在；奖励按 kind 校验 |
| Event | Data：`options` 非空 + effect 引用；Script：`scriptPath` 必填 |
| Item | `useSkillRefs` 非空；技能引用存在 |

---

## 7. 示例内容（`Config/mods/base-game/content/`）

| 文件 | 说明 |
|------|------|
| `characters/kemo.json` | 角色 + 卡牌池 + 技能/Buff |
| `enemies/slime.json`, `slime_elite.json` | 敌人定义 |
| `battles/forest_ambush.json` | 两波战斗 + Gold/CardChoice 奖励 |
| `events/shrine.json` | Data 事件 + 选项效果 |
| `items/health_potion.json` | 消耗品 + targetSpec |

---

## 8. 开放项

- `EElement` 与 CardDto `int element` 统一迁移（CardDto 仍用 `ElementFlags` 位标志）
- Enemy AI 运行时规范（DTO 仅提供 `skillRefs` 池与可选 `scriptPath`）
- `RewardEntryDto.params` 强类型化（首版 `Dictionary<string, object>`）
---

## 9. 自检

- 与 `2026-05-11-kemo-card-design.md` 宿主/Mod 分界一致
- 与 `2026-06-16-content-definition-dto-design.md` 类型约束（sealed class + JsonPropertyName + init）一致
- 实现文件：`CharacterDto.cs`, `EnemyDto.cs`, `BattleDto.cs`, `EventDto.cs`, `ItemDto.cs`, `SharedDefinitionDtos.cs`, `ContentEnums.cs`, `ContentDefinitionValidator.cs`, `ContentModLoader.cs`, `GameDefinitionRegistry.cs`
