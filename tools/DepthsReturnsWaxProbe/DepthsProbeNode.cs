using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using STS2_Things.Acts;
using STS2_Things.Events;
using STS2_Things.Relics;

public partial class DepthsProbeNode : Node
{
    private static int _checks;
    private string _root = null!;
    private bool _hasVanillaPck;

    private static void Assert(bool ok, string message)
    {
        if (!ok)
            throw new InvalidOperationException(message);
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
            _root = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "../.."));
            TestMode.TurnOnInternal();

            string? vanillaPck = System.Environment.GetEnvironmentVariable("THINGS_VANILLA_PCK");
            if (!string.IsNullOrWhiteSpace(vanillaPck) && File.Exists(vanillaPck))
            {
                Assert(ProjectSettings.LoadResourcePack(vanillaPck), "Load vanilla game PCK");
                _hasVanillaPck = true;
            }

            string? modPck = System.Environment.GetEnvironmentVariable("THINGS_PROBE_PCK");
            if (!string.IsNullOrWhiteSpace(modPck) && File.Exists(modPck))
            {
                Assert(ProjectSettings.LoadResourcePack(modPck), "Load STS2_Things PCK");
            }

            ModHelper.AddModelToPool<EventRelicPool, ThingsBottledEcho>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsShadowClaimTicket>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsMycelialDeposit>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsBorrowedEmber>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsMedusaHair>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsReturnReceipt>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsNarrowViewfinder>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsHotWaxParcel>();
            ModHelper.AddModelToPool<EventRelicPool, ThingsChilledWaxParcel>();

            STS2_Things.Platform.Sts2VersionSeam.InitializeBeforeModelDatabase();
            InitializeModelDb();
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly);
            ScriptManagerBridge.LookupScriptsInAssembly(typeof(ThingsReturnsCounter).Assembly);

            modHarmony = new Harmony("DepthsReturnsWaxProbe.ModPatches");
            modHarmony.PatchAll(typeof(ThingsReturnsCounter).Assembly);

            SaveManager.Instance.InitSettingsDataForTest();
            SaveManager.Instance.InitPrefsDataForTest();
            SaveManager.Instance.SettingsSave.Language = "zhs";
            LocManager.Initialize();
            NHoverTipSet.shouldBlockHoverTips = true;

            VerifyCatalogAndResources();
            VerifyLocalizationInBothLanguages();
            await VerifyReturnsCounterAsync();
            await VerifyReturnReceiptAndNarrowViewfinderAsync();
            await VerifyHotWaxBoxAndParcelsAsync();

            if (_hasVanillaPck)
            {
                await RenderEventsAndRelicsAsync();
            }

            string? output = System.Environment.GetEnvironmentVariable("THINGS_PROBE_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
                {
                    assertions = _checks,
                    target =
#if STS2_V111_1
                        "v111.1",
#else
                        "v113.1",
#endif
                    vanillaPckLoaded = _hasVanillaPck,
                    events = new[] { "THINGS_RETURNS_COUNTER", "THINGS_HOT_WAX_BOX" },
                    relics = new[]
                    {
                        "THINGS_RETURN_RECEIPT",
                        "THINGS_NARROW_VIEWFINDER",
                        "THINGS_HOT_WAX_PARCEL",
                        "THINGS_CHILLED_WAX_PARCEL",
                    },
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            NHoverTipSet.shouldBlockHoverTips = originalHoverBlock;
            modHarmony.UnpatchAll(modHarmony.Id);
            ClearRunState();
            GD.Print($"Depths returns & wax probe: PASS ({_checks} assertions)");
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
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(RunManager.Instance, null);
    }

    private static RunState SetupTestRun(string seed, int playerCount = 1, bool populateGrabBags = true)
    {
        ClearRunState();
        List<Player> players = [];
        CharacterModel[] roster =
        [
            ModelDb.Character<Ironclad>(),
            ModelDb.Character<Silent>(),
            ModelDb.Character<Defect>(),
        ];
        for (int i = 0; i < playerCount; i++)
        {
            Player p = Player.CreateForNewRun(roster[i % roster.Length], UnlockState.all, (ulong)(i + 1));
            players.Add(p);
        }

        List<ActModel> acts = ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList();
        RunState run = ProbeRuntimeApi.CreateForNewRun(players, acts, [], GameMode.Standard, 0, seed);
        RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), shouldSave: false);
        if (populateGrabBags)
        {
            foreach (Player p in run.Players)
                p.PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);
        }
        LocalContext.NetId = run.Players[0].NetId;
        return run;
    }

    private static TEvent BeginEventForPlayer<TEvent>(Player player, RunState run) where TEvent : EventModel
    {
        LocalContext.NetId = player.NetId;
        EventModel canonical = ModelDb.Event<TEvent>();
        RunManager.Instance.EventSynchronizer.BeginEvent(canonical, false);
        EventModel? local = RunManager.Instance.EventSynchronizer.GetEventForPlayer(player);
        Assert(local is TEvent, $"BeginEvent created mutable instance for {typeof(TEvent).Name}");
        return (TEvent)local!;
    }

    private static RelicModel ObtainRelicSync(RelicModel relic, Player player)
    {
        RelicModel mutable = relic.IsMutable ? relic : relic.ToMutable();
        RelicCmd.Obtain(mutable, player).GetAwaiter().GetResult();
        return mutable;
    }

    private static SerializableRelic RoundTripRelic(RelicModel relic)
    {
        var writer = new PacketWriter { WarnOnGrow = false };
        relic.ToSerializable().Serialize(writer);
        var reader = new PacketReader();
        reader.Reset(writer.Buffer);
        var serial = new SerializableRelic();
        serial.Deserialize(reader);
        return serial;
    }

    private static string FormatEventLoc(EventModel model, string fullKey)
    {
        var s = new LocString("events", fullKey);
        model.DynamicVars.AddTo(s);
        return s.GetFormattedText();
    }

    private void VerifyCatalogAndResources()
    {
        ThingsDepths depths = ModelDb.Act<ThingsDepths>();
        List<EventModel> depthsEvents = depths.AllEvents.ToList();
        Assert(depthsEvents.Contains(ModelDb.Event<ThingsReturnsCounter>()),
            "ThingsDepths.AllEvents includes ThingsReturnsCounter");
        Assert(depthsEvents.Contains(ModelDb.Event<ThingsHotWaxBox>()),
            "ThingsDepths.AllEvents includes ThingsHotWaxBox");

        RelicModel[] newRelics =
        [
            ModelDb.Relic<ThingsReturnReceipt>(),
            ModelDb.Relic<ThingsNarrowViewfinder>(),
            ModelDb.Relic<ThingsHotWaxParcel>(),
            ModelDb.Relic<ThingsChilledWaxParcel>(),
        ];
        List<RelicModel> eventPoolRelics = ModelDb.RelicPool<EventRelicPool>().AllRelics.ToList();
        foreach (RelicModel r in newRelics)
        {
            Assert(r.Rarity == RelicRarity.Event, $"{r.Id.Entry} has Event rarity");
            Assert(!r.IsTradable, $"{r.Id.Entry} has IsTradable=false");
            Assert(eventPoolRelics.Contains(r), $"{r.Id.Entry} registered in EventRelicPool");
        }

        if (_hasVanillaPck)
        {
            foreach (string resPath in new[]
            {
                "res://images/events/things_returns_counter.png",
                "res://images/events/things_hot_wax_box.png",
            })
            {
                Texture2D? tex = ResourceLoader.Load<Texture2D>(resPath);
                Assert(tex != null && tex.GetSize() == new Vector2I(3440, 1616),
                    $"Event art loads from PCK at 3440x1616: {resPath}");
            }
        }
    }

    private void VerifyLocalizationInBothLanguages()
    {
        if (!_hasVanillaPck)
            return;

        string[] languages = ["zhs", "eng"];
        foreach (string lang in languages)
        {
            SaveManager.Instance.SettingsSave.Language = lang;
            LocManager.Initialize();

            RunState run = SetupTestRun($"loc-{lang}");
            Player p = run.Players[0];

            var counter = BeginEventForPlayer<ThingsReturnsCounter>(p, run);
            AssertTextClean(counter.Title.GetFormattedText(), $"ReturnsCounter.Title ({lang})");
            AssertTextClean(counter.Description!.GetFormattedText(), $"ReturnsCounter.Init ({lang})");
            foreach (EventOption opt in counter.CurrentOptions)
            {
                counter.DynamicVars.AddTo(opt.Title);
                counter.DynamicVars.AddTo(opt.Description);
                AssertTextClean(opt.Title.GetFormattedText(), $"RC opt title {opt.Title.LocEntryKey} ({lang})");
                AssertTextClean(opt.Description.GetFormattedText(), $"RC opt desc {opt.Description.LocEntryKey} ({lang})");
            }
            foreach (string optKey in new[] { "RECEIPT_OWNED", "RECEIPT_LOW_HP", "VIEWFINDER_OWNED", "RUMMAGE_LOCKED" })
            {
                AssertTextClean(FormatEventLoc(counter, $"THINGS_RETURNS_COUNTER.pages.INITIAL.options.{optKey}.title"), $"RC {optKey}.title ({lang})");
                AssertTextClean(FormatEventLoc(counter, $"THINGS_RETURNS_COUNTER.pages.INITIAL.options.{optKey}.description"), $"RC {optKey}.desc ({lang})");
            }
            foreach (string pageKey in new[] { "RECEIPT.description", "RECEIPT.loss", "VIEWFINDER.description", "VIEWFINDER.loss", "RUMMAGE.description", "LEAVE.description" })
            {
                AssertTextClean(FormatEventLoc(counter, $"THINGS_RETURNS_COUNTER.pages.{pageKey}"), $"RC page {pageKey} ({lang})");
            }

            var wax = BeginEventForPlayer<ThingsHotWaxBox>(p, run);
            AssertTextClean(wax.Title.GetFormattedText(), $"HotWaxBox.Title ({lang})");
            AssertTextClean(wax.Description!.GetFormattedText(), $"HotWaxBox.Init ({lang})");
            foreach (string optKey in new[] { "ACCEPT_URGENT", "URGENT_OWNED", "URGENT_BOSS_NEAR", "RETAG_CHILLED", "CHILLED_OWNED", "CHILLED_BOSS_NEAR", "CHILLED_NO_GOLD", "PRY_OPEN", "LEAVE" })
            {
                AssertTextClean(FormatEventLoc(wax, $"THINGS_HOT_WAX_BOX.pages.INITIAL.options.{optKey}.title"), $"HWB {optKey}.title ({lang})");
                AssertTextClean(FormatEventLoc(wax, $"THINGS_HOT_WAX_BOX.pages.INITIAL.options.{optKey}.description"), $"HWB {optKey}.desc ({lang})");
            }
            foreach (string pageKey in new[] { "ACCEPT_URGENT.description", "RETAG_CHILLED.description", "PRY_OPEN.description", "PRY_OPEN.loss", "LEAVE.description" })
            {
                AssertTextClean(FormatEventLoc(wax, $"THINGS_HOT_WAX_BOX.pages.{pageKey}"), $"HWB page {pageKey} ({lang})");
            }

            foreach (RelicModel r in new RelicModel[]
            {
                ModelDb.Relic<ThingsReturnReceipt>().ToMutable(),
                ModelDb.Relic<ThingsNarrowViewfinder>().ToMutable(),
                ModelDb.Relic<ThingsHotWaxParcel>().ToMutable(),
                ModelDb.Relic<ThingsChilledWaxParcel>().ToMutable(),
            })
            {
                AssertTextClean(r.Title.GetFormattedText(), $"{r.Id.Entry}.Title ({lang})");
                AssertTextClean(r.DynamicDescription.GetFormattedText(), $"{r.Id.Entry}.Description ({lang})");
                AssertTextClean(r.Flavor.GetFormattedText(), $"{r.Id.Entry}.Flavor ({lang})");
            }

            string returnLabel = new LocString("card_reward_ui", "OPTION_THINGS_RETURN.name").GetFormattedText();
            Assert(returnLabel == (lang == "zhs" ? "退货" : "Return"),
                $"OPTION_THINGS_RETURN.name expected {(lang == "zhs" ? "退货" : "Return")}, got '{returnLabel}'");
        }

        SaveManager.Instance.SettingsSave.Language = "zhs";
        LocManager.Initialize();
    }

    private static void AssertTextClean(string text, string label)
    {
        Assert(!string.IsNullOrWhiteSpace(text), $"Empty loc text: {label}");
        Assert(!Regex.IsMatch(text, @"\{[A-Za-z0-9_:.]+\}"),
            $"Unresolved placeholder in {label}: {text}");
    }

    private async Task VerifyReturnsCounterAsync()
    {
        // Case 1: Take Receipt (MaxHp loss + relic)
        RunState run1 = SetupTestRun("rc-receipt");
        Player p1 = run1.Players[0];
        int startMaxHp = p1.Creature.MaxHp;
        var ev1 = BeginEventForPlayer<ThingsReturnsCounter>(p1, run1);
        Assert(ev1.CurrentOptions.Count == 4, "ReturnsCounter exposes 4 initial options");
        Assert(!ev1.CurrentOptions[0].IsLocked && !ev1.CurrentOptions[1].IsLocked, "Receipt and Viewfinder unlocked initially");
        EventOption cachedLeave = ev1.CurrentOptions[3];
        await ev1.CurrentOptions[0].Chosen();
        Assert(ev1.IsFinished, "Receipt option finishes event");
        Assert(p1.Creature.MaxHp == startMaxHp - ThingsReturnsCounter.ReceiptMaxHpLoss,
            $"MaxHp decreased by {ThingsReturnsCounter.ReceiptMaxHpLoss}");
        Assert(p1.GetRelic<ThingsReturnReceipt>() != null, "Obtained ThingsReturnReceipt");

        // Repeat invocation guard on stale option after completion
        await cachedLeave.Chosen();
        Assert(p1.GetRelic<ThingsNarrowViewfinder>() == null, "Finished event ignores subsequent callback");

        // Case 2: Locked receipt (owned) & locked receipt (low MaxHp <= 6)
        p1.Gold = 100;
        var ev1Owned = BeginEventForPlayer<ThingsReturnsCounter>(p1, run1);
        Assert(ev1Owned.CurrentOptions[0].IsLocked && ev1Owned.CurrentOptions[0].Title.LocEntryKey.Contains("RECEIPT_OWNED"),
            "Owning ReturnReceipt locks option 0");

        RunState runLow = SetupTestRun("rc-lowhp");
        Player pLow = runLow.Players[0];
        pLow.Creature.SetMaxHpInternal(6);
        var evLow = BeginEventForPlayer<ThingsReturnsCounter>(pLow, runLow);
        Assert(evLow.CurrentOptions[0].IsLocked && evLow.CurrentOptions[0].Title.LocEntryKey.Contains("RECEIPT_LOW_HP"),
            "MaxHp <= 6 locks ReturnReceipt option 0");

        // Case 3: Take Viewfinder (damage + relic), plus lethal Viewfinder (.loss, no relic)
        RunState runVf = SetupTestRun("rc-viewfinder");
        Player pVf = runVf.Players[0];
        int hpBeforeVf = pVf.Creature.CurrentHp;
        var evVf = BeginEventForPlayer<ThingsReturnsCounter>(pVf, runVf);
        await evVf.CurrentOptions[1].Chosen();
        Assert(pVf.Creature.CurrentHp == hpBeforeVf - ThingsReturnsCounter.ViewfinderDamage, "Viewfinder deals 10 dmg");
        Assert(pVf.GetRelic<ThingsNarrowViewfinder>() != null, "Obtained ThingsNarrowViewfinder");

        var evVfOwned = BeginEventForPlayer<ThingsReturnsCounter>(pVf, runVf);
        Assert(evVfOwned.CurrentOptions[1].IsLocked && evVfOwned.CurrentOptions[1].Title.LocEntryKey.Contains("VIEWFINDER_OWNED"),
            "Owning NarrowViewfinder locks option 1");

        RunState runLethal = SetupTestRun("rc-viewfinder-lethal");
        Player pLethal = runLethal.Players[0];
        pLethal.Creature.SetCurrentHpInternal(8);
        var evLethal = BeginEventForPlayer<ThingsReturnsCounter>(pLethal, runLethal);
        await evLethal.CurrentOptions[1].Chosen();
        Assert(pLethal.Creature.IsDead, "10 dmg kills 8 HP owner");
        Assert(pLethal.GetRelic<ThingsNarrowViewfinder>() == null, "Lethal Viewfinder does not grant relic");
        Assert(evLethal.IsFinished, "Lethal Viewfinder finishes with loss page");

        // Case 4: Rummage locked under 40G, Skip preserves gold & candidates, Choose deducts 40G and adds Upgraded+Enchanted card
        RunState runRum = SetupTestRun("rc-rummage");
        Player pRum = runRum.Players[0];
        pRum.Gold = 39;
        var evRumLocked = BeginEventForPlayer<ThingsReturnsCounter>(pRum, runRum);
        Assert(evRumLocked.CurrentOptions[2].IsLocked && evRumLocked.CurrentOptions[2].Title.LocEntryKey.Contains("RUMMAGE_LOCKED"),
            "Gold < 40 locks Rummage");

        pRum.Gold = 99;
        var evRum = BeginEventForPlayer<ThingsReturnsCounter>(pRum, runRum);
        Assert(!evRum.CurrentOptions[2].IsLocked, "Gold >= 40 unlocks Rummage");
        int deckBefore = pRum.Deck.Cards.Count;

        var skipSelector = new ProbeCardSelector(pickIndex: -1);
        using (ProbeRuntimeApi.UseSelector(skipSelector))
        {
            await evRum.CurrentOptions[2].Chosen();
        }
        Assert(!evRum.IsFinished, "Skipping Rummage returns to initial choices without finishing event");
        Assert(pRum.Gold == 99, "Skipping Rummage does not spend Gold");
        Assert(pRum.Deck.Cards.Count == deckBefore, "Skipping Rummage adds no card");
        Assert(skipSelector.LastOffered.Count == ThingsReturnsCounter.RummageChoiceCount,
            $"Rummage shows {ThingsReturnsCounter.RummageChoiceCount} candidates");
        List<string> firstOfferedIds = skipSelector.LastOffered.Select(c => c.Id.Entry).ToList();
        foreach (CardModel c in skipSelector.LastOffered)
        {
            Assert(!c.IsUpgradable || c.IsUpgraded, $"Rummage candidate {c.Id.Entry} is upgraded");
            Assert(c.Enchantment != null, $"Rummage candidate {c.Id.Entry} has an enchantment");
        }

        var pickSelector = new ProbeCardSelector(pickIndex: 1);
        using (ProbeRuntimeApi.UseSelector(pickSelector))
        {
            await evRum.CurrentOptions[2].Chosen();
        }
        List<string> secondOfferedIds = pickSelector.LastOffered.Select(c => c.Id.Entry).ToList();
        Assert(firstOfferedIds.SequenceEqual(secondOfferedIds),
            "Rummage does not reroll candidates after skipping");
        Assert(evRum.IsFinished, "Picking a Rummage card finishes event");
        Assert(pRum.Gold == 59, "Picking a Rummage card spends 40 Gold");
        Assert(pRum.Deck.Cards.Count == deckBefore + 1, "Picked Rummage card added to Deck");
        CardModel pickedInDeck = pickSelector.LastPicked!;
        Assert(pRum.Deck.Cards.Contains(pickedInDeck) && pickedInDeck.Enchantment != null,
            "Added Rummage card retains enchantment in Deck");

        // Case 5: Leave + Co-op player isolation
        RunState coop = SetupTestRun("rc-coop", playerCount: 2);
        Player owner = coop.Players[0];
        Player peer = coop.Players[1];
        ObtainRelicSync(ModelDb.Relic<ThingsReturnReceipt>(), owner);
        peer.Gold = 80;
        var evOwner = BeginEventForPlayer<ThingsReturnsCounter>(owner, coop);
        var evPeer = BeginEventForPlayer<ThingsReturnsCounter>(peer, coop);
        Assert(evOwner.CurrentOptions[0].IsLocked, "Co-op owner sees RECEIPT_OWNED");
        Assert(!evPeer.CurrentOptions[0].IsLocked, "Co-op peer without receipt sees RECEIPT unlocked");
        int peerGoldBefore = peer.Gold;
        await evPeer.CurrentOptions[3].Chosen();
        Assert(evPeer.IsFinished && peer.Gold == peerGoldBefore, "Leave finishes cleanly with zero resource change");
    }

    private async Task VerifyReturnReceiptAndNarrowViewfinderAsync()
    {
        RunState run = SetupTestRun("relics-rc-vf", playerCount: 2);
        Player owner = run.Players[0];
        Player peer = run.Players[1];

        var receipt = (ThingsReturnReceipt)ObtainRelicSync(ModelDb.Relic<ThingsReturnReceipt>(), owner);
        owner.Gold = 50;
        owner.Creature.SetCurrentHpInternal(40);

        List<CardRewardAlternative> alts = [];
        Assert(!receipt.TryModifyCardRewardAlternatives(peer, null!, alts), "ReturnReceipt ignores non-owner");
        Assert(receipt.TryModifyCardRewardAlternatives(owner, null!, alts) && alts.Count == 1,
            "ReturnReceipt adds THINGS_RETURN alternative");
        Assert(alts[0].OptionId == ThingsReturnReceipt.ReturnOptionId
            && alts[0].AfterSelected == PostAlternateCardRewardAction.EndSelectionAndCompleteReward,
            "ReturnReceipt alternative config matches contract");
        Assert(!receipt.TryModifyCardRewardAlternatives(owner, null!, alts) && alts.Count == 1,
            "ReturnReceipt prevents duplicate alternative");

        List<CardRewardAlternative> fullAlts =
        [
            new("A", () => Task.CompletedTask, PostAlternateCardRewardAction.None),
            new("B", () => Task.CompletedTask, PostAlternateCardRewardAction.None),
        ];
        Assert(!receipt.TryModifyCardRewardAlternatives(owner, null!, fullAlts),
            "ReturnReceipt respects 2-alternative cap");

        await alts[0].OnSelect();
        Assert(owner.Gold == 50 + ThingsReturnReceipt.RewardGold, "ReturnReceipt grants 20 Gold");
        Assert(owner.Creature.CurrentHp == 40 + ThingsReturnReceipt.RewardHeal, "ReturnReceipt heals 3 HP");
        Assert(peer.Gold == run.Players[1].Gold, "Co-op peer untouched");

        owner.Creature.SetCurrentHpInternal(owner.Creature.MaxHp - 1);
        await alts[0].OnSelect();
        Assert(owner.Creature.CurrentHp == owner.Creature.MaxHp, "ReturnReceipt heal caps at MaxHp");

        ObtainRelicSync(ModelDb.Relic<PaelsWing>(), owner);
        List<CardRewardAlternative> wingAlts = [];
        Assert(!receipt.TryModifyCardRewardAlternatives(owner, null!, wingAlts),
            "ReturnReceipt backs off when PaelsWing is owned");

        // NarrowViewfinder: Choice count reduction, Upgrade enforcement, Non-reward guard, Rare odds doubling
        ObtainRelicSync(ModelDb.Relic<ThingsNarrowViewfinder>(), owner);
        CardCreationOptions rewardOpts = CardCreationOptions.ForRoom(owner, RoomType.Monster)
            .WithFlags(CardCreationFlags.IsCardReward);
        List<CardCreationResult> ownerCards = CardFactory.CreateForReward(owner, 3, rewardOpts).ToList();
        Assert(ownerCards.Count == 2, $"NarrowViewfinder trims reward choices 3 -> 2 (got {ownerCards.Count})");
        Assert(ownerCards.All(r => !r.Card.IsUpgradable || r.Card.IsUpgraded),
            "NarrowViewfinder upgrades all upgradable card reward choices");

        CardReward nativeCardReward = new(CardCreationOptions.ForRoom(owner, RoomType.Monster), 3, owner);
        nativeCardReward.Populate();
        List<CardModel> nativeRewardCards = nativeCardReward.Cards.ToList();
        Assert(nativeRewardCards.Count == 2, $"Native CardReward trims 3 -> 2 (got {nativeRewardCards.Count})");
        Assert(nativeRewardCards.All(c => !c.IsUpgradable || c.IsUpgraded),
            "Native CardReward upgrades all upgradable choices");

        List<CardCreationResult> peerCards = CardFactory.CreateForReward(peer, 3,
            CardCreationOptions.ForRoom(peer, RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward)).ToList();
        Assert(peerCards.Count == 3, "Co-op peer still receives 3 card choices");

        List<CardCreationResult> noModCards = CardFactory.CreateForReward(owner, 3,
            CardCreationOptions.ForNonCombatWithDefaultOdds([owner.Character.CardPool])
                .WithFlags(CardCreationFlags.NoCardModelModifications)).ToList();
        Assert(noModCards.Count == 3, "NarrowViewfinder ignores non-IsCardReward / NoCardModelModifications");

        // Statistical check 1 (Fixed base odds, no pity-reset feedback): ForNonCombatWithDefaultOdds (0.03 -> 0.06 = ~2.0x)
        int totalWith = 0;
        int rareWith = 0;
        int totalWithout = 0;
        int rareWithout = 0;
        int totalNoRarity = 0;
        int rareNoRarity = 0;
        int totalUniformWith = 0;
        int rareUniformWith = 0;
        int totalUniformWithout = 0;
        int rareUniformWithout = 0;
        for (int i = 0; i < 1200; i++)
        {
            CardCreationOptions optWith = CardCreationOptions.ForNonCombatWithDefaultOdds([owner.Character.CardPool])
                .WithFlags(CardCreationFlags.IsCardReward);
            foreach (CardCreationResult r in CardFactory.CreateForReward(owner, 3, optWith))
            {
                totalWith++;
                if (r.Card.Rarity == CardRarity.Rare)
                    rareWith++;
            }

            CardCreationOptions optWithout = CardCreationOptions.ForNonCombatWithDefaultOdds([peer.Character.CardPool])
                .WithFlags(CardCreationFlags.IsCardReward);
            foreach (CardCreationResult r in CardFactory.CreateForReward(peer, 3, optWithout))
            {
                totalWithout++;
                if (r.Card.Rarity == CardRarity.Rare)
                    rareWithout++;
            }

            CardCreationOptions optNoRarity = CardCreationOptions.ForNonCombatWithDefaultOdds([owner.Character.CardPool])
                .WithFlags(CardCreationFlags.IsCardReward | CardCreationFlags.NoRarityModification);
            foreach (CardCreationResult r in CardFactory.CreateForReward(owner, 3, optNoRarity))
            {
                totalNoRarity++;
                if (r.Card.Rarity == CardRarity.Rare)
                    rareNoRarity++;
            }
        }
        for (int i = 0; i < 300; i++)
        {
            CardCreationOptions optUniWith = new CardCreationOptions([owner.Character.CardPool], CardCreationSource.Other, CardRarityOddsType.Uniform)
                .WithFlags(CardCreationFlags.IsCardReward);
            foreach (CardCreationResult r in CardFactory.CreateForReward(owner, 3, optUniWith))
            {
                totalUniformWith++;
                if (r.Card.Rarity == CardRarity.Rare)
                    rareUniformWith++;
            }

            CardCreationOptions optUniWithout = new CardCreationOptions([peer.Character.CardPool], CardCreationSource.Other, CardRarityOddsType.Uniform)
                .WithFlags(CardCreationFlags.IsCardReward);
            foreach (CardCreationResult r in CardFactory.CreateForReward(peer, 3, optUniWithout))
            {
                totalUniformWithout++;
                if (r.Card.Rarity == CardRarity.Rare)
                    rareUniformWithout++;
            }
        }
        double rateWith = (double)rareWith / totalWith;
        double rateWithout = (double)rareWithout / totalWithout;
        double rateNoRarity = (double)rareNoRarity / totalNoRarity;
        double ratio = rateWithout > 0 ? rateWith / rateWithout : 0;
        double noRarityRatio = rateWithout > 0 ? rateNoRarity / rateWithout : 0;
        double rateUniWith = (double)rareUniformWith / totalUniformWith;
        double rateUniWithout = (double)rareUniformWithout / totalUniformWithout;
        double uniformRatio = rateUniWithout > 0 ? rateUniWith / rateUniWithout : 0;
        GD.Print($"NarrowViewfinder base-odds sample: without={rareWithout}/{totalWithout} ({rateWithout:P2}), with={rareWith}/{totalWith} ({rateWith:P2}, {ratio:F2}x), noRarityMod={rareNoRarity}/{totalNoRarity} ({rateNoRarity:P2}, {noRarityRatio:F2}x), uniform={rateUniWith:P2}/{rateUniWithout:P2} ({uniformRatio:F2}x)");
        Assert(rareWithout > 20 && ratio is >= 1.55 and <= 2.55,
            $"Expected doubled Rare odds (~2.0x), observed {ratio:F2}x ({rareWithout}/{totalWithout} vs {rareWith}/{totalWith})");
        Assert(noRarityRatio < 1.40,
            $"NoRarityModification must not double Rare odds (observed {noRarityRatio:F2}x: {rareNoRarity}/{totalNoRarity})");
        Assert(uniformRatio is >= 0.8 and <= 1.25,
            $"Uniform rarity odds must remain un-doubled (observed {uniformRatio:F2}x: {rateUniWith:P2} vs {rateUniWithout:P2})");

        // Statistical check 2 (Isolated combat progression with pity offset): RoomType.Monster & RoomType.Elite
        int combatWith = 0;
        int combatRareWith = 0;
        int combatWithout = 0;
        int combatRareWithout = 0;
        for (int i = 0; i < 800; i++)
        {
            CardCreationOptions mWith = CardCreationOptions.ForRoom(owner, RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward);
            foreach (CardCreationResult r in CardFactory.CreateForReward(owner, 3, mWith))
            {
                combatWith++;
                if (r.Card.Rarity == CardRarity.Rare)
                    combatRareWith++;
            }

            CardCreationOptions mWithout = CardCreationOptions.ForRoom(peer, RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward);
            foreach (CardCreationResult r in CardFactory.CreateForReward(peer, 3, mWithout))
            {
                combatWithout++;
                if (r.Card.Rarity == CardRarity.Rare)
                    combatRareWithout++;
            }
        }
        double combatRateWith = (double)combatRareWith / combatWith;
        double combatRateWithout = (double)combatRareWithout / combatWithout;
        double combatRatio = combatRateWithout > 0 ? combatRateWith / combatRateWithout : 0;
        GD.Print($"NarrowViewfinder monster-pity sample: without={combatRareWithout}/{combatWithout} ({combatRateWithout:P2}), with={combatRareWith}/{combatWith} ({combatRateWith:P2}, {combatRatio:F2}x)");
        Assert(combatRatio >= 1.12,
            $"Expected increased Rare rate in RoomType.Monster progression (observed {combatRatio:F2}x)");

        int eliteWith = 0;
        int eliteRareWith = 0;
        int eliteWithout = 0;
        int eliteRareWithout = 0;
        for (int i = 0; i < 500; i++)
        {
            CardCreationOptions eWith = CardCreationOptions.ForRoom(owner, RoomType.Elite).WithFlags(CardCreationFlags.IsCardReward);
            foreach (CardCreationResult r in CardFactory.CreateForReward(owner, 3, eWith))
            {
                eliteWith++;
                if (r.Card.Rarity == CardRarity.Rare)
                    eliteRareWith++;
            }

            CardCreationOptions eWithout = CardCreationOptions.ForRoom(peer, RoomType.Elite).WithFlags(CardCreationFlags.IsCardReward);
            foreach (CardCreationResult r in CardFactory.CreateForReward(peer, 3, eWithout))
            {
                eliteWithout++;
                if (r.Card.Rarity == CardRarity.Rare)
                    eliteRareWithout++;
            }
        }
        double eliteRateWith = (double)eliteRareWith / eliteWith;
        double eliteRateWithout = (double)eliteRareWithout / eliteWithout;
        double eliteRatio = eliteRateWithout > 0 ? eliteRateWith / eliteRateWithout : 0;
        GD.Print($"NarrowViewfinder elite-pity sample: without={eliteRareWithout}/{eliteWithout} ({eliteRateWithout:P2}), with={eliteRareWith}/{eliteWith} ({eliteRateWith:P2}, {eliteRatio:F2}x)");
        Assert(eliteRatio >= 1.25,
            $"Expected boosted Rare rate in RoomType.Elite progression (observed {eliteRatio:F2}x)");
    }

    private async Task VerifyHotWaxBoxAndParcelsAsync()
    {
        // Map proximity gating tests
        RunState runMap = SetupTestRun("hwb-map");
        Player pMap = runMap.Players[0];
        pMap.Gold = 50;

        runMap.Map = new ProbeChainActMap(nonBossChainLength: 2);
        Assert(ThingsHotWaxBox.FindMaxNonBossRoomsBeforeBoss(runMap) == 2, "Map chain depth 2 detected");
        var ev2 = BeginEventForPlayer<ThingsHotWaxBox>(pMap, runMap);
        Assert(!ev2.CurrentOptions[0].IsLocked && !ev2.CurrentOptions[1].IsLocked,
            "At 2 rooms before boss, both urgent and chilled are unlocked");

        runMap.Map = new ProbeChainActMap(nonBossChainLength: 1);
        Assert(ThingsHotWaxBox.FindMaxNonBossRoomsBeforeBoss(runMap) == 1, "Map chain depth 1 detected");
        var ev1 = BeginEventForPlayer<ThingsHotWaxBox>(pMap, runMap);
        Assert(ev1.CurrentOptions[0].IsLocked && ev1.CurrentOptions[0].Title.LocEntryKey.Contains("URGENT_BOSS_NEAR"),
            "At 1 room before boss, urgent is locked");
        Assert(!ev1.CurrentOptions[1].IsLocked, "At 1 room before boss, chilled stays unlocked");

        runMap.Map = new ProbeChainActMap(nonBossChainLength: 0);
        Assert(ThingsHotWaxBox.FindMaxNonBossRoomsBeforeBoss(runMap) == 0, "Map chain depth 0 detected");
        var ev0 = BeginEventForPlayer<ThingsHotWaxBox>(pMap, runMap);
        Assert(ev0.CurrentOptions[0].IsLocked && ev0.CurrentOptions[1].IsLocked,
            "Right before boss (0 rooms), both parcels lock");

        // Pry open + Leave
        RunState runPry = SetupTestRun("hwb-pry");
        Player pPry = runPry.Players[0];
        pPry.Gold = 10;
        pPry.Creature.SetCurrentHpInternal(30);
        var evPry = BeginEventForPlayer<ThingsHotWaxBox>(pPry, runPry);
        await evPry.CurrentOptions[2].Chosen();
        Assert(evPry.IsFinished && pPry.Creature.CurrentHp == 22 && pPry.Gold == 50,
            "Pry open deals 8 dmg and grants 40 Gold + PotionReward");

        // HotWaxParcel success path + subroom floor stability + save/reload + Rare relic
        RunState runUrgent = SetupTestRun("hwb-urgent-win");
        Player pU = runUrgent.Players[0];
        runUrgent.Map = new ProbeChainActMap(nonBossChainLength: 5);
        runUrgent.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, ModelDb.Event<ThingsHotWaxBox>().Id);
        int startFloor = runUrgent.TotalFloor;
        var evU = BeginEventForPlayer<ThingsHotWaxBox>(pU, runUrgent);
        await evU.CurrentOptions[0].Chosen();
        ThingsHotWaxParcel hotParcel = pU.GetRelic<ThingsHotWaxParcel>()!;
        Assert(hotParcel != null && hotParcel.StartFloor == startFloor && hotParcel.DisplayAmount == 3,
            "Accept Urgent grants HotWaxParcel initialized to StartFloor with DisplayAmount=3");

        // Owned lock check
        var evUOwned = BeginEventForPlayer<ThingsHotWaxBox>(pU, runUrgent);
        Assert(evUOwned.CurrentOptions[0].IsLocked && evUOwned.CurrentOptions[1].IsLocked,
            "Holding HotWaxParcel locks both urgent & chilled options");

        // Simulated internal event sub-room combat (same TotalFloor!) does NOT increment RoomsElapsed
        var subCombat = CreateCombatRoom(FindFirstEncounter(RoomType.Monster), runUrgent);
        await Hook.BeforeRoomEntered(runUrgent, subCombat);
        Assert(hotParcel!.RoomsElapsed == 0 && hotParcel.DisplayAmount == 3,
            "Event sub-room without TotalFloor change does not advance parcel timer");
        DetachPlayers(subCombat, runUrgent);

        // Room 1: advance floor -> combat victory #1
        AdvanceFloorAndEnterMonster(runUrgent, out CombatRoom room1);
        await Hook.BeforeRoomEntered(runUrgent, room1);
        Assert(hotParcel.RoomsElapsed == 1 && hotParcel.DisplayAmount == 3 && hotParcel.Status == RelicStatus.Normal,
            "Floor +1 keeps DisplayAmount=3");
        await Hook.AfterCombatVictory(runUrgent, room1.CombatState, room1);
        DetachPlayers(room1, runUrgent);
        Assert(hotParcel.CombatsWon == 1 && !hotParcel.IsUsedUp, "Combat #1 increments CombatsWon to 1");

        // Room 2 + save/reload round-trip
        AdvanceFloorAndEnterMonster(runUrgent, out CombatRoom room2);
        await Hook.BeforeRoomEntered(runUrgent, room2);
        Assert(hotParcel.RoomsElapsed == 2 && hotParcel.DisplayAmount == 2, "Floor +2 drops DisplayAmount to 2");

        SerializableRelic savedHotSerial = RoundTripRelic(hotParcel);
        var restoredHot = (ThingsHotWaxParcel)RelicModel.FromSerializable(savedHotSerial);
        Assert(restoredHot.StartFloor == hotParcel.StartFloor
            && restoredHot.LastObservedFloor == hotParcel.LastObservedFloor
            && restoredHot.CombatsWon == 1
            && restoredHot.Outcome == hotParcel.Outcome
            && restoredHot.DisplayAmount == 2,
            "HotWaxParcel [SavedProperty] state survives serialization round-trip");
        DetachPlayers(room2, runUrgent);

        // Room 3: Active status, win combat #2 -> 80 Gold + Rare Relic + IsUsedUp
        AdvanceFloorAndEnterMonster(runUrgent, out CombatRoom room3);
        await Hook.BeforeRoomEntered(runUrgent, room3);
        Assert(hotParcel.DisplayAmount == 1 && hotParcel.Status == RelicStatus.Active,
            "Final allowed room sets Active status & DisplayAmount=1");
        int goldBeforeWin = pU.Gold;
        int relicCountBefore = pU.Relics.Count;
        await Hook.AfterCombatVictory(runUrgent, room3.CombatState, room3);
        DetachPlayers(room3, runUrgent);
        Assert(hotParcel.IsUsedUp && hotParcel.Status == RelicStatus.Disabled && hotParcel.DisplayAmount == 0,
            "Winning 2nd combat marks HotWaxParcel complete & Disabled");
        Assert(pU.Gold == goldBeforeWin + ThingsHotWaxParcel.GoldReward,
            $"HotWaxParcel grants {ThingsHotWaxParcel.GoldReward} Gold");
        Assert(pU.Relics.Count == relicCountBefore + 1 && pU.Relics.Last().Rarity == RelicRarity.Rare,
            "HotWaxParcel grants 1 Rare relic on completion");

        // HotWaxParcel Expiry path (room 4 incomplete -> 14 dmg + random Curse; lethal -> no Curse)
        RunState runExpire = SetupTestRun("hwb-urgent-expire");
        Player pExp = runExpire.Players[0];
        runExpire.Map = new ProbeChainActMap(nonBossChainLength: 5);
        runExpire.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, ModelDb.Event<ThingsHotWaxBox>().Id);
        var expParcel = (ThingsHotWaxParcel)ObtainRelicSync(ModelDb.Relic<ThingsHotWaxParcel>(), pExp);
        int hpBeforeExp = pExp.Creature.CurrentHp;
        int cursesBeforeExp = pExp.Deck.Cards.Count(c => c.Type == CardType.Curse);
        for (int step = 1; step <= 4; step++)
        {
            AdvanceFloorAndEnterMonster(runExpire, out CombatRoom rm);
            await Hook.BeforeRoomEntered(runExpire, rm);
            if (step < 4)
                Assert(!expParcel.IsUsedUp, $"HotWaxParcel stays active through room {step}");
        }
        Assert(expParcel.IsUsedUp && expParcel.Status == RelicStatus.Disabled,
            "Entering room 4 expires HotWaxParcel");
        Assert(pExp.Creature.CurrentHp == hpBeforeExp - ThingsHotWaxParcel.CrackDamage,
            $"Expired HotWaxParcel deals {ThingsHotWaxParcel.CrackDamage} dmg");
        Assert(pExp.Deck.Cards.Count(c => c.Type == CardType.Curse) == cursesBeforeExp + 1,
            "Expired HotWaxParcel adds 1 Curse");

        // Lethal expiry does not crash or add curse to dead owner
        RunState runExpLethal = SetupTestRun("hwb-urgent-lethal");
        Player pDead = runExpLethal.Players[0];
        runExpLethal.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, ModelDb.Event<ThingsHotWaxBox>().Id);
        ObtainRelicSync(ModelDb.Relic<ThingsHotWaxParcel>(), pDead);
        pDead.Creature.SetCurrentHpInternal(10);
        int deckOnDeathCheck = pDead.Deck.Cards.Count;
        for (int step = 1; step <= 4; step++)
        {
            AdvanceFloorAndEnterMonster(runExpLethal, out CombatRoom rm);
            await Hook.BeforeRoomEntered(runExpLethal, rm);
        }
        Assert(pDead.Creature.IsDead && pDead.Deck.Cards.Count == deckOnDeathCheck,
            "Lethal parcel crack skips adding Curse after death");

        // ChilledWaxParcel: Pay 30G, non-Elite ignored, Elite victory upgrades 2 Deck cards + 75G, Expiry is harmless
        RunState runChilled = SetupTestRun("hwb-chilled-win");
        Player pCh = runChilled.Players[0];
        runChilled.Map = new ProbeChainActMap(nonBossChainLength: 6);
        runChilled.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, ModelDb.Event<ThingsHotWaxBox>().Id);
        pCh.Gold = 29;
        var evNoGold = BeginEventForPlayer<ThingsHotWaxBox>(pCh, runChilled);
        Assert(evNoGold.CurrentOptions[1].IsLocked && evNoGold.CurrentOptions[1].Title.LocEntryKey.Contains("CHILLED_NO_GOLD"),
            "Gold < 30 locks Chilled parcel");

        pCh.Gold = 50;
        var evCh = BeginEventForPlayer<ThingsHotWaxBox>(pCh, runChilled);
        await evCh.CurrentOptions[1].Chosen();
        Assert(pCh.Gold == 20, "Chilled option spends 30 Gold");
        ThingsChilledWaxParcel chilled = pCh.GetRelic<ThingsChilledWaxParcel>()!;
        Assert(chilled != null && chilled.DisplayAmount == 4, "Obtained ChilledWaxParcel with DisplayAmount=4");

        // Non-Elite combat should not fulfill quest
        AdvanceFloorAndEnterMonster(runChilled, out CombatRoom normalCombat);
        await Hook.BeforeRoomEntered(runChilled, normalCombat);
        await Hook.AfterCombatVictory(runChilled, normalCombat.CombatState, normalCombat);
        DetachPlayers(normalCombat, runChilled);
        Assert(chilled!.CombatsWon == 0 && !chilled.IsUsedUp, "Normal combat does not satisfy Chilled Elite requirement");

        // Elite combat upgrades 2 cards + grants 75G
        int preUpgradedCount = pCh.Deck.Cards.Count(c => c.IsUpgraded);
        AdvanceFloorAndEnterElite(runChilled, out CombatRoom eliteCombat);
        await Hook.BeforeRoomEntered(runChilled, eliteCombat);
        Assert(chilled.DisplayAmount == 3, "Floor +2 shows DisplayAmount=3 on 4-room Chilled window");
        SerializableRelic savedChilledSerial = RoundTripRelic(chilled);
        var restoredChilled = (ThingsChilledWaxParcel)RelicModel.FromSerializable(savedChilledSerial);
        Assert(restoredChilled.StartFloor == chilled.StartFloor && restoredChilled.LastObservedFloor == chilled.LastObservedFloor,
            "ChilledWaxParcel [SavedProperty] state survives serialization");

        await Hook.AfterCombatVictory(runChilled, eliteCombat.CombatState, eliteCombat);
        DetachPlayers(eliteCombat, runChilled);
        Assert(chilled.IsUsedUp && chilled.Status == RelicStatus.Disabled && chilled.DisplayAmount == 0,
            "Defeating Elite finishes ChilledWaxParcel");
        Assert(pCh.Gold == 20 + ThingsChilledWaxParcel.GoldReward,
            $"ChilledWaxParcel grants {ThingsChilledWaxParcel.GoldReward} Gold");
        Assert(pCh.Deck.Cards.Count(c => c.IsUpgraded) == preUpgradedCount + ThingsChilledWaxParcel.UpgradeCount,
            $"ChilledWaxParcel upgrades {ThingsChilledWaxParcel.UpgradeCount} Deck cards");

        // ChilledWaxParcel harmless expiry on room 5
        RunState runChExp = SetupTestRun("hwb-chilled-expire");
        Player pCe = runChExp.Players[0];
        runChExp.AppendToMapPointHistory(MapPointType.Unknown, RoomType.Event, ModelDb.Event<ThingsHotWaxBox>().Id);
        var ceParcel = (ThingsChilledWaxParcel)ObtainRelicSync(ModelDb.Relic<ThingsChilledWaxParcel>(), pCe);
        int hpCeBefore = pCe.Creature.CurrentHp;
        int cursesCeBefore = pCe.Deck.Cards.Count(c => c.Type == CardType.Curse);
        for (int step = 1; step <= 5; step++)
        {
            AdvanceFloorAndEnterMonster(runChExp, out CombatRoom rm);
            await Hook.BeforeRoomEntered(runChExp, rm);
            if (step == 4)
                Assert(!ceParcel.IsUsedUp && ceParcel.Status == RelicStatus.Active && ceParcel.DisplayAmount == 1,
                    "Room 4 on Chilled window shows DisplayAmount=1 and Active");
        }
        Assert(ceParcel.IsUsedUp && ceParcel.Status == RelicStatus.Disabled,
            "Entering room 5 expires ChilledWaxParcel");
        Assert(pCe.Creature.CurrentHp == hpCeBefore && pCe.Deck.Cards.Count(c => c.Type == CardType.Curse) == cursesCeBefore,
            "Expired ChilledWaxParcel causes zero damage or curse");
    }

    private static EncounterModel FindFirstEncounter(RoomType roomType) =>
        ModelDb.AllEncounters.First(e => e.RoomType == roomType).ToMutable();

    private static CombatRoom CreateCombatRoom(EncounterModel enc, RunState run)
    {
        var room = new CombatRoom(enc, run);
        foreach (Player pl in run.Players)
        {
            pl.Creature.CombatState?.RemoveCreature(pl.Creature);
            room.CombatState.AddPlayer(pl);
        }
        return room;
    }

    private static void DetachPlayers(CombatRoom room, RunState run)
    {
        foreach (Player pl in run.Players)
        {
            if (pl.Creature.CombatState == room.CombatState)
                room.CombatState.RemoveCreature(pl.Creature);
        }
    }

    private static void AdvanceFloorAndEnterMonster(RunState run, out CombatRoom room)
    {
        EncounterModel enc = FindFirstEncounter(RoomType.Monster);
        run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, enc.Id);
        room = CreateCombatRoom(enc, run);
    }

    private static void AdvanceFloorAndEnterElite(RunState run, out CombatRoom room)
    {
        EncounterModel enc = FindFirstEncounter(RoomType.Elite);
        run.AppendToMapPointHistory(MapPointType.Elite, RoomType.Elite, enc.Id);
        room = CreateCombatRoom(enc, run);
    }

    private async Task RenderEventsAndRelicsAsync()
    {
        string? output = System.Environment.GetEnvironmentVariable("THINGS_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            return;
        Directory.CreateDirectory(output);

        // 1. Render Event Layouts (ZHS + ENG)
        var eventView = new SubViewport
        {
            Size = new Vector2I(1920, 1080),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(eventView);
        GetTree().Root.Size = eventView.Size;

        foreach (string lang in new[] { "zhs", "eng" })
        foreach (bool isWax in new[] { false, true })
        {
            RunState run = SetupTestRun($"render-{lang}-{isWax}");
            run.Map = new ProbeChainActMap(nonBossChainLength: 5);
            run.Players[0].Gold = 100;
            SaveManager.Instance.SettingsSave.Language = lang;
            LocManager.Initialize();
            EventModel model = isWax
                ? BeginEventForPlayer<ThingsHotWaxBox>(run.Players[0], run)
                : BeginEventForPlayer<ThingsReturnsCounter>(run.Players[0], run);

            var world = new Control { Size = eventView.Size };
            eventView.AddChild(world);
            world.AddChild(new ColorRect { Size = eventView.Size, Color = Colors.Black });
            NEventLayout layout = GD.Load<PackedScene>(NEventLayout.defaultScenePath).Instantiate<NEventLayout>();
            world.AddChild(layout);
            layout.SetEvent(model);
            layout.SetTitle(model.Title.GetFormattedText());
            layout.SetDescription(model.Description!.GetFormattedText());
            layout.AddOptions(model.CurrentOptions);
            layout.OnSetupComplete();

            await ToSignal(GetTree().CreateTimer(2.8), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Assert(layout.OptionButtons.Count() == 4, $"Native layout has 4 options for {(isWax ? "HotWaxBox" : "ReturnsCounter")}");

            using Image img = eventView.GetTexture().GetImage();
            string pngName = $"native-event-{(isWax ? "hot_wax_box" : "returns_counter")}-{lang}.png";
            Assert(img.SavePng(Path.Combine(output, pngName)) == Error.Ok, $"Save {pngName}");

            world.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        eventView.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        // 2. Verify all 4 new relic AtlasTexture pixels + NRelic / NRelicInventoryHolder render
        SaveManager.Instance.SettingsSave.Language = "zhs";
        LocManager.Initialize();
        RunState relicRun = SetupTestRun("render-relics");
        Player p = relicRun.Players[0];

        var relicView = new SubViewport
        {
            Size = new Vector2I(1120, 810),
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(relicView);
        GetTree().Root.Size = relicView.Size;
        var board = new Control { Size = relicView.Size };
        relicView.AddChild(board);
        board.AddChild(new ColorRect { Color = new Color("#242930"), Size = relicView.Size });
        var font = new SystemFont { FontNames = ["Microsoft YaHei"] };

        void LabelAt(string text, Vector2 pos, int size = 19)
        {
            var label = new Label { Text = text, Position = pos };
            label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", size);
            label.AddThemeColorOverride("font_color", new Color("#eee6ce"));
            board.AddChild(label);
        }

        LabelAt("退货 & 蜡封遗物 · 原生 NRelic / 遗物栏组件", new Vector2(28, 16), 25);
        LabelAt("奖励大图", new Vector2(233, 66));
        LabelAt("遗物栏 / 计数", new Vector2(421, 66));
        LabelAt("聚焦放大", new Vector2(568, 66));
        LabelAt("原生灰置", new Vector2(708, 66));
        LabelAt("原版参考", new Vector2(871, 66));

        (RelicModel Relic, RelicModel Reference, string Key, string Name)[] rows =
        [
            (ModelDb.Relic<ThingsReturnReceipt>(), ModelDb.Relic<MealTicket>(), "things_return_receipt", "退货收据"),
            (ModelDb.Relic<ThingsNarrowViewfinder>(), ModelDb.Relic<FresnelLens>(), "things_narrow_viewfinder", "窄口取景器"),
            (ModelDb.Relic<ThingsHotWaxParcel>(), ModelDb.Relic<VexingPuzzlebox>(), "things_hot_wax_parcel", "发烫的蜡封匣"),
            (ModelDb.Relic<ThingsChilledWaxParcel>(), ModelDb.Relic<SealOfGold>(), "things_chilled_wax_parcel", "冰镇的蜡封匣"),
        ];

        List<NRelicInventoryHolder> focusHolders = [];
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            float y = 116 + i * 163;
            board.AddChild(new ColorRect { Position = new Vector2(20, y + 148), Size = new Vector2(1080, 1), Color = new Color("#41454a") });
            LabelAt(row.Name, new Vector2(28, y + 53), 22);

            VerifyPixels(row.Relic.BigIcon, $"images/relics/{row.Key}.png", new Vector2I(256, 256));
            VerifyPixels(row.Relic.Icon, $"images/relics/{row.Key}_packed.png", new Vector2I(85, 85));
            VerifyPixels(row.Relic.IconOutline, $"images/atlases/relic_outline_atlas.sprites/{row.Key}_outline.png", new Vector2I(85, 85));
            Assert(row.Relic.Icon is AtlasTexture && row.Relic.IconOutline is AtlasTexture,
                row.Key + " uses native AtlasTexture resources");

            RelicModel bigModel = Mutable(row.Relic, p);
            NRelic big = GD.Load<PackedScene>("res://scenes/relics/relic.tscn").Instantiate<NRelic>();
            big.Model = bigModel;
            typeof(NRelic).GetField("_iconSize", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(big, NRelic.IconSize.Large);
            big.Position = new Vector2(218, y);
            big.Size = new Vector2(140, 140);
            board.AddChild(big);
            Assert(!big.Outline.Visible && big.Icon.Texture.GetSize() == new Vector2I(256, 256), row.Key + " large component");

            NRelicInventoryHolder normal = AddHolder(board, Mutable(row.Relic, p), new Vector2(437, y + 35));
            Assert(normal.Relic.Icon.Texture.GetSize() == new Vector2I(85, 85) && normal.Relic.Outline.Visible,
                row.Key + " small component & outline");
            Label counterLabel = normal.GetNode<Label>("%AmountLabel");
            Assert(counterLabel.Visible == row.Relic.ShowCounter, row.Key + " counter visibility");
            if (row.Relic.ShowCounter)
                Assert(counterLabel.Text == row.Relic.DisplayAmount.ToString(), row.Key + " counter value");

            NRelicInventoryHolder focus = AddHolder(board, Mutable(row.Relic, p), new Vector2(583, y + 35));
            typeof(NRelicInventoryHolder).GetMethod("OnFocus", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(focus, null);
            focusHolders.Add(focus);

            RelicModel disabledModel = Mutable(row.Relic, p);
            NRelicInventoryHolder disabled = AddHolder(board, disabledModel, new Vector2(722, y + 35));
            disabledModel.Status = RelicStatus.Disabled;
            Assert(disabled.Relic.Icon.Modulate == new Color("#808080"), row.Key + " native disabled tint");

            AddHolder(board, Mutable(row.Reference, p), new Vector2(887, y + 35));
        }

        await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
        foreach (NRelicInventoryHolder holder in focusHolders)
            Assert(holder.Relic.Icon.Scale.IsEqualApprox(Vector2.One * 1.25f), "Native focus tween scale");
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using (Image relicImg = relicView.GetTexture().GetImage())
            Assert(relicImg.SavePng(Path.Combine(output, "native-relic-components.png")) == Error.Ok, "Save native relic components screenshot");

        relicView.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static RelicModel Mutable(RelicModel canonical, Player owner)
    {
        RelicModel model = canonical.ToMutable();
        model.Owner = owner;
        return model;
    }

    private static NRelicInventoryHolder AddHolder(Control parent, RelicModel model, Vector2 position)
    {
        NRelicInventoryHolder holder = GD.Load<PackedScene>("res://scenes/relics/relic_inventory_holder.tscn")
            .Instantiate<NRelicInventoryHolder>();
        typeof(NRelicInventoryHolder).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(holder, model);
        holder.Position = position;
        parent.AddChild(holder);
        return holder;
    }

    private void VerifyPixels(Texture2D texture, string path, Vector2I size)
    {
        using Image imported = texture.GetImage();
        using Image expected = Image.LoadFromFile(Path.Combine(_root, path));
        Assert(imported.GetSize() == size, "Texture dimensions: " + path);
        imported.Convert(Image.Format.Rgba8);
        expected.Convert(Image.Format.Rgba8);
        expected.FixAlphaEdges();
        Assert(SHA256.HashData(imported.GetData()).SequenceEqual(SHA256.HashData(expected.GetData())),
            "Packaged pixels match generated art: " + path);
    }

    private sealed class ProbeCardSelector(int pickIndex) : ICardSelector
    {
        public List<CardModel> LastOffered { get; } = [];
        public CardModel? LastPicked { get; private set; }

        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            LastOffered.Clear();
            LastOffered.AddRange(options);
            if (pickIndex < 0 || pickIndex >= LastOffered.Count)
            {
                LastPicked = null;
                return Task.FromResult<IEnumerable<CardModel>>([]);
            }

            LastPicked = LastOffered[pickIndex];
            return Task.FromResult<IEnumerable<CardModel>>([LastPicked]);
        }

        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            new() { card = options.FirstOrDefault()?.Card };
    }

    private sealed class ProbeChainActMap : ActMap
    {
        private readonly MapPoint?[,] _grid;
        private readonly MapPoint _boss;
        private readonly MapPoint _start;

        public override MapPoint BossMapPoint => _boss;
        public override MapPoint StartingMapPoint => _start;
        protected override MapPoint?[,] Grid => _grid;

        public ProbeChainActMap(int nonBossChainLength)
        {
            _grid = new MapPoint?[1, Math.Max(2, nonBossChainLength + 2)];
            _start = new MapPoint(0, 0);
            _boss = new MapPoint(0, Math.Max(1, nonBossChainLength + 1))
            {
                PointType = MapPointType.Boss,
            };

            MapPoint prev = _start;
            for (int i = 1; i <= nonBossChainLength; i++)
            {
                var pt = new MapPoint(0, i)
                {
                    PointType = MapPointType.Unknown,
                };
                _grid[0, i] = pt;
                if (i == 1)
                    startMapPoints.Add(pt);
                else
                    prev.AddChildPoint(pt);
                prev = pt;
            }

            if (nonBossChainLength == 0)
                startMapPoints.Add(_boss);
            else
                prev.AddChildPoint(_boss);
        }
    }
}
