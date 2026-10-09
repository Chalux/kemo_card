# UI 框架 Godot 集成回归

使用真实 Godot 节点、原生信号、场景资源和默认 UI 状态处理器，验证 Dlg 并存、默认参数、层级可见性、导航、缓存、遮罩、请求取消、过期回调、动画打断、异常解绑和 owner 卸载。

先完成项目依赖恢复，再从仓库根目录运行（Godot 需使用 Mono 版本）：

```powershell
./Tests/kemo_card.Ui.Headless/run.ps1 -GodotPath 'D:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
```

可用 `-ResultsDirectory <目录>` 指定日志位置；默认写入已被 Git 忽略的 `.godot/ui-headless/`。脚本检查引擎退出码和 `UI_HEADLESS_SUMMARY`，缺少汇总或任一用例失败都会报错。部分测试故意让业务 hook 抛异常，Godot 会输出对应的 `expected ... failure` 日志，最终断言以汇总为准。

测试类仅在 `IncludeUiHeadlessTests=true` 时编译。脚本在 `finally` 中重新构建正常游戏程序集；默认构建不会包含测试节点。无头检查验证生命周期和节点行为，场景美术布局、鼠标命中体验仍需在编辑器中检查。
