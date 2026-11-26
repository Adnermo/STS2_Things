using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Localization;
using STS2_Things.Core.Models;
using STS2_Things.Relics;

namespace STS2_Things.Events;

public sealed class ThingsCrowdedWard : ThingsEventModel
{
    public const int LocalHeal = 26;
    public const int BandageHeal = 12;
    public const int BandageGoldCost = 35;
    private bool _settling;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new HealVar("LocalHeal", LocalHeal), new HealVar("BandageHeal", BandageHeal),
        new GoldVar("BandageGold", BandageGoldCost),
        new IntVar("Combats", ThingsAnestheticChart.Duration), new IntVar("Cards", ThingsAnestheticChart.DrawPenalty),
        new LocalizedStringVar("Curse", static _ => new LocString("cards", "NORMALITY.title").GetFormattedText()),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(p => !p.Creature.IsDead && p.Creature.CurrentHp <= p.Creature.MaxHp * 0.75m);
    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "TREAT", "ANESTHETIZE", "BANDAGES", "LEAVE");

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        var options = new List<EventOption>
        {
            new EventOption(this, Treat, InitialOptionKey("TREAT"), HoverTipFactory.FromCardWithCardHoverTips<Normality>()),
            new EventOption(this, Anesthetize, InitialOptionKey("ANESTHETIZE"),
                HoverTipFactory.FromRelic(ModelDb.Relic<ThingsAnestheticChart>())),
        };
        if (EventOwner.Gold >= BandageGoldCost)
            options.Add(new EventOption(this, TakeBandages, InitialOptionKey("BANDAGES")));
        options.Add(new EventOption(this, Leave, InitialOptionKey("LEAVE")));
        return options;
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || Owner!.Creature.IsDead) return false;
        _settling = true;
        return true;
    }

    private async Task Treat()
    {
        if (!BeginSettlement()) return;
        await CreatureCmd.Heal(Owner!.Creature, Owner.Creature.MaxHp - Owner.Creature.CurrentHp);
        await CardPileCmd.AddCurseToDeck<Normality>(Owner);
        SetEventFinished(L10NLookup("THINGS_CROWDED_WARD.pages.TREATED.description"));
    }

    private async Task Anesthetize()
    {
        if (!BeginSettlement()) return;
        // Obtain the saved native relic before healing; no hidden, unsaved debt.
        var existing = Owner!.Relics.OfType<ThingsAnestheticChart>().FirstOrDefault();
        if (existing == null) await RelicCmd.Obtain(ModelDb.Relic<ThingsAnestheticChart>().ToMutable(), Owner);
        else existing.Extend();
        await CreatureCmd.Heal(Owner.Creature, LocalHeal);
        SetEventFinished(L10NLookup("THINGS_CROWDED_WARD.pages.ANESTHETIZED.description"));
    }

    private async Task TakeBandages()
    {
        if (IsFinished || _settling) return;
        if (EventOwner.Gold < BandageGoldCost)
        {
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }
        if (!BeginSettlement()) return;
        await PlayerCmd.LoseGold(BandageGoldCost, Owner!, GoldLossType.Spent);
        await CreatureCmd.Heal(Owner!.Creature, BandageHeal);
        SetEventFinished(L10NLookup("THINGS_CROWDED_WARD.pages.BANDAGES.description"));
    }

    private Task Leave()
    {
        if (!BeginSettlement()) return Task.CompletedTask;
        SetEventFinished(L10NLookup("THINGS_CROWDED_WARD.pages.LEAVE.description"));
        return Task.CompletedTask;
    }
}
