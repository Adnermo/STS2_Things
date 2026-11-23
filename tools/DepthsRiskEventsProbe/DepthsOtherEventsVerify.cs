using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using System.Reflection;
using STS2_Things.Events;
using STS2_Things.Relics;

public partial class DepthsProbeNode
{
    private RunState NewEventTestRun(string seed, int gold = 200, bool withRestSite = false)
    {
        RunState run = NewRun(seed);
        if (withRestSite)
            run.Map = new ProbeRestSiteMap();
        typeof(RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(RunManager.Instance, null);
        RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(), shouldSave: false);
        Player player = run.Players[0];
        if (gold > player.Gold)
            PlayerCmd.GainGold(gold - player.Gold, player).GetAwaiter().GetResult();
        else if (gold < player.Gold)
            PlayerCmd.LoseGold(player.Gold - gold, player, GoldLossType.Spent).GetAwaiter().GetResult();
        return run;
    }

    private async Task VerifyAllOtherDepthsEvents(string lang)
    {
        VerifyAllEventMetadata(lang);
        await VerifyRealityAlignedHouses();
        await VerifyShadowCloakroom();
        await VerifyEchoingWell();
        await VerifyPoliteMaw();
        await VerifyMycelialBank();
        await VerifyUnlitFire();
        await VerifyRelicWorkshop();
        await VerifyPotionTasting();
        await VerifyNarrowGate();
    }

    private void VerifyAllEventMetadata(string lang)
    {
        EventModel[] all =
        [
            ModelDb.Event<ThingsRealityAlignedHouses>(),
            ModelDb.Event<ThingsShadowCloakroom>(),
            ModelDb.Event<ThingsEchoingWell>(),
            ModelDb.Event<ThingsPoliteMaw>(),
            ModelDb.Event<ThingsMycelialBank>(),
            ModelDb.Event<ThingsUnlitFire>(),
            ModelDb.Event<ThingsRelicWorkshop>(),
            ModelDb.Event<ThingsPotionTasting>(),
            ModelDb.Event<ThingsNarrowGate>(),
            ModelDb.Event<ThingsBitingChest>(),
            ModelDb.Event<ThingsCrowdedWard>(),
        ];

        foreach (EventModel canonical in all)
        {
            RunState run = NewEventTestRun("meta-" + canonical.Id.Entry, gold: 200, withRestSite: true);
            Player p = run.Players[0];
            EventModel model = Begin<EventModelStub>(p, canonical).GetAwaiter().GetResult();
            foreach (LocString info in model.GameInfoOptions)
            {
                string rendered = info.GetFormattedText();
                Assert(!string.IsNullOrWhiteSpace(rendered) && !rendered.Contains('{'),
                    $"GameInfoOption cleanly formatted ({lang}, {canonical.Id.Entry}): {rendered}");
            }
        }

        foreach (string id in new[] { "THINGS_REALITY_ALIGNED_HOUSES", "THINGS_MYCELIAL_BANK", "THINGS_BITING_CHEST", "THINGS_POLITE_MAW" })
        {
            var loss = new LocString("events", $"{id}.loss");
            Assert(loss.Exists(), $"RunHistory .loss key exists ({lang}): {id}");
        }
    }

    private async Task<EventModel> Begin<TStub>(Player owner, EventModel canonical) where TStub : class
    {
        var model = (EventModel)canonical.ToMutable();
        MethodInfo method = typeof(EventModel).GetMethods().Single(m => m.Name == nameof(EventModel.BeginEvent));
        object?[] args = method.GetParameters()
            .Select(p => p.ParameterType == typeof(Player) ? (object)owner : p.ParameterType == typeof(bool) ? false : null)
            .ToArray();
        await (Task)method.Invoke(model, args)!;
        foreach (EventOption option in model.CurrentOptions)
        {
            model.DynamicVars.AddTo(option.Description);
            model.DynamicVars.AddTo(option.Title);
            Assert(!option.Description.GetFormattedText().Contains('{'), "Formatted option " + option.TextKey);
            Assert(!string.IsNullOrWhiteSpace(option.Title.GetFormattedText()), "Option title " + option.TextKey);
        }
        return model;
    }

    private async Task VerifyRealityAlignedHouses()
    {
        // 1. Cancel merge & address finish with ABORTED leaving state unchanged; Blue Back Door is pure leave
        RunState runCancelMerge = NewEventTestRun("houses-cancel-merge", gold: 100);
        Player pCancelMerge = runCancelMerge.Players[0];
        int hpCancel = pCancelMerge.Creature.CurrentHp;
        int deckCancel = pCancelMerge.Deck.Cards.Count;
        var evCancelMerge = await Begin<ThingsRealityAlignedHouses>(pCancelMerge);
        Assert(evCancelMerge.CurrentOptions.Count == 4, "RealityAlignedHouses has 4 options");
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(_ => [])))
        {
            await evCancelMerge.CurrentOptions[0].Chosen();
            Assert(evCancelMerge.IsFinished && pCancelMerge.Creature.CurrentHp == hpCancel && pCancelMerge.Gold == 100 && pCancelMerge.Deck.Cards.Count == deckCancel,
                "Cancel merge finishes cleanly with state unchanged");
        }

        RunState runCancelAddr = NewEventTestRun("houses-cancel-addr", gold: 100);
        Player pCancelAddr = runCancelAddr.Players[0];
        int maxHpCancel = pCancelAddr.Creature.MaxHp;
        var evCancelAddr = await Begin<ThingsRealityAlignedHouses>(pCancelAddr);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(_ => [])))
        {
            await evCancelAddr.CurrentOptions[1].Chosen();
            Assert(evCancelAddr.IsFinished && pCancelAddr.Creature.MaxHp == maxHpCancel && pCancelAddr.Gold == 100,
                "Cancel address finishes cleanly with MaxHP unchanged");
        }

        RunState runBlue = NewEventTestRun("houses-blue", gold: 100);
        Player pBlue = runBlue.Players[0];
        await CreatureCmd.SetCurrentHp(pBlue.Creature, 40);
        var evBlue = await Begin<ThingsRealityAlignedHouses>(pBlue);
        await evBlue.CurrentOptions[3].Chosen();
        Assert(evBlue.IsFinished && pBlue.Creature.CurrentHp == 40 && pBlue.Gold == 100,
            "Blue Back Door is pure leave keeping HP and Gold unchanged");

        // 2. Road laps double 6/50 -> 12/100 -> stop = -18 HP, +150 Gold
        RunState runRoad = NewEventTestRun("houses-road", gold: 100);
        Player pRoad = runRoad.Players[0];
        int hpStart = pRoad.Creature.CurrentHp;
        var evRoad = await Begin<ThingsRealityAlignedHouses>(pRoad);
        await evRoad.CurrentOptions[2].Chosen();
        Assert(!evRoad.IsFinished && pRoad.Creature.CurrentHp == hpStart - 6 && pRoad.Gold == 150,
            "Road lap 1 costs 6 HP and grants 50 Gold");
        await evRoad.CurrentOptions[0].Chosen();
        Assert(!evRoad.IsFinished && pRoad.Creature.CurrentHp == hpStart - 18 && pRoad.Gold == 250,
            "Road lap 2 costs 12 HP and grants 100 Gold");
        await evRoad.CurrentOptions[1].Chosen();
        Assert(evRoad.IsFinished, "Road STOP ends event cleanly");

        // 3. Merge deals 10 HP, reduces count by 1 & upgrades keeper; Address -7 MaxHP + Upgrade + PerfectFit
        RunState runMerge = NewEventTestRun("houses-merge");
        Player pMerge = runMerge.Players[0];
        int mergeHpBefore = pMerge.Creature.CurrentHp;
        int initialCount = pMerge.Deck.Cards.Count;
        var evMerge = await Begin<ThingsRealityAlignedHouses>(pMerge);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(cards => cards.Take(1))))
            await evMerge.CurrentOptions[0].Chosen();
        Assert(evMerge.IsFinished && pMerge.Creature.CurrentHp == mergeHpBefore - 10
            && pMerge.Deck.Cards.Count == initialCount - 1 && pMerge.Deck.Cards.Any(c => c.IsUpgraded),
            "Merge deals 10 HP and folds duplicate into Upgraded copy");

        RunState runAddr = NewEventTestRun("houses-address");
        Player pAddr = runAddr.Players[0];
        int maxHpBefore = pAddr.Creature.MaxHp;
        var evAddr = await Begin<ThingsRealityAlignedHouses>(pAddr);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(cards => cards.Take(1))))
            await evAddr.CurrentOptions[1].Chosen();
        Assert(evAddr.IsFinished && pAddr.Creature.MaxHp == maxHpBefore - 7
            && pAddr.Deck.Cards.Any(c => c.IsUpgraded && c.Enchantment is PerfectFit),
            "Address deducts 7 MaxHP and applies Upgrade + PerfectFit");
    }

    private async Task VerifyShadowCloakroom()
    {
        // Lock check with 0 Gold: DEPOSIT & SWAP_COAT locked, LEAVE unlocked
        RunState runLocked = NewEventTestRun("cloakroom-lock", gold: 0);
        var evLocked = await Begin<ThingsShadowCloakroom>(runLocked.Players[0]);
        Assert(evLocked.CurrentOptions.Count == 3
            && evLocked.CurrentOptions[0].IsLocked
            && evLocked.CurrentOptions[1].IsLocked
            && !evLocked.CurrentOptions[2].IsLocked,
            "ShadowCloakroom 3 options with 0 Gold locks paid options only");

        // Cancel deposit / swap spends 0 Gold, LEAVE keeps deck, HP, and Gold unchanged
        RunState runLeave = NewEventTestRun("cloakroom-leave", gold: 100);
        Player pLeave = runLeave.Players[0];
        int deckBefore = pLeave.Deck.Cards.Count;
        int hpBefore = pLeave.Creature.CurrentHp;
        var evLeave = await Begin<ThingsShadowCloakroom>(pLeave);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(_ => [])))
        {
            await evLeave.CurrentOptions[0].Chosen();
            await evLeave.CurrentOptions[1].Chosen();
            Assert(!evLeave.IsFinished && pLeave.Gold == 100 && pLeave.Deck.Cards.Count == deckBefore,
                "Cancelable Deposit/Swap spend 0 Gold");
        }
        await evLeave.CurrentOptions[2].Chosen();
        Assert(evLeave.IsFinished && pLeave.Deck.Cards.Count == deckBefore
            && pLeave.Creature.CurrentHp == hpBefore && pLeave.Gold == 100,
            "ShadowCloakroom LEAVE finishes with deck, HP, and Gold unchanged");

        // Deposit (45G) grants Claim Ticket & locks on revisit
        RunState runDep = NewEventTestRun("cloakroom-deposit", gold: 100);
        Player pDep = runDep.Players[0];
        var evDep = await Begin<ThingsShadowCloakroom>(pDep);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(cards => cards.Take(1))))
            await evDep.CurrentOptions[0].Chosen();
        Assert(evDep.IsFinished && pDep.Gold == 55 && pDep.GetRelic<ThingsShadowClaimTicket>() != null,
            "Deposit spends 45G and grants Claim Ticket");
        var evDepAgain = await Begin<ThingsShadowCloakroom>(pDep);
        Assert(evDepAgain.CurrentOptions[0].IsLocked, "Already storing locks Deposit");

        // Swap Coat (70G) transforms card to an Upgraded card
        RunState runSwap = NewEventTestRun("cloakroom-swap", gold: 100);
        Player pSwap = runSwap.Players[0];
        CardModel victim = pSwap.Deck.Cards.First(c => c.IsTransformable);
        var evSwap = await Begin<ThingsShadowCloakroom>(pSwap);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(cards => [cards.Contains(victim) ? victim : cards[0]])))
            await evSwap.CurrentOptions[1].Chosen();
        Assert(evSwap.IsFinished && pSwap.Gold == 30 && !pSwap.Deck.Cards.Contains(victim)
            && pSwap.Deck.Cards.Any(c => c.IsUpgraded),
            "Swap Coat spends 70G and Transforms into an Upgraded card");
    }

    private async Task VerifyEchoingWell()
    {
        RunState runLocked = NewEventTestRun("well-lock", gold: 0);
        var evLocked = await Begin<ThingsEchoingWell>(runLocked.Players[0]);
        Assert(evLocked.CurrentOptions.Count == 3 && !evLocked.CurrentOptions[0].IsLocked
            && evLocked.CurrentOptions[1].IsLocked && !evLocked.CurrentOptions[2].IsLocked,
            "EchoingWell has 3 options and locks Spiral at 0 Gold");

        // Cancel RECORD/SPIRAL keeps state unchanged; LEAVE keeps HP unchanged
        RunState runLeave = NewEventTestRun("well-leave", gold: 100);
        Player pLeave = runLeave.Players[0];
        await CreatureCmd.SetCurrentHp(pLeave.Creature, 40);
        var evLeave = await Begin<ThingsEchoingWell>(pLeave);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(_ => [])))
        {
            await evLeave.CurrentOptions[0].Chosen();
            await evLeave.CurrentOptions[1].Chosen();
            Assert(!evLeave.IsFinished && pLeave.Gold == 100, "Cancel Record/Spiral preserves Gold");
        }
        await evLeave.CurrentOptions[2].Chosen();
        Assert(evLeave.IsFinished && pLeave.Creature.CurrentHp == 40 && pLeave.Gold == 100,
            "EchoingWell LEAVE finishes with HP and Gold unchanged");

        // SPIRAL (-60G, enchants Strike/Defend with Spiral)
        RunState runSpiral = NewEventTestRun("well-spiral", gold: 100);
        Player pSpiral = runSpiral.Players[0];
        var evSpiral = await Begin<ThingsEchoingWell>(pSpiral);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(cards => cards.Take(1))))
            await evSpiral.CurrentOptions[1].Chosen();
        Assert(evSpiral.IsFinished && pSpiral.Gold == 40 && pSpiral.Deck.Cards.Any(c => c.Enchantment is Spiral),
            "Carve Spiral costs 60G and enchants Basic card with Spiral");

        // RECORD removes 1 card and grants Bottled Echo
        RunState runRecord = NewEventTestRun("well-record", gold: 100);
        Player pRecord = runRecord.Players[0];
        int countBefore = pRecord.Deck.Cards.Count;
        var evRecord = await Begin<ThingsEchoingWell>(pRecord);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(cards => cards.Take(1))))
            await evRecord.CurrentOptions[0].Chosen();
        Assert(evRecord.IsFinished && pRecord.Deck.Cards.Count == countBefore - 1
            && pRecord.GetRelic<ThingsBottledEcho>() != null,
            "Record removes card and grants Bottled Echo");
    }

    private async Task VerifyPoliteMaw()
    {
        RunState runAttack = NewEventTestRun("maw-attack", gold: 50);
        Player pAttack = runAttack.Players[0];
        int attackHpBefore = pAttack.Creature.CurrentHp;
        int attackDeckBefore = pAttack.Deck.Cards.Count;
        var evAttack = await Begin<ThingsPoliteMaw>(pAttack);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(cards => cards.Take(1))))
            await evAttack.CurrentOptions[0].Chosen();
        Assert(evAttack.IsFinished && pAttack.Deck.Cards.Count == attackDeckBefore - 1
            && pAttack.Creature.CurrentHp == attackHpBefore - 8,
            "PoliteMaw ATTACK removes 1 Attack and deals 8 HP");

        RunState runSkill = NewEventTestRun("maw-skill", gold: 50);
        Player pSkill = runSkill.Players[0];
        await CardPileCmd.Add(runSkill.CreateCard<Inflame>(pSkill), PileType.Deck);
        await CardPileCmd.AddCurseToDeck<Injury>(pSkill);
        int skillHpBefore = pSkill.Creature.CurrentHp;
        int skillDeckBefore = pSkill.Deck.Cards.Count;
        var evSkill = await Begin<ThingsPoliteMaw>(pSkill);
        Assert(evSkill.CurrentOptions.Count == 5 && evSkill.CurrentOptions.All(o => !o.IsLocked),
            "PoliteMaw unlocks all 5 options with Attack/Skill/Power/Curse");
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(cards => cards.Take(1))))
            await evSkill.CurrentOptions[1].Chosen();
        Assert(evSkill.IsFinished && pSkill.Deck.Cards.Count == skillDeckBefore - 1
            && pSkill.Creature.CurrentHp == skillHpBefore - 6 && pSkill.Gold == 95,
            "PoliteMaw SKILL removes 1 Skill, deals 6 HP, and grants +45 Gold");

        RunState runPower = NewEventTestRun("maw-power", gold: 50);
        Player pPower = runPower.Players[0];
        await CardPileCmd.Add(runPower.CreateCard<Inflame>(pPower), PileType.Deck);
        int powerMaxHpBefore = pPower.Creature.MaxHp;
        int powerDeckBefore = pPower.Deck.Cards.Count;
        int powerRelicsBefore = pPower.Relics.Count;
        var evPower = await Begin<ThingsPoliteMaw>(pPower);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(cards => cards.Take(1))))
            await evPower.CurrentOptions[2].Chosen();
        Assert(evPower.IsFinished && pPower.Deck.Cards.Count == powerDeckBefore - 1
            && pPower.Creature.MaxHp == powerMaxHpBefore - 5
            && pPower.Relics.Count == powerRelicsBefore + 1
            && pPower.Relics.Last().Rarity == RelicRarity.Uncommon,
            "PoliteMaw POWER removes 1 Power, deducts 5 MaxHP, and grants Uncommon relic");

        RunState runLeave = NewEventTestRun("maw-leave", gold: 50);
        Player pLeave = runLeave.Players[0];
        int leaveHpBefore = pLeave.Creature.CurrentHp;
        int leaveDeckBefore = pLeave.Deck.Cards.Count;
        var evLeave = await Begin<ThingsPoliteMaw>(pLeave);
        Assert(evLeave.CurrentOptions[2].IsLocked && evLeave.CurrentOptions[3].IsLocked && !evLeave.CurrentOptions[4].IsLocked,
            "Starter deck locks Power & Curse options while LEAVE stays unlocked");
        await evLeave.CurrentOptions[4].Chosen();
        Assert(evLeave.IsFinished && pLeave.Gold == 50 && pLeave.Creature.CurrentHp == leaveHpBefore
            && pLeave.Deck.Cards.Count == leaveDeckBefore,
            "PoliteMaw LEAVE finishes with Gold, HP, and deck unchanged");
    }

    private async Task VerifyMycelialBank()
    {
        RunState runShort = NewEventTestRun("bank-short");
        Player pShort = runShort.Players[0];
        int hpBefore = pShort.Creature.CurrentHp;
        var evShort = await Begin<ThingsMycelialBank>(pShort);
        await evShort.CurrentOptions[0].Chosen();
        var depShort = pShort.GetRelic<ThingsMycelialDeposit>();
        Assert(evShort.IsFinished && pShort.Creature.CurrentHp == hpBefore - 10 && depShort != null
            && depShort.PaymentsRemaining == 3 && ThingsMycelialDeposit.ShortRepayment == 8,
            "MycelialBank SHORT costs 10 HP for 3x8 repayment");

        RunState runLong = NewEventTestRun("bank-long");
        Player pLong = runLong.Players[0];
        int maxHpBefore = pLong.Creature.MaxHp;
        var evLong = await Begin<ThingsMycelialBank>(pLong);
        await evLong.CurrentOptions[1].Chosen();
        Assert(evLong.IsFinished && pLong.Creature.MaxHp == maxHpBefore - 6
            && pLong.GetRelic<ThingsMycelialDeposit>() != null && ThingsMycelialDeposit.LongRepayment == 15,
            "MycelialBank LONG costs 6 MaxHP for +15 MaxHP after Boss");

        RunState runLeave = NewEventTestRun("bank-leave");
        Player pLeave = runLeave.Players[0];
        int leaveMaxBefore = pLeave.Creature.MaxHp;
        int leaveHpBefore = pLeave.Creature.CurrentHp;
        var evLeave = await Begin<ThingsMycelialBank>(pLeave);
        await evLeave.CurrentOptions[2].Chosen();
        Assert(evLeave.IsFinished && pLeave.Creature.MaxHp == leaveMaxBefore
            && pLeave.Creature.CurrentHp == leaveHpBefore
            && pLeave.GetRelic<ThingsMycelialDeposit>() == null,
            "MycelialBank LEAVE finishes with MaxHP/HP unchanged and no deposit relic");
    }

    private async Task VerifyUnlitFire()
    {
        // 1. Cancel BORROW smith returns to 3 options without healing, upgrading, or granting Ember Pact
        RunState runBorrow = NewEventTestRun("fire-borrow", gold: 60, withRestSite: true);
        Player pBorrow = runBorrow.Players[0];
        await CreatureCmd.SetCurrentHp(pBorrow.Creature, 40);
        int expectedHeal = (int)HealRestSiteOption.GetHealAmount(pBorrow);
        int upgradedBefore = pBorrow.Deck.Cards.Count(c => c.IsUpgraded);
        var evBorrow = await Begin<ThingsUnlitFire>(pBorrow);
        Assert(evBorrow.CurrentOptions.Count == 3 && !evBorrow.CurrentOptions[0].IsLocked && !evBorrow.CurrentOptions[1].IsLocked,
            "UnlitFire exposes 3 merged options when RestSite ahead");

        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(_ => [])))
        {
            await evBorrow.CurrentOptions[0].Chosen();
            Assert(!evBorrow.IsFinished && evBorrow.CurrentOptions.Count == 3
                && pBorrow.Creature.CurrentHp == 40
                && pBorrow.Deck.Cards.Count(c => c.IsUpgraded) == upgradedBefore
                && pBorrow.GetRelic<ThingsBorrowedEmber>() == null,
                "Canceling UnlitFire BORROW smith preserves HP, cards, and Ember Pact");
        }

        // 2. Confirm BORROW rests (heals 30% MaxHP) + smiths 1 card + grants debt Ember Pact
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(cards => cards.Take(1))))
        {
            await evBorrow.CurrentOptions[0].Chosen();
        }
        var debtEmber = pBorrow.GetRelic<ThingsBorrowedEmber>();
        Assert(evBorrow.IsFinished
            && pBorrow.Creature.CurrentHp == 40 + expectedHeal
            && pBorrow.Deck.Cards.Count(c => c.IsUpgraded) == upgradedBefore + 1
            && debtEmber != null && !debtEmber.Kindled && debtEmber.State == 0,
            "UnlitFire BORROW rests, upgrades 1 card, and grants debt Ember Pact");

        // 3. Fallback when deck has no upgradable cards still lets player rest + take debt Ember Pact
        RunState runNoSmith = NewEventTestRun("fire-no-smith", gold: 60, withRestSite: true);
        Player pNoSmith = runNoSmith.Players[0];
        await CreatureCmd.SetCurrentHp(pNoSmith.Creature, 40);
        foreach (var c in pNoSmith.Deck.Cards.Where(c => c.IsUpgradable).ToList()) CardCmd.Upgrade(c);
        var evNoSmith = await Begin<ThingsUnlitFire>(pNoSmith);
        Assert(!evNoSmith.CurrentOptions[0].IsLocked, "UnlitFire option 0 stays available when no upgradable cards remain");
        await evNoSmith.CurrentOptions[0].Chosen();
        var fallbackEmber = pNoSmith.GetRelic<ThingsBorrowedEmber>();
        Assert(evNoSmith.IsFinished && pNoSmith.Creature.CurrentHp == 40 + expectedHeal && fallbackEmber != null && !fallbackEmber.Kindled,
            "UnlitFire REST fallback heals and grants debt Ember Pact");

        // 4. Option 1 (FUEL): 55G -> +25 next RestSite Ember Pact; Option 2 (LEAVE): pure leave
        RunState runFuel = NewEventTestRun("fire-fuel", gold: 100, withRestSite: true);
        Player pFuel = runFuel.Players[0];
        var evFuel = await Begin<ThingsUnlitFire>(pFuel);
        await evFuel.CurrentOptions[1].Chosen();
        var ember = pFuel.GetRelic<ThingsBorrowedEmber>();
        Assert(evFuel.IsFinished && pFuel.Gold == 45 && ember != null && ember.Kindled && ThingsBorrowedEmber.KindledHeal == 25,
            "UnlitFire FUEL spends 55G and grants +25 Ember Pact");

        RunState runLeave = NewEventTestRun("fire-leave", gold: 0, withRestSite: true);
        Player pLeave = runLeave.Players[0];
        await CreatureCmd.SetCurrentHp(pLeave.Creature, 40);
        var evLeave = await Begin<ThingsUnlitFire>(pLeave);
        await evLeave.CurrentOptions[2].Chosen();
        Assert(evLeave.IsFinished && pLeave.Creature.CurrentHp == 40 && pLeave.GetRelic<ThingsBorrowedEmber>() == null,
            "UnlitFire LEAVE finishes with HP unchanged and no Ember Pact");
    }

    private async Task VerifyRelicWorkshop()
    {
        RunState runTrade = NewEventTestRun("workshop-trade", gold: 100);
        Player pTrade = runTrade.Players[0];
        await RelicCmd.Obtain(ModelDb.Relic<Vajra>().ToMutable(), pTrade);
        int relicsBefore = pTrade.Relics.Count;
        var evTrade = await Begin<ThingsRelicWorkshop>(pTrade);
        await evTrade.CurrentOptions[0].Chosen(); // SELECT_TRADE
        await evTrade.CurrentOptions[0].Chosen(); // TRADE Vajra
        Assert(evTrade.IsFinished && pTrade.Gold == 35 && pTrade.Relics.Count == relicsBefore
            && pTrade.Relics.Last().Rarity == RelicRarity.Uncommon,
            "RelicWorkshop TRADE costs 65G and grants an Uncommon relic");

        RunState runSell = NewEventTestRun("workshop-sell", gold: 50);
        Player pSell = runSell.Players[0];
        await RelicCmd.Obtain(ModelDb.Relic<Anchor>().ToMutable(), pSell);
        int sellCountBefore = pSell.Relics.Count;
        var evSell = await Begin<ThingsRelicWorkshop>(pSell);
        await evSell.CurrentOptions[1].Chosen(); // SELECT_SELL
        await evSell.CurrentOptions[0].Chosen(); // SELL Anchor
        Assert(evSell.IsFinished && pSell.Gold == 200 && pSell.Relics.Count == sellCountBefore - 1,
            "RelicWorkshop SELL grants +150 Gold");

        RunState runReforge = NewEventTestRun("workshop-reforge", gold: 0);
        Player pRef = runReforge.Players[0];
        int refHpBefore = pRef.Creature.CurrentHp;
        CardModel first = pRef.Deck.Cards[0];
        var evRef = await Begin<ThingsRelicWorkshop>(pRef);
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(_ => [])))
        {
            await evRef.CurrentOptions[2].Chosen();
            Assert(!evRef.IsFinished && pRef.Creature.CurrentHp == refHpBefore && pRef.Deck.Cards.Contains(first),
                "Cancel reforge stays on initial page without HP loss");
        }
        using (ProbeRuntimeApi.UseSelector(new LambdaCardSelector(_ => [first])))
            await evRef.CurrentOptions[2].Chosen();
        Assert(evRef.IsFinished && pRef.Creature.CurrentHp == refHpBefore - 8 && !pRef.Deck.Cards.Contains(first),
            "Reforge deals 8 HP and transforms 1 deck card");

        // Empty deck triggers pure LEAVE (HP unchanged)
        RunState runLeave = NewEventTestRun("workshop-leave");
        Player pLeave = runLeave.Players[0];
        await CardPileCmd.RemoveFromDeck(pLeave.Deck.Cards.ToList());
        await CreatureCmd.SetCurrentHp(pLeave.Creature, 40);
        var evLeave = await Begin<ThingsRelicWorkshop>(pLeave);
        await evLeave.CurrentOptions[2].Chosen();
        Assert(evLeave.IsFinished && pLeave.Creature.CurrentHp == 40,
            "Empty deck RelicWorkshop fallback is pure LEAVE keeping HP unchanged");
    }

    private async Task VerifyPotionTasting()
    {
        RunState runSample = NewEventTestRun("potion-sample", gold: 0);
        Player pSample = runSample.Players[0];
        await CreatureCmd.SetCurrentHp(pSample.Creature, 40);
        var evSample = await Begin<ThingsPotionTasting>(pSample);
        Assert(evSample.CurrentOptions.Count == 4 && !evSample.CurrentOptions[0].IsLocked,
            "PotionTasting exposes 4 options with SAMPLE unlocked");
        await evSample.CurrentOptions[0].Chosen(); // SAMPLE (-7 HP -> 1 random potion)
        Assert(evSample.IsFinished && pSample.Creature.CurrentHp == 33,
            "PotionTasting SAMPLE deals 7 HP and finishes");

        RunState runBuy = NewEventTestRun("potion-buy", gold: 100);
        Player pBuy = runBuy.Players[0];
        await CreatureCmd.SetCurrentHp(pBuy.Creature, 40);
        var evBuy = await Begin<ThingsPotionTasting>(pBuy);
        await evBuy.CurrentOptions[1].Chosen(); // BUY (-75G, 2 potions, no heal)
        Assert(evBuy.IsFinished && pBuy.Gold == 25 && pBuy.Creature.CurrentHp == 40,
            "PotionTasting BUY spends 75G without healing");

        RunState runBarter = NewEventTestRun("potion-barter", gold: 0);
        Player pBarter = runBarter.Players[0];
        await PotionCmd.TryToProcure<PotionShapedRock>(pBarter);
        PotionModel offeredPotion = pBarter.Potions.First();
        int maxHpBefore = pBarter.Creature.MaxHp;
        var evBarter = await Begin<ThingsPotionTasting>(pBarter);
        await evBarter.CurrentOptions[2].Chosen(); // PAYMENT page
        await evBarter.CurrentOptions[0].Chosen(); // GIVE potion -> discards 1 potion, -4 MaxHP, 2 potions
        Assert(evBarter.IsFinished && !pBarter.Potions.Contains(offeredPotion)
            && pBarter.Creature.MaxHp == maxHpBefore - 4,
            "PotionTasting BARTER discards offered potion and deducts 4 MaxHP");

        RunState runLeave = NewEventTestRun("potion-leave", gold: 0);
        Player pLeave = runLeave.Players[0];
        await RelicCmd.Obtain(ModelDb.Relic<Sozu>().ToMutable(), pLeave);
        await CreatureCmd.SetCurrentHp(pLeave.Creature, 40);
        var evLeave = await Begin<ThingsPotionTasting>(pLeave);
        Assert(evLeave.CurrentOptions.Count == 4
            && evLeave.CurrentOptions[0].IsLocked
            && evLeave.CurrentOptions[0].TextKey.EndsWith(".SAMPLE_LOCKED", StringComparison.Ordinal)
            && evLeave.CurrentOptions[1].IsLocked
            && evLeave.CurrentOptions[2].IsLocked
            && !evLeave.CurrentOptions[3].IsLocked
            && evLeave.CurrentOptions[3].TextKey.EndsWith(".LEAVE", StringComparison.Ordinal),
            "PotionTasting locks SAMPLE/BUY/BARTER under Sozu while LEAVE stays unlocked");
        await evLeave.CurrentOptions[3].Chosen(); // LEAVE
        Assert(evLeave.IsFinished && pLeave.Creature.CurrentHp == 40 && pLeave.Gold == 0,
            "PotionTasting LEAVE finishes cleanly under Sozu with HP and Gold unchanged");
    }

    private async Task VerifyNarrowGate()
    {
        // Ironclad has 10 cards -> 3 shown initially, 3 more on 1st reroll, 3 on 2nd reroll, 1 left on 3rd reroll, then NO_ALTERNATIVES!
        RunState runGate = NewEventTestRun("narrow-gate", gold: 200);
        Player pGate = runGate.Players[0];
        var evGate = await Begin<ThingsNarrowGate>(pGate);
        Assert(evGate.CurrentOptions.Count == 4 && !evGate.CurrentOptions[3].IsLocked,
            "NarrowGate shows 3 removable candidates + REROLL");

        await evGate.CurrentOptions[3].Chosen(); // 1st reroll (-35G)
        Assert(evGate.GoldOrNull(pGate) == 165
            && evGate.Description?.GetRawText() == new LocString("events", "THINGS_NARROW_GATE.pages.REROLLED.description").GetRawText(),
            "1st NarrowGate reroll uses REROLLED.description");

        await evGate.CurrentOptions[3].Chosen(); // 2nd reroll (-35G)
        Assert(pGate.Gold == 130
            && evGate.Description?.GetRawText() == new LocString("events", "THINGS_NARROW_GATE.pages.REROLLED_MORE.description").GetRawText(),
            "2nd NarrowGate reroll uses REROLLED_MORE.description");

        await evGate.CurrentOptions.Last().Chosen(); // 3rd reroll (final unseen card!)
        Assert(pGate.Gold == 95 && evGate.CurrentOptions.Last().IsLocked,
            "Exhausting all unseen cards locks NarrowGate REROLL");

        // Remove chosen card on final reroll deals 7 HP and finishes event cleanly
        int deckCount = pGate.Deck.Cards.Count;
        int gateHpBefore = pGate.Creature.CurrentHp;
        await evGate.CurrentOptions[0].Chosen();
        Assert(evGate.IsFinished && pGate.Creature.CurrentHp == gateHpBefore - 7 && pGate.Deck.Cards.Count == deckCount - 1,
            "NarrowGate deals 7 HP and removes selected card");

        // 0 removable cards -> pure LEAVE keeping HP unchanged
        RunState runLeave = NewEventTestRun("narrow-leave");
        Player pLeave = runLeave.Players[0];
        await CardPileCmd.RemoveFromDeck(pLeave.Deck.Cards.ToList());
        await CreatureCmd.SetCurrentHp(pLeave.Creature, 40);
        var evLeave = await Begin<ThingsNarrowGate>(pLeave);
        await evLeave.CurrentOptions[0].Chosen();
        Assert(evLeave.IsFinished && pLeave.Creature.CurrentHp == 40,
            "NarrowGate empty-deck LEAVE finishes with HP unchanged");
    }

    private sealed class EventModelStub { }

    private sealed class LambdaCardSelector(Func<IReadOnlyList<CardModel>, IEnumerable<CardModel>> pick) : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect) =>
            Task.FromResult(pick(options.ToList()));

        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            new() { card = options.FirstOrDefault()?.Card };
    }

    private sealed class ProbeRestSiteMap : ActMap
    {
        private readonly MapPoint?[,] _grid;
        private readonly MapPoint _boss;
        private readonly MapPoint _start;

        public override MapPoint BossMapPoint => _boss;
        public override MapPoint StartingMapPoint => _start;
        protected override MapPoint?[,] Grid => _grid;

        public ProbeRestSiteMap()
        {
            _grid = new MapPoint?[1, 3];
            _start = new MapPoint(0, 0);
            var rest = new MapPoint(0, 1) { PointType = MapPointType.RestSite };
            _boss = new MapPoint(0, 2) { PointType = MapPointType.Boss };
            _grid[0, 1] = rest;
            _grid[0, 2] = _boss;
            startMapPoints.Add(rest);
            rest.AddChildPoint(_boss);
        }
    }
}

internal static class ProbePlayerExtensions
{
    public static int GoldOrNull(this EventModel _, Player p) => p.Gold;
}
