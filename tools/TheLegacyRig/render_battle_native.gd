extends SceneTree

# Renders The Legacy's native Spine 4.2 rig in its real Underdocks Boss Battle framing
# (the_legacy_bg_1.png + SpineSprite at encounter position + the_legacy_fg_1.png).
# usage: godot --path <project> --rendering-method gl_compatibility --script res://tools/TheLegacyRig/render_battle_native.gd -- OUTPUT_DIR [FPS]

const DATA_PATH := "res://STS2_Things/animations/monsters/the_legacy/the_legacy_skel_data.tres"
const BG_PATH := "res://images/backgrounds/the_legacy_bg_1.png"
const FG_PATH := "res://images/backgrounds/the_legacy_fg_1.png"
const CLIPS := ["idle_loop", "attack", "cast", "power_up", "summon", "hurt", "die", "revive"]

# Battle scene coordinates (1920x1080 encounter space, zoomed 1.25x onto the boss pier)
const CAM_RECT := Rect2(860.0, 260.0, 1100.0, 620.0)
const ZOOM := 1.2
const VIEWPORT_SIZE := Vector2i(int(CAM_RECT.size.x * ZOOM), int(CAM_RECT.size.y * ZOOM))
const ENCOUNTER_POS := Vector2(1439.0, 710.0)
const VISUALS_OFFSET := Vector2(0.0, -15.08)
const VISUALS_SCALE := Vector2(0.52, 0.52)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1:
		printerr("usage: render_battle_native.gd OUTPUT_DIR [FPS]")
		quit(2)
		return
	var output_dir: String = args[0]
	var fps := float(args[1]) if args.size() > 1 else 24.0
	DirAccess.make_dir_recursive_absolute(output_dir)

	var data = load(DATA_PATH)
	if data == null or not data.is_skeleton_data_loaded():
		printerr("failed to load The Legacy skeleton data")
		quit(4)
		return

	var durations := {}
	for anim in data.get_animations():
		durations[anim.get_name()] = anim.get_duration()
	for name: String in CLIPS:
		if not durations.has(name) or float(durations[name]) <= 0.0:
			printerr("missing clip: " + name)
			quit(5)
			return

	var viewport := SubViewport.new()
	viewport.size = VIEWPORT_SIZE
	viewport.disable_3d = true
	viewport.transparent_bg = false
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	viewport.render_target_clear_mode = SubViewport.CLEAR_MODE_ALWAYS
	get_root().add_child(viewport)

	var stage := Node2D.new()
	stage.scale = Vector2(ZOOM, ZOOM)
	stage.position = -CAM_RECT.position * ZOOM
	viewport.add_child(stage)

	var bg_tex: Texture2D = load(BG_PATH)
	if bg_tex != null:
		var bg := Sprite2D.new()
		bg.centered = true
		bg.texture = bg_tex
		bg.position = Vector2(960.0, 540.0)
		bg.scale = Vector2(2401.28 / bg_tex.get_width(), 1350.72 / bg_tex.get_height())
		stage.add_child(bg)

	# Subtle ground contact reference ticks on left/right margins (y = 709 = resting bottom contact)
	var ground_y := ENCOUNTER_POS.y - 1.0
	var ref_line := Line2D.new()
	ref_line.points = PackedVector2Array([Vector2(CAM_RECT.position.x, ground_y), Vector2(CAM_RECT.end.x, ground_y)])
	ref_line.width = 1.5
	ref_line.default_color = Color(1.0, 0.78, 0.35, 0.22)
	stage.add_child(ref_line)

	var sprite := SpineSprite.new()
	sprite.set_update_mode(SpineConstant.UpdateMode_Manual)
	sprite.skeleton_data_res = data
	sprite.position = ENCOUNTER_POS + VISUALS_OFFSET
	sprite.scale = VISUALS_SCALE
	stage.add_child(sprite)

	var fg_tex: Texture2D = load(FG_PATH)
	if fg_tex != null:
		var fg := Sprite2D.new()
		fg.centered = true
		fg.texture = fg_tex
		fg.position = Vector2(960.0, 540.0)
		fg.scale = Vector2(2401.28 / fg_tex.get_width(), 1350.72 / fg_tex.get_height())
		stage.add_child(fg)

	await process_frame
	await process_frame

	var count := 0
	for name: String in CLIPS:
		var folder := output_dir.path_join(name)
		DirAccess.make_dir_recursive_absolute(folder)
		var duration: float = float(durations[name])
		var n := int(round(duration * fps)) + 1
		for i in range(n):
			var t: float = minf(float(i) / fps, maxf(0.0, duration - 1.0 / 240.0))
			var state = sprite.get_animation_state()
			state.clear_tracks()
			sprite.get_skeleton().set_to_setup_pose()
			var entry = state.set_animation(name, false, 0)
			entry.set_mix_duration(0.0)
			entry.set_track_time(t)
			sprite.update_skeleton(0.0)
			await process_frame
			RenderingServer.force_draw(false)
			var img := viewport.get_texture().get_image()
			img.save_png(folder.path_join("%04d.png" % i))
			count += 1
		print("captured battle clip: ", name)

	var rep := {
		"renderer": "native spine-godot 4.5.1 (Underdocks battle stage)",
		"spine": str(data.get_version()),
		"fps": fps,
		"frames": count,
		"viewport": [VIEWPORT_SIZE.x, VIEWPORT_SIZE.y],
		"durations": durations,
	}
	var f := FileAccess.open(output_dir.path_join("report.json"), FileAccess.WRITE)
	f.store_string(JSON.stringify(rep, "  "))
	f.close()

	sprite.get_animation_state().clear_tracks()
	sprite.skeleton_data_res = null
	viewport.queue_free()
	await process_frame
	print("THE_LEGACY_BATTLE_RENDER_PASS frames=%d" % count)
	quit(0)
