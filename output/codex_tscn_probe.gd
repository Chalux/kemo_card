extends SceneTree

# 一次性探针：验证 CodexDlg.tscn 能加载、战斗页签的导出引用能在运行时解析、页签可被收起。
# 用完即删（不属于游戏代码）。

var dlg: Node

func _initialize() -> void:
	var packed: PackedScene = load("res://Src/mod/global/Ui/CodexDlg.tscn")
	if packed == null:
		print("PROBE-FAIL load-scene")
		quit(1)
		return
	dlg = packed.instantiate()
	root.add_child(dlg)
	await process_frame

	var names := ["_tabBattle", "_iptBattleTxtFilter", "_listBattles", "_rtBattleDetail", "_tabContainer"]
	var missing := []
	for n in names:
		if dlg.get(n) == null:
			missing.append(n)
	print("PROBE-REFS missing=", missing)
	var exported := []
	for p in dlg.get_property_list():
		var pn: String = p["name"]
		if pn.contains("Battle") or pn.contains("tabContainer"):
			exported.append(pn)
	print("PROBE-PROPS ", exported)

	var tc: TabContainer = dlg.get("_tabContainer")
	print("PROBE-TABS count=", tc.get_tab_count(), " titles=", tc.get_tab_title(0), "/", tc.get_tab_title(1), "/", tc.get_tab_title(2))
	tc.set_tab_hidden(2, true)
	print("PROBE-HIDE hidden=", tc.is_tab_hidden(2), " count=", tc.get_tab_count(), " cur=", tc.current_tab)
	tc.set_tab_hidden(2, false)
	tc.current_tab = 2
	await process_frame
	var battle_tab: Control = dlg.get("_tabBattle")
	print("PROBE-SHOW battleTabVisible=", battle_tab.visible, " cur=", tc.current_tab)
	print("PROBE-DONE")
	quit(0)
