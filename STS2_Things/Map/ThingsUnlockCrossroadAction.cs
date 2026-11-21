using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2_Things.Modifiers;

namespace STS2_Things.Map;

public enum CrossroadPurchaseResult { Purchased, AlreadyOpen, Stale, NotAtRoad, InsufficientGold }

public static class Crossroads
{
    private static readonly ConditionalWeakTable<RunState, ThingsCrossroads> Ledgers = new();
    private static readonly ConditionalWeakTable<SerializableRun, SerializableModifier> SerializedLedgers = new();

    public static event Action<IRunState>? Changed;

    public static ThingsCrossroads? Ledger(IRunState run) =>
        run is RunState state && Ledgers.TryGetValue(state, out var attached)
            ? attached
            : run.Modifiers.OfType<ThingsCrossroads>().FirstOrDefault();

    internal static void Attach(RunState run, ThingsCrossroads ledger)
    {
        ledger.Bind(run);
        Ledgers.AddOrUpdate(run, ledger);
    }

    internal static bool IsLedgerId(ModelId? id) =>
        id != null &&
        string.Equals(id.Category, "MODIFIER", StringComparison.Ordinal) &&
        string.Equals(id.Entry, "THINGS_CROSSROADS", StringComparison.Ordinal);

    internal static void SetSerializedLedger(SerializableRun save, SerializableModifier ledger) =>
        SerializedLedgers.AddOrUpdate(save, ledger);

    internal static bool TryGetSerializedLedger(SerializableRun save, out SerializableModifier? ledger) =>
        SerializedLedgers.TryGetValue(save, out ledger);

    internal static SerializableModifier? ExtractSerializedLedger(SerializableRun? save)
    {
        if (save?.Modifiers == null) return null;
        SerializableModifier? extracted = save.Modifiers.FirstOrDefault(m => IsLedgerId(m.Id));
        if (extracted != null)
        {
            SerializedLedgers.AddOrUpdate(save, extracted);
            save.Modifiers.RemoveAll(m => IsLedgerId(m.Id));
            return extracted;
        }

        return SerializedLedgers.TryGetValue(save, out var existing) ? existing : null;
    }

    internal static SerializableModifier AnonymizeSerializedLedger(SerializableModifier ledger)
    {
        if (ledger.Props?.strings is not { Count: > 0 } strings)
            return ledger;

        List<SavedProperties.SavedProperty<string>>? updated = null;
        for (int i = 0; i < strings.Count; i++)
        {
            var entry = strings[i];
            if (!string.Equals(entry.name, nameof(ThingsCrossroads.Data), StringComparison.Ordinal) ||
                string.IsNullOrEmpty(entry.value))
                continue;

            string rewritten = Regex.Replace(
                entry.value,
                "\"Payer\":(\\d+)",
                static m => ulong.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out ulong payer) && payer != 0
                    ? "\"Payer\":" + IdAnonymizer.Anonymize(payer).ToString(CultureInfo.InvariantCulture)
                    : m.Value);
            if (!string.Equals(rewritten, entry.value, StringComparison.Ordinal))
            {
                updated ??= [.. strings];
                updated[i] = new SavedProperties.SavedProperty<string>(entry.name, rewritten);
            }
        }

        if (updated == null)
            return ledger;

        return new SerializableModifier
        {
            Id = ledger.Id,
            Props = new SavedProperties
            {
                ints = ledger.Props.ints,
                bools = ledger.Props.bools,
                strings = updated,
                intArrays = ledger.Props.intArrays,
                modelIds = ledger.Props.modelIds,
                cards = ledger.Props.cards,
                cardArrays = ledger.Props.cardArrays,
            },
        };
    }

    public static async Task<CrossroadPurchaseResult> Purchase(Player payer, int act, ulong fingerprint, MapCoord from, MapCoord to, int quotedPrice)
    {
        if (payer.RunState is not RunState run || !run.Players.Contains(payer)) return CrossroadPurchaseResult.Stale;
        if (run.CurrentActIndex != act || quotedPrice != CrossroadPlan.Price || run.Map == null ||
            CrossroadPlan.Fingerprint(run.Map) != fingerprint) return CrossroadPurchaseResult.Stale;
        if (run.CurrentMapCoord != from || run.VisitedMapCoords.Contains(to)) return CrossroadPurchaseResult.NotAtRoad;
        var ledger = Ledger(run);
        var plan = ledger?.EnsurePlan(run, run.Map, act);
        var road = plan?.Roads.FirstOrDefault(r => r.Touches(from) && r.Other(from) == to);
        if (road == null) return CrossroadPurchaseResult.Stale;
        if (road.IsOpen) return CrossroadPurchaseResult.AlreadyOpen;
        if (run.Map.GetPoint(from) is not { } source || run.Map.GetPoint(to) is not { } destination ||
            source.Children.Contains(destination) || destination.Children.Contains(source)) return CrossroadPurchaseResult.Stale;
        if (payer.Gold < CrossroadPlan.Price) return CrossroadPurchaseResult.InsufficientGold;

        // Native gold spending is synchronous. Reserve before yielding so two
        // confirmed requests cannot charge twice or open a reverse cycle.
        road.FromColumn = from.col;
        road.Payer = payer.NetId;
        await PlayerCmd.LoseGold(CrossroadPlan.Price, payer, GoldLossType.Spent);
        source.AddChildPoint(destination);
        ledger!.Commit();
        Changed?.Invoke(run);
        return CrossroadPurchaseResult.Purchased;
    }
}

public sealed class ThingsUnlockCrossroadAction(Player player, int act, ulong fingerprint, MapCoord from, MapCoord to, int price) : GameAction
{
    public override ulong OwnerId => player.NetId;
    public override GameActionType ActionType => GameActionType.NonCombat;
    protected override async Task ExecuteAction() =>
        await Crossroads.Purchase(player, act, fingerprint, from, to, price);
    public override INetAction ToNetAction() => new ThingsNetUnlockCrossroadAction { Act = act, Fingerprint = fingerprint, From = from, To = to, Price = price };
}

public struct ThingsNetUnlockCrossroadAction : INetAction, IPacketSerializable
{
    public int Act;
    public ulong Fingerprint;
    public MapCoord From;
    public MapCoord To;
    public int Price;
    public GameAction ToGameAction(Player player) => new ThingsUnlockCrossroadAction(player, Act, Fingerprint, From, To, Price);
    public void Serialize(PacketWriter writer)
    { writer.WriteInt(Act); writer.WriteULong(Fingerprint); writer.Write(From); writer.Write(To); writer.WriteInt(Price); }
    public void Deserialize(PacketReader reader)
    { Act = reader.ReadInt(); Fingerprint = reader.ReadULong(); From = reader.Read<MapCoord>(); To = reader.Read<MapCoord>(); Price = reader.ReadInt(); }
}
