using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Config;
using STS2_Things.Encounters;
using STS2_Things.Hooks;

namespace STS2_Things.Acts;

/// <summary>A complete alternate second act, selected and saved through native ActModel rules.</summary>
public sealed class ThingsDepths : ActModel
{
    public override int Index => 1;
    public override bool IsDefault => false;
    public override bool IsUnlocked(UnlockState unlockState) => true;
    protected override int NumberOfWeakEncounters => 2;
    protected override int BaseNumberOfRooms => 14;

    public override Color MapTraveledColor => new("233F4A");
    public override Color MapUntraveledColor => new("506E7C");
    public override Color MapBgColor => new("A6B9BF");
    public override string[] BgMusicOptions => ModelDb.Act<Hive>().BgMusicOptions;
    public override string[] MusicBankPaths => ModelDb.Act<Hive>().MusicBankPaths;
    public override string AmbientSfx => ModelDb.Act<Hive>().AmbientSfx;
    public override string ChestOpenSfx => ModelDb.Act<Hive>().ChestOpenSfx;
    public override string ChestSpineResourcePath => ModelDb.Act<Hive>().ChestSpineResourcePath;
    public override string ChestSpineSkinNameNormal => ModelDb.Act<Hive>().ChestSpineSkinNameNormal;
    public override string ChestSpineSkinNameStroke => ModelDb.Act<Hive>().ChestSpineSkinNameStroke;

    internal RoomSet Rooms => _rooms;

    // Keep the established Act 2 ancient rules, and prefer mod-authored regular events
    // while falling back to native Hive events if every custom event is disabled.
    public override IEnumerable<AncientEventModel> AllAncients => ModelDb.Act<Hive>().AllAncients;
    public override IEnumerable<EventModel> AllEvents
    {
        get
        {
            EventModel[] hiveEvents = ModelDb.Act<Hive>().AllEvents.ToArray();
            EventModel[] customEvents = ThingsEventCatalog.AddDepthsEvents(
                hiveEvents.Where(e => e.GetType().Assembly != typeof(ActModel).Assembly)).ToArray();
            return customEvents.Length > 0
                ? customEvents
                : hiveEvents.Where(e => e.GetType().Assembly == typeof(ActModel).Assembly);
        }
    }
    public override IEnumerable<AncientEventModel> GetUnlockedAncients(UnlockState state) =>
        ModelDb.Act<Hive>().GetUnlockedAncients(state);
    public override MapPointTypeCounts GetMapPointTypes(Rng mapRng) => ModelDb.Act<Hive>().GetMapPointTypes(mapRng);
    protected override void ApplyActDiscoveryOrderModifications(UnlockState unlockState) { }

    public override IEnumerable<EncounterModel> BossDiscoveryOrder
    {
        get
        {
            bool towerForced = ThingsModConfig.IsForced(ThingsModConfig.BossHumanFaceTowerForced);
            bool inkmawForced = ThingsModConfig.IsForced(ThingsModConfig.BossAbyssalInkmawForced);
            bool caveGodForced = ThingsModConfig.IsForced(ThingsModConfig.BossCaveGodForced);
            EncounterModel? depthsForced = towerForced
                ? ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>()
                : inkmawForced
                    ? ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>()
                    : null;
            if (depthsForced != null && caveGodForced)
                return [depthsForced, ModelDb.Encounter<ThingsCaveGodBossEncounter>()];
            if (depthsForced != null)
                return [depthsForced];
            if (caveGodForced)
                return [ModelDb.Encounter<ThingsCaveGodBossEncounter>()];
            if (ThingsModConfig.GetInt(ThingsModConfig.BossHumanFaceTowerWeightPercent) != 100 ||
                ThingsModConfig.GetInt(ThingsModConfig.BossAbyssalInkmawWeightPercent) != 100 ||
                ThingsModConfig.GetInt(ThingsModConfig.BossCaveGodWeightPercent) != 100)
                return [];
            return AllBossEncounters;
        }
    }

    public override IEnumerable<EncounterModel> GenerateAllEncounters()
    {
        // 6 pure Weak encounters
        yield return ModelDb.Encounter<ThingsLanternFishWeak>();
        yield return ModelDb.Encounter<ThingsSanguineLeechWeak>();
        yield return ModelDb.Encounter<ThingsCaveMawWeak>();
        yield return ModelDb.Encounter<ThingsFleetingEchoWeak>();
        yield return ModelDb.Encounter<ThingsSnailTrioWeak>();
        yield return ModelDb.Encounter<ThingsScalePeelerWeak>();

        // 11 Regular encounters: 6 pure + 5 <=2-species complementary pairings
        yield return ModelDb.Encounter<ThingsLanternFishEncounter>();
        yield return ModelDb.Encounter<ThingsSanguineLeechEncounter>();
        yield return ModelDb.Encounter<ThingsLeechMotherEncounter>();
        yield return ModelDb.Encounter<ThingsSilkMothTrio>();
        yield return ModelDb.Encounter<ThingsHumanFaceColumnEncounter>();
        yield return ModelDb.Encounter<ThingsChromaticSlimeWeak>();
        yield return ModelDb.Encounter<ThingsSpongeLeechEncounter>();
        yield return ModelDb.Encounter<ThingsLanternSpongeEncounter>();
        yield return ModelDb.Encounter<ThingsSilkSnailEncounter>();
        yield return ModelDb.Encounter<ThingsScalePeelerEncounter>();
        yield return ModelDb.Encounter<ThingsCaveMawLanternEncounter>();

        // 3 custom Depths Elites
        yield return ModelDb.Encounter<ThingsMycorrhizalTwinsElite>();
        yield return ModelDb.Encounter<ThingsReverseSalamanderElite>();
        yield return ModelDb.Encounter<ThingsRadioJellyfishElite>();

        // Cave God shares Hive/Depths settings; Human-Faced Tower and Abyssal Inkmaw are exclusive to Depths.
        // Fall back to Kaiser Crab only when all Depths bosses are disabled/unavailable.
        EncounterModel caveGod = ModelDb.Encounter<ThingsCaveGodBossEncounter>();
        EncounterModel humanFaceTower = ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>();
        EncounterModel abyssalInkmaw = ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>();
        if (MonsterEncounterCatalog.IsUnfilteredCatalogScope)
        {
            yield return caveGod;
            yield return humanFaceTower;
            yield return abyssalInkmaw;
            yield break;
        }
        bool towerForced = ThingsModConfig.IsForced(ThingsModConfig.BossHumanFaceTowerForced);
        bool inkmawForced = ThingsModConfig.IsForced(ThingsModConfig.BossAbyssalInkmawForced);
        bool caveGodForced = ThingsModConfig.IsForced(ThingsModConfig.BossCaveGodForced);
        EncounterModel? depthsForced = towerForced ? humanFaceTower : inkmawForced ? abyssalInkmaw : null;
        if (depthsForced != null && caveGodForced)
        {
            yield return depthsForced;
            yield return caveGod;
            yield break;
        }
        if (depthsForced != null)
        {
            yield return depthsForced;
            yield break;
        }
        if (caveGodForced)
        {
            yield return caveGod;
            yield break;
        }

        bool yieldedBoss = false;
        if (EncounterSelectionPolicy.IsAvailable(caveGod))
        {
            yieldedBoss = true;
            yield return caveGod;
        }
        if (EncounterSelectionPolicy.IsAvailable(humanFaceTower))
        {
            yieldedBoss = true;
            yield return humanFaceTower;
        }
        if (EncounterSelectionPolicy.IsAvailable(abyssalInkmaw))
        {
            yieldedBoss = true;
            yield return abyssalInkmaw;
        }
        if (!yieldedBoss)
        {
            yield return ModelDb.Encounter<KaiserCrabBoss>();
        }
    }
}
