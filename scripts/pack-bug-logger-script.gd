extends SceneTree

# Godot must be able to resolve the ScriptPath of a directly constructed C#
# Logger. Add that resource without exporting unrelated working assets.
const LOGGER := "res://STS2_Things/Diagnostics/BugEngineLogger.cs"

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() != 4 or not ProjectSettings.load_resource_pack(args[0]):
		_fail("Expected baseline PCK, logger source, output PCK and hash report")
		return
	var paths: Array[String] = []
	_collect("res://", paths)
	var before := {}
	for path in paths:
		before[path] = FileAccess.get_sha256(path)
	if not paths.has(LOGGER):
		paths.append(LOGGER)
	paths.sort()
	var pack := PCKPacker.new()
	if pack.pck_start(args[2]) != OK:
		_fail("Cannot start output pack")
		return
	for path in paths:
		if pack.add_file(path, args[1] if path == LOGGER else path) != OK:
			_fail("Cannot add: " + path)
			return
	if pack.flush() != OK or not ProjectSettings.load_resource_pack(args[2]):
		_fail("Cannot flush or remount final pack")
		return
	var changes := []
	for path in paths:
		var expected: String = FileAccess.get_sha256(args[1]) if path == LOGGER else before[path]
		if FileAccess.get_sha256(path) != expected:
			_fail("Hash mismatch: " + path)
			return
		if expected != before.get(path, "added"):
			changes.append({"path":path, "before":before.get(path, "added"), "after":expected})
	var report := FileAccess.open(args[3], FileAccess.WRITE)
	report.store_string(JSON.stringify({"baseline_sha256":FileAccess.get_sha256(args[0]),
		"output_sha256":FileAccess.get_sha256(args[2]), "resources_verified":paths.size(), "changes":changes}, "\t") + "\n")
	report.close()
	print("BUG_LOGGER_PACK_PASS resources=", paths.size(), " changes=", changes.size())
	quit(0)

func _collect(directory: String, paths: Array[String]) -> void:
	var dir := DirAccess.open(directory)
	dir.include_hidden = true
	for name in dir.get_files():
		if directory == "res://" and name == "project.godot":
			continue
		paths.append(directory.path_join(name))
	for name in dir.get_directories():
		_collect(directory.path_join(name), paths)

func _fail(message: String) -> void:
	push_error(message)
	quit(1)
