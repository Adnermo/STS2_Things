using Godot;
using System.Reflection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using STS2_Things.Events;

public partial class DepthsProbeNode
{
    private async Task RenderEventLayouts()
    {
        var output = System.Environment.GetEnvironmentVariable("THINGS_PROBE_OUTPUT")!;
        var view = new SubViewport
        {
            Size = new Vector2I(1920, 1080),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(view);
        GetTree().Root.Size = view.Size;
        var prevFastMode = SaveManager.Instance.PrefsSave.FastMode;
        SaveManager.Instance.PrefsSave.FastMode = MegaCrit.Sts2.Core.Settings.FastModeType.Fast;

        (string Key, Func<Player, RunState, Task<EventModel>> Begin)[] events =
        [
            ("reality_aligned_houses", (p, _) => Begin<ThingsRealityAlignedHouses>(p).ContinueWith<EventModel>(t => t.Result)),
            ("shadow_cloakroom", (p, _) => Begin<ThingsShadowCloakroom>(p).ContinueWith<EventModel>(t => t.Result)),
            ("echoing_well", (p, _) => Begin<ThingsEchoingWell>(p).ContinueWith<EventModel>(t => t.Result)),
            ("polite_maw", (p, _) => Begin<ThingsPoliteMaw>(p).ContinueWith<EventModel>(t => t.Result)),
            ("mycelial_bank", (p, _) => Begin<ThingsMycelialBank>(p).ContinueWith<EventModel>(t => t.Result)),
            ("unlit_fire", (p, _) => Begin<ThingsUnlitFire>(p).ContinueWith<EventModel>(t => t.Result)),
            ("relic_workshop", (p, _) => Begin<ThingsRelicWorkshop>(p).ContinueWith<EventModel>(t => t.Result)),
            ("potion_tasting", (p, _) => Begin<ThingsPotionTasting>(p).ContinueWith<EventModel>(t => t.Result)),
            ("narrow_gate", (p, _) => Begin<ThingsNarrowGate>(p).ContinueWith<EventModel>(t => t.Result)),
            ("biting_chest", (p, _) => Begin<ThingsBitingChest>(p).ContinueWith<EventModel>(t => t.Result)),
            ("crowded_ward", (p, _) => Begin<ThingsCrowdedWard>(p).ContinueWith<EventModel>(t => t.Result)),
        ];

        var boundsReport = new System.Text.StringBuilder();
        foreach (string lang in new[] { "zhs", "eng" })
        {
            foreach (var entry in events)
            {
                SaveManager.Instance.SettingsSave.Language = lang;
                LocManager.Initialize();

                RunState run = NewEventTestRun("render-" + entry.Key, gold: 300, withRestSite: true);
                Player p = run.Players[0];
                await CreatureCmd.SetCurrentHp(p.Creature, 45);
                await CardPileCmd.Add(run.CreateCard<Inflame>(p), MegaCrit.Sts2.Core.Entities.Cards.PileType.Deck);
                await CardPileCmd.AddCurseToDeck<Injury>(p);
                await PotionCmd.TryToProcure<PotionShapedRock>(p);
                await RelicCmd.Obtain(ModelDb.Relic<Vajra>().ToMutable(), p);

                EventModel model = await entry.Begin(p, run);
                var world = new Control { Size = view.Size };
                view.AddChild(world);
                world.AddChild(new ColorRect { Size = view.Size, Color = Colors.Black });
                var layout = GD.Load<PackedScene>(NEventLayout.defaultScenePath).Instantiate<NEventLayout>();
                model.SetNode(layout);
                world.AddChild(layout);
                layout.SetEvent(model);
                layout.SetTitle(model.Title.GetFormattedText());
                var desc = model.Description!;
                desc.Add("character", p.Character.Title);
                desc.Add("IsMultiplayer", run.Players.Count > 1);
                model.DynamicVars.AddTo(desc);
                layout.SetDescription(desc.GetFormattedText());
                layout.AddOptions(model.CurrentOptions);
                layout.OnSetupComplete();

                await ToSignal(GetTree().CreateTimer(1.05), SceneTreeTimer.SignalName.Timeout);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

                var opts = layout.OptionButtons.ToList();
                Assert(opts.Count >= 3, $"Layout {entry.Key} ({lang}) rendered >=3 options");
                var descNode = layout.GetNodeOrNull<Control>("%EventDescription");
                var lastBtn = opts[^1];
                float bottomY = lastBtn.GlobalPosition.Y + lastBtn.Size.Y;
                string boundLine = $"key={entry.Key} lang={lang} opts={opts.Count} descH={(descNode?.Size.Y ?? -1f):F1} lastBottomY={bottomY:F1}";
                boundsReport.AppendLine(boundLine);
                using (var image = view.GetTexture().GetImage())
                    Assert(image.SavePng(Path.Combine(output, $"event-{entry.Key}-{lang}.png")) == Error.Ok,
                        $"Native event layout capture ({entry.Key}-{lang})");
                Assert(bottomY <= 1048f, $"Layout {entry.Key} ({lang}) bottomY={bottomY:F1} overflows 1080p safe bottom");
                world.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }

        File.WriteAllText(Path.Combine(output, "render_bounds.txt"), boundsReport.ToString());
        SaveManager.Instance.PrefsSave.FastMode = prevFastMode;
        view.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
