# Renders the artwork RomBox writes to Steam's grid folder when it adds itself to Steam.
# Run from the project folder with a window (it needs the GPU):
#   godot --path . --script tools/steam-art/render_steam_art.gd
# Re-run after changing the icon; the PNGs land in assets/steam/.
extends SceneTree

const OUTPUT_DIR := "res://assets/steam/"
const GRADIENT_START := Color("#7B4DFF")
const GRADIENT_END := Color("#341783")

const GLYPH_SVG := """<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <path d="M128 52 L200 92 L128 132 L56 92 Z" fill="#FFFFFF" fill-opacity="0.28"/>
  <g fill="none" stroke="#FFFFFF" stroke-width="14" stroke-linejoin="round" stroke-linecap="round">
    <path d="M128 52 L200 92 L200 168 L128 208 L56 168 L56 92 Z"/>
    <path d="M56 92 L128 132 L200 92"/>
    <path d="M128 132 L128 208"/>
  </g>
</svg>"""

var jobs := []
var frames_waited := 0

func _initialize() -> void:
	jobs = [
		_make_capsule(),
		_make_wide_capsule(),
		_make_hero(),
		_make_logo(),
	]

func _process(_delta: float) -> bool:
	frames_waited += 1
	if frames_waited < 4:
		return false
	for job in jobs:
		var viewport: SubViewport = job[0]
		var image := viewport.get_texture().get_image()
		image.save_png(ProjectSettings.globalize_path(OUTPUT_DIR + job[1]))
		print("wrote ", job[1], " ", image.get_size())
	return true

func _viewport(size: Vector2i, transparent: bool) -> SubViewport:
	var viewport := SubViewport.new()
	viewport.size = size
	viewport.transparent_bg = transparent
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(viewport)
	return viewport

func _background(viewport: SubViewport) -> void:
	var gradient := Gradient.new()
	gradient.set_color(0, GRADIENT_START)
	gradient.set_color(1, GRADIENT_END)
	var texture := GradientTexture2D.new()
	texture.gradient = gradient
	texture.fill_from = Vector2(0, 0)
	texture.fill_to = Vector2(1, 1)
	texture.width = 256
	texture.height = 256
	var rect := TextureRect.new()
	rect.texture = texture
	rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	rect.stretch_mode = TextureRect.STRETCH_SCALE
	rect.size = viewport.size
	viewport.add_child(rect)

func _glyph(viewport: SubViewport, center: Vector2, size: float, alpha := 1.0) -> void:
	var image := Image.new()
	image.load_svg_from_string(GLYPH_SVG, size / 256.0)
	var rect := TextureRect.new()
	rect.texture = ImageTexture.create_from_image(image)
	rect.position = center - Vector2(size, size) / 2.0
	rect.modulate.a = alpha
	viewport.add_child(rect)

func _wordmark(viewport: SubViewport, text: String, font_size: int, position: Vector2, width: float, alignment := HORIZONTAL_ALIGNMENT_CENTER) -> void:
	var font := FontVariation.new()
	font.base_font = load("res://assets/fonts/InterVariable.woff2")
	font.variation_opentype = { TextServerManager.get_primary_interface().name_to_tag("wght"): 800 }
	var label := Label.new()
	label.text = text
	label.horizontal_alignment = alignment
	label.add_theme_font_override("font", font)
	label.add_theme_font_size_override("font_size", font_size)
	label.add_theme_color_override("font_color", Color.WHITE)
	label.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.35))
	label.add_theme_constant_override("shadow_offset_y", int(font_size / 24.0))
	label.position = position
	label.size = Vector2(width, font_size * 1.4)
	viewport.add_child(label)

# 600x900 library capsule.
func _make_capsule() -> Array:
	var viewport := _viewport(Vector2i(600, 900), false)
	_background(viewport)
	_glyph(viewport, Vector2(300, 360), 340)
	_wordmark(viewport, "RomBox", 110, Vector2(0, 580), 600)
	return [viewport, "capsule.png"]

# 920x430 wide capsule, shown in recent games and Big Picture.
func _make_wide_capsule() -> Array:
	var viewport := _viewport(Vector2i(920, 430), false)
	_background(viewport)
	_glyph(viewport, Vector2(190, 215), 240)
	_wordmark(viewport, "RomBox", 130, Vector2(330, 125), 560, HORIZONTAL_ALIGNMENT_LEFT)
	return [viewport, "wide_capsule.png"]

# 1920x620 hero behind the game page header. Steam draws the logo over it, so it carries no text.
func _make_hero() -> Array:
	var viewport := _viewport(Vector2i(1920, 620), false)
	_background(viewport)
	var placements := [
		[Vector2(180, 140), 220, 0.10], [Vector2(520, 470), 300, 0.08], [Vector2(900, 120), 180, 0.07],
		[Vector2(1250, 430), 360, 0.09], [Vector2(1620, 170), 260, 0.10], [Vector2(1820, 520), 200, 0.07],
	]
	for placement in placements:
		_glyph(viewport, placement[0], placement[1], placement[2])
	return [viewport, "hero.png"]

# 1280x400 transparent logo, drawn over the hero.
func _make_logo() -> Array:
	var viewport := _viewport(Vector2i(1280, 400), true)
	_glyph(viewport, Vector2(200, 200), 300)
	_wordmark(viewport, "RomBox", 200, Vector2(370, 60), 900, HORIZONTAL_ALIGNMENT_LEFT)
	return [viewport, "logo.png"]
