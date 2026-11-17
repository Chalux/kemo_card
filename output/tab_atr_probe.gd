extends SceneTree

# 一次性探针：TabContainer 的页签标题是否走自动翻译（atr）。用完即删。

func _initialize() -> void:
	var t := Translation.new()
	t.locale = "zh_CN"
	t.add_message("UI_CARD", "卡牌")
	t.add_message("UI_BATTLE", "战斗")
	TranslationServer.add_translation(t)
	TranslationServer.set_locale("zh_CN")

	var tc := TabContainer.new()
	root.add_child(tc)
	for n in ["UI_CARD", "UI_BATTLE"]:
		var c := Control.new()
		c.name = n
		tc.add_child(c)
	await process_frame
	print("PROBE-TRANSLATE ", TranslationServer.translate("UI_CARD"), " / ", TranslationServer.get_locale())
	print("PROBE-ATR titles=", tc.get_tab_title(0), "/", tc.get_tab_title(1))
	print("PROBE-DONE")
	quit(0)
