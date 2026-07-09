# MenuWin 图鉴入口 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为主菜单绑定「图鉴」「退出」按钮逻辑，并在场景中新增图鉴按钮。

**Architecture:** 布局在 `MenuWin.tscn` 增加 `CodexBtn`；逻辑在 `MenuWin.InitEvent` 用 `OnClicks` 绑定，图鉴走已有 `GlobalModController.OpenCodexAsync()`，退出走 `GetTree().Quit()`。

**Tech Stack:** Godot 4.6 Mono / C#

**Spec:** `Doc/superpowers/specs/2026-07-09-menu-win-codex-button-design.md`

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Src/mod/global/Ui/MenuWin.tscn` | 主菜单布局；新增 CodexBtn |
| `Src/mod/global/Ui/MenuWin.cs` | 按钮 Export 与点击逻辑 |

---

### Task 1: 场景增加图鉴按钮

**Files:**
- Modify: `Src/mod/global/Ui/MenuWin.tscn`

- [ ] **Step 1: 更新根节点 Export 列表与 NodePath**

将根节点 `node_paths` 与 Export 改为包含 `CodexBtn`：

```
node_paths=PackedStringArray("StartBtn", "LoadBtn", "SettingsBtn", "CodexBtn", "QuitBtn")
...
StartBtn = NodePath("Panel/VBox/StartBtn")
LoadBtn = NodePath("Panel/VBox/LoadBtn")
SettingsBtn = NodePath("Panel/VBox/SettingsBtn")
CodexBtn = NodePath("Panel/VBox/CodexBtn")
QuitBtn = NodePath("Panel/VBox/QuitBtn")
```

- [ ] **Step 2: 在 SettingsBtn 与 QuitBtn 之间插入 CodexBtn 节点**

```
[node name="CodexBtn" type="Button" parent="Panel/VBox"]
layout_mode = 2
text = "图鉴"
```

---

### Task 2: 绑定图鉴与退出逻辑

**Files:**
- Modify: `Src/mod/global/Ui/MenuWin.cs`

- [ ] **Step 1: 增加 CodexBtn Export，并在 InitEvent 绑定**

完整实现：

```csharp
using Godot;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Global.Ui;

public partial class MenuWin : BaseWin
{
    [Export] public Button? StartBtn { get; set; }
    [Export] public Button? LoadBtn { get; set; }
    [Export] public Button? SettingsBtn { get; set; }
    [Export] public Button? CodexBtn { get; set; }
    [Export] public Button? QuitBtn { get; set; }

    public override string UIId => GlobalUiIds.Menu;
    public override string UIDir => "Src/mod/global/Ui";

    protected override void InitEvent()
    {
        if (CodexBtn != null)
        {
            OnClicks(CodexBtn, () => _ = GlobalModController.OpenCodexAsync());
        }

        if (QuitBtn != null)
        {
            OnClicks(QuitBtn, () => GetTree().Quit());
        }
    }

    protected override void OnOpen()
    {
    }

    protected override void UpdateView()
    {
    }
}
```

注意：`GlobalModController` 在 `KemoCard.Mod.Global` 命名空间；同模块下若未自动可见，需 `using KemoCard.Mod.Global;`。

- [ ] **Step 2: 编译确认**

Run: `dotnet build kemo_card.csproj`（或项目内对应 csproj）  
Expected: 成功，无 CS 错误。

---

### Task 3: 手动验收

- [ ] 运行游戏，主菜单可见「图鉴」
- [ ] 点击「图鉴」打开 CodexDlg，菜单不关
- [ ] 点击「退出」退出游戏
- [ ] 「新游戏 / 继续游戏 / 设置」无反应
