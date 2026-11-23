using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Afflictions;
using STS2_Things.Cards;
using STS2_Things.Encounters;
using STS2_Things.Events;
using STS2_Things.Events.Minigames;
using STS2_Things.Hooks;
using STS2_Things.Monsters;
using STS2_Things.Platform;
using STS2_Things.Powers;
using STS2_Things.Visuals;

public partial class DepthsProbeNode : Node
{
    private static int _checks;

    private static void Assert(bool ok, string message)
    {
        if (!ok)
            throw new InvalidOperationException($"[ExtendedPartyProbe FAIL] {message}");
        _checks++;
    }

    public override void _Ready()
    {
        AssemblyLoadContext.Default.Resolving += ResolveRuntimeDependency;
        EnsureRuntimeDependency("Sentry");
        EnsureRuntimeDependency("Sentry.Godot");
        _ = RunAll();
    }

    private async Task RunAll()
    {
        Harmony? modHarmony = null;
        bool originalHoverBlock = NHoverTipSet.shouldBlockHoverTips;
        try
        {
            TestMode.TurnOnInternal();

            string? gamePck = System.Environment.GetEnvironmentVariable("THINGS_PROBE_GAME_PCK");
            if (!string.IsNullOrWhiteSpace(gamePck) && File.Exists(gamePck))
                ProjectSettings.LoadResourcePack(gamePck);

            string? modPck = System.Environment.GetEnvironmentVariable("THINGS_PROBE_PCK");
            if (!string.IsNullOrWhiteSpace(modPck) && File.Exists(modPck))
                ProjectSettings.LoadResourcePack(modPck);

            Sts2VersionSeam.InitializeBeforeModelDatabase();
            InitializeModelDb();
            Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly);
            Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(ThingsQuirkPower).Assembly);

            modHarmony = new Harmony("ExtendedPartyProbe.ModPatches");
            modHarmony.PatchAll(typeof(ThingsQuirkPower).Assembly);

            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "zhs";
            LocManager.Initialize();
            NHoverTipSet.shouldBlockHoverTips = true;

            VerifyMultiplayerCompatHelpers();
            await VerifyQuirkPowerExtendedPartiesAsync();
            await VerifyReverseSalamanderExtendedPartyAsync();
            await VerifySanguineLeechExtendedPartyAsync();
            await VerifyMockeryEchoExtendedPartyAsync();
            VerifyRadioJellyfishExtendedParty();
            VerifyAbyssalInkmawAndScalePeelerScaling();
            VerifyBronzeCauldronProtocolAndMinigame();
            await VerifyIndividualMinigameRngIsolationAsync();
            VerifyMonumentalAndCaveGodLayouts();
            VerifyCaveGodClusterOffsets();
            VerifyDepthsRestSiteExtendedSeating();
            VerifyBugReportExtendedPartySafety();

            NHoverTipSet.shouldBlockHoverTips = originalHoverBlock;
            modHarmony.UnpatchAll(modHarmony.Id);
            ClearRunState();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"Extended Party (>4P) compatibility probe: PASS ({_checks} assertions)");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            NHoverTipSet.shouldBlockHoverTips = originalHoverBlock;
            modHarmony?.UnpatchAll(modHarmony.Id);
            ClearRunState();
            GD.PushError(ex.ToString());
            GetTree().Quit(1);
        }
    }

    private static void ClearRunState()
    {
        DeactivateSyntheticCombat();
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(RunManager.Instance, null);
    }

    private static RunState SetupRun(string seed, int playerCount)
    {
        ClearRunState();
        List<Player> players = [];
        CharacterModel[] roster =
        [
            ModelDb.Character<Ironclad>(),
            ModelDb.Character<Silent>(),
            ModelDb.Character<Defect>(),
            ModelDb.Character<Necrobinder>(),
            ModelDb.Character<Regent>(),
        ];
        for (int i = 0; i < playerCount; i++)
            players.Add(Player.CreateForNewRun(roster[i % roster.Length], UnlockState.all, (ulong)(i + 1)));

        List<ActModel> acts = ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList();
        RunState run = ProbeRuntimeApi.CreateForNewRun(players, acts, [], GameMode.Standard, 0, seed);
        RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), shouldSave: false);
        LocalContext.NetId = run.Players[0].NetId;
        return run;
    }

    private static CombatState SetupCombat(RunState run, EncounterModel canonicalEncounter)
    {
        EncounterModel encounter = canonicalEncounter.ToMutable();
        run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, encounter.Id);
        var room = new CombatRoom(encounter, run);
        run.PushRoom(room);
        CombatState state = room.CombatState;
        foreach (Player p in run.Players)
        {
            p.ResetCombatState();
            state.AddPlayer(p);
            p.PopulateCombatState(run.Rng.Shuffle, state);
        }
        encounter.GenerateMonstersWithSlots(run);
        foreach (var (monster, slot) in encounter.MonstersWithSlots)
        {
            Creature entity = state.CreateCreature(monster, CombatSide.Enemy, slot);
            state.AddCreature(entity);
            monster.SetUpForCombat();
        }
        state.CurrentSide = CombatSide.Player;
        ActivateSyntheticCombat(state);
        return state;
    }

    private static CardPlay MakeCardPlay(CardModel card, Player player, Creature? target = null) =>
        new()
        {
            Card = card,
            Player = player,
            Target = target,
            ResultPile = PileType.Discard,
            Resources = new ResourceInfo
            {
                EnergySpent = 0,
                EnergyValue = 0,
                StarsSpent = 0,
                StarValue = 0,
            },
            IsAutoPlay = false,
            PlayIndex = 0,
            PlayCount = 1,
        };

    private static void SetEffectiveOverride(Func<int, int>? resolver)
    {
        PropertyInfo prop = typeof(MultiplayerCompat).GetProperty(
            "EffectivePlayerCountOverrideForTests",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        prop.SetValue(null, resolver);
    }

    private static void VerifyMultiplayerCompatHelpers()
    {
        try
        {
            SetEffectiveOverride(raw => Math.Max(4, raw / 2));
            for (int n = 1; n <= 4; n++)
            {
                Assert(!MultiplayerCompat.IsExtendedParty(n), $"IsExtendedParty({n}) must be false");
                Assert(MultiplayerCompat.GetEffectivePlayerCount(n) == n, $"1-4p effective count ({n}) must ignore override");
                Assert(MathF.Abs(MultiplayerCompat.GetExtendedPartyScaleRatio(n) - 1f) < 0.001f, $"1-4p scale ratio ({n}) must be 1.0");
            }
            Assert(MultiplayerCompat.IsExtendedParty(5), "IsExtendedParty(5) must be true");
            Assert(MultiplayerCompat.GetEffectivePlayerCount(16) == 8, "16p effective count must honor external resolver override (8)");
            Assert(MathF.Abs(MultiplayerCompat.GetExtendedPartyScaleRatio(16) - 2f) < 0.001f, "16p effective=8 scale ratio must be 2.0");

            SetEffectiveOverride(null);
            Assert(MultiplayerCompat.GetEffectivePlayerCount(16) == 16, "16p fallback effective count must be 16");
            Assert(MultiplayerCompat.GetEffectivePlayerCount(32) == 32, "32p fallback effective count must be 32");

            var seenColors = new HashSet<string>();
            for (int i = 0; i < 32; i++)
            {
                Color c = MultiplayerCompat.GetSeatColor(i);
                Assert(c.A > 0.99f, $"Seat color {i} must be opaque");
                seenColors.Add(c.ToHtml(false));
            }
            Assert(seenColors.Count == 32, $"All 32 seat colors must be distinct (got {seenColors.Count})");
            Assert(MultiplayerCompat.GetSeatColor(0).ToHtml(false) == new Color("#ffd15c").ToHtml(false), "Slot 0 color preserved");
            Assert(MultiplayerCompat.GetSeatColor(3).ToHtml(false) == new Color("#a6f28a").ToHtml(false), "Slot 3 color preserved");
        }
        finally
        {
            SetEffectiveOverride(null);
        }
    }

    private static async Task VerifyQuirkPowerExtendedPartiesAsync()
    {
        foreach (int partySize in new[] { 4, 16, 32 })
        {
            RunState run = SetupRun($"quirk-{partySize}p", partySize);
            for (int i = 0; i < run.Players.Count; i++)
            {
                Player p = run.Players[i];
                PotionModel potion = ModelDb.Potion<FirePotion>().ToMutable();
                p.AddPotionInternal(potion, 0);
            }

            CombatState state = SetupCombat(run, ModelDb.Encounter<ThingsQuirkyHopperWeak>());
            Creature hopperCreature = state.Enemies.First(e => e.Monster is ThingsQuirkyHopper);
            ThingsQuirkyHopper hopper = (ThingsQuirkyHopper)hopperCreature.Monster!;

            MethodInfo thievery = typeof(ThingsQuirkyHopper).GetMethod(
                "ThieveryMove", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var targets = state.Players.Select(p => p.Creature).ToList();
            Task moveTask = (Task)thievery.Invoke(hopper, [targets])!;
            await moveTask;

            List<ThingsQuirkPower> powers = hopperCreature.GetPowerInstances<ThingsQuirkPower>().ToList();
            List<ThingsQuirkPower> cardStates = powers.Where(p => !p.IsPotionState).ToList();
            List<ThingsQuirkPower> potionStates = powers.Where(p => p.IsPotionState).ToList();

            Assert(cardStates.Count == partySize, $"QuirkPower {partySize}p expected {partySize} card states, got {cardStates.Count}");
            Assert(potionStates.Count == partySize, $"QuirkPower {partySize}p expected {partySize} potion states, got {potionStates.Count}");

            PropertyInfo visibleProp = typeof(PowerModel).GetProperty(
                "IsVisibleInternal", BindingFlags.Instance | BindingFlags.NonPublic)!;
            int visibleCount = powers.Count(p => (bool)visibleProp.GetValue(p)!);
            if (partySize <= 4)
                Assert(visibleCount == partySize, $"QuirkPower 4p must keep all {partySize} card icons visible (got {visibleCount})");
            else
                Assert(visibleCount == 1, $"QuirkPower {partySize}p must show only 1 icon on HP bar (got {visibleCount})");

            // Verify every encoded amount stays strictly below PowerModel's 999_999_999 clamp, is collision-free, and resolves accurately
            MethodInfo resolveCard = typeof(ThingsQuirkPower).GetMethod("ResolveStolenDeckCard", BindingFlags.Instance | BindingFlags.NonPublic)!;
            MethodInfo resolvePotion = typeof(ThingsQuirkPower).GetMethod("ResolveStolenPotion", BindingFlags.Instance | BindingFlags.NonPublic)!;
            const int potionFlag = 1 << 29;
            foreach (ThingsQuirkPower cs in cardStates)
            {
                Assert(cs.Amount > 0 && cs.Amount < potionFlag, $"Card state amount {cs.Amount} out of range in {partySize}p");
                Assert(resolveCard.Invoke(cs, null) is CardModel, $"Card state failed to resolve card in {partySize}p");
            }
            foreach (ThingsQuirkPower ps in potionStates)
            {
                Assert(ps.Amount > potionFlag && ps.Amount < 999_999_999, $"Potion state amount {ps.Amount} out of range in {partySize}p");
                Assert(resolvePotion.Invoke(ps, null) is PotionModel, $"Potion state failed to resolve potion in {partySize}p");
            }
        }
    }

    private static async Task VerifyReverseSalamanderExtendedPartyAsync()
    {
        RunState run = SetupRun("salamander-16p", 16);
        CombatState state = SetupCombat(run, ModelDb.Encounter<ThingsReverseSalamanderElite>());
        Creature sal = state.Enemies.First(e => e.Monster is ThingsReverseSalamander);
        await sal.Monster!.AfterAddedToRoom();
        await sal.Monster!.BeforeCombatStart();

        var ctx = new ThrowingPlayerChoiceContext();
        ThingsReverseCurrentPower power = sal.GetPower<ThingsReverseCurrentPower>()!;
        Assert(power != null, "Reverse Salamander must have ThingsReverseCurrentPower");

        // Give each of the 16 players a card in hand + ReverseSurge so AfterPlayerTurnStartLate afflicts all 16 with UpstreamRecall
        foreach (Player p in state.Players)
        {
            CardModel strike = state.CreateCard<StrikeIronclad>(p);
            await CardPileCmd.AddGeneratedCardToCombat(strike, PileType.Hand, p);
            await PowerCmd.Apply<ThingsReverseSurgePower>(ctx, p.Creature, 1, sal, null);
        }

        foreach (Player p in state.Players)
            await power!.AfterPlayerTurnStartLate(ctx, p);

        int playedCount = 0;
        foreach (Player p in state.Players)
        {
            CardModel? reflux = CardPile.GetCards(p, PileType.Hand).FirstOrDefault(c => c.Affliction is ThingsUpstreamRecall);
            if (reflux != null)
            {
                await power!.AfterCardPlayed(ctx, MakeCardPlay(reflux, p));
                playedCount++;
            }
        }

        int gainedStr = sal.GetPowerAmount<StrengthPower>();
        Assert(playedCount == 16, $"Expected all 16 players to play recalled Reflux cards (played={playedCount})");
        Assert(gainedStr == power!.Amount * 4,
            $"16p Reverse Salamander Strength must be capped at 4 triggers ({power.Amount * 4}), got {gainedStr} (played={playedCount})");
    }

    private static async Task VerifySanguineLeechExtendedPartyAsync()
    {
        RunState run = SetupRun("leech-16p", 16);
        CombatState state = SetupCombat(run, ModelDb.Encounter<ThingsSanguineLeechEncounter>());
        Creature leech = state.Enemies.First(e => e.Monster is ThingsSanguineLeech);
        await leech.Monster!.AfterAddedToRoom();
        await leech.Monster!.BeforeCombatStart();

        var ctx = new ThrowingPlayerChoiceContext();
        MethodInfo onTurnEndInHand = typeof(ThingsLeechParasite).GetMethod(
            "OnTurnEndInHand", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        for (int i = 0; i < state.Players.Count; i++)
        {
            Player p = state.Players[i];
            var parasite = (ThingsLeechParasite)state.CreateCard<ThingsLeechParasite>(p);
            parasite.BindOwnerLeech(leech);
            await CardPileCmd.AddGeneratedCardToCombat(parasite, PileType.Hand, p);
            await (Task)onTurnEndInHand.Invoke(parasite, [ctx])!;
        }

        int str = leech.GetPowerAmount<StrengthPower>();
        Assert(str == 8, $"16p Sanguine Leech end-of-turn Strength must cap at 4 feeds (+8 Str), got {str}");

        // Exhausting just 1 of 16 players' parasites should NOT lock leech into re-infest
        ThingsLeechInfestationPower infest = leech.GetPower<ThingsLeechInfestationPower>()!;
        Player p0 = state.Players[0];
        ThingsLeechParasite firstParasite = CardPile.GetCards(p0, PileType.Hand).OfType<ThingsLeechParasite>().First();
        await infest.AfterCardExhausted(ctx, firstParasite, causedByEthereal: false);
        FieldInfo reinfestField = typeof(ThingsSanguineLeech).GetField("_reinfestPending", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert(!(bool)reinfestField.GetValue(leech.Monster)!, "1/16 exhausted parasite must NOT immediately lock Sanguine Leech into re-infest");
    }

    private static async Task VerifyMockeryEchoExtendedPartyAsync()
    {
        RunState run = SetupRun("mockery-16p", 16);
        CombatState state = SetupCombat(run, ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>());
        var mockeryMonster = (ThingsHumanFaceTowerMockery)ModelDb.Monster<ThingsHumanFaceTowerMockery>().ToMutable();
        Creature target = state.CreateCreature(mockeryMonster, CombatSide.Enemy, null);
        state.AddCreature(target);
        mockeryMonster.SetUpForCombat();
        var ctx = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<ThingsMockeryEchoPower>(ctx, target, 1, target, null);
        ThingsMockeryEchoPower echo = target.GetPower<ThingsMockeryEchoPower>()!;
        Assert(echo != null, "Mockery must have ThingsMockeryEchoPower");

        foreach (Player p in state.Players)
        {
            CardModel strike1 = state.CreateCard<StrikeIronclad>(p);
            CardModel strike2 = state.CreateCard<StrikeIronclad>(p);
            await echo!.AfterCardPlayed(ctx, MakeCardPlay(strike1, p, target));
            await echo!.AfterCardPlayed(ctx, MakeCardPlay(strike2, p, target));
        }

        int str = target.GetPowerAmount<StrengthPower>();
        Assert(str == 4, $"16p Mockery Echo Strength per round must cap at +4, got {str}");
    }

    private static void VerifyRadioJellyfishExtendedParty()
    {
        var ch4 = new RadioChannel(Attacks: 1, Skills: 2, AttackThreshold: 1);
        Assert(ch4.HasAttack, "4p RadioChannel with 1 Attack must trigger HasAttack");

        RunState run16 = SetupRun("radio-16p", 16);
        CombatState state16 = SetupCombat(run16, ModelDb.Encounter<ThingsRadioJellyfishElite>());
        Creature jelly = state16.Enemies.First(e => e.Monster is ThingsRadioJellyfish);
        var power = (ThingsRadioReceptionPower)ModelDb.Power<ThingsRadioReceptionPower>().ToMutable();
        typeof(PowerModel).GetProperty("Owner")!.SetValue(power, jelly);

        object data = typeof(PowerModel).GetMethod("GetInternalData", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(ThingsRadioReceptionPower).GetNestedType("Data", BindingFlags.NonPublic)!)
            .Invoke(power, null)!;
        var records = (Dictionary<ulong, List<CardType>>)data.GetType().GetField("Records")!.GetValue(data)!;
        records[state16.Players[0].NetId] = [CardType.Attack];
        RadioProgram prog1 = power.GetProgram();
        Assert(prog1.First.AttackThreshold == 4, $"16p RadioJellyfish attack threshold must be 4, got {prog1.First.AttackThreshold}");
        Assert(!prog1.First.HasAttack && prog1.AttackCount == 1, $"1/16 Attack must not trigger extra beat (AttackCount={prog1.AttackCount})");

        for (int i = 1; i < 4; i++)
            records[state16.Players[i].NetId] = [CardType.Attack];
        RadioProgram prog4 = power.GetProgram();
        Assert(prog4.First.HasAttack && prog4.AttackCount == 2, $"4/16 Attacks must trigger extra beat (AttackCount={prog4.AttackCount})");
    }

    private static void VerifyAbyssalInkmawAndScalePeelerScaling()
    {
        try
        {
            SetEffectiveOverride(raw => raw == 16 ? 8 : raw);
            RunState run16 = SetupRun("inkmaw-scale-16p", 16);
            CombatState state16 = SetupCombat(run16, ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>());
            Creature boss = state16.Enemies.First(e => e.Monster is ThingsAbyssalInkmaw);
            MethodInfo? scaledRecoil = typeof(ThingsAbyssalInkmaw).GetMethod(
                "GetScaledSeverRecoilDamage", BindingFlags.Instance | BindingFlags.NonPublic);
            if (scaledRecoil != null)
            {
                int dmg = (int)scaledRecoil.Invoke(boss.Monster, null)!;
                Assert(dmg == 192, $"16p effective=8 Inkmaw SeverRecoilDamage must scale to 192 (20 * 8 * 1.2), got {dmg}");
            }
            else
            {
                Assert(MathF.Abs(MultiplayerCompat.GetExtendedPartyScaleRatio(16) - 2f) < 0.001f, "Scale ratio 2x");
            }
        }
        finally
        {
            SetEffectiveOverride(null);
        }
    }

    private static void VerifyBronzeCauldronProtocolAndMinigame()
    {
        var req = new ThingsCauldronGrabRequestMessage { Location = new RunLocation(0, null, 4), GrabX = 300f, GrabY = 300f };
        Assert(!req.ShouldBroadcast, "ThingsCauldronGrabRequestMessage.ShouldBroadcast must be false");

        byte[] testIndices = [0, 7, 55, 64, 99, 139];
        var ack = new ThingsCauldronGrabAckMessage
        {
            Location = new RunLocation(0, null, 4),
            GrabberNetId = 16UL,
            GrabX = 320f,
            GrabY = 360f,
            ClaimedIndices = testIndices,
            GrabsRemaining = 1,
        };
        var wa = new PacketWriter(); ack.Serialize(wa);
        var ra = new PacketReader(); ra.Reset(wa.Buffer);
        var ackDec = new ThingsCauldronGrabAckMessage(); ackDec.Deserialize(ra);
        Assert(ackDec.ClaimedIndices != null && ackDec.ClaimedIndices.SequenceEqual(testIndices),
            "ThingsCauldronGrabAckMessage must roundtrip item indices >= 64");
        Assert(ackDec.ClaimedBitmask == ((1UL << 0) | (1UL << 7) | (1UL << 55)),
            "ThingsCauldronGrabAckMessage ClaimedBitmask compatibility projection mismatch");

        var outcomes32 = new List<ThingsBronzeCauldronPlayerOutcome>(32);
        for (int i = 0; i < 32; i++)
            outcomes32.Add(new ThingsBronzeCauldronPlayerOutcome((ulong)(i + 1), 30 + i * 10, (i % 3) * 3, i % 2, i % 3, i % 4, i % 2, i % 2, i % 2, 3 + i));
        var finalMsg = ThingsCauldronFinalOutcomeMessage.FromOutcomes(new RunLocation(1, null, 20), outcomes32);
        var wf = new PacketWriter(); finalMsg.Serialize(wf);
        Assert(wf.BytePosition < 1024, $"32p ThingsCauldronFinalOutcomeMessage must fit under 1024 bytes (got {wf.BytePosition}B)");
        var rf = new PacketReader(); rf.Reset(wf.Buffer);
        var finalDec = new ThingsCauldronFinalOutcomeMessage(); finalDec.Deserialize(rf);
        List<ThingsBronzeCauldronPlayerOutcome> dec32 = finalDec.ToSanitizedOutcomes();
        Assert(dec32.Count == 32, $"32p FinalOutcome must decode all 32 players (got {dec32.Count})");
        Assert(dec32[31].PlayerNetId == 32UL && dec32[31].GoldAmount == 340 && dec32[31].ItemsGrabbed == 34,
            "Player 32 outcome must match exactly without 8-player truncation");

        RunState run4 = SetupRun("cauldron-4p", 4);
        var game4 = new BronzeCauldronMinigame(run4.Players, run4.Players[0].NetId, isHostAuthority: true, sharedSeed: 12345u);
        Assert(game4.Items.Count == 60, $"4p Cauldron must keep 60 items (got {game4.Items.Count})");
        Assert(game4.Hands[0].Y > BronzeCauldronMinigame.BoardSize && game4.Hands[1].Y < 0f, "4p Cauldron starting edges preserved");

        RunState run16 = SetupRun("cauldron-16p", 16);
        var game16 = new BronzeCauldronMinigame(run16.Players, run16.Players[0].NetId, isHostAuthority: true, sharedSeed: 12345u);
        Assert(game16.Items.Count > 64, $"16p Cauldron must scale beyond 64 items (got {game16.Items.Count})");

        RunState run32 = SetupRun("cauldron-32p", 32);
        var game32 = new BronzeCauldronMinigame(run32.Players, run32.Players[0].NetId, isHostAuthority: true, sharedSeed: 12345u);
        Assert(game32.Items.Count >= 100, $"32p Cauldron must scale to >= 100 items (got {game32.Items.Count})");
        int startCoords = game32.Hands.Select(h => $"{MathF.Round(h.X)},{MathF.Round(h.Y)}").Distinct().Count();
        Assert(startCoords == 32, $"32p Cauldron must have 32 distinct perimeter start positions (got {startCoords})");

        for (int step = 0; step < 100 && game32.Phase == CauldronPhase.Countdown; step++)
            game32.Advance(0.05f);
        Assert(game32.Phase == CauldronPhase.Scramble, $"Expected CauldronPhase.Scramble, got {game32.Phase}");
        CauldronTreasureItem highItem = game32.Items[75];
        byte[] claimed = game32.ResolveAuthoritativeGrabIndices(run32.Players[31].NetId, highItem.X, highItem.Y);
        Assert(claimed.Contains((byte)75) && highItem.IsClaimed && highItem.ClaimedByNetId == 32UL,
            "32p Cauldron must allow Player 32 to claim item index 75 (> 63)");
    }

    private static async Task VerifyIndividualMinigameRngIsolationAsync()
    {
        RunState run = SetupRun("plinko-rng-isolation", 16);
        Player localPlayer = run.Players[0];
        Player remotePlayer = run.Players[1];
        LocalContext.NetId = localPlayer.NetId;

        EventModel canonical = ModelDb.Event<ThingsForestPlinko>();
        run.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, canonical.Id);
        RunManager.Instance.EventSynchronizer.BeginEvent(canonical, false);

        var localEvent = (ThingsForestPlinko)RunManager.Instance.EventSynchronizer.GetEventForPlayer(localPlayer)!;
        var remoteEvent = (ThingsForestPlinko)RunManager.Instance.EventSynchronizer.GetEventForPlayer(remotePlayer)!;

        FieldInfo counterField = localEvent.Rng.GetType().GetField("_counter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        int beforeCounter = (int)counterField.GetValue(localEvent.Rng)!;
        int counterAfterLocalMinigame = -1;

        PropertyInfo driverProp = typeof(ThingsForestPlinko).GetProperty("MinigameTestDriver", BindingFlags.NonPublic | BindingFlags.Instance)!;
        driverProp.SetValue(localEvent, new Func<ForestPlinkoMinigame, Task>(g =>
        {
            g.TryDropSeed(3);
            for (int i = 0; i < 200 && g.ActiveBall.Active; i++)
                g.Advance(0.05f);
            counterAfterLocalMinigame = (int)counterField.GetValue(localEvent.Rng)!;
            return Task.CompletedTask;
        }));

        MethodInfo? dropMethod = typeof(ThingsForestPlinko).GetMethod("RunPlinkoTierAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(dropMethod != null, "Expected RunPlinkoTierAsync on ThingsForestPlinko");
        await (Task)dropMethod!.Invoke(localEvent, [0, 1])!;

        int remoteCounter = (int)counterField.GetValue(remoteEvent.Rng)!;
        Assert(counterAfterLocalMinigame == beforeCounter && beforeCounter == remoteCounter,
            $"ThingsForestPlinko local minigame must NOT mutate EventModel.Rng (before={beforeCounter}, afterMinigame={counterAfterLocalMinigame}, remote={remoteCounter})");
    }

    private static void VerifyMonumentalAndCaveGodLayouts()
    {
        PackedScene? creatureScene = ResourceLoader.Load<PackedScene>("res://scenes/combat/creature.tscn");
        if (creatureScene == null)
        {
            GD.Print("Skipping NCreature scene layout check (vanilla creature.tscn unavailable in headless probe)");
            return;
        }

        foreach (var (encounterModel, label, maxSpan, minY, maxY) in new (EncounterModel, string, float, float, float)[]
        {
            (ModelDb.Encounter<ThingsAbyssalInkmawBossEncounter>(), "Inkmaw", 860f, 146f, 202f),
            (ModelDb.Encounter<ThingsHumanFaceTowerBossEncounter>(), "Tower", 960f, 130f, 212f),
            (ModelDb.Encounter<ThingsCaveGodBossEncounter>(), "CaveGod", 880f, 126f, 202f),
        })
        {
            foreach (int partySize in new[] { 8, 16, 32 })
            {
                RunState run = SetupRun($"layout-{label}-{partySize}p", partySize);
                CombatState state = SetupCombat(run, encounterModel);
                var container = new Control();
                var nodes = new List<NCreature>(partySize);
                try
                {
                    for (int i = 0; i < partySize; i++)
                    {
                        NCreature node = creatureScene.Instantiate<NCreature>();
                        typeof(NCreature).GetProperty(nameof(NCreature.Entity))!.SetValue(node, state.Players[i].Creature);
                        var visuals = new NCreatureVisuals();
                        var bounds = new Control { Name = "Bounds", Size = new Vector2(180f, 220f) };
                        visuals.AddChild(bounds);
                        typeof(NCreatureVisuals).GetProperty(nameof(NCreatureVisuals.Bounds))!.SetValue(visuals, bounds);
                        typeof(NCreature).GetProperty(nameof(NCreature.Visuals))!.SetValue(node, visuals);
                        node.AddChild(visuals);
                        container.AddChild(node);
                        nodes.Add(node);
                    }

                    EncounterModel encounter = state.Encounter!;
                    NCombatRoom.PositionPlayersAndPets(nodes, encounter.GetCameraScaling(), encounter.FullyCenterPlayers);

                    float minX = nodes.Min(n => n.Position.X);
                    float maxX = nodes.Max(n => n.Position.X);
                    float spanX = maxX - minX;
                    float avgX = nodes.Average(n => n.Position.X);
                    float lowY = nodes.Min(n => n.Position.Y);
                    float highY = nodes.Max(n => n.Position.Y);

                    Assert(spanX <= maxSpan + 2f, $"[{label} {partySize}p] spanX={spanX:F1} exceeds {maxSpan}");
                    Assert(MathF.Abs(avgX) <= 60f, $"[{label} {partySize}p] party not centered (avgX={avgX:F1})");
                    Assert(lowY >= minY && highY <= maxY, $"[{label} {partySize}p] Y range [{lowY:F1}..{highY:F1}] outside [{minY}..{maxY}]");
                    Assert(nodes[0].Position.Y >= highY - 10f, $"[{label} {partySize}p] Local player must be in front row");

                    int distinctRows = nodes.Select(n => MathF.Round(n.Position.Y)).Distinct().Count();
                    Assert(distinctRows >= 2, $"[{label} {partySize}p] expected multi-row layout, got {distinctRows} row(s)");
                }
                finally
                {
                    container.Free();
                }
            }
        }
    }

    private static void VerifyCaveGodClusterOffsets()
    {
        MethodInfo getCluster = typeof(NCaveGodBossBackground).GetMethod(
            "GetClusterOffset", BindingFlags.Static | BindingFlags.NonPublic)!;
        var seen = new HashSet<string>();
        for (int i = 0; i < 32; i++)
        {
            Vector2 off = (Vector2)getCluster.Invoke(null, [i, 32])!;
            Assert(MathF.Abs(off.X) <= 56f && MathF.Abs(off.Y) <= 38f,
                $"CaveGod 32p cluster offset #{i} out of fist bounds: {off}");
            seen.Add($"{MathF.Round(off.X)},{MathF.Round(off.Y)}");
        }
        Assert(seen.Count >= 28, $"CaveGod 32p cluster must distribute captives instead of stacking on (22,10) (unique={seen.Count})");
    }

    private static void VerifyDepthsRestSiteExtendedSeating()
    {
        PackedScene? depthsRestScene = ResourceLoader.Load<PackedScene>("res://scenes/rest_site/things_depths_rest_site.tscn");
        if (depthsRestScene == null)
        {
            GD.Print("Skipping Depths rest site scene check (mod PCK not loaded)");
            return;
        }

        foreach (bool stackedCaseB in new[] { false, true })
        {
            var room = new NRestSiteRoom();
            var bgContainer = new Control { Name = "BgContainer" };
            room.AddChild(bgContainer);

            Control depthsBg = depthsRestScene.Instantiate<Control>();
            bgContainer.AddChild(depthsBg);

            Vector2[] vanillaPositions = [new(625, 642), new(1239, 649), new(750, 585), new(1092, 583)];
            var containers = new List<Control>();
            for (int i = 0; i < 4; i++)
            {
                var c = new Control { Name = $"Character_{i + 1}", Position = vanillaPositions[i] };
                bgContainer.AddChild(c);
                containers.Add(c);
            }

            const int partySize = 16;
            for (int i = 0; i < partySize; i++)
            {
                var ch = new NRestSiteCharacter { Name = $"RestChar_{i}" };
                if (i < 4)
                {
                    containers[i].AddChild(ch);
                }
                else if (stackedCaseB)
                {
                    containers[3].AddChild(ch);
                }
                else
                {
                    var extra = new Control
                    {
                        Name = $"Character_Auto_{i + 1}",
                        Position = new Vector2((i % 2 == 0 ? 375f : 1489f) + (i / 2 - 2) * (i % 2 == 0 ? -70f : 70f), 640f - (i / 2 - 2) * 45f)
                    };
                    bgContainer.AddChild(extra);
                    extra.AddChild(ch);
                }
                room.Characters.Add(ch);
            }

            DepthsRestSiteExtendedPartyPatch.ApplyExtendedDepthsSeating(room);

            Assert(depthsBg.GetNodeOrNull("ExtendedDepthsSeatsApplied") != null,
                $"DepthsRestSiteExtendedPartyPatch marker missing (stacked={stackedCaseB})");

            Node? backSeats = depthsBg.GetNodeOrNull("ExtendedDepthsSeatsBack");
            Node? frontSeats = depthsBg.GetNodeOrNull("ExtendedDepthsSeatsFront");
            int clonedStools = (backSeats?.GetChildCount() ?? 0) + (frontSeats?.GetChildCount() ?? 0);
            Assert(clonedStools >= (partySize - 4),
                $"Expected cloned stone stools for 12 extra players (stacked={stackedCaseB}, got nodes={clonedStools})");

            Control lighting = depthsBg.GetNode<Control>("RestSiteLighting");
            if (backSeats != null)
                Assert(backSeats.GetIndex() < lighting.GetIndex(), "Back extended stools must render behind RestSiteLighting");
            if (frontSeats != null)
                Assert(frontSeats.GetIndex() < lighting.GetIndex(), "Front extended stools must render behind RestSiteLighting");

            if (stackedCaseB)
            {
                int distinctPos = room.Characters.Skip(4)
                    .Select(c => $"{MathF.Round(c.Position.X)},{MathF.Round(c.Position.Y)}")
                    .Distinct().Count();
                Assert(distinctPos == partySize - 4,
                    $"Case B (Sts2Unlimited stacked) must spread all {partySize - 4} extra characters (distinct={distinctPos})");
            }

            room.Free();
        }
    }

    private static void VerifyBugReportExtendedPartySafety()
    {
        RunState run16 = SetupRun("bug-snapshot-16p", 16);
        LocalContext.NetId = run16.Players[14].NetId;

        Assembly modAsm = typeof(ThingsQuirkPower).Assembly;
        Type bugSnapshotType = modAsm.GetType("STS2_Things.Diagnostics.BugSnapshot", throwOnError: true)!;
        MethodInfo captureMethod = bugSnapshotType.GetMethod(
            "CaptureInternal", BindingFlags.Static | BindingFlags.NonPublic)!;
        JsonObject snapshot = (JsonObject)captureMethod.Invoke(null, [run16, null, true])!;

        int emittedLocalIdx = int.Parse(snapshot["local_player_index"]!.ToJsonString());
        int totalPlayers = int.Parse(snapshot["total_player_count"]!.ToJsonString());
        bool truncated = snapshot["players_truncated"]!.GetValue<bool>();
        JsonArray playersArr = snapshot["players"]!.AsArray();

        Assert(totalPlayers == 16, $"BugSnapshot total_player_count must be 16 (got {totalPlayers})");
        Assert(truncated, "BugSnapshot players_truncated must be true for 16p");
        Assert(playersArr.Count == 8, $"BugSnapshot must stay <= 8 wire slice for un-redeployed Worker safety (got {playersArr.Count})");
        Assert(emittedLocalIdx >= 0 && emittedLocalIdx < playersArr.Count,
            $"local_player_index ({emittedLocalIdx}) must be in-bounds of emitted players ({playersArr.Count})");
        Assert(int.Parse(playersArr[emittedLocalIdx]!["slot_index"]!.ToJsonString()) == 14,
            "Emitted local player slot_index must be 14");

        var fakePlayers = new JsonArray();
        for (int i = 0; i < 32; i++)
            fakePlayers.Add(new JsonObject { ["character"] = "CHARACTER.IRONCLAD" });
        var fakeState = new JsonObject
        {
            ["seed"] = "TEST",
            ["players"] = fakePlayers,
        };
        Type schemaType = modAsm.GetType("STS2_Things.Diagnostics.BugReportSchemaV4", throwOnError: true)!;
        MethodInfo validSnapshot = schemaType.GetMethod("ValidSnapshot", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert((bool)validSnapshot.Invoke(null, [fakeState])!, "BugReportSchemaV4 must accept 32 players");
    }
}
