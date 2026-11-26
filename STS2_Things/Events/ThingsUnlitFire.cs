using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using STS2_Things.Core.Models;
using STS2_Things.Relics;

namespace STS2_Things.Events;

public sealed class ThingsUnlitFire : ThingsEventModel
{
    public const int FuelCost = 55;
    private List<RestSiteOption> _options = [];
    private bool CanMakePlan => !EventOwner.Creature.IsDead && EventOwner.GetRelic<ThingsBorrowedEmber>() == null &&
        ThingsBorrowedEmber.FindNextRestSites(EventOwner.RunState).Count > 0;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _options = [];
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [new HealVar(0m), new GoldVar(FuelCost), new IntVar("BonusHeal", ThingsBorrowedEmber.KindledHeal)];

    public override bool IsAllowed(IRunState runState) =>
        ThingsBorrowedEmber.FindNextRestSites(runState).Count > 0
        && runState.Players.Any(player => !player.Creature.IsDead && player.GetRelic<ThingsBorrowedEmber>() == null);

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "BORROW", "FUEL", "LEAVE");

    protected override Task BeforeEventStarted(bool isPreFinished)
    {
        // Generate once so native relic restrictions and modified smith counts
        // are honored without repeatedly consuming option-generation RNG.
        if (!isPreFinished) _options = RestSiteOption.Generate(EventOwner);
        return Task.CompletedTask;
    }

    public override void CalculateVars() => DynamicVars.Heal.BaseValue = HealRestSiteOption.GetHealAmount(EventOwner);

    public override IEnumerable<string> GetAssetPaths(IRunState runState) => base.GetAssetPaths(runState)
        .Concat(NCardSmithVfx.AssetPaths).Distinct();

    private IEnumerable<IHoverTip> PlanTips(bool kindled)
    {
        var preview = (ThingsBorrowedEmber)ModelDb.Relic<ThingsBorrowedEmber>().ToMutable();
        preview.Configure(kindled);
        return preview.HoverTips;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        bool rest = _options.OfType<HealRestSiteOption>().Any(option => option.IsEnabled);
        bool smith = _options.OfType<SmithRestSiteOption>().Any(option => option.IsEnabled);
        bool canBorrow = CanMakePlan && (rest || smith);
        string borrowKey = !canBorrow ? "BORROW_LOCKED"
            : rest && smith ? "BORROW"
            : smith ? "SMITH"
            : "REST";
        bool fuel = CanMakePlan && EventOwner.Gold >= FuelCost;
        return
        [
            new EventOption(this, canBorrow ? BorrowRestAndSmith : null, InitialOptionKey(borrowKey), PlanTips(false)),
            new EventOption(this, fuel ? AddFuel : null, InitialOptionKey(fuel ? "FUEL" : "FUEL_LOCKED"), PlanTips(true)),
            new EventOption(this, Leave, InitialOptionKey("LEAVE")),
        ];
    }

    private async Task MakePlan(bool kindled)
    {
        var ember = (ThingsBorrowedEmber)ModelDb.Relic<ThingsBorrowedEmber>().ToMutable();
        ember.Configure(kindled);
        await RelicCmd.Obtain(ember, EventOwner);
    }

    private async Task BorrowRestAndSmith()
    {
        if (IsFinished || !CanMakePlan) return;
        var heal = _options.OfType<HealRestSiteOption>().FirstOrDefault(option => option.IsEnabled);
        var smith = _options.OfType<SmithRestSiteOption>().FirstOrDefault(option => option.IsEnabled);
        if (heal == null && smith == null) return;
        if (smith != null && !await smith.OnSelect())
        {
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }
        if (IsFinished || !CanMakePlan) return;
        await MakePlan(false);
        if (heal != null) await PlayerCmd.MimicRestSiteHeal(EventOwner);
        if (smith != null && LocalContext.IsMe(EventOwner)) await smith.DoLocalPostSelectVfx();
        Finish(heal != null && smith != null ? "BORROW" : smith != null ? "SMITH" : "REST");
    }

    private async Task AddFuel()
    {
        if (IsFinished || !CanMakePlan || EventOwner.Gold < FuelCost) return;
        await PlayerCmd.LoseGold(FuelCost, EventOwner, GoldLossType.Spent);
        await MakePlan(true);
        Finish("FUEL");
    }

    private Task Leave()
    {
        if (IsFinished) return Task.CompletedTask;
        Finish("LEAVE");
        return Task.CompletedTask;
    }

    private void Finish(string page) => SetEventFinished(L10NLookup($"THINGS_UNLIT_FIRE.pages.{page}.description"));
}
