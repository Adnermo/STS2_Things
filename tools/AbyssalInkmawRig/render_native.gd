extends SceneTree

# Native Godot 4.5.1 + SpineSprite stage renderer for 渊墨巨蛸 (Abyssal Inkmaw) v3.
# Mounts ALL FOUR AAA Zero-Cheap-Stretch Spine 4.2.0 Rigs:
#   1. Subterranean Cavern Lake (2048x960)
#   2. Colossal Half-Submerged Kraken Upper Body + 6 Misty 12-Bone Lake Tentacles (`abyssal_inkmaw`)
#   2.5 Primary Hero Combat Tentacles emerging FROM THE LAKE WATER (`base_water` / `base_water_burst`)
#   3. Separated Ancient Stone Bridge Deck (2048x960 RGBA)
#   3.5 Optional Secondary / Phase-2 Bridge-Floor Breach Tentacle (`base_floor_burst` -> `base_floor`)
#   3.8 Foreground High-Pressure Left-to-Right Sweeping Hydro-Ink Jet (`abyssal_ink_beam`)
#   4. Dark Bottom-Edge Repoussoir (y > 87%)

const BODY_ATLAS := "res://STS2_Things/animations/monsters/abyssal_inkmaw/abyssal_inkmaw.atlas"
const BODY_JSON := "res://STS2_Things/animations/monsters/abyssal_inkmaw/abyssal_inkmaw.spjson"
const BEAM_ATLAS := "res://STS2_Things/animations/monsters/abyssal_ink_beam/abyssal_ink_beam.atlas"
const BEAM_JSON := "res://STS2_Things/animations/monsters/abyssal_ink_beam/abyssal_ink_beam.spjson"
const CRUSH_ATLAS := "res://STS2_Things/animations/monsters/abyssal_crushing_tentacle/abyssal_crushing_tentacle.atlas"
const CRUSH_JSON := "res://STS2_Things/animations/monsters/abyssal_crushing_tentacle/abyssal_crushing_tentacle.spjson"
const INK_ATLAS := "res://STS2_Things/animations/monsters/abyssal_ink_tentacle/abyssal_ink_tentacle.atlas"
const INK_JSON := "res://STS2_Things/animations/monsters/abyssal_ink_tentacle/abyssal_ink_tentacle.spjson"

const BG_WATER_PATH := "res://images/rooms/abyssal_inkmaw/abyssal_inkmaw_bg_water.png"
const BRIDGE_PATH := "res://images/rooms/abyssal_inkmaw/abyssal_inkmaw_bridge.png"
const FG_FRAME_PATH := "res://images/rooms/abyssal_inkmaw/abyssal_inkmaw_fg_frame.png"

const PLATE_W := 2304.0
const PLATE_H := 1080.0
const LEFT_WATER_POS := Vector2(-470.0, 96.0)
const RIGHT_WATER_POS := Vector2(470.0, 96.0)
const LEFT_FLOOR_POS := Vector2(-475.0, 240.0)


func _initialize() -> void:
	call_deferred("_run")


func _load_tex(res_path: String) -> Texture2D:
	var abs_p := ProjectSettings.globalize_path(res_path)
	var img := Image.load_from_file(abs_p)
	return ImageTexture.create_from_image(img)


func _load_spine_data(atlas_path: String, json_path: String, default_mix: float = 0.14) -> SpineSkeletonDataResource:
	var atlas := SpineAtlasResource.new()
	if atlas.load_from_atlas_file(atlas_path) != OK:
		push_error("Failed to load Spine atlas: " + atlas_path)
		return null
	var skel_file := SpineSkeletonFileResource.new()
	if skel_file.load_from_file(ProjectSettings.globalize_path(json_path)) != OK:
		push_error("Failed to load spjson: " + json_path)
		return null
	var skel_data := SpineSkeletonDataResource.new()
	skel_data.atlas_res = atlas
	skel_data.skeleton_file_res = skel_file
	skel_data.default_mix = default_mix
	if not skel_data.is_skeleton_data_loaded():
		push_error("Skeleton data failed to initialize: " + json_path)
		return null
	return skel_data


func _centered_plate(tex: Texture2D, plate_scale: float = 1.0) -> TextureRect:
	var r := TextureRect.new()
	r.texture = tex
	r.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	r.stretch_mode = TextureRect.STRETCH_SCALE
	var w := PLATE_W * plate_scale
	var h := PLATE_H * plate_scale
	r.position = Vector2(-w * 0.5, -h * 0.5)
	r.size = Vector2(w, h)
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return r


func _prepare_clean_dir(dir_path: String) -> void:
	DirAccess.make_dir_recursive_absolute(dir_path)
	var d := DirAccess.open(dir_path)
	if d != null:
		d.list_dir_begin()
		var fname := d.get_next()
		while fname != "":
			if not d.current_is_dir() and fname.ends_with(".png"):
				d.remove(fname)
			fname = d.get_next()
		d.list_dir_end()


func _sync_camera_shake(chamber_rig: Node2D, base_center: Vector2, body_sprite: SpineSprite) -> void:
	var skel = body_sprite.get_skeleton()
	if skel == null:
		return
	var cam_bone = skel.find_bone("camera_shake")
	if cam_bone != null:
		chamber_rig.position = Vector2(base_center.x + float(cam_bone.get_x()), base_center.y - float(cam_bone.get_y()))


func _make_spine_sprite(skel_data: SpineSkeletonDataResource, pos: Vector2) -> SpineSprite:
	var sp := SpineSprite.new()
	sp.skeleton_data_res = skel_data
	sp.position = pos
	sp.set_update_mode(SpineConstant.UpdateMode_Manual)
	return sp


func _set_tentacle(sp: SpineSprite, mode_anim: String, mode_loop: bool, action_anim: String, action_loop: bool) -> void:
	var st = sp.get_animation_state()
	st.clear_tracks()
	sp.get_skeleton().set_to_setup_pose()
	if action_anim != "":
		st.set_animation(action_anim, action_loop, 0).set_mix_duration(0.0)
	if mode_anim != "":
		st.set_animation(mode_anim, mode_loop, 1).set_mix_duration(0.0)
	sp.update_skeleton(0.0)


func _reset_all(
	chamber_rig: Node2D,
	base_center: Vector2,
	body_sp: SpineSprite,
	beam_sp: SpineSprite,
	crush_water_sp: SpineSprite,
	ink_water_sp: SpineSprite,
	crush_floor_sp: SpineSprite,
	body_anim: String = "idle_loop",
	crush_anim: String = "idle_loop",
	ink_anim: String = "idle_loop",
	action_loop: bool = true
) -> void:
	var bst = body_sp.get_animation_state()
	bst.clear_tracks()
	body_sp.get_skeleton().set_to_setup_pose()
	bst.set_animation(body_anim, action_loop, 0).set_mix_duration(0.0)
	body_sp.update_skeleton(0.0)

	var bmst = beam_sp.get_animation_state()
	bmst.clear_tracks()
	beam_sp.get_skeleton().set_to_setup_pose()
	bmst.set_animation("idle_hidden", true, 0).set_mix_duration(0.0)
	beam_sp.update_skeleton(0.0)

	crush_water_sp.visible = true
	_set_tentacle(crush_water_sp, "base_water", true, crush_anim, action_loop)
	ink_water_sp.visible = true
	_set_tentacle(ink_water_sp, "base_water", true, ink_anim, action_loop)
	crush_floor_sp.visible = false
	_set_tentacle(crush_floor_sp, "base_floor", true, "idle_loop", true)
	_sync_camera_shake(chamber_rig, base_center, body_sp)


func _step_and_capture(
	viewport: SubViewport,
	chamber_rig: Node2D,
	base_center: Vector2,
	sprites: Array,
	body_sp: SpineSprite,
	duration: float,
	fps: float,
	out_dir: String,
	start_idx: int
) -> int:
	var steps := maxi(1, int(round(duration * fps)))
	var dt := 1.0 / fps
	var idx := start_idx
	for i in range(steps):
		var step_dt := dt if i > 0 else 0.0001
		for sp in sprites:
			if sp.visible:
				sp.update_skeleton(step_dt)
		_sync_camera_shake(chamber_rig, base_center, body_sp)
		await process_frame
		RenderingServer.force_draw(false)
		viewport.get_texture().get_image().save_png(out_dir.path_join("%04d.png" % idx))
		idx += 1
	return idx


func _render_aspect_still(
	body_data: SpineSkeletonDataResource,
	crush_data: SpineSkeletonDataResource,
	ink_data: SpineSkeletonDataResource,
	tex_water: Texture2D,
	tex_bridge: Texture2D,
	tex_fg: Texture2D,
	width: int,
	height: int,
	out_png: String
) -> void:
	var vp := SubViewport.new()
	vp.size = Vector2i(width, height)
	vp.disable_3d = true
	vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	get_root().add_child(vp)

	var rig := Node2D.new()
	rig.position = Vector2(width * 0.5, height * 0.5)
	var plate_scale := maxf(1.0, float(width) / PLATE_W)
	vp.add_child(rig)

	rig.add_child(_centered_plate(tex_water, plate_scale))
	var body_sp := _make_spine_sprite(body_data, Vector2.ZERO)
	rig.add_child(body_sp)
	var crush_sp := _make_spine_sprite(crush_data, LEFT_WATER_POS)
	var ink_sp := _make_spine_sprite(ink_data, RIGHT_WATER_POS)
	rig.add_child(crush_sp)
	rig.add_child(ink_sp)
	rig.add_child(_centered_plate(tex_bridge, plate_scale))
	rig.add_child(_centered_plate(tex_fg, plate_scale))

	await process_frame
	body_sp.get_animation_state().set_animation("idle_loop", true, 0).set_mix_duration(0.0)
	_set_tentacle(crush_sp, "base_water", true, "idle_loop", true)
	_set_tentacle(ink_sp, "base_water", true, "idle_loop", true)
	body_sp.update_skeleton(0.35)
	crush_sp.update_skeleton(0.35)
	ink_sp.update_skeleton(0.35)
	await process_frame
	RenderingServer.force_draw(false)
	vp.get_texture().get_image().save_png(out_png)
	vp.queue_free()
	await process_frame


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1:
		push_error("usage: <OUTPUT_DIR> [SCALE] [FPS] [ONLY_SEQ]")
		quit(2)
		return
	var out_root: String = args[0]
	var scale := float(args[1]) if args.size() > 1 else 0.5
	var fps := float(args[2]) if args.size() > 2 else 18.0
	var only_seq: String = args[3] if args.size() > 3 else ""
	DirAccess.make_dir_recursive_absolute(out_root)

	var body_data := _load_spine_data(BODY_ATLAS, BODY_JSON, 0.16)
	var beam_data := _load_spine_data(BEAM_ATLAS, BEAM_JSON, 0.06)
	var crush_data := _load_spine_data(CRUSH_ATLAS, CRUSH_JSON, 0.10)
	var ink_data := _load_spine_data(INK_ATLAS, INK_JSON, 0.10)
	if body_data == null or beam_data == null or crush_data == null or ink_data == null:
		quit(3)
		return

	var tex_water := _load_tex(BG_WATER_PATH)
	var tex_bridge := _load_tex(BRIDGE_PATH)
	var tex_fg := _load_tex(FG_FRAME_PATH)

	var vp_size := Vector2i(int(round(1920 * scale)), int(round(1080 * scale)))
	var viewport := SubViewport.new()
	viewport.size = vp_size
	viewport.disable_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	get_root().add_child(viewport)

	var world := Node2D.new()
	world.scale = Vector2.ONE * scale
	viewport.add_child(world)

	var base_center := Vector2(960.0, 540.0)
	var chamber_rig := Node2D.new()
	chamber_rig.position = base_center
	chamber_rig.scale = Vector2(1.022, 1.022)
	world.add_child(chamber_rig)

	# Full 4-Rig Sandwich Depth Architecture!
	chamber_rig.add_child(_centered_plate(tex_water))
	var body_sp := _make_spine_sprite(body_data, Vector2.ZERO)
	chamber_rig.add_child(body_sp)
	var crush_water_sp := _make_spine_sprite(crush_data, LEFT_WATER_POS)
	var ink_water_sp := _make_spine_sprite(ink_data, RIGHT_WATER_POS)
	chamber_rig.add_child(crush_water_sp)
	chamber_rig.add_child(ink_water_sp)
	chamber_rig.add_child(_centered_plate(tex_bridge))
	var crush_floor_sp := _make_spine_sprite(crush_data, LEFT_FLOOR_POS)
	crush_floor_sp.visible = false
	chamber_rig.add_child(crush_floor_sp)
	var beam_sp := _make_spine_sprite(beam_data, Vector2.ZERO)
	chamber_rig.add_child(beam_sp)
	chamber_rig.add_child(_centered_plate(tex_fg))

	var all_sp := [body_sp, crush_water_sp, ink_water_sp, crush_floor_sp, beam_sp]
	await process_frame
	await process_frame

	# -------------------------------------------------------------------------
	# SEQUENCE 1: Colossal Half-Submerged Kraken + Water-Emerging Tentacles Idle Loop (4.0s)
	# -------------------------------------------------------------------------
	if only_seq == "" or only_seq == "seq_1":
		var dir1 := out_root.path_join("seq_1_stage_idle")
		_prepare_clean_dir(dir1)
		_reset_all(chamber_rig, base_center, body_sp, beam_sp, crush_water_sp, ink_water_sp, crush_floor_sp)
		await _step_and_capture(viewport, chamber_rig, base_center, all_sp, body_sp, 4.0, fps, dir1, 0)

	# -------------------------------------------------------------------------
	# SEQUENCE 2: Left->Right Sweeping Hydro-Ink Beam -> Phase 2【暗潮狂涌】Breach -> Enraged Idle
	# -------------------------------------------------------------------------
	if only_seq == "" or only_seq == "seq_2":
		var dir2 := out_root.path_join("seq_2_ink_and_phase2")
		_prepare_clean_dir(dir2)
		_reset_all(chamber_rig, base_center, body_sp, beam_sp, crush_water_sp, ink_water_sp, crush_floor_sp)
		var fidx := 0
		fidx = await _step_and_capture(viewport, chamber_rig, base_center, all_sp, body_sp, 0.30, fps, dir2, fidx)

		# 1) Synchronized Body + Foreground Sweeping Hydro-Ink Jet (`-46° -> 0° ink_hit_player! -> +46°`)!
		body_sp.get_animation_state().set_animation("ink_spray", false, 0).set_mix_duration(0.08)
		body_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		beam_sp.get_animation_state().set_animation("ink_spray", false, 0).set_mix_duration(0.0)
		beam_sp.get_animation_state().add_animation("idle_hidden", 0.0, true, 0)
		fidx = await _step_and_capture(viewport, chamber_rig, base_center, all_sp, body_sp, 2.15, fps, dir2, fidx)

		# 2) Brand-new Colossal Tidal Crash【暗潮压境】lunge + 6-arm wave slam + spray plume + droplets!
		body_sp.get_animation_state().set_animation("tidal_crash", false, 0).set_mix_duration(0.10)
		body_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		fidx = await _step_and_capture(viewport, chamber_rig, base_center, all_sp, body_sp, 1.70, fps, dir2, fidx)

		# 3) Phase 2【暗潮狂涌】Breach + Spray Plume + Enraged Idle
		body_sp.get_animation_state().set_animation("phase2_surge", false, 0).set_mix_duration(0.12)
		body_sp.get_animation_state().add_animation("phase2_idle_loop", 0.0, true, 0)
		crush_water_sp.get_animation_state().set_animation("guard_coil", false, 0).set_mix_duration(0.10)
		crush_water_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		crush_water_sp.get_animation_state().set_animation("base_water_burst", false, 1).set_mix_duration(0.06)
		crush_water_sp.get_animation_state().add_animation("base_water", 0.0, true, 1)
		ink_water_sp.get_animation_state().set_animation("ink_bolster", false, 0).set_mix_duration(0.10)
		ink_water_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		ink_water_sp.get_animation_state().set_animation("base_water_burst", false, 1).set_mix_duration(0.06)
		ink_water_sp.get_animation_state().add_animation("base_water", 0.0, true, 1)
		fidx = await _step_and_capture(viewport, chamber_rig, base_center, all_sp, body_sp, 2.80, fps, dir2, fidx)

	# -------------------------------------------------------------------------
	# SEQUENCE 3: Hardware-Clipped 1:1 Water Emergence -> 12-Bone Slam & Whip -> Bridge-Floor Breach w/ Hinged Slabs & Bouncing Shards!
	# -------------------------------------------------------------------------
	if only_seq == "" or only_seq == "seq_3":
		var dir3 := out_root.path_join("seq_3_tentacles_actions")
		_prepare_clean_dir(dir3)
		_reset_all(chamber_rig, base_center, body_sp, beam_sp, crush_water_sp, ink_water_sp, crush_floor_sp, "summon_tentacle", "emerge", "emerge", false)
		body_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		# Pair water emergence with live bursting waves & parabolic water droplets!
		crush_water_sp.get_animation_state().set_animation("base_water_burst", false, 1).set_mix_duration(0.0)
		crush_water_sp.get_animation_state().add_animation("base_water", 0.0, true, 1)
		crush_water_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		ink_water_sp.get_animation_state().set_animation("base_water_burst", false, 1).set_mix_duration(0.0)
		ink_water_sp.get_animation_state().add_animation("base_water", 0.0, true, 1)
		ink_water_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		var fidx := 0
		# 1) Twin 1:1 unstretched serpentine emergence out of lake water with hand-painted waves & droplets!
		fidx = await _step_and_capture(viewport, chamber_rig, base_center, all_sp, body_sp, 1.25, fps, dir3, fidx)

		# 2) Left Crushing Tentacle 12-bone S-whip heavy slam + water splash burst!
		crush_water_sp.get_animation_state().set_animation("heavy_slam", false, 0).set_mix_duration(0.08)
		crush_water_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		crush_water_sp.get_animation_state().set_animation("base_water_burst", false, 1).set_mix_duration(0.06)
		crush_water_sp.get_animation_state().add_animation("base_water", 0.0, true, 1)
		fidx = await _step_and_capture(viewport, chamber_rig, base_center, all_sp, body_sp, 1.60, fps, dir3, fidx)

		# 3) Right Ink-Sac Tentacle 2-hit serpentine whip lash + water splash burst!
		ink_water_sp.get_animation_state().set_animation("whip_lash", false, 0).set_mix_duration(0.08)
		ink_water_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		ink_water_sp.get_animation_state().set_animation("base_water_burst", false, 1).set_mix_duration(0.06)
		ink_water_sp.get_animation_state().add_animation("base_water", 0.0, true, 1)
		fidx = await _step_and_capture(viewport, chamber_rig, base_center, all_sp, body_sp, 1.50, fps, dir3, fidx)

		# 4) Left Water Tentacle slithers back below waterline + Boss recoil hurt!
		crush_water_sp.get_animation_state().set_animation("retreat_die", false, 0).set_mix_duration(0.06)
		crush_water_sp.get_animation_state().set_animation("base_water_burst", false, 1).set_mix_duration(0.05)
		body_sp.get_animation_state().set_animation("hurt", false, 0).set_mix_duration(0.06)
		body_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		fidx = await _step_and_capture(viewport, chamber_rig, base_center, all_sp, body_sp, 1.15, fps, dir3, fidx)

		# 5) Bridge-Floor Breach Demo: 4 hinged flagstones HEAVE open + 5 spinning bouncing stone shards + dust + 1:1 serpentine emergence!
		crush_water_sp.visible = false
		crush_floor_sp.visible = true
		_set_tentacle(crush_floor_sp, "base_floor_burst", false, "emerge", false)
		crush_floor_sp.get_animation_state().add_animation("base_floor", 0.0, true, 1)
		crush_floor_sp.get_animation_state().add_animation("idle_loop", 0.0, true, 0)
		fidx = await _step_and_capture(viewport, chamber_rig, base_center, all_sp, body_sp, 1.50, fps, dir3, fidx)

	# -------------------------------------------------------------------------
	# MULTI-ASPECT NATIVE 1:1 STILLS (16:9, 21:9, 4:3)
	# -------------------------------------------------------------------------
	if only_seq == "" or only_seq == "aspects":
		var asp_dir := out_root.path_join("aspects")
		_prepare_clean_dir(asp_dir)
		await _render_aspect_still(body_data, crush_data, ink_data, tex_water, tex_bridge, tex_fg, 1920, 1080, asp_dir.path_join("aspect_16x9.png"))
		await _render_aspect_still(body_data, crush_data, ink_data, tex_water, tex_bridge, tex_fg, 2560, 1080, asp_dir.path_join("aspect_21x9.png"))
		await _render_aspect_still(body_data, crush_data, ink_data, tex_water, tex_bridge, tex_fg, 1440, 1080, asp_dir.path_join("aspect_4x3.png"))

	print("ABYSSAL_NATIVE_RENDER_OK")
	quit(0)
