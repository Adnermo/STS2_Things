extends SceneTree

# Native Godot 4.5.1 + SpineSprite stage renderer for 人面塔 (The Visage Tower).
# Renders the 3D concave cylindrical wall + 4 relief stone faces + whole-chamber overscan shake!
# Usage:
#   godot --path . --rendering-method gl_compatibility --resolution 960x540 \
#         --script res://tools/HumanFaceTowerRig/render_native.gd -- <OUTPUT_DIR> [SCALE] [FPS]

const ATLAS_PATH := "res://STS2_Things/animations/monsters/human_face_tower/human_face_tower.atlas"
const SPJSON_PATH := "res://STS2_Things/animations/monsters/human_face_tower/human_face_tower.spjson"
const BG_PATH := "res://images/rooms/human_face_tower/tower_bg_chamber.png"
const FG_PATH := "res://images/rooms/human_face_tower/tower_fg_frame.png"
const LIGHT_PATH := "res://images/rooms/human_face_tower/tower_light_shaft.png"


func _initialize() -> void:
	call_deferred("_run")


func _load_tex(res_path: String) -> Texture2D:
	var abs_p := ProjectSettings.globalize_path(res_path)
	var img := Image.load_from_file(abs_p)
	return ImageTexture.create_from_image(img)


func _centered_rect(tex: Texture2D) -> TextureRect:
	var r := TextureRect.new()
	r.texture = tex
	r.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	r.stretch_mode = TextureRect.STRETCH_SCALE
	r.position = Vector2(-960, -540)
	r.size = Vector2(1920, 1080)
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


func _sync_chamber_shake(chamber_rig: Node2D, sprite: SpineSprite) -> void:
	var skel = sprite.get_skeleton()
	if skel == null:
		return
	var cam_bone = skel.find_bone("camera_shake")
	if cam_bone != null:
		chamber_rig.position = Vector2(960.0 + float(cam_bone.get_x()), 540.0 - float(cam_bone.get_y()))


func _step_and_capture(
	viewport: SubViewport,
	chamber_rig: Node2D,
	sprite: SpineSprite,
	duration: float,
	fps: float,
	out_dir: String,
	start_idx: int
) -> int:
	var steps := maxi(1, int(round(duration * fps)))
	var dt := 1.0 / fps
	var idx := start_idx
	for i in range(steps):
		sprite.update_skeleton(dt if i > 0 else 0.0001)
		_sync_chamber_shake(chamber_rig, sprite)
		await process_frame
		RenderingServer.force_draw(false)
		viewport.get_texture().get_image().save_png(out_dir.path_join("%04d.png" % idx))
		idx += 1
	return idx


func _reset_rig(
	chamber_rig: Node2D,
	sprite: SpineSprite,
	slot_index: int,
	idle_anim: String,
	shattered_slots: Array = [],
	bastion_bolted: bool = true
) -> void:
	var state = sprite.get_animation_state()
	state.clear_tracks()
	sprite.get_skeleton().set_to_setup_pose()
	var e0 = state.set_animation("hold_slot_%d" % slot_index, true, 0)
	e0.set_mix_duration(0.0)
	var e1 = state.set_animation("state_bastion_bolted" if bastion_bolted else "state_bastion_unbolted", true, 1)
	e1.set_mix_duration(0.0)
	var e2 = state.set_animation(idle_anim, true, 2)
	e2.set_mix_duration(0.0)
	for dead_idx in shattered_slots:
		var es = state.set_animation("state_shattered_%d" % int(dead_idx), true, 3 + int(dead_idx))
		es.set_mix_duration(0.0)
	sprite.update_skeleton(0.0)
	_sync_chamber_shake(chamber_rig, sprite)


func _run() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 1:
		push_error("usage: <OUTPUT_DIR> [SCALE] [FPS]")
		quit(2)
		return
	var out_root: String = args[0]
	var scale := float(args[1]) if args.size() > 1 else 0.5
	var fps := float(args[2]) if args.size() > 2 else 18.0
	var only_seq: String = args[3] if args.size() > 3 else ""
	DirAccess.make_dir_recursive_absolute(out_root)

	var atlas := SpineAtlasResource.new()
	if atlas.load_from_atlas_file(ATLAS_PATH) != OK:
		push_error("Failed to load Spine atlas: " + ATLAS_PATH)
		quit(3)
		return
	var skel_file := SpineSkeletonFileResource.new()
	if skel_file.load_from_file(ProjectSettings.globalize_path(SPJSON_PATH)) != OK:
		push_error("Failed to load spjson: " + SPJSON_PATH)
		quit(3)
		return
	var skel_data := SpineSkeletonDataResource.new()
	skel_data.atlas_res = atlas
	skel_data.skeleton_file_res = skel_file
	skel_data.default_mix = 0.10
	if not skel_data.is_skeleton_data_loaded():
		push_error("Skeleton data failed to initialize")
		quit(3)
		return

	var vp_size := Vector2i(int(round(1920 * scale)), int(round(1080 * scale)))
	var viewport := SubViewport.new()
	viewport.size = vp_size
	viewport.disable_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	get_root().add_child(viewport)

	var world := Node2D.new()
	world.scale = Vector2.ONE * scale
	viewport.add_child(world)

	# 1.032x overscanned whole-chamber parent so 10,000-ton grinding & stop-wedge slams shake the entire tower room!
	var chamber_rig := Node2D.new()
	chamber_rig.position = Vector2(960, 540)
	chamber_rig.scale = Vector2(1.032, 1.032)
	world.add_child(chamber_rig)

	var bg := _centered_rect(_load_tex(BG_PATH))
	chamber_rig.add_child(bg)

	var sprite := SpineSprite.new()
	sprite.skeleton_data_res = skel_data
	sprite.position = Vector2.ZERO
	sprite.set_update_mode(SpineConstant.UpdateMode_Manual)
	chamber_rig.add_child(sprite)

	var fg := _centered_rect(_load_tex(FG_PATH))
	chamber_rig.add_child(fg)

	var light := _centered_rect(_load_tex(LIGHT_PATH))
	chamber_rig.add_child(light)

	await process_frame
	await process_frame
	var st = sprite.get_animation_state()
	var fidx := 0

	# -------------------------------------------------------------------------
	# SEQUENCE 1: Full 360° 4-Face Monumental Stone Rotation Cycle
	# -------------------------------------------------------------------------
	if only_seq == "" or only_seq == "seq_1":
		var dir1 := out_root.path_join("seq_1_rotation_cycle")
		_prepare_clean_dir(dir1)
		_reset_rig(chamber_rig, sprite, 0, "wrath_idle")
		st = sprite.get_animation_state()
		fidx = 0
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 0.40, fps, dir1, fidx)

		st.set_animation("turn_0_to_1", false, 0).set_mix_duration(0.05)
		st.set_animation("lament_idle", true, 2).set_mix_duration(0.22)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 2.30, fps, dir1, fidx)

		st.set_animation("turn_1_to_2", false, 0).set_mix_duration(0.05)
		st.set_animation("bastion_idle_bolted", true, 2).set_mix_duration(0.22)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 2.30, fps, dir1, fidx)

		st.set_animation("turn_2_to_3", false, 0).set_mix_duration(0.05)
		st.set_animation("mockery_idle", true, 2).set_mix_duration(0.22)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 2.30, fps, dir1, fidx)

		st.set_animation("turn_3_to_0", false, 0).set_mix_duration(0.05)
		st.set_animation("wrath_idle", true, 2).set_mix_duration(0.22)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 2.30, fps, dir1, fidx)

	# -------------------------------------------------------------------------
	# SEQUENCE 2: Wrath (裂石重槌 + 碎颌双震) & Lament (风化悲鸣 + 血泪连雨)
	# -------------------------------------------------------------------------
	if only_seq == "" or only_seq == "seq_2":
		var dir2 := out_root.path_join("seq_2_wrath_and_lament")
		_prepare_clean_dir(dir2)
		_reset_rig(chamber_rig, sprite, 0, "wrath_idle")
		st = sprite.get_animation_state()
		fidx = 0
		st.set_animation("wrath_attack_heavy", false, 2).set_mix_duration(0.08)
		st.add_animation("wrath_idle", 0.0, true, 2)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.25, fps, dir2, fidx)

		st.set_animation("wrath_attack_double", false, 2).set_mix_duration(0.08)
		st.add_animation("wrath_idle", 0.0, true, 2)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.25, fps, dir2, fidx)

		st.set_animation("turn_0_to_1", false, 0).set_mix_duration(0.05)
		st.set_animation("lament_idle", true, 2).set_mix_duration(0.20)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 2.25, fps, dir2, fidx)

		st.set_animation("lament_cast_wail", false, 2).set_mix_duration(0.08)
		st.add_animation("lament_idle", 0.0, true, 2)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.45, fps, dir2, fidx)

		st.set_animation("lament_attack_rain", false, 2).set_mix_duration(0.08)
		st.add_animation("lament_idle", 0.0, true, 2)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.35, fps, dir2, fidx)

	# -------------------------------------------------------------------------
	# SEQUENCE 3: Bastion (万钧崩落 -> 破栓碎裂 -> 裸唇受击 -> 镇岳合栓重锁) & Mockery (斜睨游移 + 戏谑反讽 + 冷笑双刺 + 旋牙突咬)
	# -------------------------------------------------------------------------
	if only_seq == "" or only_seq == "seq_3":
		var dir3 := out_root.path_join("seq_3_bastion_and_mockery")
		_prepare_clean_dir(dir3)
		_reset_rig(chamber_rig, sprite, 2, "bastion_idle_bolted", [], true)
		st = sprite.get_animation_state()
		fidx = 0
		st.set_animation("bastion_attack_colossus", false, 2).set_mix_duration(0.08)
		st.add_animation("bastion_idle_bolted", 0.0, true, 2)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.30, fps, dir3, fidx)

		st.set_animation("state_bastion_unbolted", true, 1).set_mix_duration(0.0)
		st.set_animation("bastion_unbolt", false, 2).set_mix_duration(0.0)
		st.add_animation("bastion_idle_unbolted", 0.0, true, 2)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.30, fps, dir3, fidx)

		st.set_animation("state_bastion_bolted", true, 1).set_mix_duration(0.0)
		st.set_animation("bastion_rebolt_attack", false, 2).set_mix_duration(0.0)
		st.add_animation("bastion_idle_bolted", 0.0, true, 2)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.05, fps, dir3, fidx)

		st.set_animation("turn_2_to_3", false, 0).set_mix_duration(0.05)
		st.set_animation("mockery_idle", true, 2).set_mix_duration(0.20)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 2.30, fps, dir3, fidx)

		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.35, fps, dir3, fidx)

		st.set_animation("mockery_taunt", false, 2).set_mix_duration(0.08)
		st.add_animation("mockery_attack_twin", 0.0, false, 2)
		st.add_animation("mockery_attack_fang", 0.0, false, 2)
		st.add_animation("mockery_idle", 0.0, true, 2)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 3.00, fps, dir3, fidx)

	# -------------------------------------------------------------------------
	# SEQUENCE 4: Mid-Turn Face Shatter + Rotational 2-Hit Combo Skipping Dead Face + Climax Roar!
	# -------------------------------------------------------------------------
	if only_seq == "" or only_seq == "seq_4":
		var dir4 := out_root.path_join("seq_4_shatter_and_combo")
		_prepare_clean_dir(dir4)
		_reset_rig(chamber_rig, sprite, 1, "lament_idle")
		st = sprite.get_animation_state()
		fidx = 0
		st.set_animation("face_hurt_1", false, 2).set_mix_duration(0.04)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 0.30, fps, dir4, fidx)

		st.clear_track(2)
		sprite.get_skeleton().set_bones_to_setup_pose()
		st.set_animation("face_shatter_1", false, 4).set_mix_duration(0.0)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.05, fps, dir4, fidx)
		st.set_animation("state_shattered_1", true, 4).set_mix_duration(0.0)

		st.set_animation("combo_turn_1_to_2", false, 0).set_mix_duration(0.04)
		st.set_animation("face_stun_2", true, 2).set_mix_duration(0.12)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.15, fps, dir4, fidx)

		_reset_rig(chamber_rig, sprite, 0, "wrath_idle", [1])
		st = sprite.get_animation_state()
		st.set_animation("wrath_attack_heavy", false, 2).set_mix_duration(0.06)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 0.78, fps, dir4, fidx)

		st.set_animation("combo_turn_0_to_2", false, 0).set_mix_duration(0.04)
		st.set_animation("bastion_idle_bolted", true, 2).set_mix_duration(0.10)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 0.98, fps, dir4, fidx)

		st.set_animation("bastion_attack_colossus", false, 2).set_mix_duration(0.06)
		st.add_animation("bastion_idle_bolted", 0.0, true, 2)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.25, fps, dir4, fidx)

		_reset_rig(chamber_rig, sprite, 0, "wrath_idle", [1])
		st = sprite.get_animation_state()
		st.set_animation("climax_cycle_roar_0", false, 2).set_mix_duration(0.06)
		st.add_animation("wrath_idle", 0.0, true, 2)
		fidx = await _step_and_capture(viewport, chamber_rig, sprite, 1.35, fps, dir4, fidx)

	print("NATIVE_RENDER_OK")
	quit(0)
