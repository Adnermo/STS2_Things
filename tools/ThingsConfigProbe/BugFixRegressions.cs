using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things;
using STS2_Things.Acts;
using STS2_Things.Audio;
using STS2_Things.Cards;
using STS2_Things.Config;
using STS2_Things.Encounters;
using STS2_Things.Monsters;
using STS2_Things.Modifiers;
using STS2_Things.Powers;

internal static class BugFixRegressions
{
    private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;

    public static void Run(Node host)
    {
        VerifyGravetideLoopAndVolume();
        VerifyBowlbugSlotOrder();
        VerifyHiveDiscoveryAndSecondBossGuard();
        VerifyPortraitResolverMatcherAndAdapters();
        VerifyHoverTipNonOverlappingLayout(host);
        VerifyCaveGodForceKillContracts();
        VerifyDailySaveCrossroadExtraction();
        VerifySnailChipRunAssetRetention();
        VerifyBossStateDisplayEffectiveZAndPckGuard(host);
        GD.Print("BUGFIX_REGRESSIONS_PASS groups=9");
    }

    private static void VerifyGravetideLoopAndVolume()
    {
        var wav = ResourceLoader.Load<AudioStreamWav>("res://music/gravetide_slug/gravetide_slug_boss_theme.wav");
        Require(wav != null, "Gravetide boss theme failed to load from published PCK.");
        Require(wav!.LoopMode == AudioStreamWav.LoopModeEnum.Forward && wav.LoopEnd > 6_500_000 && wav.MixRate == 48_000 && wav.Stereo,
            $"Gravetide boss theme has invalid loop/format metadata: mode={wav.LoopMode}, end={wav.LoopEnd}, rate={wav.MixRate}, stereo={wav.Stereo}.");

        var towerWav = ResourceLoader.Load<AudioStreamWav>("res://music/human_face_tower/human_face_tower_boss_theme.wav");
        Require(towerWav != null, "Human Face Tower boss theme failed to load from published PCK.");
        Require(towerWav!.LoopMode == AudioStreamWav.LoopModeEnum.Forward && towerWav.LoopEnd > 5_500_000 && towerWav.MixRate == 48_000 && towerWav.Stereo,
            $"Human Face Tower boss theme has invalid loop/format metadata: mode={towerWav.LoopMode}, end={towerWav.LoopEnd}, rate={towerWav.MixRate}, stereo={towerWav.Stereo}.");

        var caveGodWav = ResourceLoader.Load<AudioStreamWav>("res://music/cave_god/cave_god_boss_theme.wav");
        Require(caveGodWav != null, "Cave God boss theme failed to load from published PCK.");
        Require(caveGodWav!.LoopMode == AudioStreamWav.LoopModeEnum.Forward && caveGodWav.LoopEnd > 5_800_000 && caveGodWav.MixRate == 48_000 && caveGodWav.Stereo,
            $"Cave God boss theme has invalid loop/format metadata: mode={caveGodWav.LoopMode}, end={caveGodWav.LoopEnd}, rate={caveGodWav.MixRate}, stereo={caveGodWav.Stereo}.");

        var bowlbugWav = ResourceLoader.Load<AudioStreamWav>("res://music/bowlbug_progenitor/bowlbug_progenitor_boss_theme.wav");
        Require(bowlbugWav != null, "Bowlbug Progenitor boss theme failed to load from published PCK.");
        Require(bowlbugWav!.LoopMode == AudioStreamWav.LoopModeEnum.Forward && bowlbugWav.LoopEnd > 6_000_000 && bowlbugWav.MixRate == 48_000 && bowlbugWav.Stereo,
            $"Bowlbug Progenitor boss theme has invalid loop/format metadata: mode={bowlbugWav.LoopMode}, end={bowlbugWav.LoopEnd}, rate={bowlbugWav.MixRate}, stereo={bowlbugWav.Stereo}.");

        var inkmawWav = ResourceLoader.Load<AudioStreamWav>("res://music/abyssal_inkmaw/abyssal_inkmaw_boss_theme.wav");
        Require(inkmawWav != null, "Abyssal Inkmaw boss theme failed to load from published PCK.");
        Require(inkmawWav!.LoopMode == AudioStreamWav.LoopModeEnum.Forward && inkmawWav.LoopEnd > 5_500_000 && inkmawWav.MixRate == 48_000 && inkmawWav.Stereo,
            $"Abyssal Inkmaw boss theme has invalid loop/format metadata: mode={inkmawWav.LoopMode}, end={inkmawWav.LoopEnd}, rate={inkmawWav.MixRate}, stereo={inkmawWav.Stereo}.");

        var fogmogWav = ResourceLoader.Load<AudioStreamWav>("res://music/origin_fogmog/origin_fogmog_boss_theme.wav");
        Require(fogmogWav != null, "Origin Fogmog boss theme failed to load from published PCK.");
        Require(fogmogWav!.LoopMode == AudioStreamWav.LoopModeEnum.Forward && fogmogWav.LoopEnd > 5_000_000 && fogmogWav.MixRate == 48_000 && fogmogWav.Stereo,
            $"Origin Fogmog boss theme has invalid loop/format metadata: mode={fogmogWav.LoopMode}, end={fogmogWav.LoopEnd}, rate={fogmogWav.MixRate}, stereo={fogmogWav.Stereo}.");

        var beetleWav = ResourceLoader.Load<AudioStreamWav>("res://music/scale_beetle/scale_beetle_boss_theme.wav");
        Require(beetleWav != null, "Scale Beetle boss theme failed to load from published PCK.");
        Require(beetleWav!.LoopMode == AudioStreamWav.LoopModeEnum.Forward && beetleWav.LoopEnd > 4_000_000 && beetleWav.MixRate == 48_000 && beetleWav.Stereo,
            $"Scale Beetle boss theme has invalid loop/format metadata: mode={beetleWav.LoopMode}, end={beetleWav.LoopEnd}, rate={beetleWav.MixRate}, stereo={beetleWav.Stereo}.");

        Type customMusicPatchType = typeof(NativeSfxPlayer).Assembly.GetType("STS2_Things.Audio.CustomMusicPlayPatch")
            ?? throw new InvalidOperationException("Missing STS2_Things.Audio.CustomMusicPlayPatch.");
        MethodInfo tryGetVolume = customMusicPatchType.GetMethod("TryGetAuthoredVolumeDb", NonPublicStatic)
            ?? throw new InvalidOperationException("Missing CustomMusicPlayPatch.TryGetAuthoredVolumeDb.");

        bool origBgmEnabled = ThingsModConfig.GetBool(ThingsModConfig.FeatureCustomBgmEnabled);
        ThingsModConfig.SetValue(ThingsModConfig.FeatureCustomBgmEnabled, true);
        try
        {
            var gravetideEnc = (ThingsGravetideSlugBossEncounter)ModelDb.Encounter<ThingsGravetideSlugBossEncounter>().ToMutable();
            var fogmogEnc = (ThingsOriginFogmogBossEncounter)ModelDb.Encounter<ThingsOriginFogmogBossEncounter>().ToMutable();
            var beetleEnc = (ThingsScaleBeetleBossEncounter)ModelDb.Encounter<ThingsScaleBeetleBossEncounter>().ToMutable();
            var towerEnc = (ThingsHumanFaceTowerBossEncounter)ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>().ToMutable();
            var caveGodEnc = (ThingsCaveGodBossEncounter)ModelDb.Encounter<ThingsCaveGodBossEncounter>().ToMutable();
            var bowlbugEnc = (ThingsBowlbugProgenitorBossEncounter)ModelDb.Encounter<ThingsBowlbugProgenitorBossEncounter>().ToMutable();
            var inkmawEnc = (ThingsAbyssalInkmawBossEncounter)ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>().ToMutable();
            Require(gravetideEnc.CustomBgm == "res://music/gravetide_slug/gravetide_slug_boss_theme.wav",
                $"Gravetide Slug CustomBgm mismatch: {gravetideEnc.CustomBgm}");
            Require(fogmogEnc.CustomBgm == "res://music/origin_fogmog/origin_fogmog_boss_theme.wav",
                $"Origin Fogmog CustomBgm mismatch: {fogmogEnc.CustomBgm}");
            Require(beetleEnc.CustomBgm == "res://music/scale_beetle/scale_beetle_boss_theme.wav",
                $"Scale Beetle CustomBgm mismatch: {beetleEnc.CustomBgm}");
            Require(towerEnc.CustomBgm == "res://music/human_face_tower/human_face_tower_boss_theme.wav",
                $"Human Face Tower CustomBgm mismatch: {towerEnc.CustomBgm}");
            Require(caveGodEnc.CustomBgm == "res://music/cave_god/cave_god_boss_theme.wav",
                $"Cave God CustomBgm mismatch: {caveGodEnc.CustomBgm}");
            Require(bowlbugEnc.CustomBgm == "res://music/bowlbug_progenitor/bowlbug_progenitor_boss_theme.wav",
                $"Bowlbug Progenitor CustomBgm mismatch: {bowlbugEnc.CustomBgm}");
            Require(inkmawEnc.CustomBgm == "res://music/abyssal_inkmaw/abyssal_inkmaw_boss_theme.wav",
                $"Abyssal Inkmaw CustomBgm mismatch: {inkmawEnc.CustomBgm}");

            foreach (string track in new[] { gravetideEnc.CustomBgm, fogmogEnc.CustomBgm, beetleEnc.CustomBgm, towerEnc.CustomBgm, caveGodEnc.CustomBgm, bowlbugEnc.CustomBgm, inkmawEnc.CustomBgm })
            {
                object?[] args = [track, 0f];
                bool recognized = (bool)tryGetVolume.Invoke(null, args)!;
                float volDb = (float)args[1]!;
                Require(recognized && Math.Abs(volDb - (-4.5f)) < 0.05f,
                    $"CustomMusicPlayPatch did not recognize authored Boss theme {track} (recognized={recognized}, volDb={volDb}).");
            }
        }
        finally
        {
            ThingsModConfig.SetValue(ThingsModConfig.FeatureCustomBgmEnabled, origBgmEnabled);
        }

        MethodInfo resolveDb = typeof(NativeSfxPlayer).GetMethod("ResolveMusicVolumeDb", NonPublicStatic)
            ?? throw new InvalidOperationException("Missing NativeSfxPlayer.ResolveMusicVolumeDb.");
        float origMaster = SaveManager.Instance.SettingsSave.VolumeMaster;
        float origBgm = SaveManager.Instance.SettingsSave.VolumeBgm;
        try
        {
            SaveManager.Instance.SettingsSave.VolumeMaster = 0.5f;
            SaveManager.Instance.SettingsSave.VolumeBgm = 0f;
            float mutedDb = (float)resolveDb.Invoke(null, [-6f])!;
            Require(mutedDb <= -79f, $"Muted BGM volume did not silence NativeSfxPlayer ({mutedDb} dB).");

            SaveManager.Instance.SettingsSave.VolumeBgm = 0.5f;
            float defaultDb = (float)resolveDb.Invoke(null, [0f])!;
            Require(Math.Abs(defaultDb) < 0.05f, $"Default 0.5x0.5 volume drifted from 0 dB ({defaultDb} dB).");
        }
        finally
        {
            SaveManager.Instance.SettingsSave.VolumeMaster = origMaster;
            SaveManager.Instance.SettingsSave.VolumeBgm = origBgm;
        }
    }

    private static void VerifyBowlbugSlotOrder()
    {
        var encounter = (ThingsBowlbugProgenitorBossEncounter)ModelDb.Encounter<ThingsBowlbugProgenitorBossEncounter>().ToMutable();
        IReadOnlyList<string> slots = encounter.Slots;
        Require(slots.Count == 17 && slots[0] == "bowlbug_1" && slots[^1] == "bowlbug_progenitor",
            $"BowlbugProgenitorBossEncounter slot order broke minion-before-boss ordering: [{string.Join(',', slots)}].");
    }

    private static void VerifyHiveDiscoveryAndSecondBossGuard()
    {
        Hive hive = (Hive)ModelDb.Act<Hive>().ToMutable();
        Require(hive.AllBossEncounters.Any(e => e is ThingsCaveGodBossEncounter),
            "Hive.AllBossEncounters must retain CaveGodBossEncounter.");
        Require(hive.BossDiscoveryOrder.All(e => e is not ThingsCaveGodBossEncounter),
            "Hive.BossDiscoveryOrder must not tutorial-force CaveGodBossEncounter when BossCaveGodForced is off.");
        ThingsDepths depths = (ThingsDepths)ModelDb.Act<ThingsDepths>().ToMutable();
        Require(depths.BossDiscoveryOrder.Any(e => e is ThingsCaveGodBossEncounter),
            "Depths.BossDiscoveryOrder must still discover CaveGodBossEncounter.");

        ThingsModConfig.SetValue(ThingsModConfig.BossCaveGodForced, true);
        try
        {
            Hive forcedHive = (Hive)ModelDb.Act<Hive>().ToMutable();
            Require(forcedHive.BossDiscoveryOrder.Any(e => e is ThingsCaveGodBossEncounter),
                "BossCaveGodForced=true must include CaveGodBossEncounter in Hive.BossDiscoveryOrder.");
        }
        finally
        {
            ThingsModConfig.SetValue(ThingsModConfig.BossCaveGodForced, false);
        }

        ThingsModConfig.SetValue(ThingsModConfig.BossOnlyModBosses, true);
        try
        {
            Overgrowth multiBossAct = (Overgrowth)ModelDb.Act<Overgrowth>().ToMutable();
            multiBossAct.GenerateRooms(new Rng(17), UnlockState.all);
            multiBossAct.SetSecondBossEncounter(null);
            Require(multiBossAct.SecondBossEncounter is null,
                "Multi-boss act unexpectedly set SecondBossEncounter when SetSecondBossEncounter(null) was called.");
        }
        finally
        {
            ThingsModConfig.SetValue(ThingsModConfig.BossOnlyModBosses, false);
        }
    }

    private static bool DummyResolver4(string cardId, ulong instanceId, int key, out Texture2D? tex)
    {
        tex = null;
        return false;
    }

    private static bool DummyResolver5(CardModel card, string cardId, ulong instanceId, int key, out Texture2D? tex)
    {
        tex = null;
        return false;
    }

    private static void VerifyPortraitResolverMatcherAndAdapters()
    {
        Assembly asm = typeof(STS2_ThingsInit).Assembly;
        Type guard = asm.GetType("STS2_Things.Compatibility.Internal.PortraitCacheGuard", throwOnError: true)!;
        MethodInfo matcher4 = guard.GetMethod("MatchesReplacementResolver4", NonPublicStatic)!;
        MethodInfo matcher5 = guard.GetMethod("MatchesReplacementResolver5", NonPublicStatic)!;
        MethodInfo m4 = typeof(BugFixRegressions).GetMethod(nameof(DummyResolver4), NonPublicStatic)!;
        MethodInfo m5 = typeof(BugFixRegressions).GetMethod(nameof(DummyResolver5), NonPublicStatic)!;
        Require((bool)matcher4.Invoke(null, [m4])! && !(bool)matcher4.Invoke(null, [m5])!,
            "PortraitCacheGuard.MatchesReplacementResolver4 must match only 4-arg CardArtEditor resolvers.");
        Require((bool)matcher5.Invoke(null, [m5])! && !(bool)matcher5.Invoke(null, [m4])!,
            "PortraitCacheGuard.MatchesReplacementResolver5 must match only 5-arg CardArtEditor resolvers.");

        Type adaptersType = asm.GetType("STS2_Things.Compatibility.Mods.KnownModAdapters", throwOnError: true)!;
        var adapters = (System.Collections.IEnumerable)adaptersType
            .GetField("All", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        HashSet<string> ids = adapters.Cast<object>()
            .Select(a => (string)a.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(a)!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (string expected in new[]
        {
            "AiSilentNsfw",
            "CardArtProjectExportMod",
            "MechanekoDefectCardArt",
            "PornographicBeautificationCardsV21",
            "PornographicBeautificationCardsV20",
            "PornographicBeautificationCardsV19",
            "PornographicBeautificationCardsV18",
        })
        {
            Require(ids.Contains(expected), $"Missing KnownModAdapters entry '{expected}'.");
        }
    }

    private static void VerifyHoverTipNonOverlappingLayout(Node host)
    {
        Type patch = typeof(STS2_ThingsInit).Assembly.GetType("STS2_Things.Hooks.MonsterHoverTipLayoutPatch", throwOnError: true)!;
        MethodInfo layout = patch.GetMethod("LayoutNonOverlapping", NonPublicStatic)!;
        var textContainer = new Control();
        var cardContainer = new NHoverTipCardContainer();
        host.AddChild(textContainer);
        host.AddChild(cardContainer);
        try
        {
            Rect2 leftArmHitbox = new(new Vector2(280f, 380f), new Vector2(180f, 220f));
            Vector2 viewport = new(1920f, 1080f);
            Vector2 textSize = new(360f, 280f);
            Vector2 cardSize = new(540f, 360f);
            layout.Invoke(null, [leftArmHitbox, viewport, true, textContainer, textSize, cardContainer, cardSize, true]);

            Rect2 textRect = new(textContainer.GlobalPosition, textSize);
            Rect2 cardRect = new(cardContainer.GlobalPosition, cardSize);
            Require(!textRect.Intersects(cardRect),
                $"Left-side hover tips overlapped: text={textRect}, cards={cardRect}.");
            Require(textRect.Position.X >= 12f && textRect.End.X <= 1908f && textRect.Position.Y >= 12f && textRect.End.Y <= 1068f,
                $"Left-side text hover tip exceeded viewport margin: {textRect}.");
            Require(cardRect.Position.X >= 12f && cardRect.End.X <= 1908f && cardRect.Position.Y >= 12f && cardRect.End.Y <= 1068f,
                $"Left-side card hover tip exceeded viewport margin: {cardRect}.");
        }
        finally
        {
            textContainer.Free();
            cardContainer.Free();
        }
    }

    private static void VerifyCaveGodForceKillContracts()
    {
        MethodInfo agingRemove = typeof(ThingsCaveGodAgingPower).GetMethod(nameof(PowerModel.ShouldPowerBeRemovedAfterOwnerDeath))!;
        MethodInfo agingFatal = typeof(ThingsCaveGodAgingPower).GetMethod(nameof(PowerModel.ShouldOwnerDeathTriggerFatal))!;
        MethodInfo bulwarkModify = typeof(ThingsCaveGodAncientCorePower).GetMethod(
            "OnModifyDamageMultiplicative",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        MethodInfo handRemove = typeof(ThingsCaveGodHand).GetMethod(nameof(MonsterModel.ShouldCreatureBeRemovedFromCombatAfterDeath))!;
        PropertyInfo bodyFade = typeof(ThingsCaveGodBody).GetProperty(nameof(MonsterModel.ShouldFadeAfterDeath))!;
        Require(agingRemove.DeclaringType == typeof(ThingsCaveGodAgingPower) && agingFatal.DeclaringType == typeof(ThingsCaveGodAgingPower),
            "ThingsCaveGodAgingPower must override ShouldPowerBeRemovedAfterOwnerDeath and ShouldOwnerDeathTriggerFatal.");
        Require(bulwarkModify.DeclaringType == typeof(ThingsCaveGodAncientCorePower),
            "ThingsCaveGodAncientCorePower must override OnModifyDamageMultiplicative.");
        Require(handRemove.DeclaringType == typeof(ThingsCaveGodHand) && bodyFade.DeclaringType == typeof(ThingsCaveGodBody),
            "ThingsCaveGodHand must override ShouldCreatureBeRemovedFromCombatAfterDeath and ThingsCaveGodBody must override ShouldFadeAfterDeath.");
#if STS2_V113_1
        PropertyInfo interactable = typeof(ThingsCaveGodHand).GetProperty("IsInteractable", BindingFlags.Public | BindingFlags.Instance)!;
        Require(interactable.GetMethod?.GetBaseDefinition().DeclaringType == typeof(MonsterModel),
            "ThingsCaveGodHand.IsInteractable must override MonsterModel.IsInteractable on v0.113.1.");
        var caveGodHand = (ThingsCaveGodLeftHand)ModelDb.Monster<ThingsCaveGodLeftHand>().ToMutable();
        Require(caveGodHand.IsInteractable, "Active ThingsCaveGodLeftHand must expose IsInteractable=true on v0.113.1.");
        typeof(ThingsCaveGodHand).GetProperty(nameof(ThingsCaveGodHand.IsGrabbing))!.SetValue(caveGodHand, true);
        Require(!caveGodHand.IsInteractable, "Grabbing ThingsCaveGodLeftHand must expose IsInteractable=false on v0.113.1.");
#else
        PropertyInfo healthBarVisible = typeof(ThingsCaveGodHand).GetProperty("IsHealthBarVisible", BindingFlags.Public | BindingFlags.Instance)!;
        Require(healthBarVisible.GetMethod?.GetBaseDefinition().DeclaringType == typeof(MonsterModel),
            "ThingsCaveGodHand.IsHealthBarVisible must override MonsterModel.IsHealthBarVisible on v0.111.1.");
        var caveGodHand = (ThingsCaveGodLeftHand)ModelDb.Monster<ThingsCaveGodLeftHand>().ToMutable();
        Require(caveGodHand.IsHealthBarVisible, "Active ThingsCaveGodLeftHand must expose IsHealthBarVisible=true on v0.111.1.");
        typeof(ThingsCaveGodHand).GetProperty(nameof(ThingsCaveGodHand.IsGrabbing))!.SetValue(caveGodHand, true);
        Require(!caveGodHand.IsHealthBarVisible, "Grabbing ThingsCaveGodLeftHand must expose IsHealthBarVisible=false on v0.111.1.");
#endif
    }

    private static void VerifyDailySaveCrossroadExtraction()
    {
        var ledger = (ThingsCrossroads)ModelDb.Modifier<ThingsCrossroads>().ToMutable();
        ledger.Data = "{\"Version\":1,\"Acts\":[{\"Act\":0,\"Fingerprint\":999,\"Roads\":[],\"History\":[]}]}";
        var dailySave = new SerializableRun
        {
            Modifiers =
            [
                ModelDb.Modifier<Specialized>().ToMutable().ToSerializable(),
                ModelDb.Modifier<Vintage>().ToMutable().ToSerializable(),
                ModelDb.Modifier<Specialized>().ToMutable().ToSerializable(),
                ledger.ToSerializable(),
            ],
        };
        MethodInfo extract = typeof(STS2_Things.Map.Crossroads).GetMethod("ExtractSerializedLedger", NonPublicStatic)!;
        var extracted = (SerializableModifier?)extract.Invoke(null, [dailySave]);
        Require(extracted != null && dailySave.Modifiers.Count == 3 &&
                dailySave.Modifiers.All(m => m.Id?.Entry != "THINGS_CROSSROADS"),
            "Legacy 4-modifier Daily save did not strip THINGS_CROSSROADS down to 3 Daily modifiers.");
        var restored = (ThingsCrossroads)ModifierModel.FromSerializable(extracted!);
        Require(restored.Data == ledger.Data, "Extracted Daily save ledger lost its serialized route data.");
    }

    private static void VerifySnailChipRunAssetRetention()
    {
        var chip = ModelDb.Card<ThingsSnailCrystalChip>();
        var prop = typeof(CardModel).GetProperty("ExtraRunAssetPaths", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
        var paths = (IEnumerable<string>)prop.GetValue(chip)!;
        Require(paths.Contains(chip.PortraitPath),
            "SnailCrystalChip must retain its portrait path in ExtraRunAssetPaths across room unloads.");
    }

    private static void VerifyBossStateDisplayEffectiveZAndPckGuard(Node host)
    {
        // 1) ComputeEffectiveZIndex walks ZAsRelative parents and stops at !ZAsRelative (cf56c660 regression).
        MethodInfo computeEffectiveZ = typeof(STS2_ThingsInit).Assembly
            .GetType("STS2_Things.Hooks.BossStateDisplayLayerPatch", throwOnError: true)!
            .GetMethod("ComputeEffectiveZIndex", NonPublicStatic)!;
        var sceneContainer = new Control { ZIndex = -10, ZAsRelative = true };
        var allyContainer = new Control { ZIndex = 0, ZAsRelative = true };
        var normalCreature = new Control { ZIndex = 0, ZAsRelative = true };
        var absoluteWrapper = new Control { ZIndex = 0, ZAsRelative = false };
        var moddedCreature = new Control { ZIndex = 0, ZAsRelative = true };
        host.AddChild(sceneContainer);
        sceneContainer.AddChild(allyContainer);
        allyContainer.AddChild(normalCreature);
        sceneContainer.AddChild(absoluteWrapper);
        absoluteWrapper.AddChild(moddedCreature);
        try
        {
            int normalZ = (int)computeEffectiveZ.Invoke(null, [normalCreature])!;
            int moddedZ = (int)computeEffectiveZ.Invoke(null, [moddedCreature])!;
            Require(normalZ == -10, $"Expected effective Z=-10 under SceneContainer, got {normalZ}.");
            Require(moddedZ == 0, $"Expected effective Z=0 under ZAsRelative=false wrapper, got {moddedZ}.");
        }
        finally
        {
            sceneContainer.Free();
        }

        // 2) PckIntegrityGuard pins files read-shared on Windows, permitting readers while blocking writers.
        if (OperatingSystem.IsWindows() && !STS2_Things.Platform.PckIntegrityGuard.IsPinned)
        {
            string tempPck = Path.Combine(Path.GetTempPath(), $"sts2_things_pck_guard_{Guid.NewGuid():N}.pck");
            File.WriteAllText(tempPck, "synthetic-pck-guard-probe");
            MethodInfo tryPinPath = typeof(STS2_Things.Platform.PckIntegrityGuard)
                .GetMethod("TryPinPath", NonPublicStatic)!;
            MethodInfo releaseForTest = typeof(STS2_Things.Platform.PckIntegrityGuard)
                .GetMethod("ReleaseForTest", NonPublicStatic)!;
            try
            {
                bool pinned = (bool)tryPinPath.Invoke(null, [tempPck])!;
                Require(pinned && STS2_Things.Platform.PckIntegrityGuard.IsPinned,
                    "PckIntegrityGuard.TryPinPath failed to pin temporary PCK on Windows.");
                using (var reader = new FileStream(tempPck, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    Require(reader.Length > 0, "Pinned PCK must remain readable by Godot and SHA-256 hashing.");
                }

                bool writeBlocked = false;
                try
                {
                    using var writer = new FileStream(tempPck, FileMode.Open, System.IO.FileAccess.Write, FileShare.None);
                }
                catch (IOException)
                {
                    writeBlocked = true;
                }
                Require(writeBlocked, "Pinned PCK allowed an external write stream instead of blocking mid-session hot-swap.");
            }
            finally
            {
                releaseForTest.Invoke(null, null);
                if (File.Exists(tempPck))
                    File.Delete(tempPck);
            }
            Require(!STS2_Things.Platform.PckIntegrityGuard.IsPinned,
                "PckIntegrityGuard.ReleaseForTest did not release the pinned handle.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("BugFixRegressions: " + message);
    }
}
