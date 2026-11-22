using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using STS2_Things.Platform;

namespace STS2_Things.Diagnostics;

internal static class BugSnapshot
{
    // Cap emitted snapshot/route player arrays at 8 so uploads stay backward-compatible with
    // any live Cloudflare Worker not yet redeployed with the 32-player validation cap, while
    // guaranteeing the local player is always present in the slice and recording truthful totals.
    private const int MaxWireSnapshotPlayers = 8;

    private sealed class CachedRouteState(
        WeakReference<RunState> runRef,
        int actCount,
        int totalPoints,
        int currentActIndex,
        int actFloor,
        int totalFloor,
        int lastPointRoomsCount,
        int lastPointPlayerStatsCount,
        int playerCount,
        int rawLocalPlayerIndex,
        JsonArray sanitizedRoute,
        int redactions,
        bool integerEncodingChanged)
    {
        internal WeakReference<RunState> RunRef { get; } = runRef;
        internal int ActCount { get; } = actCount;
        internal int TotalPoints { get; } = totalPoints;
        internal int CurrentActIndex { get; } = currentActIndex;
        internal int ActFloor { get; } = actFloor;
        internal int TotalFloor { get; } = totalFloor;
        internal int LastPointRoomsCount { get; } = lastPointRoomsCount;
        internal int LastPointPlayerStatsCount { get; } = lastPointPlayerStatsCount;
        internal int PlayerCount { get; } = playerCount;
        internal int RawLocalPlayerIndex { get; } = rawLocalPlayerIndex;
        internal JsonArray SanitizedRoute { get; } = sanitizedRoute;
        internal int Redactions { get; } = redactions;
        internal bool IntegerEncodingChanged { get; } = integerEncodingChanged;
    }

    private static readonly string Session = Guid.NewGuid().ToString("N")[..8];
    private sealed class Identity { internal string Value { get; } = "entity-" + Session + "-" + Interlocked.Increment(ref _next); }
    private static long _next;
    private static readonly ConditionalWeakTable<object, Identity> Ids = new();
    private static readonly ConcurrentDictionary<(Type Type, string Name), FieldInfo?> FieldCache = new();
    private static readonly ConcurrentDictionary<(Type Type, string Name), (PropertyInfo? Property, FieldInfo? Field)> MemberCache = new();
    private static CachedRouteState? _cachedRoute;

    internal static string? InstanceId(object? value) => value is null ? null : Ids.GetValue(value, _ => new Identity()).Value;
    internal static JsonObject Object(object value) => JsonSerializer.SerializeToNode(value)!.AsObject();
    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0d;

    private static object Card(CardModel card)
    {
        try
        {
            return new
            {
                instance_id = InstanceId(card),
                id = card.Id.ToString(),
                upgrade = card.CurrentUpgradeLevel,
                // Observation must not re-run global cost or playability hooks. Those
                // are gameplay paths (and can be expensive with rune/character mods).
                // The existing UI snapshot retains the displayed cost when available.
                energy = card.EnergyCost.GetWithModifiers(CostModifiers.Local),
                energy_scope = "local",
                costs_x = card.EnergyCost.CostsX,
                stars = card.CurrentStarCost,
                type = card.Type.ToString(),
                target_type = card.TargetType.ToString(),
                playable = (bool?)null,
                affliction = card.Affliction?.Id.ToString(),
                enchantment = card.Enchantment?.Id.ToString(),
                native = JsonSerializer.SerializeToNode(card.ToSerializable(), JsonSerializationUtility.GetTypeInfo<MegaCrit.Sts2.Core.Saves.Runs.SerializableCard>()),
            };
        }
        catch (Exception ex)
        {
            return new { instance_id = InstanceId(card), id = card.Id.ToString(), unavailable = true, error = Error(ex) };
        }
    }

    private static object[] Powers(IEnumerable<PowerModel> powers) => powers.Take(400).Select(p => (object)new
    {
        id = p.Id.ToString(),
        amount = p.Amount,
    }).ToArray();

    internal static object? Error(Exception? exception)
    {
        if (exception is null) return null;
        var parsed = LogSanitizer.ParseException(exception);
        return new
        {
            type = parsed.ExceptionType,
            exception_type = parsed.ExceptionType,
            message = parsed.Message,
            top_frame = parsed.TopFrame,
            signature = parsed.Signature,
            frames = parsed.Frames.Take(15).ToArray(),
            inner_exceptions = parsed.InnerExceptions.Select(i => new
            {
                type = i.ExceptionType,
                exception_type = i.ExceptionType,
                message = i.Message,
                frames = i.Frames.Take(10).ToArray(),
            }).ToArray(),
        };
    }

    internal static JsonObject Capture(RunState run, GameAction? waitingAction = null) =>
        CaptureInternal(run, waitingAction, forceFreshRoute: true);

    internal static JsonObject CapturePeriodic(RunState run, GameAction? waitingAction = null) =>
        CaptureInternal(run, waitingAction, forceFreshRoute: false);

    private static int[] SelectSnapshotPlayerIndices(int totalPlayers, int rawLocalPlayerIndex, out int emittedLocalPlayerIndex)
    {
        int emitCount = Math.Min(totalPlayers, MaxWireSnapshotPlayers);
        var indices = new int[emitCount];
        if (totalPlayers <= MaxWireSnapshotPlayers || rawLocalPlayerIndex < MaxWireSnapshotPlayers)
        {
            for (int i = 0; i < emitCount; i++)
                indices[i] = i;
            emittedLocalPlayerIndex = rawLocalPlayerIndex;
            return indices;
        }

        // Guarantee the local player is always included in the 8-player wire slice:
        // keep slots 0..6 and place the local player at emitted index 7.
        for (int i = 0; i < MaxWireSnapshotPlayers - 1; i++)
            indices[i] = i;
        indices[MaxWireSnapshotPlayers - 1] = rawLocalPlayerIndex;
        emittedLocalPlayerIndex = MaxWireSnapshotPlayers - 1;
        return indices;
    }

    private static JsonObject CaptureInternal(RunState run, GameAction? waitingAction, bool forceFreshRoute)
    {
        var manager = CombatManager.Instance;
        var combat = manager.DebugOnlyGetState();
        var action = RunManager.Instance.ActionExecutor?.CurrentlyRunningAction ?? waitingAction;
        int rawLocalPlayerIndex = -1;
        var playerIds = new ulong[run.Players.Count];
        for (int i = 0; i < run.Players.Count; i++)
        {
            var player = run.Players[i];
            playerIds[i] = player.NetId;
            if (rawLocalPlayerIndex < 0 && LocalContext.IsMe(player))
            {
                rawLocalPlayerIndex = i;
            }
        }

        int[] selectedPlayerIndices = SelectSnapshotPlayerIndices(run.Players.Count, rawLocalPlayerIndex, out int emittedLocalPlayerIndex);
        bool playersTruncated = run.Players.Count > MaxWireSnapshotPlayers;

        var result = Object(new
        {
            seed = run.Rng.StringSeed,
            numeric_seed = run.Rng.Seed.ToString(),
            game_mode = run.GameMode.ToString(),
            is_custom_game = Sts2VersionSeam.IsCustomGame(run),
            dominion_id = Sts2VersionSeam.GetActiveDominionId(run),
            run_models = Sts2VersionSeam.GetRunModelIds(run).Take(64).ToArray(),
            ascension = run.AscensionLevel,
            current_act_index = run.CurrentActIndex,
            current_act = run.Act.Id.ToString(),
            network_type = RunManager.Instance.NetService.Type.ToString(),
            local_player_index = emittedLocalPlayerIndex,
            total_player_count = run.Players.Count,
            players_truncated = playersTruncated,
            network = new
            {
                connected = RunManager.Instance.NetService.IsConnected,
                loading = RunManager.Instance.NetService.IsGameLoading,
                platform = RunManager.Instance.NetService.Platform.ToString(),
                player_count = run.Players.Count,
            },
            rng = JsonSerializer.SerializeToNode(run.Rng.ToSerializable(), JsonSerializationUtility.GetTypeInfo<MegaCrit.Sts2.Core.Saves.Runs.SerializableRunRngSet>()),
            current_floor = run.ActFloor,
            total_floor = run.TotalFloor,
            map_coord = run.CurrentMapCoord?.ToString(),
            current_room = run.CurrentRoom?.GetType().Name,
            acts = run.Acts.Take(20).Select(a => a.Id.ToString()).ToArray(),
            players = selectedPlayerIndices.Select((sourceIndex, emittedIndex) =>
            {
                var p = run.Players[sourceIndex];
                return new
                {
                    index = emittedIndex,
                    slot_index = sourceIndex,
                    character = p.Character.Id.ToString(),
                    hp = p.Creature.CurrentHp,
                    max_hp = p.Creature.MaxHp,
                    block = p.Creature.Block,
                    gold = p.Gold,
                    deck = p.Deck.Cards.Take(1200).Select(Card).ToArray(),
                    relics = p.Relics.Take(400).Select(r => r.Id.ToString()).ToArray(),
                    relic_states = p.Relics.Take(400).Select(r => JsonSerializer.SerializeToNode(r.ToSerializable(), JsonSerializationUtility.GetTypeInfo<MegaCrit.Sts2.Core.Saves.Runs.SerializableRelic>())).ToArray(),
                    potions = p.PotionSlots.Take(80).Select(potion => potion?.Id.ToString()).ToArray(),
                    powers = Powers(p.Creature.Powers),
                    combat = p.PlayerCombatState is { } pc ? new
                    {
                        energy = pc.Energy,
                        stars = pc.Stars,
                        turn = pc.TurnNumber,
                        phase = pc.Phase.ToString(),
                        orbs = pc.OrbQueue.Orbs.Take(50).Select(o => new { id = o.Id.ToString(), passive = o.PassiveVal, evoke = o.EvokeVal }).ToArray(),
                        hand = pc.Hand.Cards.Take(200).Select(Card).ToArray(),
                        draw_pile = pc.DrawPile.Cards.Take(1200).Select(Card).ToArray(),
                        discard_pile = pc.DiscardPile.Cards.Take(1200).Select(Card).ToArray(),
                        exhaust_pile = pc.ExhaustPile.Cards.Take(1200).Select(Card).ToArray(),
                        play_pile = pc.PlayPile.Cards.Take(1200).Select(Card).ToArray(),
                    } : null,
                };
            }).ToArray(),
            combat = manager.IsInProgress && combat is not null ? new
            {
                round = combat.RoundNumber,
                side = combat.CurrentSide.ToString(),
                actions_disabled = manager.PlayerActionsDisabled,
                monsters = combat.Enemies.Take(80).Select(c => new
                {
                    instance_id = InstanceId(c),
                    id = c.ModelId.ToString(),
                    hp = c.CurrentHp,
                    max_hp = c.MaxHp,
                    block = c.Block,
                    alive = c.IsAlive,
                    hittable = c.IsHittable,
                    powers = Powers(c.Powers),
                    next_move = c.Monster?.NextMove?.Id,
                    intents = c.Monster?.NextMove?.Intents.Take(20).Select(i => i.GetType().Name).ToArray(),
                }).ToArray(),
            } : null,
            execution = action is null ? null : new
            {
                type = action.GetType().Name,
                id = action.Id,
                state = action.State.ToString(),
                error = Error(action.Exception),
            },
            performance = new
            {
                fps = FiniteOrZero(Engine.GetFramesPerSecond()),
                process_ms = FiniteOrZero(Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimeProcess) * 1000),
                physics_ms = FiniteOrZero(Godot.Performance.GetMonitor(Godot.Performance.Monitor.TimePhysicsProcess) * 1000),
            },
            queues = CaptureQueues(run),
            ui = CaptureUi(),
        });
        var sanitized = BugReplayCodec.SanitizeSnapshot(result, playerIds);
        var (routeArray, routeRedactions, routeIntegerEncoding) = CaptureRoute(
            run,
            playerIds,
            selectedPlayerIndices,
            rawLocalPlayerIndex,
            forceFreshRoute);
        sanitized["route"] = routeArray;
        if (routeRedactions > 0)
        {
            sanitized["privacy_redactions"] = (sanitized["privacy_redactions"]?.GetValue<int>() ?? 0) + routeRedactions;
        }
        if (routeIntegerEncoding)
        {
            sanitized["integer_encoding"] = "exact-decimal-strings-above-2^53";
        }
        return sanitized;
    }

    private static (JsonArray Route, int Redactions, bool IntegerEncodingChanged) CaptureRoute(
        RunState run,
        ulong[] playerIds,
        int[] selectedPlayerIndices,
        int rawLocalPlayerIndex,
        bool forceFresh)
    {
        var history = run.MapPointHistory;
        int actCount = history.Count;
        int totalPoints = 0;
        int lastPointRoomsCount = 0;
        int lastPointPlayerStatsCount = 0;
        for (int i = 0; i < actCount; i++)
        {
            var act = history[i];
            totalPoints += act.Count;
            if (i == actCount - 1 && act.Count > 0)
            {
                var lastPoint = act[act.Count - 1];
                lastPointRoomsCount = lastPoint.Rooms.Count;
                lastPointPlayerStatsCount = lastPoint.PlayerStats.Count;
            }
        }

        if (!forceFresh
            && _cachedRoute is { } cached
            && cached.RunRef.TryGetTarget(out var cachedRun)
            && ReferenceEquals(cachedRun, run)
            && cached.ActCount == actCount
            && cached.TotalPoints == totalPoints
            && cached.CurrentActIndex == run.CurrentActIndex
            && cached.ActFloor == run.ActFloor
            && cached.TotalFloor == run.TotalFloor
            && cached.LastPointRoomsCount == lastPointRoomsCount
            && cached.LastPointPlayerStatsCount == lastPointPlayerStatsCount
            && cached.PlayerCount == run.Players.Count
            && cached.RawLocalPlayerIndex == rawLocalPlayerIndex)
        {
            return ((JsonArray)cached.SanitizedRoute.DeepClone(), cached.Redactions, cached.IntegerEncodingChanged);
        }

        var rawRoute = JsonSerializer.SerializeToNode(history.Take(20).Select((act, ai) => new
        {
            act_index = ai,
            points = act.Take(400).Select((point, fi) => new
            {
                floor_index = fi,
                map_point_type = point.MapPointType.ToString(),
                rooms = point.Rooms.Take(40).Select(room => new
                {
                    type = room.RoomType.ToString(),
                    model_id = room.ModelId?.ToString(),
                    monster_ids = room.MonsterIds.Take(80).Select(id => id.ToString()).ToArray(),
                    turns = room.TurnsTaken,
                }).ToArray(),
                players = CaptureRoutePlayers(point.PlayerStats, selectedPlayerIndices),
            }).ToArray(),
        }).ToArray())!.AsArray();

        var sanitizedWrapper = BugReplayCodec.SanitizeSnapshot(new JsonObject { ["route"] = rawRoute }, playerIds);
        var sanitizedRoute = (JsonArray)sanitizedWrapper["route"]!;
        sanitizedWrapper.Remove("route");
        int redactions = sanitizedWrapper["privacy_redactions"]?.GetValue<int>() ?? 0;
        bool intEncoded = sanitizedWrapper["integer_encoding"] is not null;

        _cachedRoute = new CachedRouteState(
            new WeakReference<RunState>(run),
            actCount,
            totalPoints,
            run.CurrentActIndex,
            run.ActFloor,
            run.TotalFloor,
            lastPointRoomsCount,
            lastPointPlayerStatsCount,
            run.Players.Count,
            rawLocalPlayerIndex,
            (JsonArray)sanitizedRoute.DeepClone(),
            redactions,
            intEncoded);

        return (sanitizedRoute, redactions, intEncoded);
    }

    private static JsonObject[] CaptureRoutePlayers(
        IReadOnlyList<PlayerMapPointHistoryEntry> stats,
        int[] selectedPlayerIndices)
    {
        if (stats.Count <= MaxWireSnapshotPlayers && selectedPlayerIndices.Length == stats.Count)
            return stats.Select(CaptureRoutePlayer).ToArray();

        var list = new List<JsonObject>(Math.Min(stats.Count, MaxWireSnapshotPlayers));
        for (int emittedIdx = 0; emittedIdx < selectedPlayerIndices.Length; emittedIdx++)
        {
            int sourceIdx = selectedPlayerIndices[emittedIdx];
            if (sourceIdx >= 0 && sourceIdx < stats.Count)
                list.Add(CaptureRoutePlayer(stats[sourceIdx], emittedIdx));
        }
        return list.ToArray();
    }

    private static JsonObject CaptureRoutePlayer(PlayerMapPointHistoryEntry ps, int index)
    {
        var entry = Object(new
        {
            index,
            hp = ps.CurrentHp,
            max_hp = ps.MaxHp,
            gold = ps.CurrentGold,
            damage_taken = ps.DamageTaken,
            healed = ps.HpHealed,
            event_choices = ps.EventChoices.Take(200).Select(c => new { key = c.Title.LocEntryKey, table = c.Title.LocTable }).ToArray(),
            relic_choices = ps.RelicChoices.Take(200).Select(c => new { id = c.choice.ToString(), picked = c.wasPicked }).ToArray(),
            potion_choices = ps.PotionChoices.Take(200).Select(c => new { id = c.choice.ToString(), picked = c.wasPicked }).ToArray(),
            card_choices = ps.CardChoices.Take(200).Select(c => new { id = c.Card.Id?.ToString(), picked = c.wasPicked }).ToArray(),
            rest_choices = ps.RestSiteChoices.Take(200).Select(c => c.ToString()).ToArray(),
            cards_gained = ps.CardsGained.Take(500).Select(c => c.ToString()).ToArray(),
            cards_removed = ps.CardsRemoved.Take(500).Select(c => c.ToString()).ToArray(),
        });
#if STS2_V113_1
        if (ps.BlitzMerchantChoices.Count > 0)
        {
            entry["blitz_merchant_choices"] = JsonSerializer.SerializeToNode(
                ps.BlitzMerchantChoices.Take(200).Select(c => new
                {
                    type = c.Type.ToString(),
                    model_id = c.modelId?.ToString(),
                    picked = c.wasPicked,
                }).ToArray());
        }
#endif
        return entry;
    }

    private static int FindPlayerIndexByOwnerId(RunState run, object? ownerId)
    {
        var players = run.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (Equals(players[i].NetId, ownerId)) return i;
        }
        return -1;
    }

    private static object? CaptureQueues(RunState run)
    {
        try
        {
            var queue = RunManager.Instance.ActionQueueSet;
            return new
            {
                empty = queue.IsEmpty,
                next_action_id = Read(queue, "NextActionId"),
                players = Items(Field(queue, "_actionQueues")).Select(q => new
                {
                    player_index = FindPlayerIndexByOwnerId(run, Read(q, "ownerId")),
                    paused = Read(q, "isPaused"),
                    cancelling = Read(q, "isCancellingCombatActions"),
                    actions = Items(Read(q, "actions")).OfType<GameAction>().Select(a => new
                    {
                        type = a.GetType().Name,
                        id = a.Id,
                        state = a.State.ToString(),
                        error = Error(a.Exception),
                    }).ToArray(),
                }).ToArray(),
            };
        }
        catch (Exception ex)
        {
            return new { unavailable = true, error_type = ex.GetType().Name };
        }
    }

    // Only fixed game fields cross the compatibility seam, never report/user-supplied reflection names.
    private static FieldInfo? ResolveField(Type rootType, string name)
    {
        for (var type = rootType; type is not null; type = type.BaseType)
        {
            if (type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } field)
            {
                return field;
            }
        }
        return null;
    }

    private static PropertyInfo? ResolveProperty(Type rootType, string name)
    {
        for (var type = rootType; type is not null; type = type.BaseType)
        {
            if (type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } prop)
            {
                return prop;
            }
        }
        return null;
    }

    private static object? Field(object value, string name) =>
        FieldCache.GetOrAdd((value.GetType(), name), static key => ResolveField(key.Type, key.Name))?.GetValue(value);

    private static object? Read(object? value, string name)
    {
        if (value is null) return null;
        var member = MemberCache.GetOrAdd((value.GetType(), name), static key =>
        {
            PropertyInfo? prop = ResolveProperty(key.Type, key.Name);
            if (prop is not null) return (prop, (FieldInfo?)null);
            return ((PropertyInfo?)null, ResolveField(key.Type, key.Name));
        });
        return member.Property is not null
            ? member.Property.GetValue(value)
            : member.Field?.GetValue(value);
    }

    private static List<object> Items(object? value) => value is IEnumerable items ? items.Cast<object>().Take(200).ToList() : [];

    private static object? CaptureUi()
    {
        try
        {
            var hand = NPlayerHand.Instance;
            if (hand is null)
            {
                return new
                {
                    hand_present = false,
                    end_turn_present = NCombatRoom.Instance?.Ui?.GetNodeOrNull<Control>("EndTurnButton") is not null,
                    tree_paused = (Godot.Engine.GetMainLoop() as SceneTree)?.Paused,
                    time_scale = FiniteOrZero(Engine.TimeScale),
                };
            }
            var holders = hand.CardHolderContainer.GetChildren().Where(n => n.GetType().Name.Contains("HandCardHolder"))
                .Concat(Items(Field(hand, "_selectedHandCardContainer") is { } container ? Read(container, "Holders") : null).OfType<Node>())
                .Take(200)
                .ToList();
            var selected = Items(Field(hand, "_selectedCards"));
            var prefs = Field(hand, "_prefs");
            var confirm = Field(hand, "_selectModeConfirmButton") as Control;
            var endTurn = NCombatRoom.Instance?.Ui?.GetNodeOrNull<Control>("EndTurnButton");
            return new
            {
                mode = hand.CurrentMode.ToString(),
                selection_active = hand.IsInCardSelection,
                in_card_play = hand.InCardPlay,
                selected = selected.Select(InstanceId).ToArray(),
                min = Read(prefs, "MinSelect"),
                max = Read(prefs, "MaxSelect"),
                confirm_enabled = confirm?.IsVisibleInTree() == true && Read(confirm, "IsEnabled") is true,
                dragged_holder_index = Field(hand, "_draggedHolderIndex"),
                awaiting_queue = Items(Field(hand, "_holdersAwaitingQueue")).Count,
                end_turn_present = endTurn is not null,
                end_turn_visible = endTurn?.IsVisibleInTree(),
                end_turn_enabled = endTurn is not null ? Read(endTurn, "IsEnabled") : null,
                tree_paused = hand.GetTree().Paused,
                time_scale = FiniteOrZero(Engine.TimeScale),
                holders = holders.Select(h =>
                {
                    var cardNodeObj = Read(h, "CardNode");
                    var card = Read(cardNodeObj, "Model");
                    var cardNode = cardNodeObj as Node;
                    var cost = cardNode?.GetNodeOrNull<Label>("%EnergyLabel");
                    return new
                    {
                        instance_id = InstanceId(card),
                        id = Read(card, "Id")?.ToString(),
                        visible = h is CanvasItem item && item.IsVisibleInTree(),
                        clickable = Field(h, "_isClickable"),
                        shown_cost = cost?.Text,
                        cost_visible = cost?.IsVisibleInTree(),
                    };
                }).ToArray(),
            };
        }
        catch (Exception ex)
        {
            return new { unavailable = true, error_type = ex.GetType().Name };
        }
    }
}
