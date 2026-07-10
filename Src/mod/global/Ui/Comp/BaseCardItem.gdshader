shader_type canvas_item;

/// 扇区颜色，最多 3 个；实际使用数量由 color_count 决定。
uniform vec4 colors[3];
/// 实际参与平分的颜色数量，范围 1–3。
uniform int color_count : hint_range(1, 3) = 1;
/// 色环旋转角度（弧度）。
uniform float rotation = 0.0;
/// 圆半径（UV 空间，0.5 为贴满方形控件的内切圆）。
uniform float radius : hint_range(0.0, 0.5) = 0.5;

void fragment() {
	vec2 centered = UV - vec2(0.5);
	float dist = length(centered);

	if (dist > radius) {
		COLOR = vec4(0.0);
		discard;
	}

	int count = clamp(color_count, 1, 3);
	float angle = atan(centered.y, centered.x) + rotation;
	// 映射到 [0, TAU)，再按扇区平分取色。
	angle = mod(angle + TAU, TAU);
	float sector = TAU / float(count);
	int index = int(angle / sector);
	index = clamp(index, 0, count - 1);

	COLOR = colors[index];
}
