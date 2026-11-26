using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
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
/// Multiplayer-exclusive shared voting event ("谁去推门 / The Spiked Door").
/// Appears across Act 2 (Depths / Hive) and Act 3 (Glory) when Players.Count &gt; 1:
/// - Push Together: every player takes 12 unblockable damage and gains 1 random Relic.
/// - Draw Straws: deterministically picks 1 scapegoat across all peers — the scapegoat
///   takes 22 unblockable damage and gains Injury, in exchange for 1 Rare Relic + 100 Gold;
///   every other player takes 0 damage and gains 1 random Potion + 50 Gold (+30G fallback if belt full).
/// - Squeeze Through the Crack: every player randomly downgrades 1 upgraded card (or loses up to 40 Gold).
/// </summary>
public sealed class ThingsSpikedDoor : ThingsEventModel
{
    public const int PushAllDamage = 12;
    public const int ScapegoatDamage = 22;
    public const int ScapegoatGold = 100;
    public const int BystanderGold = 50;
    public const int BystanderFullBeltFallbackGold = 30;
    public const int DetourFallbackGold = 40;
    public const int RareRelicFallbackGold = 150;

    private bool _settling;

    public override bool IsShared => true;

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _settling = false;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars =>
    [
        new DamageVar("PushDamage", PushAllDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new DamageVar("ScapegoatDamage", ScapegoatDamage, ValueProp.Unblockable | ValueProp.Unpowered),
        new GoldVar("ScapegoatGold", ScapegoatGold),
        new GoldVar("BystanderGold", BystanderGold),
        new GoldVar("DetourGold", DetourFallbackGold),
    ];

    public override bool IsAllowed(IRunState runState) =>
        runState.Players.Count > 1
        && runState.Players.Any(player =>
            !player.Creature.IsDead
            && player.Creature.CurrentHp > PushAllDamage);

    public override IEnumerable<LocString> GameInfoOptions =>
        DepthsEventDefaults.DescribeOptions(this, "PUSH_TOGETHER", "DRAW_LOTS", "SQUEEZE_PAST");

    protected override IReadOnlyList<EventOption> GenerateInitialOptions()
    {
        return
        [
            new EventOption(this, PushTogetherAsync, InitialOptionKey("PUSH_TOGETHER"))
                .ThatDoesDamage(PushAllDamage),
            new EventOption(this, DrawLotsAsync, InitialOptionKey("DRAW_LOTS"), HoverTipFactory.FromCard<Injury>()),
            new EventOption(this, SqueezePastAsync, InitialOptionKey("SQUEEZE_PAST")),
        ];
    }

    internal static ulong PickDeterministicScapegoatNetId(IRunState runState)
    {
        List<Player> ordered = runState.Players
            .Where(p => !p.Creature.IsDead)
            .OrderBy(p => p.NetId)
            .ToList();
        if (ordered.Count == 0)
            ordered = runState.Players.OrderBy(p => p.NetId).ToList();
        if (ordered.Count == 0)
            return 0UL;

        unchecked
        {
            uint h = 2166136261u;
            string seedStr = runState.Rng.StringSeed ?? "things_spiked_door";
            foreach (char ch in seedStr)
            {
                h ^= ch;
                h *= 16777619u;
            }
            h ^= (uint)(runState.TotalFloor * 65521 + runState.CurrentActIndex * 409 + ordered.Count * 29);
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            int index = (int)(h % (uint)ordered.Count);
            return ordered[index].NetId;
        }
    }

    private bool BeginSettlement()
    {
        if (IsFinished || _settling || EventOwner.Creature.IsDead)
            return false;
        _settling = true;
        return true;
    }

    private async Task PushTogetherAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        await ThingsEventHelpers.DealUnblockableDamageAsync(player, PushAllDamage);
        if (player.Creature.IsDead)
        {
            Finish("PUSH_TOGETHER.loss");
            return;
        }

        RelicModel relic = RelicFactory.PullNextRelicFromFront(player, Rng).ToMutable();
        await RelicCmd.Obtain(relic, player);
        Finish("PUSH_TOGETHER.description");
    }

    private async Task DrawLotsAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        ulong scapegoatNetId = PickDeterministicScapegoatNetId(player.RunState);
        if (player.NetId == scapegoatNetId)
        {
            await ThingsEventHelpers.DealUnblockableDamageAsync(player, ScapegoatDamage);
            if (player.Creature.IsDead)
            {
                Finish("DRAW_LOTS_VICTIM.loss");
                return;
            }

            await CardPileCmd.AddCurseToDeck<Injury>(player);
            if (ThingsEventHelpers.HasGrabBagRelic(player, RelicRarity.Rare))
                await ThingsEventHelpers.ObtainGrabBagRelicAsync(player, RelicRarity.Rare);
            else
                await PlayerCmd.GainGold(RareRelicFallbackGold, player);

            await PlayerCmd.GainGold(ScapegoatGold, player);
            Finish("DRAW_LOTS_VICTIM.description");
            return;
        }

        await PlayerCmd.GainGold(BystanderGold, player);
        PotionModel? potion = PotionFactory.CreateRandomPotionOutOfCombat(player, Rng);
        if (ThingsEventHelpers.CanReceivePotion(player, potion))
            await PotionCmd.TryToProcure(potion!.ToMutable(), player);
        else
            await PlayerCmd.GainGold(BystanderFullBeltFallbackGold, player);

        Finish("DRAW_LOTS_SAFE.description");
    }

    private async Task SqueezePastAsync()
    {
        if (!BeginSettlement())
            return;

        Player player = EventOwner;
        List<CardModel> upgraded = player.Deck.Cards
            .Where(card => card.IsUpgraded)
            .ToList();

        if (upgraded.Count > 0 && Rng.NextItem(upgraded) is { } victim)
        {
            CardCmd.Downgrade(victim);
            Finish("SQUEEZE_DOWNGRADE.description");
            return;
        }

        int goldLoss = Math.Min(DetourFallbackGold, player.Gold);
        if (goldLoss > 0)
            await PlayerCmd.LoseGold(goldLoss, player, GoldLossType.Stolen);

        Finish("SQUEEZE_GOLD.description");
    }

    private void Finish(string pageSuffix)
    {
        if (!IsFinished)
            SetEventFinished(L10NLookup($"{Id.Entry}.pages.{pageSuffix}"));
    }
}
