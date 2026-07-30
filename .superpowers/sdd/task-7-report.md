# Task 7 Report: Integration tests + doc status

**Status:** DONE

**Branch:** `feature/condition-system`

**Commit:** `dcf7dc5` — 条件系统：完成集成测试并更新规格状态

## Deliverables

| File | Action |
|------|--------|
| `Tests/kemo_card.Ui.Tests/Condition/ConditionIntegrationTests.cs` | Created — 4 integration cases |
| `Doc/superpowers/specs/2026-07-30-condition-system-design.md` | Status line updated |
| `Doc/INDEX.md` | Skipped — plan already listed |

## Integration test cases

1. `{"HasFlag":["intro"],"HasAllItems":[["wood",2],["stone",1]]}` + Flags={intro}, Items={wood:2,stone:0} → Passed=false, Leaves.Count==2
2. Same JSON + stone:1 → Passed=true
3. `[]` → Passed=false
4. `{}` → Passed=true

## Test run

```
dotnet test ... --filter "FullyQualifiedName~Condition"
Passed: 34, Failed: 0, Skipped: 0
```

## Concerns

- None. Combat CondType and content DTO wiring remain out of scope per spec status.

## Final code review fix

**Status:** DONE

- `ConditionParser.TryParseAndObject` 以 `HashSet<string>(StringComparer.Ordinal)` 拒绝同一 AND 对象内重复的 CondType。
- 新增重复 `Tag` 键回归测试；错误同时包含来源路径与 `Tag`。

### Test run

```
dotnet test Tests\kemo_card.Ui.Tests\kemo_card.Ui.Tests.csproj --filter "FullyQualifiedName~Condition" --nologo -v q
Passed: 35, Failed: 0, Skipped: 0
```
