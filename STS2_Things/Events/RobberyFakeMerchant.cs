using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;

namespace STS2_Things.Events;

public sealed class RobberyFakeMerchant : EventModel
{
    private static readonly RelicModel[] FakeRelicPool = new RelicModel[] //所有假遗物
    {
        ModelDb.Relic<FakeAnchor>(),
        ModelDb.Relic<FakeBloodVial>(),
        ModelDb.Relic<FakeHappyFlower>(),
        ModelDb.Relic<FakeLeesWaffle>(),
        ModelDb.Relic<FakeMango>(),
        ModelDb.Relic<FakeOrichalcum>(),
        ModelDb.Relic<FakeSneckoEye>(),
        ModelDb.Relic<FakeStrikeDummy>(),
        ModelDb.Relic<FakeVenerableTeaSet>()
    };

    private static readonly RelicModel[] TrueRelicPool = new RelicModel[] //所有假遗物
    {
        ModelDb.Relic<Anchor>(),
        ModelDb.Relic<BloodVial>(),
        ModelDb.Relic<HappyFlower>(),
        ModelDb.Relic<LeesWaffle>(),
        ModelDb.Relic<Mango>(),
        ModelDb.Relic<Orichalcum>(),
        ModelDb.Relic<SneckoEye>(),
        ModelDb.Relic<StrikeDummy>(),
        ModelDb.Relic<VenerableTeaSet>()
    };

    public override EventLayoutType LayoutType => EventLayoutType.Combat;

    public override EncounterModel CanonicalEncounter => ModelDb.Encounter<FakeMerchantEventEncounter>();

    public override bool IsShared => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("GoldsCount", 67),
        new IntVar("FakeRelicsCount", 2),
        new IntVar("TrueRelicsCount", 2)
    ];

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        return new EventOption[3]
        {
            new(this, TakeRelics, "ROBBERY_FAKE_MERCHANT.pages.INITIAL.options.TAKERELICS"),
            new(this, TakeGolds, "ROBBERY_FAKE_MERCHANT.pages.INITIAL.options.TAKEGOLDS"),
            new(this, BeginFight, "ROBBERY_FAKE_MERCHANT.pages.INITIAL.options.BEGINFIGHT")
        };
    }

    private async Task TakeGolds()
    {
        await PlayerCmd.GainGold((int)DynamicVars["GoldsCount"].BaseValue, Owner);
        SetEventFinished(L10NLookup("ROBBERY_FAKE_MERCHANT.pages.INITIAL.options.TAKEGOLDS.description"));
    }

    private async Task TakeRelics()
    {
        var relics = FakeRelicPool
            .ToList()
            .UnstableShuffle(Rng)
            .Take((int)DynamicVars["FakeRelicsCount"].BaseValue)
            .ToList();

        foreach (var relic in relics)
            await RelicCmd.Obtain(relic.ToMutable(), Owner);

        SetEventFinished(L10NLookup("ROBBERY_FAKE_MERCHANT.pages.TAKERELICS.description"));
    }

    private Task BeginFight()
    {
        SetEventState(L10NLookup("ROBBERY_FAKE_MERCHANT.pages.BEGINFIGHT.description"),
            [new EventOption(this, Fight, "THE_LANTERN_KEY.pages.KEEP_THE_KEY.options.FIGHT")]); //别动这个 用自带的文本会卡住
        return Task.CompletedTask;
    }

    private Task Fight()
    {
        var extraRewards = TrueRelicPool
            .ToList()
            .UnstableShuffle(Rng)
            .Take((int)DynamicVars["TrueRelicsCount"].BaseValue)
            .Select(relic => (Reward)new RelicReward(relic.ToMutable(), Owner))
            .ToList();
        EnterCombatWithoutExitingEvent<FakeMerchantEventEncounter>(extraRewards, false);
        return Task.CompletedTask;
    }
}