# 设置窗口与通用确认框设计

**日期**：2026-07-14  
**状态**：已实现  
**范围**：`SettingDlg` 布局与逻辑（静态设置行）、三种可复用设置行组件、`BaseCmp` 补强、`AlertDlg`（倒计时确认）、显示/音频/语言设置的运行时应用与持久化；不含 mod 实际向注册表填数据、多显示器选择、其它画质项。

**选定方案**：三个可复用行组件（Toggle / Dropdown / Slider）+ `SettingDlg` 场景内静态编排；显示模式与分辨率变更经 `AlertDlg` 确认后立刻写盘，其余项立即生效、关窗写盘。

---

## 1. 背景与决策摘要

- 现有 `SettingDlg.tscn` 已具备 `BaseDlgComp` → `TabContainer` →「主设置」→ `ScrollContainer` → `SettingVBoxContainer`，脚本几乎为空。
- 音频键与启动恢复已由 Sound 设计落地（`AudioSettingKeys` / `AudioSettingsLoader`）；本轮补设置 UI 写回，并新增显示与语言相关键。
- 不做设置项动态生成：所有预设行直接放在 `SettingVBoxContainer` 下。
- 分辨率与语言使用可扩展注册表；本轮只注册内置项，mod 填充留扩展点。
- 不按玩家屏幕宽高比过滤分辨率选项。

---

## 2. 目标与非目标

### 2.1 目标

- 三种行组件：开关（滑块动效）、下拉、进度条/滑条；水平边缘布局（名左、控件右、中间 Spacer Expand）。
- 静态列出全部设置项并接线：显示模式、分辨率、垂直同步、最高帧率、三路静音/音量、语言。
- `AlertDlg`：payload 驱动，支持倒计时自动关闭与关闭时回调策略。
- 显示模式/分辨率：试用 → 确认（默认 10s）→ 确认立刻持久化；取消/超时回退。
- 其余项：立即应用到运行时；设置窗口关闭时写入 `GlobalSave.Settings` 并 `SaveToDisk`。
- 启动时应用显示模式、分辨率、垂直同步、最高帧率、语言（音频已有）。
- 补强 `BaseCmp`：通用反订阅、`InitEvent`、Cmp 元数据默认值、可选 `OnUnbind`。
- 主菜单「设置」打开 `SettingDlg`；文案全部走本地化键。

### 2.2 非目标

- 按屏幕宽高比或可用像素过滤/灰显分辨率。
- mod 实际注册额外语言或分辨率（仅预留 API）。
- 垂直同步高级模式（Adaptive / Mailbox）；本轮仅开/关。
- 多显示器、抗锯齿、阴影等其它图形设置。
- 设置多 Tab 业务内容（可保留现有「主设置」Tab 结构，本轮只填充该页）。

---

## 3. 架构

```
MenuWin.SettingsBtn
        │
        ▼
   SettingDlg (BaseDlg)
   └─ SettingVBoxContainer（场景静态摆放全部行）
        ├─ SettingDropdownRow × N
        ├─ SettingToggleRow    × N
        └─ SettingSliderRow   × N
                │
    ┌───────────┼──────────────────┐
    ▼           ▼                  ▼
 Sound.*   Display 应用逻辑    TranslationServer
    │      (window mode / size /
    │       vsync / max_fps)
    │
    └─ 关窗：SetSetting + SaveToDisk（显示模式/分辨率除外：确认后已写盘）

AlertDlg (BaseDlg) ← 显示模式 / 分辨率变更后弹出
```

| 职责 | 路径（建议） |
|------|----------------|
| `BaseCmp` 补强 | `Src/frame/ui/Base/BaseCmp.cs`（可扩展 `BaseUI` 订阅表） |
| 行组件 | `Src/mod/global/Ui/Comp/Setting*Row.{cs,tscn}` |
| `SettingDlg` / `AlertDlg` | `Src/mod/global/Ui/` |
| 显示设置键与应用 | `Src/frame/` 下纯逻辑 + Godot 应用（不得依赖 `mod`） |
| 语言/分辨率注册表 | `Src/frame/` 或 `Src/mod/global` 静态注册表；本轮内置种子数据 |
| 打开入口 | `MenuWin` + `GlobalModController.OpenSettingAsync`（命名以现有 Codex 模式为准） |

依赖方向：`mod` → `frame`；显示/音频应用逻辑放 `frame` 时只吃字典与引擎 API。

---

## 4. 组件设计

### 4.1 BaseCmp

在现有 `UIType = Cmp` 基础上补充：

1. **通用订阅表**：`Bind(Action subscribe, Action unsubscribe)`（或等价），离开树时与现有 `OnClicks` 一并清理（扩展 `BaseUI.ClearLifeCycle` 或在 `BaseCmp` 内维护并于 `_ExitTree` 调用）。
2. **`InitEvent()`**：在 `OnReady` 末尾调用的虚方法，子类只在此绑事件。
3. **元数据默认值**：`UIId` / `UIDir` 提供虚默认（如类型名 / 空字符串），嵌入组件无需为 UIManager 填场景路径；独立 Open 时再 override。
4. **`OnUnbind()`**：虚方法，在清理订阅之后或之中调用，供子类 Kill Tween 等。

业务默认值、写盘、Sound/Display 调用不放进 `BaseCmp`。

### 4.2 行组件（均继承 BaseCmp）

统一布局：`HBoxContainer` = 设置名 `Label` + `Control` Spacer（`SizeFlagsHorizontal = Expand`）+ 右侧控件。

| 组件 | 右侧控件 | 能力 |
|------|----------|------|
| `SettingToggleRow` | 自定义滑块开关 | `DefaultValue`；圆钮左右滑动 + 底色 Tween；`ValueChanged(bool)` |
| `SettingDropdownRow` | `OptionButton` | 预设选项 + `DefaultValue`；选中变更事件 |
| `SettingSliderRow` | `HSlider`（可附数值显示） | Min/Max/Step + `DefaultValue`；`ValueChanged` |

组件只负责 UI 状态与事件；不直接读写存档。

---

## 5. 设置项清单

全部静态子节点置于 `SettingVBoxContainer`，顺序如下。

| # | 项 | 组件 | Settings 键 | 默认 | 立即生效 | 写盘时机 |
|---|----|------|-------------|------|----------|----------|
| 1 | 显示模式 | Dropdown | `display.window_mode` | 窗口 | 试用后等确认 | **确认后立刻** |
| 2 | 分辨率 | Dropdown | `display.resolution` | `1920x1080` | 试用后等确认 | **确认后立刻** |
| 3 | 垂直同步 | Toggle | `display.vsync` | 开 | 是 | 关设置窗 |
| 4 | 最高帧率 | Dropdown | `display.max_fps` | 60 | 是 | 关设置窗 |
| 5 | 主音量静音 | Toggle | `audio.mute_flag` bit0 | 关 | 是 | 关设置窗 |
| 6 | 主音量 | Slider 0–100 | `audio.master_volume` | 100 | 是 | 关设置窗 |
| 7 | 音乐静音 | Toggle | `audio.mute_flag` bit1 | 关 | 是 | 关设置窗 |
| 8 | 音乐音量 | Slider 0–100 | `audio.sound_volume` | 100 | 是 | 关设置窗 |
| 9 | 音效静音 | Toggle | `audio.mute_flag` bit2 | 关 | 是 | 关设置窗 |
| 10 | 音效音量 | Slider 0–100 | `audio.sfx_volume` | 100 | 是 | 关设置窗 |
| 11 | 语言 | Dropdown | `locale.language` | `zh_CN` | 是 | 关设置窗 |

### 5.1 显示模式

选项（本地化文案）：窗口 / 无边框窗口 / 无边框全屏 / 全屏。  
存储值为稳定枚举字符串（如 `windowed` / `borderless_window` / `borderless_fullscreen` / `fullscreen`），映射 Godot `Window.Mode`。

### 5.2 分辨率

- 可扩展注册表；条目含稳定 id、宽、高；**下拉展示文案为 `宽x高`**（如 `1280x720`），不展示 `720p` 简称。
- 本轮内置：`1280x720`、`1920x1080`、`2560x1440`、`3840x2160`。
- 不做屏幕宽高比过滤；全部选项始终可选。
- Settings 存 id 或 `宽x高` 字符串（实现时二选一并写死约定；推荐存 `1280x720` 形式便于阅读）。

### 5.3 垂直同步与最高帧率

- 垂直同步：布尔；开 → `VSyncMode` 启用，关 → Disabled。
- 最高帧率选项：`30`、`60`、`120`、`144`、`165`、`244`、不限制。  
  「不限制」存 `0`，对应 `Engine.MaxFps = 0`。

### 5.4 音频

与既有 `AudioSettingKeys` / `SoundBus` 位掩码一致：bit0 Master、bit1 Sound（音乐）、bit2 SFX。  
变更时调用 `Sound.SetBusVolumePercent` / `Sound.SetMuteFlag`（或等价按位更新后写回 flag）。

### 5.5 语言

- 可扩展注册表；本轮仅 `zh_CN`、`en`。
- 变更立即 `TranslationServer.SetLocale`（或项目既有本地化入口）。
- mod 填充为后续工作，本轮只留注册 API。

---

## 6. AlertDlg

新建 `AlertDlg : BaseDlg`，通过 UIManager 打开，payload 建议字段：

| 字段 | 说明 |
|------|------|
| `Title` | 标题（翻译键或已翻译文本；实现时统一为键） |
| `Desc` | 描述 |
| `OkText` / `CancelText` | 按钮文案键 |
| `OkCallback` / `CancelCallback` | 确认 / 取消回调 |
| `CallbackWhenClose` | 关闭（非点按钮）时策略：调用 Ok / 调用 Cancel / 什么都不做；**默认 Cancel** |
| `Time` | 倒计时秒数；默认 **10**；`≤0` 表示无倒计时自动关闭 |

倒计时可在描述或按钮旁展示剩余秒数；超时等同 Cancel（执行 `CancelCallback` 并关闭）。

`AlertDlg` 不包含分辨率/显示模式业务，只执行回调。

---

## 7. 显示模式 / 分辨率确认流

1. 用户更改下拉 → 记录旧值 → **立即试用**新显示。
2. 打开 `AlertDlg`（默认 10s，文案说明需确认否则回退）。
3. **OK**：将新值写入 Settings 并 `SaveToDisk`；保留当前显示；同步下拉选中。
4. **Cancel / 超时 / 关闭且策略为 Cancel**：恢复旧显示与下拉选中；不写新值。
5. 确认流进行中再次更改显示项：先按取消结束当前流，再启动新流。
6. 关闭 `SettingDlg` 时若 Alert 仍打开：先按取消回退，再持久化第 3–11 项并关闭。

---

## 8. SettingDlg 生命周期

- **打开**：从 `GlobalModController.Snapshot.Settings`（及当前运行时）灌入各行；绑定行事件。
- **行变更（非 1–2）**：立即应用到 Sound / VSync / MaxFps / Locale；内存中可记「脏」值，关窗再写盘。
- **关闭**：将第 3–11 项当前值 `SetSetting` 后 `SaveToDisk`；清理确认流。
- **入口**：`MenuWin.SettingsBtn` → `OpenSettingAsync`；注册 `GlobalUiIds.Setting`（及 Alert id）。

---

## 9. 启动恢复

在现有音频 Settings 应用之外，启动路径（如 `MainRoot`）增加：

- 应用 `display.window_mode`、`display.resolution`
- 应用 `display.vsync`、`display.max_fps`
- 应用 `locale.language`

非法或缺省 → 使用 §5 默认值并打日志。

---

## 10. 本地化

所有面向用户的标签、选项、Alert 文案写入 `Resource/Locale/strings.csv`（及需要时的 mod CSV）。  
分辨率选项展示用字面 `1280x720` 等形式（数字可不翻译）；「不限制」等用翻译键。

---

## 11. 错误处理

- Settings 缺键 / 解析失败 → 默认值 + 日志。
- 显示试用失败 → 回退旧值；不打开 Alert 或立即以 Cancel 结束。
- 组件 Export 未绑定时 → 日志警告，跳过该行逻辑，避免抛崩。

---

## 12. 测试要点

- 行组件：默认值、变更事件、开关 Tween、离开树后无残留订阅。
- Alert：OK / Cancel / 超时 / 关闭策略默认 Cancel；`Time≤0` 无自动关。
- 显示确认流：确认写盘、取消回退、连续更改、关设置窗时未确认回退。
- 关窗写盘：音量/静音/语言/垂直同步/最高帧率写入后重启仍生效。
- 分辨率/语言注册表：内置项齐全；注册 API 可追加（单测或最小手测）。

---

## 13. 决策记录

| 议题 | 决定 |
|------|------|
| 组件拆分 | 三行组件 + SettingDlg 静态编排 |
| 开关动效 | 滑块（圆钮滑动 + 底色） |
| 分辨率标签 | `宽x高`，内置四档标准像素 |
| 分辨率/语言扩展 | 注册表；本轮只种子数据 |
| 宽高比过滤 | 不做 |
| 显示确认倒计时 | 默认 10 秒 |
| 显示模式/分辨率写盘 | 确认后立刻 |
| 其余写盘 | 关设置窗 |
| 运行时生效 | 除确认项外均立即生效（含语言） |
| 垂直同步 | Toggle 开/关，默认开 |
| 最高帧率 | 含「不限制」→ 0 |
