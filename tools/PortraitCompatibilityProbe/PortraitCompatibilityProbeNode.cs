using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Godot;
using ScriptManagerBridge = Godot.Bridge.ScriptManagerBridge;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using System.Text.Json;

public partial class PortraitCompatibilityProbeNode : Node
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static int _checks;
    private static readonly List<string> Cases = [];
    private string[] _args = [];
    private Type _portrait = null!;
    private MethodInfo _remember = null!;
    private MethodInfo _restore = null!;
    private readonly HashSet<Assembly> _initializedForeign = [];
    private string _foreignNamespace = string.Empty;
    private string _primaryReplacementJson = string.Empty;
    private Assembly? _secondForeign;
    private Assembly? _thirdForeign;

    public override async void _Ready()
    {
        try
        {
            _args = OS.GetCmdlineUserArgs();
            Require(_args.Length >= 4, "Expected native PCK, Things PCK, foreign DLL and implementation DLL.");
            AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(GetType().Assembly)!;
            context.Resolving += ResolveDependency;
            Require(ProjectSettings.LoadResourcePack(_args[0]), "Native pack mounts.");
            Require(ProjectSettings.LoadResourcePack(_args[1]), "Things pack mounts.");
            Require(ProjectSettings.LoadResourcePack(Path.ChangeExtension(_args[2], ".pck")), "Foreign portrait pack mounts.");
            _primaryReplacementJson = Godot.FileAccess.GetFileAsString("res://generated/card_replacements.json");
            bool baseline = _args.Contains("--baseline");
            bool early = _args.Contains("--early");
            Assembly? implementation = baseline && !_args.Contains("--cards") ? null
                : context.LoadFromAssemblyPath(Path.GetFullPath(_args[3]));
            if (!baseline && early) InitializeCompatibility(implementation!);
            Assembly foreign = context.LoadFromAssemblyPath(Path.GetFullPath(_args[2]));
            GD.Print($"PORTRAIT_FOREIGN_MVID={foreign.ManifestModule.ModuleVersionId:N}");
            BindForeign(foreign);
            InitializeForeign(foreign);
            int secondArgument = Array.IndexOf(_args, "--second-foreign");
            if (secondArgument >= 0)
            {
                string secondPath = Path.GetFullPath(_args[secondArgument + 1]);
                Require(ProjectSettings.LoadResourcePack(Path.ChangeExtension(secondPath, ".pck")), "Second portrait pack mounts.");
                _secondForeign = context.LoadFromAssemblyPath(secondPath);
                InitializeForeign(_secondForeign);
                GD.Print($"PORTRAIT_SECOND_MVID={_secondForeign.ManifestModule.ModuleVersionId:N}");
            }
            int thirdArgument = Array.IndexOf(_args, "--third-foreign");
            if (thirdArgument >= 0)
            {
                string thirdPath = Path.GetFullPath(_args[thirdArgument + 1]);
                Require(ProjectSettings.LoadResourcePack(Path.ChangeExtension(thirdPath, ".pck")), "Third portrait pack mounts.");
                _thirdForeign = context.LoadFromAssemblyPath(thirdPath);
                InitializeForeign(_thirdForeign);
            }
            if (!baseline && !early) InitializeCompatibility(implementation!);
            if (baseline && _args.Contains("--cards"))
            {
                await NativeCards(implementation!, foreign);
                throw new Exception("Baseline unexpectedly survived native pooled-card reuse after resource unloading.");
            }
            ExpiredDefault("DefaultPortraitStates", "STS2_Things.Cards.LeechParasite");
            if (baseline) throw new Exception("Baseline unexpectedly survived the reported expired-texture path.");
            ExpiredDefault("DefaultAncientPortraitStates", "STS2_Things.Cards.LeechParasite");
            ValidDefault();
            DeadRect();
            NullDefault();
            InitializeCompatibility(implementation!);
            ValidDefault();
            if (_secondForeign != null)
            {
                foreach (Assembly extra in new[] { _secondForeign, _thirdForeign }.OfType<Assembly>())
                {
                    BindForeign(extra);
                    ExpiredDefault("DefaultPortraitStates", "STS2_Things.Cards.LeechParasite");
                    ExpiredDefault("DefaultAncientPortraitStates", "STS2_Things.Cards.LeechParasite");
                    ValidDefault();
                }
                BindForeign(foreign);
                // Return to the first private state type after exercising the
                // second: a single shared PropertyInfo breaks this restoration.
                ValidDefault();
                Cases.Add("independent-mod-caches");
            }
            if (_args.Contains("--cards")) await NativeCards(implementation!, foreign);
            GD.Print($"PORTRAIT_COMPAT_PASS checks={_checks} mod={_foreignNamespace} combined={_secondForeign != null} order={(early ? "Things-first" : "Foreign-first")} cases={string.Join(',', Cases)}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            Exception cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            GD.Print($"PORTRAIT_COMPAT_FAIL type={cause.GetType().FullName} stack={cause.StackTrace}");
            GetTree().Quit(1);
        }
    }

    private static void InitializeCompatibility(Assembly assembly)
    {
        (assembly.GetType("STS2_Things.Compatibility.ModCompatibility")
            ?? assembly.GetType("STS2_Things.Compatibility.PortraitCacheCompatibility")
            ?? assembly.GetType("STS2_Things.Compatibility.BetterCallSpireCompatibility", true)!)
            .GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
    }

    private void ExpiredDefault(string field, string id)
    {
        IDictionary cache = (IDictionary)_portrait.GetField(field, PrivateStatic)!.GetValue(null)!;
        cache.Clear();
        using Image image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
        using ImageTexture original = ImageTexture.CreateFromImage(image);
        using ImageTexture current = ImageTexture.CreateFromImage(image);
        bool hasFilter = _portrait.GetNestedType("PortraitState", BindingFlags.NonPublic)?.GetProperty("TextureFilter") != null;
        var rect = new TextureRect { Texture = original, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps };
        _remember.Invoke(null, [id, rect, cache]);
        Require(cache.Contains(id), "Real portrait patch recorded the native texture.");
        rect.Texture = current;
        rect.StretchMode = TextureRect.StretchModeEnum.Scale;
        if (hasFilter) rect.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        original.Dispose();
        Require(!GodotObject.IsInstanceValid(original) && GodotObject.IsInstanceValid(rect), "Expired texture with a living portrait control.");
        try
        {
            _restore.Invoke(null, [id, rect, cache]);
            Require(rect.Texture == current && !cache.Contains(id), "Expired cached portrait leaves the current portrait intact and is evicted.");
            Require(rect.StretchMode == TextureRect.StretchModeEnum.KeepAspectCentered, "Expired texture still restores the original fitting style.");
            if (hasFilter) Require(rect.TextureFilter == CanvasItem.TextureFilterEnum.LinearWithMipmaps, "Expired Regent texture still restores the original filter.");
            _remember.Invoke(null, [id, rect, cache]);
            rect.Texture = null;
            _restore.Invoke(null, [id, rect, cache]);
            Require(rect.Texture == current, "Next refresh captures and restores a valid default.");
        }
        finally { rect.Free(); cache.Clear(); }
        Cases.Add(field);
    }

    private void ValidDefault()
    {
        IDictionary cache = (IDictionary)_portrait.GetField("DefaultPortraitStates", PrivateStatic)!.GetValue(null)!;
        cache.Clear();
        using Image image = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
        using ImageTexture original = ImageTexture.CreateFromImage(image);
        using ImageTexture changed = ImageTexture.CreateFromImage(image);
        bool hasFilter = _portrait.GetNestedType("PortraitState", BindingFlags.NonPublic)?.GetProperty("TextureFilter") != null;
        var rect = new TextureRect { Texture = original, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps };
        _remember.Invoke(null, ["valid-default", rect, cache]);
        rect.Texture = changed;
        rect.StretchMode = TextureRect.StretchModeEnum.Scale;
        if (hasFilter) rect.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        _restore.Invoke(null, ["valid-default", rect, cache]);
        Require(rect.Texture == original && rect.StretchMode == TextureRect.StretchModeEnum.KeepAspectCentered,
            "Original default restoration and stretch mode are preserved.");
        if (hasFilter) Require(rect.TextureFilter == CanvasItem.TextureFilterEnum.LinearWithMipmaps, "Regent's original texture filter is restored.");
        rect.Free();
        cache.Clear();
        Cases.Add("valid-default");
    }

    private void DeadRect()
    {
        IDictionary cache = (IDictionary)_portrait.GetField("DefaultPortraitStates", PrivateStatic)!.GetValue(null)!;
        var rect = new TextureRect();
        rect.Free();
        _remember.Invoke(null, ["dead-rect", rect, cache]);
        _restore.Invoke(null, ["dead-rect", rect, cache]);
        Require(!cache.Contains("dead-rect"), "Freed portrait control is never read or written.");
        Cases.Add("dead-rect");
    }

    private void NullDefault()
    {
        IDictionary cache = (IDictionary)_portrait.GetField("DefaultAncientPortraitStates", PrivateStatic)!.GetValue(null)!;
        var rect = new TextureRect();
        _remember.Invoke(null, ["empty-portrait", rect, cache]);
        Require(cache.Contains("empty-portrait"), "Null is a valid original portrait, not a disposed texture.");
        _restore.Invoke(null, ["empty-portrait", rect, cache]);
        Require(rect.Texture == null && cache.Contains("empty-portrait"), "Empty hidden portrait restoration is preserved.");
        rect.Free();
        cache.Clear();
        Cases.Add("null-default");
    }

    private async Task NativeCards(Assembly implementation, Assembly foreign)
    {
        TestMode.TurnOnInternal();
        SaveManager.Instance.InitSettingsDataForTest();
        SaveManager.Instance.InitPrefsDataForTest();
        SaveManager.Instance.SettingsSave.Language = "eng";
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly);
        ScriptManagerBridge.LookupScriptsInAssembly(implementation);
        Type[] modTypes = implementation.GetTypes();
        typeof(ReflectionHelper).GetField("_modTypes", PrivateStatic)!.SetValue(null, modTypes);
        RegisterProbeMod(implementation);
        PropertyInfo? managerState = typeof(ModManager).GetProperty("State", BindingFlags.Public | BindingFlags.Static);
        managerState?.SetValue(null, Enum.Parse(managerState.PropertyType, "Initialized"));
        typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Modding.AssemblyInfo")?.GetMethod("Init")?.Invoke(null, null);
        Type[] models = AbstractModelSubtypes.All.Concat(modTypes.Where(t => !t.IsAbstract && typeof(AbstractModel).IsAssignableFrom(t))).ToArray();
        MethodInfo init = typeof(ModelDb).GetMethods().Single(m => m.Name == "Init");
        init.Invoke(null, init.GetParameters().Length == 0 ? null : [models]);
        typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache", true)!
            .GetMethod("Init")!.Invoke(null, null);
        typeof(ModelDb).GetMethod("InitIds")!.Invoke(null, null);
        LocManager.Initialize();
        InitializeForeign(foreign);

        var pool = NodePool.Init<NCard>("res://scenes/cards/card.tscn", 0);
        CardModel Card(Type type) => ((CardModel)typeof(ModelDb).GetMethod("Card")!
            .MakeGenericMethod(type).Invoke(null, null)!).ToMutable();
        Type parasite = implementation.GetType("STS2_Things.Cards.LeechParasite", true)!;
        var caches = new[] { "DefaultPortraitStates", "DefaultAncientPortraitStates" }
            .Select(f => (IDictionary)_portrait.GetField(f, PrivateStatic)!.GetValue(null)!).ToArray();
        foreach (IDictionary cache in caches) cache.Clear();
        NCard first = pool.Get();
        first.Model = Card(parasite);
        AddChild(first);
        first.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        TextureRect Portrait(NCard node) => (TextureRect)typeof(NCard).GetField("_portrait", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(node)!;
        Require(Portrait(first).Texture?.ResourcePath == first.Model!.PortraitPath, "Generated parasite uses its actual native card scene and image.");
        Require(caches[0].Contains(parasite.FullName!), "Real UpdateVisuals postfix recorded the parasite default.");
        // Exercise the game's disposal seam, rather than disposing an arbitrary
        // test wrapper: the cache unloads this resource at a safe room boundary.
        Texture2D previous = PreloadManager.Cache.GetAsset<Texture2D>(first.Model.PortraitPath);
        RemoveChild(first);
        pool.Free(first);
        PreloadManager.Cache.UnloadMissedCacheAssets();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(!GodotObject.IsInstanceValid(previous), "Native AssetCache invalidated the cached portrait wrapper.");
        NCard reused = pool.Get();
        Require(ReferenceEquals(first, reused), "Generated status reuses an actual NCard from the native pool.");
        reused.Model = Card(parasite);
        AddChild(reused);
        reused.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        reused.UpdateVisuals(PileType.Hand, CardPreviewMode.Upgrade);
        Require(GodotObject.IsInstanceValid(Portrait(reused).Texture) && Portrait(reused).Texture!.ResourcePath == reused.Model!.PortraitPath,
            "EnterTree and UpdateVisuals survive expired cache without losing the parasite artwork.");
        RemoveChild(reused);
        pool.Free(reused);
        Cases.Add("native-pooled-parasite");

        // CaveMaw offers retained native Debris; exercise that actual card type
        // as well as the custom parasite. Disposing the prior managed wrapper
        // recreates the same cache lifetime boundary for an atlas portrait.
        NCard debris = pool.Get();
        debris.Model = Card(typeof(MegaCrit.Sts2.Core.Models.Cards.Debris));
        debris.Model.AddKeyword(CardKeyword.Retain);
        AddChild(debris);
        debris.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        Texture2D oldDebris = Portrait(debris).Texture!;
        Require(caches[0].Contains(debris.Model.GetType().FullName!), "Native Debris is recorded by the real portrait patch.");
        RemoveChild(debris);
        pool.Free(debris);
        oldDebris.Dispose();
        NCard freshDebris = pool.Get();
        freshDebris.Model = Card(typeof(MegaCrit.Sts2.Core.Models.Cards.Debris));
        freshDebris.Model.AddKeyword(CardKeyword.Retain);
        AddChild(freshDebris);
        freshDebris.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        Require(GodotObject.IsInstanceValid(Portrait(freshDebris).Texture) && freshDebris.Model.Keywords.Contains(CardKeyword.Retain),
            "Retained native Debris survives expired-portrait refresh.");
        RemoveChild(freshDebris);
        pool.Free(freshDebris);
        Cases.Add("native-retained-debris");

        if (_secondForeign != null)
        {
            foreach (NCard card in pool.DebugFreeObjects) card.Free();
            foreach (IDictionary cache in caches) cache.Clear();
            return; // Normal artwork/filter assertions are run with each mod alone.
        }

        using JsonDocument replacements = JsonDocument.Parse(_primaryReplacementJson);
        Type? replacedType = replacements.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => typeof(ModelDb).Assembly.GetType(e.GetProperty("cardId").GetString()!)).FirstOrDefault(t => t != null);
        Require(replacedType != null, "A real original card has a foreign portrait replacement.");
        Type registry = foreign.GetType(_foreignNamespace + ".CardReplacementRegistry", true)!;
        object?[] lookup = [replacedType!.FullName, null];
        Require((bool)registry.GetMethod("TryGetTexture")!.Invoke(null, lookup)!, "Actual replacement image loads from its workshop PCK.");
        var replacement = (Texture2D)lookup[1]!;
        NCard replaced = pool.Get();
        replaced.Model = Card(replacedType);
        AddChild(replaced);
        replaced.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        Require(Portrait(replaced).Texture == replacement && Portrait(replaced).StretchMode == TextureRect.StretchModeEnum.KeepAspectCovered,
            "Original portrait replacement and fit mode remain active.");
        if (_foreignNamespace == "regentCardsAnimeRework")
            Require(Portrait(replaced).TextureFilter == (CanvasItem.TextureFilterEnum)4, "Regent's replacement texture filter is preserved.");
        RemoveChild(replaced);
        pool.Free(replaced);
        // Reusing a replaced card node for a parasite must restore the parasite,
        // rather than letting the previous card's replacement bleed into it.
        NCard next = pool.Get();
        next.Model = Card(parasite);
        AddChild(next);
        next.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        Require(Portrait(next).Texture!.ResourcePath == next.Model!.PortraitPath, "Replacement-to-status reuse restores the correct card portrait.");
        RemoveChild(next);
        pool.Free(next);
        foreach (NCard card in pool.DebugFreeObjects) card.Free();
        foreach (IDictionary cache in caches) cache.Clear();
        Cases.Add("native-replacement-preserved");
        if (_foreignNamespace == "LuolinCard") VerifyLuolinFeatures(foreign);
    }

    private void InitializeForeign(Assembly foreign)
    {
        if (!_initializedForeign.Add(foreign)) return;
        foreign.GetType(foreign.GetName().Name + ".MainFile", true)!.GetMethod("Initialize")!.Invoke(null, null);
    }

    private void BindForeign(Assembly foreign)
    {
        _foreignNamespace = foreign.GetName().Name!;
        _portrait = foreign.GetType(_foreignNamespace + ".PortraitPatch", true)!;
        _remember = _portrait.GetMethod("RememberDefaultState", PrivateStatic)!;
        _restore = _portrait.GetMethod("RestoreDefaultState", PrivateStatic)!;
    }

    private static void RegisterProbeMod(Assembly implementation)
    {
        Assembly game = typeof(ModManager).Assembly;
        Type modType = game.GetType("MegaCrit.Sts2.Core.Modding.Mod", true)!;
        Type manifestType = game.GetType("MegaCrit.Sts2.Core.Modding.ModManifest", true)!;
        object mod = Activator.CreateInstance(modType)!;
        object manifest = Activator.CreateInstance(manifestType)!;
        manifestType.GetField("id")!.SetValue(manifest, "STS2_Things");
        modType.GetField("manifest")!.SetValue(mod, manifest);
        modType.GetField("path")!.SetValue(mod, "probe://STS2_Things");
        FieldInfo state = modType.GetField("state")!;
        state.SetValue(mod, Enum.Parse(state.FieldType, "Loaded"));
        if (modType.GetField("assemblies")?.GetValue(mod) is IList assemblies) assemblies.Add(implementation);
        else modType.GetField("assembly")!.SetValue(mod, implementation);
        IList mods = (IList)typeof(ModManager).GetField("_mods", PrivateStatic)!.GetValue(null)!;
        mods.Clear();
        mods.Add(mod);
    }

    private static Assembly? ResolveDependency(AssemblyLoadContext context, AssemblyName name)
    {
        string file = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
        return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
}
