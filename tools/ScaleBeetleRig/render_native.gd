extends SceneTree

# Native Spine/Godot review frames of an arbitrary Scale Beetle spjson (no deployment or import needed).
# godot --path PROJECT --rendering-method gl_compatibility --script res://tools/ScaleBeetleRig/render_native.gd -- OUTPUT SPJSON FPS CLIPS
#   CLIPS: comma separated names or "all". "clip@0.3|0.64" renders exact times (frames are named NNNNNms.png).
# Framing matches the baseline captures (viewport 1550x920, sprite at (980,805) x0.72) so sheets compare directly.

const ATLAS := "res://STS2_Things/animations/monsters/scale_beetle/scale_beetle.atlas"
const VIEWPORT_SIZE := Vector2i(1550, 920)


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 4:
		push_error("usage: OUTPUT SPJSON FPS CLIPS")
		quit(2)
		return
	var output: String = args[0]
	var spjson: String = args[1]
	var fps := float(args[2])
	var clip_arg: String = args[3]
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
	if not data.is_skeleton_data_loaded():
		push_error("skeleton data failed")
		quit(3)
		return
	var durations := {}
	for clip in data.get_animations():
		durations[clip.get_name()] = clip.get_duration()
	var clips: Array = durations.keys() if clip_arg == "all" else Array(clip_arg.split(","))

	var viewport := SubViewport.new()
	viewport.size = VIEWPORT_SIZE
	viewport.disable_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	get_root().add_child(viewport)
	var background := ColorRect.new()
	background.color = Color("20242a")
	background.size = Vector2(VIEWPORT_SIZE)
	viewport.add_child(background)
	var floor_line := ColorRect.new()
	floor_line.color = Color(1, 1, 1, 0.10)
	floor_line.position = Vector2(0, 805)
	floor_line.size = Vector2(VIEWPORT_SIZE.x, 2)
	viewport.add_child(floor_line)
	var sprite := SpineSprite.new()
	sprite.set_update_mode(SpineConstant.UpdateMode_Manual)
	sprite.skeleton_data_res = data
	sprite.position = Vector2(980, 805)
	sprite.scale = Vector2(0.72, 0.72)
	viewport.add_child(sprite)
	await process_frame
	await process_frame

	var count := 0
	for spec in clips:
		var parts: PackedStringArray = String(spec).split("@")
		var name: String = parts[0]
		if not durations.has(name):
			push_error("Missing clip: " + name)
			continue
		var duration: float = durations[name]
		var exact := parts.size() > 1
		var picked: Array = []
		if exact:
			for value in parts[1].split("|"):
				picked.append(float(value))
		else:
			for i in range(int(ceil(duration * fps)) + 1):
				picked.append(minf(float(i) / fps, duration))
		var folder: String = output.path_join(name)
		DirAccess.make_dir_recursive_absolute(folder)
		for i in range(picked.size()):
			var t: float = picked[i]
			var state = sprite.get_animation_state()
			state.clear_tracks()
			sprite.get_skeleton().set_to_setup_pose()
			var entry = state.set_animation(name, false, 0)
			entry.set_mix_duration(0.0)
			entry.set_track_time(t)
			sprite.update_skeleton(0.0)
			await process_frame
			RenderingServer.force_draw(false)
			var file_name := ("%05dms.png" % int(round(t * 1000.0))) if exact else ("%04d.png" % i)
			viewport.get_texture().get_image().save_png(folder.path_join(file_name))
			count += 1
		print("captured ", name)
	var report := FileAccess.open(output.path_join("report.json"), FileAccess.WRITE)
	report.store_string(JSON.stringify({"renderer": "native Spine/Godot", "spine": data.get_version(), "frames": count,
		"fps": fps, "durations": durations, "spjson": spjson, "position": [980, 805], "scale": 0.72}, "\t"))
	report.close()
	sprite.get_animation_state().clear_tracks()
	sprite.skeleton_data_res = null
	viewport.queue_free()
	await process_frame
	print("SCALE_BEETLE_NATIVE_RENDER_PASS frames=", count)
	quit(0)
