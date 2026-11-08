shader_type canvas_item;

// Simple style panel: one opaque colour inside the stylebox's rounded shape. It samples no screen
// texture, so Godot makes no back-buffer copy for it. The uniforms match mica_panel.gdshader so the
// shared material keeps the frost settings when the style switches back.

uniform float blur_radius : hint_range(0.0, 150.0) = 45.0;
uniform vec4 mix_color : source_color = vec4(0.0, 0.0, 0.0, 0.4);
uniform float luminosity_floor : hint_range(0.0, 1.0) = 0.0;
uniform int sample_count : hint_range(0, 64) = 8;

void fragment() {
    COLOR = vec4(mix_color.rgb, 1.0);
}
