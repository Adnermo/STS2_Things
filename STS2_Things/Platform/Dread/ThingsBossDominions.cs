#if STS2_V113_1
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Dread;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Encounters;
using STS2_Things.Powers;

namespace STS2_Things.Platform.Dread;

public sealed class ThingsOriginFogmogDominion : DominionModel
{
    public override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromPowerWithPowerHoverTips<RegenPower>(2);

    protected override async Task AfterEnemiesEnteredCombat(IEnumerable<Creature> creatures)
    {
        AssertMutable();
        Creature[] aliveEnemies = creatures.Where(c => c.IsAlive && c.Side == CombatSide.Enemy).ToArray();
        if (aliveEnemies.Length == 0)
            return;

        RoomType? roomType = aliveEnemies[0].CombatState?.Encounter?.RoomType
            ?? (aliveEnemies[0].CombatState?.RunState?.CurrentRoom as CombatRoom)?.Encounter?.RoomType
            ?? (RunState?.CurrentRoom as CombatRoom)?.Encounter?.RoomType;
        decimal amount = roomType == RoomType.Elite ? 3m : 2m;
        foreach (Creature creature in aliveEnemies)
        {
            if (!creature.IsAlive)
                continue;
            await PowerCmd.Apply<RegenPower>(new ThrowingPlayerChoiceContext(), creature, amount, null, null);
        }
    }
}

public sealed class ThingsScaleBeetleDominion : DominionModel
{
    public override IEnumerable<IHoverTip> ExtraHoverTips =>
    [
        HoverTipFactory.FromPower<ThingsScaleUpPower>(),
        HoverTipFactory.FromPower<ThingsScaleDownPower>()
    ];

    protected override async Task AfterEnemiesEnteredCombat(IEnumerable<Creature> creatures)
    {
        AssertMutable();
        Creature[] aliveEnemies = creatures.Where(c => c.IsAlive && c.Side == CombatSide.Enemy).ToArray();
        if (aliveEnemies.Length == 0)
            return;

        await PowerCmd.Apply<ThingsScaleUpPower>(new ThrowingPlayerChoiceContext(), aliveEnemies, 10m, null, null);
    }

    protected override async Task AfterPlayersEnteredCombat(IReadOnlyList<Player> players)
    {
        AssertMutable();
        Creature[] alivePlayers = players.Select(p => p.Creature).Where(c => c.IsAlive).ToArray();
        if (alivePlayers.Length == 0)
            return;

        await PowerCmd.Apply<ThingsScaleDownPower>(new ThrowingPlayerChoiceContext(), alivePlayers, 10m, null, null);
    }
}

public sealed class ThingsBowlbugProgenitorDominion : DominionModel
{
    public override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromPower<StrengthPower>()];

    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        await base.AfterRoomEntered(room);
        if (room is CombatRoom combatRoom)
        {
            Creature[] enemies = combatRoom.Enemies.ToArray();
            if (enemies.Length == 1 && enemies[0].IsAlive)
            {
                await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), enemies[0], 2m, null, null);
            }
        }
    }

    public override async Task AfterDeath(
        PlayerChoiceContext choiceContext,
        Creature creature,
        CardModel? source,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        AssertMutable();
        if (CombatManager.Instance.IsOverOrEnding || wasRemovalPrevented || creature.Side != CombatSide.Enemy || creature.CombatState is not { } combat)
            return;

        Creature[] livingAllies = combat.Enemies.Where(e => e.IsAlive && e != creature).ToArray();
        if (livingAllies.Length == 0)
            return;

        await PowerCmd.Apply<StrengthPower>(choiceContext, livingAllies, 2m, null, null);
    }
}

public sealed class ThingsCaveGodDominion : DominionModel
{
    public override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromCardWithCardHoverTips<Debris>();

    protected override async Task AfterPlayersEnteredCombat(IReadOnlyList<Player> players)
    {
        AssertMutable();
        foreach (Player player in players)
        {
            await CardPileCmd.AddToCombatAndPreview<Debris>(
                player.Creature, PileType.Draw, 2, player, CardPilePosition.Random);
        }
    }
}

public sealed class ThingsHumanFaceTowerDominion : DominionModel
{
    private Dictionary<ulong, CardType> _lastCardType = new();
    private Dictionary<ulong, int> _consecutiveCount = new();

    public override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.Static(StaticHoverTip.Block)];

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _lastCardType = new Dictionary<ulong, CardType>();
        _consecutiveCount = new Dictionary<ulong, int>();
    }

    protected override void AfterCloned()
    {
        base.AfterCloned();
        _lastCardType = new Dictionary<ulong, CardType>();
        _consecutiveCount = new Dictionary<ulong, int>();
    }

    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        AssertMutable();
        _lastCardType.Clear();
        _consecutiveCount.Clear();
        await base.AfterRoomEntered(room);
    }

    public override Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        if (side == CombatSide.Player)
        {
            AssertMutable();
            _lastCardType.Clear();
            _consecutiveCount.Clear();
        }
        return Task.CompletedTask;
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        AssertMutable();
        if (CombatManager.Instance.IsOverOrEnding)
            return;
        if (cardPlay.Card.Owner is not { } player || player.Creature.CombatState is not { } combat)
            return;

        ulong playerId = player.NetId;
        CardType cardType = cardPlay.Card.Type;
        int count = _lastCardType.TryGetValue(playerId, out CardType lastType) && lastType == cardType
            ? _consecutiveCount.GetValueOrDefault(playerId) + 1
            : 1;

        if (count >= 3)
        {
            _consecutiveCount[playerId] = 0;
            _lastCardType.Remove(playerId);

            foreach (Creature enemy in combat.Enemies.Where(e => e.IsAlive).ToArray())
            {
                if (enemy.IsAlive && !CombatManager.Instance.IsOverOrEnding)
                {
                    await CreatureCmd.GainBlock(enemy, 6m, ValueProp.Unpowered, null);
                }
            }
            return;
        }

        _lastCardType[playerId] = cardType;
        _consecutiveCount[playerId] = count;
    }
}

public sealed class ThingsAbyssalInkmawDominion : DominionModel
{
    public override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromPowerWithPowerHoverTips<WeakPower>(1)
            .Concat(HoverTipFactory.FromPowerWithPowerHoverTips<FrailPower>(1))
            .Concat(HoverTipFactory.FromCardWithCardHoverTips<Slimed>());

    protected override async Task AfterPlayersEnteredCombat(IReadOnlyList<Player> players)
    {
        AssertMutable();
        Creature[] alivePlayers = players.Select(p => p.Creature).Where(c => c.IsAlive).ToArray();
        if (alivePlayers.Length > 0)
        {
            await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(), alivePlayers, 1m, null, null);
            await PowerCmd.Apply<FrailPower>(new ThrowingPlayerChoiceContext(), alivePlayers, 1m, null, null);
        }

        foreach (Player player in players)
        {
            await CardPileCmd.AddToCombatAndPreview<Slimed>(
                player.Creature, PileType.Draw, 1, player, CardPilePosition.Random);
        }
    }
}

public sealed class ThingsGravetideSlugDominion : DominionModel
{
    public override IEnumerable<IHoverTip> ExtraHoverTips =>
        HoverTipFactory.FromPowerWithPowerHoverTips<RegenPower>(3)
            .Concat(HoverTipFactory.FromCardWithCardHoverTips<Slimed>());

    protected override async Task AfterEnemiesEnteredCombat(IEnumerable<Creature> creatures)
    {
        AssertMutable();
        Creature[] aliveEnemies = creatures.Where(c => c.IsAlive && c.Side == CombatSide.Enemy).ToArray();
        if (aliveEnemies.Length == 0)
            return;

        await PowerCmd.Apply<RegenPower>(new ThrowingPlayerChoiceContext(), aliveEnemies, 3m, null, null);
    }

    protected override async Task AfterPlayersEnteredCombat(IReadOnlyList<Player> players)
    {
        AssertMutable();
        foreach (Player player in players)
        {
            await CardPileCmd.AddToCombatAndPreview<Slimed>(
                player.Creature, PileType.Draw, 2, player, CardPilePosition.Random);
        }
    }
}

public sealed class ThingsTheLegacyDominion : DominionModel
{
    public override IEnumerable<IHoverTip> ExtraHoverTips => [];

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        AssertMutable();
        if (CombatManager.Instance.IsOverOrEnding)
            return;
        if (cardPlay.Card.Owner?.Creature is { IsAlive: true } creature && creature.CombatState != null)
        {
            await CreatureCmd.Damage(choiceContext, creature, 1m, ValueProp.Unpowered, null, null);
        }
    }
}

[HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.GetDominion))]
internal static class ThingsEncounterDominionPatch
{
    [HarmonyPostfix]
    private static void Postfix(EncounterModel __instance, ref DominionModel? __result)
    {
        if (__result != null)
            return;

        __result = __instance switch
        {
            ThingsOriginFogmogBossEncounter => ModelDb.Dominion<ThingsOriginFogmogDominion>(),
            ThingsScaleBeetleBossEncounter => ModelDb.Dominion<ThingsScaleBeetleDominion>(),
            ThingsBowlbugProgenitorBossEncounter => ModelDb.Dominion<ThingsBowlbugProgenitorDominion>(),
            ThingsCaveGodBossEncounter => ModelDb.Dominion<ThingsCaveGodDominion>(),
            ThingsHumanFaceTowerBossEncounter => ModelDb.Dominion<ThingsHumanFaceTowerDominion>(),
            ThingsAbyssalInkmawBossEncounter => ModelDb.Dominion<ThingsAbyssalInkmawDominion>(),
            ThingsGravetideSlugBossEncounter => ModelDb.Dominion<ThingsGravetideSlugDominion>(),
            ThingsTheLegacyBossEncounter => ModelDb.Dominion<ThingsTheLegacyDominion>(),
            _ => null
        };
    }
}
#endif
