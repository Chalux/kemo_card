# 卡牌 / 技能 / Buff / 效果 内容 DTO 设计规格

**日期**：2026-06-16  
**状态**：已实现（与代码 `Src/frame/content/definitions/` 对齐）  
**范围**：Card / Skill / Buff / Effect 四类 Mod JSON DTO、枚举、加载与校验

---

## 1. 引用链与时机分层

- 出牌 → 卡牌按 `skillRefs` 顺序触发技能
- 技能一旦被调用，`effectRefs` 同步即时执行完毕
- 延时 / 跨回合效果：`ApplyBuff` + Buff `Hooks`
- Buff 仅由效果或宿主白名单 API 添加

| 层级 | 是否延时 | 说明 |
|------|----------|------|
| 卡牌执行队列 | 可延时 | 按 `priority` 排队，轮到才调用技能 |
| 技能 | 始终即时 | 调用瞬间跑完 `effectRefs` |
| Buff | 可延时 | 钩子驱动持续与延时效果 |

Skill DTO **不含** `ESkillTrigger` / `IsInstant`。

---

## 2. DTO 类型约束

- 可 JSON 往返：`string`, `int`, `bool`, `double`, 枚举, `List<T>`, `Dictionary<string, object>?`（仅 params）
- `tags` 使用 `List<string>`
- 形态：`sealed class` + `JsonPropertyName` + `init`

---

## 3. CardDto 字段

见 `CardDto.cs`。无 `descId`；描述由 UI 组合 `skillRefs` 对应技能的 `descId`。升级链：`cardGroupId` + `upgradeTier`。

---

## 4. 枚举

`ECostType`, `ECardType`, `ECostScalingKind`, `ETargetSide`, `ETargetScope`, `ERetargetPolicy`, `ERarity`, `EBuffDurationType`, `EBuffStackRule`, `EEffectKind`。

---

## 5. Mod 目录

```
content/cards/*.json
content/skills/*.json
content/buffs/*.json
content/effects/*.json
```

`id` = 文件名（无扩展名）；加载时注入 DTO `Id`。

---

## 6. 脚本

- 主通道：TypeScript（`ExecuteScript` + `ScriptPath`）
- 进阶：HybridCLR（信任 Mod）
- 接口桩：`IContentEffectScriptHost`

---

## 7. 自检

- 无 TBD；与 `2026-05-11-kemo-card-design.md` 宿主/Mod 分界一致
- 非法引用：加载合并后校验，失败项不进入 `GameDefinitionStore`
