# 克鲁克斯游戏显示资源

当前仅启用用户确认的 `output/imagegen/chalux-animation/idle-suit-v9` 待机动画。入场、攻击、Buff/治疗和战败动画暂时移除，后续确认后再添加。白色西装、略小的蓬松尾巴、右向斜持直剑、左手位于口袋外。

- `idle.png`：32 帧、8×4 图集，464×560 透明画布，10 FPS，一轮 3.2 秒，循环；移除其余动画时像素和文件内容均未改变。
- `animations.tres`：仅声明 `idle`，保留原画布与居中偏移。
- `portrait.png`：高清 Neutral 立绘，供立绘入口与动画加载失败时使用。

角色配置位于 `Config/mods/base-game/content/characters/chalux.json`，动画列表仅包含 `idle`。图鉴、详情、队伍编辑和战斗均保留待机；缺少动作的请求立即结束，不重启动画，也不触发立绘角色的靠近目标或攻击缩放回退。

居中以人物本体为准：464 像素宽画布的躯干中心约 X=264，`animations.tres` 的 `presentation_offset` 为 `Vector2(-32, 0)`。原有缩放、人物锚点和最近邻过滤保持不变。

仅待机装配入口：`output/imagegen/chalux-animation/idle-suit-v9/package_game_assets.py`，不再自动委托完整动作装配器。

移除的游戏图集、导入配置和原五组 SpriteFrames 保存于 `output/imagegen/chalux-animation/disabled-actions-v14/`；生成源与历次预览仍保留在 `output/imagegen/chalux-animation/`。`suit-actions-v10/package_actions.py` 是历史完整装配入口，运行会重新启用旧动作，待新动作确认后再使用或更新。
