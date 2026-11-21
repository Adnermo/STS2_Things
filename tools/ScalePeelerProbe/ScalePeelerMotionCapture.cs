using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Platform;
using STS2_Things.Visuals;

public partial class ScalePeelerProbeNode
{
    private sealed record MotionSegment(
        string Clip,
        float Duration,
        string LabelZh,
        string LabelEn,
        string SubtitleEn,
        float HoldTimeScale = 1f);

    private sealed record FrameRecord(
        int Index,
        string File,
        string Clip,
        string LabelZh,
        string LabelEn,
        string SubtitleEn,
        float ClipTime,
        int WyrmBlock,
        int PlayerBlock,
        bool ContactPeak);

    private async Task RenderMotionClips(
        SubViewport view,
        Fixture scenario,
        NCreature ironcladNode,
        NCreature peelerNode,
        string output)
    {
        const int fps = 24;
        const float dt = 1f / fps;
        string motionRoot = Path.Combine(output, "motion");
        string framesDir = Path.Combine(motionRoot, "frames");
        string peaksDir = Path.Combine(motionRoot, "peaks");
        Directory.CreateDirectory(framesDir);
        Directory.CreateDirectory(peaksDir);

        var visuals = (NScalePeelerVisuals)peelerNode.Visuals;
        var wyrmSprite = visuals.GetNode<Node2D>("Visuals");
        var ironcladSprite = ironcladNode.Visuals.SpineBody?.BoundObject as Node2D;
        var wyrmSpine = visuals.SpineBody!;

        wyrmSprite.Call("set_update_mode", ClassDB.ClassGetIntegerConstant("SpineConstant", "UpdateMode_Manual"));
        ironcladSprite?.Call("set_update_mode", ClassDB.ClassGetIntegerConstant("SpineConstant", "UpdateMode_Manual"));

        var applyPlatesMethod = typeof(NScalePeelerVisuals).GetMethod(
            "ApplyPlateColors", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var lastPlateField = typeof(NScalePeelerVisuals).GetField(
            "_lastPlateCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var plateAlphaField = typeof(NScalePeelerVisuals).GetField(
            "_plateAlpha", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var vfxElapsedField = typeof(NArmorSiphonVfx).GetField(
            "_elapsed", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var fadeTweenField = typeof(NCreature).GetField(
            "_intentFadeTween", BindingFlags.NonPublic | BindingFlags.Instance)!;

        void LockIntentOpaque(bool visible)
        {
            if (fadeTweenField.GetValue(peelerNode) is Tween fadeTween && fadeTween.IsValid())
                fadeTween.Kill();
            peelerNode.IntentContainer.Visible = visible;
            peelerNode.IntentContainer.Modulate = Colors.White;
        }

        // Disable automatic per-process fade/VFX ticking so 24fps stepping is 100% deterministic.
        visuals.SetProcess(false);

        static void PoseManual(Node2D? sprite, string clip, float time, bool loop = false)
        {
            if (sprite == null)
                return;
            sprite.Call("get_skeleton").AsGodotObject().Call("set_to_setup_pose");
            var state = sprite.Call("get_animation_state").AsGodotObject();
            state.Call("clear_tracks");
            var track = state.Call("set_animation", clip, loop, 0).AsGodotObject();
            track.Call("set_mix_duration", 0f);
            track.Call("set_track_time", time);
            sprite.Call("update_skeleton", 0f);
        }

        void SyncPlates(float stepDt, string clip, bool snap = false)
        {
            var creature = scenario.Peeler.Creature;
            int[] thresholds = [1, 6, 12, 20];
            int block = creature.IsDead ? 0 : creature.Block;
            int targetCount = block <= 0 ? 0 : thresholds.Count(t => block >= t);
            if (clip == "die")
                targetCount = 0;

            int lastCount = (int)lastPlateField.GetValue(visuals)!;
            if (!snap && lastCount >= 0 && targetCount < lastCount && creature.IsAlive)
            {
                NArmorSiphonVfx.SpawnShatterBurst(creature, (lastCount - targetCount) * 3);
            }
            lastPlateField.SetValue(visuals, targetCount);

            float[] alphas = (float[])plateAlphaField.GetValue(visuals)!;
            float rate = stepDt * 9.5f;
            for (int i = 0; i < 4; i++)
            {
                float target = i < targetCount ? 1f : 0f;
                alphas[i] = snap ? target : Mathf.MoveToward(alphas[i], target, rate);
            }

            if (clip != "die" && clip != "revive")
            {
                applyPlatesMethod.Invoke(visuals, null);
                wyrmSprite.Call("update_skeleton", 0f);
            }
            else if (targetCount == 0)
            {
                // Keep shed plates hidden if dying/reviving unarmored.
                applyPlatesMethod.Invoke(visuals, null);
                wyrmSprite.Call("update_skeleton", 0f);
            }
        }

        void StepVfx(float stepDt)
        {
            foreach (var vfx in view.GetChildren().OfType<NArmorSiphonVfx>().ToArray())
            {
                vfx.SetProcess(false);
                float next = (float)vfxElapsedField.GetValue(vfx)! + stepDt;
                float limit = (bool)typeof(NArmorSiphonVfx)
                    .GetField("_isBurst", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(vfx)!
                    ? 0.52f
                    : 0.68f;
                if (next >= limit)
                {
                    view.RemoveChild(vfx);
                    vfx.QueueFree();
                }
                else
                {
                    vfxElapsedField.SetValue(vfx, next);
                    vfx.QueueRedraw();
                }
            }
        }

        async Task ResetBlock(Creature target, int desired)
        {
            if (target.Block > desired)
                await Sts2VersionSeam.LoseBlock(Choice, target, target.Block - desired, target);
            else if (target.Block < desired)
                await CreatureCmd.GainBlock(target, desired - target.Block, ValueProp.Unpowered, null);
        }

        // Clear any leftover VFX from earlier static checks.
        foreach (var vfx in view.GetChildren().OfType<NArmorSiphonVfx>().ToArray())
        {
            view.RemoveChild(vfx);
            vfx.QueueFree();
        }

        await ResetBlock(scenario.Peeler.Creature, 6);
        await ResetBlock(scenario.Player.Creature, 24);
        peelerNode.TrackBlockStatus(scenario.Peeler.Creature);
        ironcladNode.TrackBlockStatus(scenario.Player.Creature);

        var moveSm = scenario.Peeler.MoveStateMachine!;
        async Task SelectIntent(string moveId)
        {
            for (int guard = 0; guard < 16 && scenario.Peeler.NextMove.Id != moveId; guard++)
            {
                moveSm.OnMovePerformed(scenario.Peeler.NextMove);
                scenario.Peeler.RollMove([scenario.Player.Creature]);
            }
            await peelerNode.UpdateIntent([scenario.Player.Creature]);
            LockIntentOpaque(true);
        }

        await SelectIntent("CHISEL_BITE_MOVE");

        SyncPlates(dt, "summon", snap: true);

        var ironcladSpine = ironcladNode.Visuals.SpineBody;
        string? ResolveHeroClip(params string[] candidates)
            => candidates.FirstOrDefault(name => ironcladSpine?.HasAnimation(name) == true);

        string? heroIdle = ResolveHeroClip("idle_loop", "Idle");
        string? heroAttack = ResolveHeroClip("attack", "Attack") ?? heroIdle;
        string? heroHurt = ResolveHeroClip("hit", "hurt", "Hurt") ?? heroIdle;

        MotionSegment[] segments =
        [
            new("summon", 0.95f, "破土登场", "Summon", "6 Block · 2 Dorsal Plates"),
            new("idle_loop", 2.00f, "洞窟待机", "Idle Loop", "Rhythmic Coils & Flickering Feelers"),
            new("cast", 1.40f, "噬甲吸取", "Armor Siphon (Cast)", "Turn-End Block Drain 24 -> 4 Dorsal Plates"),
            new("slam", 1.28f, "震甲重砸", "Plated Slam", "Armor-Scaled Strike + Plate Shatter Recoil"),
            new("flurry", 1.32f, "掘石蓄甲", "Burrow Scrape (Flurry)", "Double Strike -> Regrow Dorsal Plates"),
            new("attack", 1.06f, "凿壳啃咬", "Chisel Bite (Attack)", "Twin Chisel Fang Lunge"),
            new("power_up", 0.95f, "晶体蓄能", "Power Up", "Throat Crystal Surge"),
            new("hurt", 0.58f, "受击硬直", "Hurt", "Carapace Recoil"),
            new("die", 1.45f, "塌陷倒地", "Die", "Plates Shed & Sink Into Rubble"),
            new("revive", 1.10f, "破土复苏", "Revive", "Emerge From Burrow"),
            new("idle_loop", 0.60f, "回位待机", "Idle Settle", "Seamless Return To Idle"),
        ];

        Dictionary<string, float> peakTargets = new()
        {
            ["idle_loop"] = 1.00f,
            ["attack"] = 0.46f,
            ["slam"] = 0.58f,
            ["cast"] = 0.78f,
            ["flurry"] = 0.72f,
            ["hurt"] = 0.25f,
            ["power_up"] = 0.48f,
            ["summon"] = 0.45f,
            ["die"] = 1.08f,
            ["revive"] = 0.58f,
        };

        List<FrameRecord> records = [];
        HashSet<string> savedPeaks = [];
        int globalIndex = 0;
        float heroClock = 0f;

        foreach (var seg in segments)
        {
            int frameCount = Math.Max(1, (int)Math.Ceiling(seg.Duration * fps));
            bool siphonSpawned = false;
            bool siphonSettled = false;
            bool slamRecoiled = false;
            bool flurryBuffed = false;
            bool powerUpped = false;
            bool dieCleared = false;
            bool reviveRestored = false;

            if (seg.Clip == "idle_loop" && seg.LabelZh == "回位待机")
            {
                await SelectIntent("CHISEL_BITE_MOVE");
            }
            else if (seg.Clip == "cast")
            {
                await ResetBlock(scenario.Player.Creature, 24);
                await ResetBlock(scenario.Peeler.Creature, 6);
                ironcladNode.TrackBlockStatus(scenario.Player.Creature);
                peelerNode.TrackBlockStatus(scenario.Peeler.Creature);
                await SelectIntent("PLATED_SLAM_MOVE");
            }
            else if (seg.Clip == "slam")
            {
                await SelectIntent("PLATED_SLAM_MOVE");
            }
            else if (seg.Clip == "flurry")
            {
                await SelectIntent("BURROW_SCRAPE_MOVE");
            }
            else if (seg.Clip == "attack")
            {
                await SelectIntent("CHISEL_BITE_MOVE");
            }
            else if (seg.Clip == "die" || seg.Clip == "revive")
            {
                peelerNode.IntentContainer.Visible = false;
            }

            for (int f = 0; f < frameCount; f++)
            {
                float t = Math.Min(f * dt, seg.Duration);
                heroClock += dt;

                // Synchronized combat state transitions & VFX events per clip.
                if (seg.Clip == "cast")
                {
                    if (!siphonSpawned && t >= 0.28f)
                    {
                        siphonSpawned = true;
                        NArmorSiphonVfx.PlaySiphonStream(scenario.Peeler.Creature, [scenario.Player.Creature]);
                        foreach (var vfx in view.GetChildren().OfType<NArmorSiphonVfx>())
                            vfx.SetProcess(false);
                    }
                    if (t >= 0.36f && t <= 0.88f)
                    {
                        float p = Mathf.Clamp((t - 0.36f) / 0.52f, 0f, 1f);
                        int targetPlayer = Math.Max(0, Mathf.RoundToInt(24f * (1f - p)));
                        int targetWyrm = Math.Min(24, 6 + Mathf.RoundToInt(18f * p));
                        await ResetBlock(scenario.Player.Creature, targetPlayer);
                        await ResetBlock(scenario.Peeler.Creature, targetWyrm);
                        ironcladNode.TrackBlockStatus(scenario.Player.Creature);
                        peelerNode.TrackBlockStatus(scenario.Peeler.Creature);
                        await peelerNode.UpdateIntent([scenario.Player.Creature]);
                        peelerNode.IntentContainer.Modulate = Colors.White;
                    }
                    else if (!siphonSettled && t > 0.88f)
                    {
                        siphonSettled = true;
                        await ResetBlock(scenario.Player.Creature, 0);
                        await ResetBlock(scenario.Peeler.Creature, 24);
                        ironcladNode.TrackBlockStatus(scenario.Player.Creature);
                        peelerNode.TrackBlockStatus(scenario.Peeler.Creature);
                        await peelerNode.UpdateIntent([scenario.Player.Creature]);
                        peelerNode.IntentContainer.Modulate = Colors.White;
                    }
                }
                else if (seg.Clip == "slam" && !slamRecoiled && t >= 0.58f)
                {
                    slamRecoiled = true;
                    await Sts2VersionSeam.LoseBlock(Choice, scenario.Peeler.Creature, 12, scenario.Peeler.Creature);
                    peelerNode.TrackBlockStatus(scenario.Peeler.Creature);
                    await peelerNode.UpdateIntent([scenario.Player.Creature]);
                    peelerNode.IntentContainer.Modulate = Colors.White;
                }
                else if (seg.Clip == "flurry" && !flurryBuffed && t >= 0.72f)
                {
                    flurryBuffed = true;
                    await ResetBlock(scenario.Peeler.Creature, 20);
                    peelerNode.TrackBlockStatus(scenario.Peeler.Creature);
                }
                else if (seg.Clip == "power_up" && !powerUpped && t >= 0.38f)
                {
                    powerUpped = true;
                    await ResetBlock(scenario.Peeler.Creature, 24);
                    peelerNode.TrackBlockStatus(scenario.Peeler.Creature);
                }
                else if (seg.Clip == "die" && !dieCleared && t >= 0.45f)
                {
                    dieCleared = true;
                    await ResetBlock(scenario.Peeler.Creature, 0);
                    peelerNode.TrackBlockStatus(scenario.Peeler.Creature);
                }
                else if (seg.Clip == "revive" && !reviveRestored && t >= 0.40f)
                {
                    reviveRestored = true;
                    await ResetBlock(scenario.Peeler.Creature, 6);
                    peelerNode.TrackBlockStatus(scenario.Peeler.Creature);
                }

                // Pose Ironclad cleanly alongside Wyrm reactions.
                string? activeHeroClip = heroIdle;
                float activeHeroTime = heroClock % 3f;
                bool activeHeroLoop = true;
                if (seg.Clip == "hurt" && t <= 0.55f && heroAttack != null)
                {
                    activeHeroClip = heroAttack;
                    activeHeroTime = Mathf.Min(t * 1.15f, 0.7f);
                    activeHeroLoop = false;
                }
                else if (seg.Clip == "slam" && t >= 0.56f && t <= 1.05f && heroHurt != null)
                {
                    activeHeroClip = heroHurt;
                    activeHeroTime = t - 0.56f;
                    activeHeroLoop = false;
                }
                else if (seg.Clip == "attack" && t >= 0.44f && t <= 0.86f && heroHurt != null)
                {
                    activeHeroClip = heroHurt;
                    activeHeroTime = t - 0.44f;
                    activeHeroLoop = false;
                }
                if (activeHeroClip != null)
                    PoseManual(ironcladSprite, activeHeroClip, activeHeroTime, loop: activeHeroLoop);

                PoseManual(wyrmSprite, seg.Clip, t);
                SyncPlates(dt, seg.Clip, snap: seg.Clip == "summon" && f == 0);
                StepVfx(dt);
                LockIntentOpaque(seg.Clip != "die" && seg.Clip != "revive");

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                RenderingServer.ForceDraw();

                using var image = view.GetTexture().GetImage();
                bool isPeak = peakTargets.TryGetValue(seg.Clip, out float peakT) &&
                              !savedPeaks.Contains(seg.Clip) &&
                              (t >= peakT || f == frameCount - 1);
                if (isPeak)
                {
                    savedPeaks.Add(seg.Clip);
                    Assert(image.SavePng(Path.Combine(peaksDir, $"{seg.Clip}.png")) == Error.Ok,
                        $"Saved peak PNG for {seg.Clip}");
                }

                image.Resize(1280, 720, Image.Interpolation.Lanczos);
                string fileName = $"frame_{globalIndex:D4}.jpg";
                Assert(image.SaveJpg(Path.Combine(framesDir, fileName), 0.94f) == Error.Ok,
                    $"Saved motion frame {fileName}");

                records.Add(new FrameRecord(
                    globalIndex,
                    fileName,
                    seg.Clip,
                    seg.LabelZh,
                    seg.LabelEn,
                    seg.SubtitleEn,
                    MathF.Round(t, 3),
                    scenario.Peeler.Creature.Block,
                    scenario.Player.Creature.Block,
                    isPeak));
                globalIndex++;
            }
        }

        visuals.SetProcess(true);
        await ResetBlock(scenario.Player.Creature, 18);
        await ResetBlock(scenario.Peeler.Creature, 10);

        File.WriteAllText(
            Path.Combine(motionRoot, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                renderer = "Native Godot 4.5.1 + Spine 4.2 live combat viewport",
                fps,
                width = 1280,
                height = 720,
                total_frames = records.Count,
                duration_seconds = MathF.Round(records.Count / (float)fps, 2),
                clips = segments.Select(s => new { s.Clip, s.Duration, s.LabelZh, s.LabelEn, s.SubtitleEn }),
                frames = records,
            }, new JsonSerializerOptions { WriteIndented = true }));

        GD.Print($"Captured {records.Count} native 24fps motion frames and {savedPeaks.Count} peak sheets.");
    }
}
