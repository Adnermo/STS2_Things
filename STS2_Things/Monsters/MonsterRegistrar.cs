#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Audio;
using STS2_Things.Hooks;

namespace STS2_Things.Monsters;

public static class MonsterRegistrar
{
    // ========== Act 标识 ==========
    public static readonly Type ActOvergrowth = typeof(Overgrowth);
    public static readonly Type ActUnderdocks = typeof(Underdocks);
    public static readonly Type ActHive = typeof(Hive);

    private static readonly List<Entry> _entries = new();
    private static readonly Dictionary<Type, Entry> _byMonsterType = new();

    private static readonly Dictionary<Type, HashSet<Type>> _encountersByAct = new()
    {
        [typeof(Overgrowth)] = new HashSet<Type>(),
        [typeof(Underdocks)] = new HashSet<Type>(),
        [typeof(Hive)] = new HashSet<Type>()
    };

    private static readonly FieldInfo _bodyField =
        typeof(NCreatureVisuals).GetField("_body", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo _visualsField =
        typeof(NCombatRoom).GetField("_visuals", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo _bgContainerProp =
        typeof(NCombatRoom).GetProperty("BgContainer", BindingFlags.Instance | BindingFlags.NonPublic);

    private static Control _activeFgContainer;

    private static string PascalToSnake(string pascal)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i]) && char.IsLower(pascal[i - 1]))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(pascal[i]));
        }

        return sb.ToString();
    }

    public static string SlotName<T>() where T : MonsterModel
    {
        return PascalToSnake(typeof(T).Name);
    }

    public static string SubSlotName<T>(int index) where T : MonsterModel
    {
        return $"{PascalToSnake(typeof(T).Name)}_{index + 1}";
    }

    private static string InferTexPath(Type t)
    {
        return $"res://images/monsters/{PascalToSnake(t.Name)}.png";
    }

    private static string InferBgPath(Type t)
    {
        return $"res://images/backgrounds/{PascalToSnake(t.Name)}_bg.png";
    }

    private static string ResolveRandomVariant(Type monsterType)
    {
        var baseName = PascalToSnake(monsterType.Name);
        var candidates = new List<string>();
        for (var i = 1; i <= 20; i++)
        {
            var path = $"res://images/monsters/{baseName}_{i}.png";
            if (ResourceLoader.Exists(path)) candidates.Add(path);
        }

        return candidates.Count > 0
            ? candidates[Random.Shared.Next(candidates.Count)]
            : $"res://images/monsters/{baseName}.png";
    }

    // ========== Initialize ==========

    public static void Initialize(Harmony harmony)
    {
        harmony.Patch(ActOvergrowth.GetMethod(nameof(Overgrowth.GenerateAllEncounters)),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(InjectEncounters)));
        harmony.Patch(ActUnderdocks.GetMethod(nameof(Underdocks.GenerateAllEncounters)),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(InjectEncounters)));
        harmony.Patch(ActHive.GetMethod(nameof(Hive.GenerateAllEncounters)),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(InjectEncounters)));
        harmony.Patch(
            AccessTools.PropertyGetter(typeof(Overgrowth), nameof(Overgrowth.BossDiscoveryOrder)),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(InjectBossDiscoveryOrder)));
        harmony.Patch(
            AccessTools.PropertyGetter(typeof(Underdocks), nameof(Underdocks.BossDiscoveryOrder)),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(InjectBossDiscoveryOrder)));
        harmony.Patch(
            AccessTools.PropertyGetter(typeof(Hive), nameof(Hive.BossDiscoveryOrder)),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(InjectBossDiscoveryOrder)));
        harmony.Patch(typeof(MonsterModel).GetMethod(nameof(MonsterModel.CreateVisuals)),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(InjectVisuals)));
        harmony.Patch(typeof(NCombatRoom).GetMethod(nameof(NCombatRoom.SetUpBackground)),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(InjectBackground)));
        harmony.Patch(typeof(RunManager).GetMethod(nameof(RunManager.GenerateRooms)),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(ForceBossSpawn)));
        harmony.Patch(typeof(NCombatRoom).GetMethod("_ExitTree"),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(OnCombatRoomExit)));
        harmony.Patch(typeof(FightConsoleCmd).GetMethod(nameof(FightConsoleCmd.GetArgumentCompletions)),
            postfix: new HarmonyMethod(typeof(MonsterRegistrar), nameof(InjectBossFightCompletions)));
        Log.Info("[MonsterRegistrar] 已初始化");
    }

    // ========== Registration API ==========

    public static void AddMinion<T>(float? scale = null, float? offsetY = null, Color? colorMod = null)
        where T : MonsterModel
    {
        var e = new Entry
            { MonsterType = typeof(T), Scale = scale ?? 0.52f, OffsetY = offsetY, ColorModulation = colorMod };
        _entries.Add(e);
        _byMonsterType[typeof(T)] = e;
    }

    /// <param name="acts">MonsterRegistrar.Overgrowth, MonsterRegistrar.Underdocks</param>
    public static void AddBoss<T>(Type encounterType, float? scale = null, float? offsetY = null,
        bool hasBackground = false, bool forceSpawn = false, Type forceSpawnAct = null,
        params Type[] acts)
    {
        var e = new Entry
        {
            MonsterType = typeof(T), EncounterType = encounterType,
            HasBackground = hasBackground, Scale = scale ?? 0.52f, OffsetY = offsetY,
            ForceSpawnBoss = forceSpawn, ForceSpawnAct = forceSpawnAct ?? typeof(Overgrowth)
        };
        _entries.Add(e);
        _byMonsterType[typeof(T)] = e;
        RegisterEncounter(encounterType, acts);
    }

    /// <param name="acts">MonsterRegistrar.Overgrowth, MonsterRegistrar.Underdocks</param>
    public static void AddElite<T>(Type encounterType, float? scale = null, float? offsetY = null,
        Color? colorMod = null, params Type[] acts) where T : MonsterModel
    {
        var e = new Entry
        {
            MonsterType = typeof(T), EncounterType = encounterType,
            Scale = scale ?? 0.52f, OffsetY = offsetY, ColorModulation = colorMod
        };
        _entries.Add(e);
        _byMonsterType[typeof(T)] = e;
        RegisterEncounter(encounterType, acts);
    }

    public static void AddEncounter(Type encounterType, params Type[] acts)
    {
        RegisterEncounter(encounterType, acts);
    }

    private static void RegisterEncounter(Type encounterType, params Type[] acts)
    {
        // 注意：不要在这里调用 ModelDb.Inject。游戏会在 ModelDb.Init 中自动扫描并实例化
        // mod 程序集里的所有 AbstractModel 子类；提前 Inject 会导致重复注册，抛出 DuplicateModelException。

        if (acts == null || acts.Length == 0) acts = new[] { typeof(Overgrowth) };
        foreach (var act in acts)
            if (_encountersByAct.TryGetValue(act, out var set))
                set.Add(encounterType);
    }

    /// <summary>
    /// 将本 mod 注册的 Boss 遭遇加入 fight 命令的 Tab 补全列表（原版只补全普通/精英遭遇）。
    /// </summary>
    private static void InjectBossFightCompletions(ref CompletionResult __result)
    {
        if (__result == null) return;
        var bossEntries = _encountersByAct.Values
            .SelectMany(types => types)
            .Distinct()
            .Select(t => ModelDb.GetByIdOrNull<EncounterModel>(ModelDb.GetId(t)))
            .Where(e => e is { RoomType: RoomType.Boss })
            .Select(e => e!.Id.Entry)
            .Where(entry => !string.IsNullOrEmpty(entry));

        __result.Candidates = __result.Candidates.Concat(bossEntries).Distinct().ToList();
    }

    /// <summary>
    /// 安全地通过反射调用 ModelDb.Encounter<T>()，避免 AmbiguousMatchException
    /// </summary>
    private static EncounterModel? GetEncounter(Type encounterType)
    {
        var method = typeof(ModelDb).GetMethods()
            .FirstOrDefault(m => m.Name == "Encounter" && m.IsGenericMethod
                && m.GetParameters().Length == 0);
        if (method == null) return null;
        return (EncounterModel?)method.MakeGenericMethod(encounterType).Invoke(null, null);
    }

    // ========== Harmony callbacks ==========

    private static void InjectEncounters(ActModel __instance, ref IEnumerable<EncounterModel> __result)
    {
        if (!_encountersByAct.TryGetValue(__instance.GetType(), out var types)) return;
        var added = new List<string>();
        foreach (var t in types)
        {
            var e = GetEncounter(t);
            if (e != null && e.RoomType != RoomType.Boss) // Boss遭遇不注入普通遭遇池，仅通过BossDiscoveryOrder和SetBossEncounter注入
            {
                __result = __result.Append(e);
                added.Add(t.Name);
            }
        }

        __result = __result.Distinct();
        if (added.Count > 0)
            Log.Info(
                $"[MonsterRegistrar] 注入 {__instance.GetType().Name}.GenerateAllEncounters: {string.Join(", ", added)}");
    }

    /// <summary>
    ///     把注册到当前 Act 的 Boss 注入 BossDiscoveryOrder（用于解锁首见顺序）
    /// </summary>
    private static void InjectBossDiscoveryOrder(ActModel __instance, ref IEnumerable<EncounterModel> __result)
    {
        if (!_encountersByAct.TryGetValue(__instance.GetType(), out var types)) return;
        var added = new List<string>();
        foreach (var t in types)
        {
            var e = GetEncounter(t);
            if (e != null && e.RoomType == RoomType.Boss)
            {
                __result = __result.Append(e);
                added.Add(t.Name);
            }
        }

        __result = __result.Distinct();
        if (added.Count > 0)
            Log.Info(
                $"[MonsterRegistrar] 注入 {__instance.GetType().Name}.BossDiscoveryOrder: {string.Join(", ", added)}");
    }

    private static void InjectVisuals(MonsterModel __instance, NCreatureVisuals __result)
    {
        if (!_byMonsterType.TryGetValue(__instance.GetType(), out var cfg)) return;
        if (cfg.ColorModulation.HasValue)
        {
            foreach (var child in __result.GetChildren())
                if (child is CanvasItem ci)
                    ci.Modulate = cfg.ColorModulation.Value;
            return;
        }

        var texPath = ResolveRandomVariant(__instance.GetType());
        var tex = ResourceLoader.Load<Texture2D>(texPath);
        if (tex == null)
        {
            Log.Warn($"[MonsterRegistrar] 贴图未找到: {texPath}");
            return;
        }

        foreach (var child in __result.GetChildren())
            if (child is Node2D n2d)
                n2d.Hide();

        float dw = tex.GetWidth() * cfg.Scale, dh = tex.GetHeight() * cfg.Scale;
        var bounds = __result.GetNodeOrNull<Control>("%Bounds");
        if (bounds != null)
        {
            bounds.Position = new Vector2(-dw / 2f, -dh);
            bounds.Size = new Vector2(dw, dh);
        }

        var center = __result.GetNodeOrNull<Marker2D>("%CenterPos");
        if (center != null) center.Position = new Vector2(0f, -dh / 2f);
        var intent = __result.GetNodeOrNull<Marker2D>("%IntentPos");
        if (intent != null) intent.Position = new Vector2(0f, -dh - 10f);

        var sprite = new Sprite2D
        {
            Name = $"{__instance.GetType().Name}Illustration", Texture = tex, Centered = true,
            Position = new Vector2(0f, cfg.OffsetY ?? -dh / 2f),
            Scale = new Vector2(cfg.Scale, cfg.Scale), ZIndex = 0
        };
        __result.AddChild(sprite);
        _bodyField?.SetValue(__result, sprite);
    }

    private static string InferBgTscnPath(Type encounterType)
    {
        return $"res://scenes/creature_visuals/{PascalToSnake(encounterType.Name)}_bg.tscn";
    }

    private static void InjectBackground(NCombatRoom __instance, IRunState state)
    {
        var visuals = _visualsField?.GetValue(__instance) as ICombatRoomVisuals;
        if (visuals?.Encounter == null) return;
        var match = _entries.FirstOrDefault(e =>
            e.HasBackground && e.EncounterType != null && e.EncounterType.IsInstanceOfType(visuals.Encounter));
        if (match == null) return;
        var bgContainer = _bgContainerProp?.GetValue(__instance) as Control;
        if (bgContainer == null) return;

        // 清理旧元素
        foreach (var child in bgContainer.GetChildren())
        {
            if (GodotObject.IsInstanceValid(child))
                child.QueueFree();
        }
        if (_activeFgContainer != null && GodotObject.IsInstanceValid(_activeFgContainer))
        {
            _activeFgContainer.QueueFree();
            _activeFgContainer = null;
        }

        // 优先尝试 tscn 背景
        var tscnPath = InferBgTscnPath(match.EncounterType);
        if (ResourceLoader.Exists(tscnPath))
        {
            InjectTscnBackground(__instance, bgContainer, match, tscnPath);
            return;
        }

        // 回退到单张贴图背景
        InjectTextureBackground(bgContainer, match);
    }

    private static void InjectTscnBackground(NCombatRoom room, Control bgContainer, Entry match, string tscnPath)
    {
        var scene = ResourceLoader.Load<PackedScene>(tscnPath);
        if (scene == null)
        {
            Log.Warn($"[MonsterRegistrar] tscn加载失败: {tscnPath}");
            return;
        }

        var root = scene.Instantiate();
        bgContainer.AddChild(root);

        // 提取 fg_ 前缀节点到前景容器
        var fgChildren = new List<Node>();
        foreach (var child in root.GetChildren())
            if (child.Name.ToString().StartsWith("fg_", StringComparison.OrdinalIgnoreCase))
                fgChildren.Add(child);

        if (fgChildren.Count > 0)
        {
            var fgContainer = new Control { Name = "CustomForeground", MouseFilter = Control.MouseFilterEnum.Ignore };
            foreach (var fg in fgChildren)
            {
                root.RemoveChild(fg);
                if (fg is Control c) c.MouseFilter = Control.MouseFilterEnum.Ignore;
                fgContainer.AddChild(fg);
            }

            // 放到 SceneContainer 下 → 跟随画面震动，ZIndex=1 盖住怪物
            room.SceneContainer.AddChild(fgContainer);
            fgContainer.ZIndex = 1;
            _activeFgContainer = fgContainer;
        }

        Log.Info($"[MonsterRegistrar] {match.MonsterType.Name} 背景(tscn)注入完成。");
    }

    private static void InjectTextureBackground(Control bgContainer, Entry match)
    {
        var tex = ResourceLoader.Load<Texture2D>(InferBgPath(match.MonsterType));
        if (tex == null)
        {
            Log.Warn($"[MonsterRegistrar] 背景未找到: {InferBgPath(match.MonsterType)}");
            return;
        }

        var bgRect = new TextureRect
        {
            Name = $"{match.MonsterType.Name}CustomBackground", Texture = tex,
            AnchorsPreset = (int)Control.LayoutPreset.FullRect,
            GrowHorizontal = Control.GrowDirection.Both, GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true
        };
        bgContainer.AddChild(bgRect);

        var coverScale = Mathf.Max(1920f / tex.GetWidth(), 1080f / tex.GetHeight());
        bgRect.Scale = new Vector2(coverScale, coverScale);

        // 图片不足以填满屏幕时，等比放大并居中
        if (coverScale > 1f) bgRect.PivotOffset = new Vector2(960f, 540f);
        Log.Info($"[MonsterRegistrar] {match.MonsterType.Name} 背景(贴图)注入完成。");
    }

    private static void ForceBossSpawn(RunManager __instance)
    {
        var state = (RunState?)typeof(RunManager).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(__instance);
        if (state == null) return;
        foreach (var e in _entries)
        {
            if (!e.ForceSpawnBoss || e.EncounterType == null) continue;
            foreach (var act in state.Acts)
            {
                if (!_encountersByAct.TryGetValue(act.GetType(), out var set) || !set.Contains(e.EncounterType))
                    continue;
                var encounter = GetEncounter(e.EncounterType);
                if (encounter == null) continue;
                act.SetBossEncounter(encounter);
                Log.Info($"[MonsterRegistrar] Boss强制: {e.MonsterType.Name} → {act.GetType().Name}");
            }
        }
    }

    private static void OnCombatRoomExit()
    {
        NativeSfxPlayer.StopSequentialLoop();
        NativeSfxPlayer.StopMusic();
        NativeSfxPlayer.CleanupActivePlayers();
        SfxHooks.ResetDeathSfx();
    }

    private sealed class Entry
    {
        public Color? ColorModulation;
        public Type EncounterType;
        public Type ForceSpawnAct;
        public bool ForceSpawnBoss;
        public bool HasBackground;
        public Type MonsterType;
        public float? OffsetY;
        public float Scale = 0.52f;
    }
}