# BaseDlgComp 对话框壳组件设计

**日期：** 2026-07-10  
**状态：** 已确认

## 1. 目标

完成 `BaseDlgComp`：作为可复用的对话框视觉壳，绑定关闭按钮；点击后沿父节点链找到最近的 `BaseWin` 并调用 `Close()`，使 `CodexDlg` 等宿主无需再单独绑定关闭逻辑。

## 2. 范围

### 做

- `[Export]` 绑定场景中的 `CloseBtn`
- `_Ready` 绑定 / `_ExitTree` 解绑 `Pressed`
- 点击关闭：向上查找 `BaseWin` → `Close()`；找不到则 `GD.PushWarning`
- 命名空间与其它 Comp 一致：`KemoCard.Mod.Global.Ui.Comp`
- 场景 `node_paths` 绑定 Export

### 不做

- 标题文案、遮罩、Esc 关闭
- 可隐藏关闭按钮 API
- 信号 / 回调注入（宿主不需接线）
- 修改对话框业务逻辑（过滤、列表等）

宿主场景可做最小布局调整：将 `BaseDlgComp` 置于内容节点之上，并把壳层 `mouse_filter` 设为 Ignore，仅 `CloseBtn` 接收点击，避免挡住内容。

## 3. 架构

```
BaseDlg / BaseWin（宿主）
  └─ BaseDlgComp（子节点实例）
       └─ CloseBtn.Pressed → 沿 Parent 找 BaseWin → Close()
```

关闭解析规则：从 `this` 开始沿 `GetParent()` 向上，第一个 `is BaseWin` 的节点即为目标。`BaseDlg` 继承 `BaseWin`，因此 `CodexDlg` 可直接命中。

## 4. 文件变更

| 文件 | 变更 |
|------|------|
| `Src/mod/global/Ui/Comp/BaseDlgComp.cs` | 实现绑定与关闭逻辑 |
| `Src/mod/global/Ui/Comp/BaseDlgComp.tscn` | `node_paths` 绑定 `_btnClose` |

## 5. 验收

1. 打开带 `BaseDlgComp` 的对话框（如图鉴），点击右上角关闭按钮可关闭该对话框。
2. 宿主脚本无需为关闭按钮写额外绑定。
3. 组件未挂在 `BaseWin` 子树下时，点击仅打警告、不抛异常。
