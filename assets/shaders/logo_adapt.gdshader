shader_type canvas_item;

// Keeps platform logos readable on light palettes. Many logos are drawn white for dark screens, so
// grey pixels are mapped from white toward the text colour, and black toward white. Coloured
// pixels keep their colour. At strength 0, which dark palettes use, the logo is drawn unchanged.

uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform vec4 ink_color : source_color = vec4(0.11, 0.12, 0.14, 1.0);

void fragment() {
    vec4 color = texture(TEXTURE, UV) * COLOR;
    float high = max(color.r, max(color.g, color.b));
    float low = min(color.r, min(color.g, color.b));
    float saturation = high > 0.0 ? (high - low) / high : 0.0;
    float grey = 1.0 - smoothstep(0.12, 0.3, saturation);
    float value = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));
    vec3 inked = mix(vec3(1.0), ink_color.rgb, value);
    COLOR = vec4(mix(color.rgb, inked, grey * strength), color.a);
}
