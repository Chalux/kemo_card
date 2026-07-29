# MenuWin 完善与图鉴入口设计

**日期：** 2026-07-09  
**状态：** 已确认

## 目标

完善主菜单 `MenuWin` 的可立即使用逻辑：绑定「退出」与新增「图鉴」按钮；新游戏 / 继续游戏 / 设置暂不实现任何行为。

## 范围

### 做

- 在 `MenuWin.tscn` 的 `Panel/VBox` 中，于 `SettingsBtn` 与 `QuitBtn` 之间新增 `CodexBtn`（文案：「图鉴」）。
- 在 `MenuWin.cs` 增加 `[Export] CodexBtn`，并在场景中绑定 NodePath。
- 在 `InitEvent` 中：
  - `CodexBtn` → 调用 `GlobalModController.OpenCodexAsync()`（fire-and-forget：`_ = ...`）。
  - `QuitBtn` → 调用 `GetTree().Quit()`。
- 打开图鉴时不关闭主菜单（`CodexDlg` 叠在 Dlg 层）。

### 不做

- 不绑定 / 不实现「新游戏」「继续游戏」「设置」。
- 不为未实现按钮写日志或「暂未实现」提示。
- 不改动 `CodexDlg` 内部内容与布局。
- 不新增设置界面或其它流程。

## 架构与数据流

```
MenuWin (Win 层)
  CodexBtn.Pressed → GlobalModController.OpenCodexAsync()
                   → UIManager.OpenAsync<CodexDlg>
                   → CodexDlg 显示在 Dlg 层（菜单保持打开）
  QuitBtn.Pressed  → SceneTree.Quit()
```

复用已有入口 `GlobalModController.OpenCodexAsync()`，不在 UI 内直接拼 `UIManager` 打开参数。

## 文件变更

| 文件 | 变更 |
|------|------|
| `Src/mod/global/Ui/MenuWin.tscn` | 新增 `CodexBtn` 节点与 Export 绑定 |
| `Src/mod/global/Ui/MenuWin.cs` | Export + `InitEvent` 绑定图鉴 / 退出 |

## 验收

1. 运行游戏后主菜单出现「图鉴」按钮。
2. 点击「图鉴」打开 `CodexDlg`，主菜单仍在。
3. 点击「退出」关闭游戏。
4. 点击「新游戏 / 继续游戏 / 设置」无任何反应。
