extends SceneTree

# Native Spine/Godot review frames of the 活体巨岩 rig in its real stage composition:
# far wall -> body pass (arms hidden) -> basalt platform -> arms pass (body hidden).
# godot --path PROJECT --script res://tools/CaveGodRig/render_native.gd -- OUTPUT SPJSON FPS CLIPS [SCALE] [MODE]
#   CLIPS: comma separated names, or "all".
#   MODE:  "frames" (default) samples each clip by seeking; "mix" plays clip -> queued idle with the
#          controller's mixes so the hand-off into idle is reviewed as the game drives it.

const ATLAS := "res://animations/monsters/cave_god/cave_god.atlas"
const BG_BLUE := "res://images/rooms/cave_god_boss/cavegod_bg_far_blue.png"
const BG_RED := "res://images/rooms/cave_god_boss/cavegod_bg_far.png"
const FG := "res://images/rooms/cave_god_boss/cavegod_fg_platform.png"

const ARM_SLOTS := ["arm1_2", "arm1_2_red", "arm1_2_back", "arm1_2_back_red", "arm1_3", "arm2_2", "arm2_2_red", "arm2_3"]
const BODY_SLOTS := ["body_bottom", "body", "body_red", "arm1_1", "arm2_1", "neck", "neck_red", "head", "head_red",
	"eyes", "eyes_red", "beard3", "beard3_red", "beard2", "beard2_red", "beard1", "beard1_red", "crastalls_roof",
	"crastalls_roof_red", "horn1", "horn1_red", "horn2", "horn2_red", "head_skale", "head_skale_red", "horizon"]
const RED_SLOTS := ["beard1_red", "beard2_red", "beard3_red", "body_red", "crastalls_roof_red", "eyes_red", "head_red",
	"head_skale_red", "horn1_red", "horn2_red", "neck_red", "arm1_2_red", "arm1_2_back_red", "arm2_2_red"]
const BLUE_SLOTS := ["beard1", "beard2", "beard3", "body", "crastalls_roof", "eyes", "head", "head_skale", "horn1",
	"horn2", "neck", "arm1_2", "arm1_2_back", "arm2_2"]

var _hide_body := {}
var _hide_arms := {}
var _phase := "blue"   # blue | red | both


func _initialize() -> void:
	call_deferred("_run")


func _mask(sprite: SpineSprite, hidden: Array) -> void:
	var skeleton := sprite.get_skeleton()
	if skeleton == null:
		return
	var names: Array = hidden.duplicate()
	if _phase == "blue":
		names.append_array(RED_SLOTS)
	elif _phase == "red":
		names.append_array(BLUE_SLOTS)
	for name in names:
		var slot = skeleton.find_slot(name)
		if slot != null:
			slot.set_attachment(null)
			slot.set_color(Color(1, 1, 1, 0))


func _texture_rect(path: String) -> TextureRect:
	var rect := TextureRect.new()
	rect.texture = load(path)
	rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	rect.position = Vector2(-1440, -720)
	rect.size = Vector2(2880, 1440)
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return rect


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 4:
		push_error("usage: OUTPUT SPJSON FPS CLIPS [SCALE] [MODE]")
		quit(2)
		return
	var output: String = args[0]
	var spjson: String = args[1]
	var fps := float(args[2])
	var clip_arg: String = args[3]
	var scale := float(args[4]) if args.size() > 4 else 0.5
	var mode: String = args[5] if args.size() > 5 else "frames"
	DirAccess.make_dir_recursive_absolute(output)

	var atlas := SpineAtlasResource.new()
	if atlas.load_from_atlas_file(ATLAS) != OK:
		push_error("atlas failed")
		quit(3)
		return
	var file := SpineSkeletonFileResource.new()
	if file.load_from_file(spjson) != OK:
		push_error("skeleton file failed: " + spjson)
		quit(3)
		return
	var data := SpineSkeletonDataResource.new()
	data.atlas_res = atlas
	data.skeleton_file_res = file
	data.default_mix = 0.05
	if not data.is_skeleton_data_loaded():
		push_error("skeleton data failed")
		quit(3)
		return
	var durations := {}
	for clip in data.get_animations():
		durations[clip.get_name()] = clip.get_duration()
	var clips: Array = []
	if clip_arg == "all":
		clips = durations.keys()
	else:
		clips = Array(clip_arg.split(","))

	var size := Vector2i(int(1920 * scale), int(1080 * scale))
	var viewport := SubViewport.new()
	viewport.size = size
	viewport.disable_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	get_root().add_child(viewport)
	var world := Node2D.new()
	world.scale = Vector2.ONE * scale
	viewport.add_child(world)
	# Same composition as the CaveGod probe: encounter background at (983,540) x0.75.
	var stage := Node2D.new()
	stage.position = Vector2(983, 540)
	stage.scale = Vector2.ONE * 0.75
	world.add_child(stage)
	var far_blue := _texture_rect(BG_BLUE)
	var far_red := _texture_rect(BG_RED)
	stage.add_child(far_blue)
	stage.add_child(far_red)
	var body := SpineSprite.new()
	body.skeleton_data_res = data
	body.position = Vector2(0, 15)
	body.scale = Vector2(1.3, 1.3)
	stage.add_child(body)
	var fg := _texture_rect(FG)
	stage.add_child(fg)
	var arms := SpineSprite.new()
	arms.skeleton_data_res = data
	arms.position = Vector2(0, 15)
	arms.scale = Vector2(1.3, 1.3)
	stage.add_child(arms)
	# Reference player anchor (feet at the probe's single-player slot).
	var player := ColorRect.new()
	player.color = Color(1.0, 0.82, 0.25, 0.55)
	player.position = Vector2(945, 690)
	player.size = Vector2(30, 125)
	world.add_child(player)

	for sprite in [body, arms]:
		sprite.set_update_mode(SpineConstant.UpdateMode_Manual)
	body.before_world_transforms_change.connect(func(s): _mask(s, ARM_SLOTS))
	arms.before_world_transforms_change.connect(func(s): _mask(s, BODY_SLOTS))
	await process_frame
	await process_frame

	var count := 0
	for spec in clips:
		# "clip@0.3|0.64" renders exact times; a bare clip name samples the whole clip at FPS.
		var parts: PackedStringArray = String(spec).split("@")
		var name: String = parts[0]
		var picked: Array = []
		if parts.size() > 1:
			for value in parts[1].split("|"):
				picked.append(float(value))
		if not durations.has(name):
			push_error("Missing clip: " + name)
			continue
		var angry: bool = name.ends_with("_angry") or name == "main_angry"
		_phase = "both" if name == "main_angry" else ("red" if angry else "blue")
		far_red.modulate.a = 1.0 if angry and name != "main_angry" else 0.0
		# Death retreat: the controller moves the platform in front of both rig passes.
		fg.z_index = 10 if name.begins_with("hide") else 0
		var folder: String = output.path_join(name)
		DirAccess.make_dir_recursive_absolute(folder)
		var duration: float = durations[name]
		if mode == "mix":
			var idle := "idle_front_angry" if angry else "idle_front"
			for sprite in [body, arms]:
				var state = sprite.get_animation_state()
				state.clear_tracks()
				sprite.get_skeleton().set_to_setup_pose()
				state.set_animation(idle, true, 0)
				sprite.update_skeleton(0.6)
				var entry = state.set_animation(name, false, 0)
				entry.set_mix_duration(0.3)
				state.add_animation(idle, 0.0, true, 0)
			var frames := int(ceil((duration + 1.0) * fps)) + 1
			for i in range(frames):
				for sprite in [body, arms]:
					sprite.update_skeleton(1.0 / fps if i > 0 else 0.0)
				await process_frame
				RenderingServer.force_draw(false)
				viewport.get_texture().get_image().save_png(folder.path_join("%04d.png" % i))
				count += 1
		else:
			var exact := not picked.is_empty()
			if not exact:
				for i in range(int(ceil(duration * fps)) + 1):
					picked.append(minf(float(i) / fps, duration))
			var overlay: bool = name.begins_with("hit_recoil")
			for i in range(picked.size()):
				var t: float = picked[i]
				for sprite in [body, arms]:
					var state = sprite.get_animation_state()
					state.clear_tracks()
					sprite.get_skeleton().set_to_setup_pose()
					# The flinch is an additive overlay on the reaction track, as the controller plays it.
					var entry = state.set_animation(("idle_front_angry" if angry else "idle_front") if overlay else name, false, 0)
					entry.set_mix_duration(0.0)
					entry.set_track_time(0.0 if overlay else t)
					if overlay:
						var hurt = state.set_animation(name, false, 1)
						hurt.set_mix_duration(0.0)
						hurt.set_mix_blend(SpineConstant.MixBlend_Add)
						hurt.set_track_time(t)
					sprite.update_skeleton(0.0)
				await process_frame
				RenderingServer.force_draw(false)
				var file_name := ("%05dms.png" % int(round(t * 1000.0))) if exact else ("%04d.png" % i)
				viewport.get_texture().get_image().save_png(folder.path_join(file_name))
				count += 1
		print("captured ", name)
	var report := FileAccess.open(output.path_join("report.json"), FileAccess.WRITE)
	report.store_string(JSON.stringify({"renderer": "native Spine/Godot", "spine": data.get_version(), "frames": count,
		"fps": fps, "mode": mode, "scale": scale, "durations": durations, "spjson": spjson}, "\t"))
	report.close()
	for sprite in [body, arms]:
		sprite.get_animation_state().clear_tracks()
		sprite.skeleton_data_res = null
	viewport.queue_free()
	await process_frame
	print("CAVEGOD_NATIVE_RENDER_PASS frames=", count)
	quit(0)
