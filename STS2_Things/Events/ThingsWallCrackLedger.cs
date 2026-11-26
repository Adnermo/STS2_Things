using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Core.Models;

namespace STS2_Things.Events;

/// <summary>
/// Depths (Act 2) loan-shark ledger event ("墙缝账房 / The Wall-Crack Ledger").
/// - Sign the Ledger: gain 220 Gold + 1 random Relic, take 8 unblockable damage, add 2 Debt curses.
/// - Pawn Relic: lose 1 randomly named tradable Rare or Uncommon Relic -> heal to full HP + gain 1 random Common Relic.
/// - Shove It Back: take 8 unblockable damage as the withered hand rakes your wrist, and leave.
/// </summary>
public sealed class ThingsWallCrackLedger : ThingsEventModel
{
    public const int LoanGold = 220;
    public const int LoanDamage = 8;
    public const int RefuseDamage = 8;
    public const int CurseCount = 2;
    public const int CommonRelicFallbackGold = 60;

    private RelicModel? _pawnRelic;
    private bool _pawnRelicRolled;
    private bool _settling;

    public RelicModel? PawnRelic => _pawnRelic;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _pawnRelic = null;
        _pawnRelicRolled = false;
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new GoldVar("LoanGold", LoanGold),
        new DamageVar("LoanDamage", LoanDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new DamageVar("RefuseDamage", RefuseDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new StringVar("PawnRelic"),
        new CardsVar("CurseCount", CurseCount),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Any(player =>
            !player.Creature.IsDead
            && (player.Creature.CurrentHp > LoanDamage
                || player.Relics.Any(IsPawnableRelic)));

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "SIGN_LOAN", "PAWN_RELIC", "SHOVE_BACK");

    private static bool IsPawnableRelic(RelicModel relic) =>
        relic.IsTradable && (relic.Rarity == RelicRarity.Rare || relic.Rarity == RelicRarity.Uncommon);

    private RelicModel? EnsurePawnRelicRolled()
    {
        if (!_pawnRelicRolled && Owner != null)
        {
            _pawnRelicRolled = true;
            List<RelicModel> candidates = EventOwner.Relics.Where(IsPawnableRelic).ToList();
            _pawnRelic = candidates.Count > 0 ? Rng.NextItem(candidates) : null;
        }

        ((StringVar)DynamicVars["PawnRelic"]).StringValue = _pawnRelic?.Title.GetFormattedText() ?? string.Empty;
        return _pawnRelic;
    }

    public override void CalculateVars()
    {
        if (Owner == null)
            return;

        EnsurePawnRelicRolled();
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        RelicModel? pawnRelic = EnsurePawnRelicRolled();

        EventOption loanOption = new EventOption(
                this,
                SignLoanAsync,
                InitialOptionKey("SIGN_LOAN"),
                HoverTipFactory.FromCard<Debt>())
            .ThatDoesDamage(LoanDamage);

        EventOption pawnOption = pawnRelic != null
            ? new EventOption(
                    this,
                    PawnRelicAsync,
                    InitialOptionKey("PAWN_RELIC"),
                    pawnRelic.HoverTips)
                .ThatHasDynamicTitle()
            : new EventOption(this, null, InitialOptionKey("PAWN_LOCKED"));

        EventOption shoveOption = new EventOption(
                this,
                ShoveBackAsync,
                InitialOptionKey("SHOVE_BACK"))
            .ThatDoesDamage(RefuseDamage);

        var options = new List<EventOption>
        {
            loanOption,
            pawnOption,
            shoveOption,
        };
        if (pawnRelic == null && EventOwner.Creature.CurrentHp <= Math.Min(LoanDamage, RefuseDamage))
        {
            options.Add(new EventOption(this, LeaveAsync, InitialOptionKey("LEAVE")));
        }

        return options;
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private async Task SignLoanAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        await ThingsEventHelpers.DealUnblockableDamageAsync(player, LoanDamage);
        if (player.Creature.IsDead)
        {
            Finish("SIGN_LOAN.loss");
            return;
        }

        await PlayerCmd.GainGold(LoanGold, player);
        RelicModel randomRelic = RelicFactory.PullNextRelicFromFront(player, Rng).ToMutable();
        await RelicCmd.Obtain(randomRelic, player);
        await CardPileCmd.AddCursesToDeck(
            [ModelDb.Card<Debt>(), ModelDb.Card<Debt>()],
            player);

        Finish("SIGN_LOAN.description");
    }

    private async Task PawnRelicAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        RelicModel? pawnRelic = _pawnRelic;
        if (pawnRelic == null || !player.Relics.Contains(pawnRelic))
        {
            _settling = false;
            _pawnRelicRolled = false;
            SetEventState(InitialDescription, GenerateInitialOptions());
            return;
        }

        await RelicCmd.Remove(pawnRelic);

        int missingHp = player.Creature.MaxHp - player.Creature.CurrentHp;
        if (missingHp > 0)
            await CreatureCmd.Heal(player.Creature, missingHp);

        if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Common))
            await ThingsEventHelpers.ObtainGrabBagRelicAsync(player, RelicRarity.Common);
        else
            await PlayerCmd.GainGold(CommonRelicFallbackGold, player);

        Finish("PAWN_RELIC.description");
    }

    private async Task ShoveBackAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        await ThingsEventHelpers.DealUnblockableDamageAsync(player, RefuseDamage);
        if (player.Creature.IsDead)
        {
            Finish("SHOVE_BACK.loss");
            return;
        }

        Finish("SHOVE_BACK.description");
    }

    private Task LeaveAsync()
    {
        if (!BeginSettlement())
            return Task.CompletedTask;

        Finish("LEAVE.description");
        return Task.CompletedTask;
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
