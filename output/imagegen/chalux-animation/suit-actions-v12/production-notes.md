# 克鲁克斯：横砍与战败衔接预览 v12

生成方式：内置 `image_gen`，透明背景。美术来自三次生成；Python 仅负责切帧、整帧等比最近邻缩放、脚底对齐、图集和 GIF 封装，没有绘制或拼接身体部件。

本轮为预览，游戏中仍使用原来的资源，没有覆盖 `Resource/Assets/Characters/chalux/`。

## 交付

- `two-actions-preview.gif`：两动作并排播放，末尾暂停后重播，方便比较。
- `attack/attack-preview.gif`、`defeat/defeat-preview.gif`：各自的完整预览。
- `attack/frames/`、`defeat/frames/`：每套 16 张透明 PNG，800×592。
- `attack/attack-sheet.png`、`defeat/defeat-sheet.png`：各为 4×4 图集，3200×2368。
- `manifest.json`：原图区域、缩放、脚底对齐偏移、每帧时间和输出边界。
- `package_preview.py`：可复现的机械封装脚本。

## 动作

攻击为一次腰部高度横向斩击：提剑、向后蓄力、转身平扫、短暂随挥、收剑回到待机。斩击特效保持水平，完整保留向左蓄力时和向右出刀时的剑尖。16 帧动作时间合计 1080 ms，首尾使用同一张已确认待机帧。

战败保留单膝跪地、扶膝低头的动作设计。补生成了从深蹲到落膝的过渡，采用其中第 2、3 帧；原序列第 9、10 帧会造成头部高度回跳，因此舍弃，并把原第 8 帧安排在后续低头阶段。最终 16 帧合计 1650 ms，固定支撑脚位置和地面基准。GIF 的最后一帧停留 900 ms 用于观看；真实动作时间记录在 manifest 中。

预览画布比此前宽，以容纳横砍左右两侧剑的范围；这是独立预览画布，尚未修改游戏内缩放、角色卡布局或战斗表现。

## 原图与完整提示词

- 横砍：`attack/generated-sheet.png`（1254×1254），完整提示词 `attack/prompt.txt`；参考已确认的 v9 待机。
- 战败：`defeat/generated-sheet.png`（1271×1238），完整提示词 `defeat/prompt.txt`；参考 v11 战败设计和 v9 待机。
- 落膝补帧：`defeat/generated-transition.png`（1774×887），完整提示词 `defeat/transition-prompt.txt`；参考本轮切出的第 4、5 帧。

## 检查

逐帧检查透明 PNG 和接触表。两套均为 16 帧、相同画布、RGBA；图集布局和时长记录一致；所有实色像素边界保留画布内余量，剑尖和尾巴未裁切；攻击首尾逐像素相同。GIF 使用全序列共享色板与无抖动量化，避免逐帧独立量化带来的颜色跳变。
