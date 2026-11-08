shader_type canvas_item;

// Simple style background: the theme colour and nothing else. It reads no TIME, so with Godot's
// low-processor mode the screen is only redrawn when something on it changes. The uniforms match
// bg_common.gdshaderinc so the shared material keeps every value when the style switches back.

uniform vec4 bg_color : source_color = vec4(0.02, 0.02, 0.03, 1.0);
uniform vec4 primary_color : source_color = vec4(0.08, 0.04, 0.15, 1.0);
uniform vec4 secondary_color : source_color = vec4(0.04, 0.08, 0.15, 1.0);
uniform float speed : hint_range(0.0, 5.0) = 0.2;
uniform float scale : hint_range(0.1, 10.0) = 1.0;
uniform float grain_amount : hint_range(0.0, 8.0) = 1.6;
uniform float vignette_amount : hint_range(0.0, 2.0) = 0.7;

void fragment() {
    COLOR = vec4(bg_color.rgb, 1.0);
}
