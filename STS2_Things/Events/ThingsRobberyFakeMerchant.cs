using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;
using STS2_Things.Encounters;

namespace STS2_Things.Events;

public sealed class ThingsRobberyFakeMerchant : ThingsEventModel
{
    public const int StealDamage = 7;
    public const int GoldsCount = 45;
    public const int FakeRelicsCount = 2;
    public const int TrueRelicsCount = 1;

    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }
    // 改为懒加载，避免类首次访问时 ModelDb 尚未初始化
    private static RelicModel[]? _fakeRelicPool;
    private static RelicModel[] FakeRelicPool => _fakeRelicPool ??= new RelicModel[] //所有假遗物
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

    private static RelicModel[]? _trueRelicPool;
    private static RelicModel[] TrueRelicPool => _trueRelicPool ??= new RelicModel[] //所有真遗物
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

    public override EncounterModel CanonicalEncounter => ModelDb.Encounter<ThingsRobberyFakeMerchantEncounter>();

    public override bool IsShared => true;

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 0 && runState.Players.All(p => !p.Creature.IsDead);

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new IntVar("GoldsCount", GoldsCount),
        new IntVar("FakeRelicsCount", FakeRelicsCount),
        new IntVar("TrueRelicsCount", TrueRelicsCount),
        new DamageVar("StealDamage", StealDamage, ValueProp.Unblockable | ValueProp.Unpowered)
    ];

    private Rng CreatePlayerEventRng(ulong salt)
    {
        IRunState runState = EventOwner.RunState;
        int playerIndex = 0;
        for (int i = 0; i < runState.Players.Count; i++)
        {
            if (ReferenceEquals(runState.Players[i], EventOwner) || runState.Players[i].NetId == EventOwner.NetId)
            {
                playerIndex = i;
                break;
            }
        }

        ulong z = runState.Rng.Seed
            ^ ((ulong)(uint)(playerIndex + 1) * 0x9E3779B97F4A7C15UL)
            ^ ((ulong)(uint)(runState.TotalFloor + 1) * 0xBF58476D1CE4E5B9UL)
            ^ salt;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x9E3779B97F4A7C15UL;
        ulong mixed = z ^ (z >> 31);
        uint seed32 = unchecked((uint)(mixed ^ (mixed >> 32)));
        return new Rng(seed32);
    }

    private List<RelicModel> RollUnownedRelics(IReadOnlyList<RelicModel> pool, int count, Rng rng)
    {
        var unowned = pool
            .Where(candidate => EventOwner.Relics.All(owned => owned.Id != candidate.Id))
            .ToList()
            .UnstableShuffle(rng);
        if (unowned.Count >= count)
            return unowned.Take(count).ToList();

        RelicModel circlet = ModelDb.Relic<Circlet>();
        while (unowned.Count < count)
            unowned.Add(circlet);
        return unowned;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        return
        [
            new EventOption(this, TakeRelics, InitialOptionKey("TAKERELICS"))
                .ThatDoesDamage(StealDamage),
            new EventOption(this, TakeGolds, InitialOptionKey("TAKEGOLDS"))
                .ThatDoesDamage(StealDamage),
            new EventOption(this, BeginFight, InitialOptionKey("BEGINFIGHT")),
            new EventOption(this, Leave, InitialOptionKey("LEAVE")),
        ];
    }

    private async Task TakeGolds()
    {
        if (IsFinished || _settling) return;
        _settling = true;
        Player player = EventOwner;
        await ThingsEventHelpers.DealUnblockableDamageAsync(player, StealDamage);
        if (player.Creature.IsDead)
        {
            SetEventFinished(L10NLookup("THINGS_ROBBERY_FAKE_MERCHANT.pages.STEAL.loss"));
            return;
        }

        await PlayerCmd.GainGold((int)DynamicVars["GoldsCount"].BaseValue, player);
        SetEventFinished(L10NLookup("THINGS_ROBBERY_FAKE_MERCHANT.pages.TAKEGOLDS.description"));
    }

    private async Task TakeRelics()
    {
        if (IsFinished || _settling) return;
        _settling = true;
        Player player = EventOwner;
        await ThingsEventHelpers.DealUnblockableDamageAsync(player, StealDamage);
        if (player.Creature.IsDead)
        {
            SetEventFinished(L10NLookup("THINGS_ROBBERY_FAKE_MERCHANT.pages.STEAL.loss"));
            return;
        }

        var relics = RollUnownedRelics(
            FakeRelicPool,
            (int)DynamicVars["FakeRelicsCount"].BaseValue,
            CreatePlayerEventRng(0xFA4E5EED01UL));

        foreach (var relic in relics)
            await RelicCmd.Obtain(relic.ToMutable(), player);

        SetEventFinished(L10NLookup("THINGS_ROBBERY_FAKE_MERCHANT.pages.TAKERELICS.description"));
    }

    private Task Leave()
    {
        if (IsFinished || _settling) return Task.CompletedTask;
        _settling = true;
        SetEventFinished(L10NLookup("THINGS_ROBBERY_FAKE_MERCHANT.pages.LEAVE.description"));
        return Task.CompletedTask;
    }

    private Task BeginFight()
    {
        if (IsFinished || _settling) return Task.CompletedTask;
        SetEventState(L10NLookup("THINGS_ROBBERY_FAKE_MERCHANT.pages.BEGINFIGHT.description"),
            [new EventOption(this, Fight, "THE_LANTERN_KEY.pages.KEEP_THE_KEY.options.FIGHT")]); //别动这个 用自带的文本会卡住
        return Task.CompletedTask;
    }

    private async Task Fight()
    {
        if (IsFinished || _settling) return;
        _settling = true;
        // 两名原版假商人各为原版三分之一生命、攻击伤害降低 40%，合起来接近一层精英；共享场景只调整一次。
        var combatState = CombatLayoutState;
        await ((ThingsRobberyFakeMerchantEncounter)combatState.Encounter!).PrepareForFight(combatState);

        var chosenRelics = RollUnownedRelics(
            TrueRelicPool,
            (int)DynamicVars["TrueRelicsCount"].BaseValue,
            CreatePlayerEventRng(0x720E5EED02UL));
        foreach (var relic in chosenRelics)
        {
            EventOwner.RelicGrabBag.Remove(relic);
            EventOwner.RunState.SharedRelicGrabBag.Remove(relic);
        }

        var extraRewards = chosenRelics
            .Select(relic => (Reward)new RelicReward(relic.ToMutable(), EventOwner))
            .ToList();
        EnterCombatWithoutExitingEvent<ThingsRobberyFakeMerchantEncounter>(extraRewards, false);
    }
}
