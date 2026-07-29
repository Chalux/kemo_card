# BaseDlgComp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 `BaseDlgComp` 关闭按钮绑定，点击后沿父链关闭最近的 `BaseWin`。

**Architecture:** 与 `BasePager` 相同的 Export + EnsureBound / Unbind 模式；关闭时向上查找 `BaseWin` 并调用 `Close()`，宿主无需接线。

**Tech Stack:** Godot 4.6 Mono / C#

**Spec:** `Doc/superpowers/specs/2026-07-10-base-dlg-comp-design.md`

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `Src/mod/global/Ui/Comp/BaseDlgComp.cs` | 绑定 CloseBtn、查找 BaseWin、调用 Close |
| `Src/mod/global/Ui/Comp/BaseDlgComp.tscn` | Export `node_paths` |

---

### Task 1: 实现 BaseDlgComp 逻辑

**Files:**
- Modify: `Src/mod/global/Ui/Comp/BaseDlgComp.cs`
- Modify: `Src/mod/global/Ui/Comp/BaseDlgComp.tscn`

- [ ] **Step 1: 替换 BaseDlgComp.cs**

```csharp
using Godot;
using KemoCard.Frame.UI.Base;

namespace KemoCard.Mod.Global.Ui.Comp;

public partial class BaseDlgComp : Control
{
	[Export] private Button? _btnClose;

	private bool _bound;

	public override void _Ready()
	{
		EnsureBound();
	}

	public override void _ExitTree()
	{
		UnbindControls();
		base._ExitTree();
	}

	private void EnsureBound()
	{
		if (_bound)
		{
			return;
		}

		_bound = true;
		if (_btnClose != null)
		{
			_btnClose.Pressed += OnClosePressed;
		}
	}

	private void UnbindControls()
	{
		if (!_bound)
		{
			return;
		}

		_bound = false;
		if (_btnClose != null)
		{
			_btnClose.Pressed -= OnClosePressed;
		}
	}

	private void OnClosePressed()
	{
		var win = FindOwnerWin();
		if (win == null)
		{
			GD.PushWarning("BaseDlgComp: 未找到父级 BaseWin，无法关闭。");
			return;
		}

		win.Close();
	}

	private BaseWin? FindOwnerWin()
	{
		Node? node = this;
		while (node != null)
		{
			if (node is BaseWin win)
			{
				return win;
			}

			node = node.GetParent();
		}

		return null;
	}
}
```

- [ ] **Step 2: 更新 BaseDlgComp.tscn 的 node_paths**

在根节点增加：

```
node_paths=PackedStringArray("_btnClose")
_btnClose = NodePath("DlgBg/CloseBtn")
```

- [ ] **Step 3: 编译验证**

Run: `dotnet build kemo_card.csproj`（或项目内对应 csproj）  
Expected: 成功，无 BaseDlgComp 相关错误

---

## Spec 覆盖自检

| 需求 | Task |
|------|------|
| Export 绑定 CloseBtn | Task 1 |
| Ready/ExitTree 绑定解绑 | Task 1 |
| 向上找 BaseWin → Close | Task 1 |
| 找不到则 PushWarning | Task 1 |
| 场景 node_paths | Task 1 Step 2 |
| 不改 CodexDlg | 无对应改动 |
