using System.Reflection;
using System.Runtime.Loader;
using Godot;
using ScriptManagerBridge = Godot.Bridge.ScriptManagerBridge;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

public partial class LatestReportProbeNode : Node
{
    private static int _checks;
    public override void _Ready()
    {
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(GetType().Assembly)!;
            context.Resolving += (c, name) => {
                string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
                return File.Exists(path) ? c.LoadFromAssemblyPath(path) : null;
            };
            TestMode.TurnOnInternal();
            Require(ProjectSettings.LoadResourcePack(args[0]), "Native pack mounts.");
            Require(ProjectSettings.LoadResourcePack(args[1]), "Things pack mounts.");
            Assembly implementation = context.LoadFromAssemblyPath(Path.GetFullPath(args[2]));
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly);
            ScriptManagerBridge.LookupScriptsInAssembly(implementation);
            implementation.GetType("STS2_Things.Compatibility.ModCompatibility", true)!.GetMethod("Initialize")!.Invoke(null, null);
            if (!args.Contains("--music-only"))
            {
                foreach (string encounter in new[] { "origin_fogmog", "bowlbug_progenitor", "the_legacy", "gravetide_slug" })
                {
                    string path = $"res://scenes/backgrounds/{encounter}_boss_encounter/{encounter}_boss_encounter_background.tscn";
                    Control node = GD.Load<PackedScene>(path).Instantiate<Control>();
                    try
                    {
                        node.GetType().GetMethod("_Ready")!.Invoke(node, null);
                        Require(node.GetNode<Control>("Layer_00").ZIndex == -2, "Existing background layer is ordered correctly.");
                        if (node.GetNodeOrNull<Control>("Foreground") is { } foreground)
                            Require(foreground.ZIndex == 3, "Existing foreground is ordered correctly.");
                        if (node.GetNodeOrNull<Control>("Layer_01") is { } middle)
                            Require(middle.ZIndex == 1, "Optional middle layer keeps its native depth.");
                    }
                    finally { node.Free(); }
                }
                GD.Print("LATEST_BACKGROUND_PASS");
            }
            if (!args.Contains("--background-only"))
            {
                Assembly momo = context.LoadFromAssemblyPath(Path.GetFullPath(args[3]));
                MethodInfo prefix = momo.GetType("MomoLib.Patch.MomoCustomBgmPatch", true)!
                    .GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
                string theme = "res://music/gravetide_slug/gravetide_slug_boss_theme.wav";
                var oldMusic = new AudioStreamPlayer();
                AddChild(oldMusic);
                FieldInfo bgm = prefix.DeclaringType!.GetField("_bgmPlayer", BindingFlags.NonPublic | BindingFlags.Static)!;
                bgm.SetValue(null, oldMusic);
                Require((bool)prefix.Invoke(null, [null, theme, null])!, "Momo delegates only the Things WAV to the existing Things/native pipeline.");
                Require(bgm.GetValue(null) == null && oldMusic.IsQueuedForDeletion(), "Previous Momo music is stopped and cleared before handing over.");
                RemoveChild(oldMusic);
                oldMusic.Free();
                Require((bool)prefix.Invoke(null, [null, "event:/music/act1", null])!, "Native event music behavior is unchanged.");
                Require((bool)prefix.Invoke(null, [null, theme.ToUpperInvariant(), null])!, "Non-matching paths keep Momo's own behavior.");
                GD.Print("LATEST_MOMO_PASS");
            }
            GD.Print($"Latest report probe: PASS checks={_checks}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            Exception actual = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            GD.Print($"LATEST_REPORT_FAIL type={actual.GetType().FullName} stack={actual.StackTrace}");
            GetTree().Quit(1);
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
}
