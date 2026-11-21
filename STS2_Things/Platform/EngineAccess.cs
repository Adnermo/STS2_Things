using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
#if STS2_V113_1
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
#endif
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace STS2_Things.Platform;

/// <summary>
/// Centralized, strongly-typed non-public engine accessors using .NET 9 <see cref="UnsafeAccessorAttribute"/>
/// and once-cached compiled delegates. Eliminates scattered ad-hoc reflection across gameplay and UI hooks.
/// </summary>
public static class EngineAccess
{
#if STS2_V111_1
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "RelicOption")]
    private static extern EventOption CallAncientRelicOptionCore(
        AncientEventModel instance,
        RelicModel relic,
        string pageName,
        string? customDonePage);
#else
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "RelicOption")]
    private static extern EventOption CallAncientRelicOptionCore(
        AncientEventModel instance,
        RelicModel relic,
        string pageName);
#endif

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_ancientPortrait")]
    private static extern ref TextureRect? GetCardAncientPortraitRef(NCard card);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_enchantmentIcon")]
    private static extern ref TextureRect? GetCardEnchantmentIconRef(NCard card);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_textHoverTipContainer")]
    private static extern ref VFlowContainer? GetTextHoverTipContainerRef(NHoverTipSet tipSet);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_cardHoverTipContainer")]
    private static extern ref NHoverTipCardContainer? GetCardHoverTipContainerRef(NHoverTipSet tipSet);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Flash")]
    private static extern void CallPowerFlash(PowerModel instance);

    private static readonly Action<CardModel, int> SetBaseStarCostDelegate = BuildBaseStarCostSetter();

    private static readonly FieldInfo? ActiveHoverTipsField =
        AccessTools.Field(typeof(NHoverTipSet), "_activeHoverTips");

#if STS2_V113_1
    private static readonly MethodInfo? HandleLobbyGameModeChangedMethod =
        AccessTools.Method(typeof(StartRunLobby), "HandleGameModeChangedMessage",
            [typeof(LobbyGameModeChangedMessage), typeof(ulong)]);
#endif

    private static readonly FieldInfo? EventCombatSyncField =
        AccessTools.Field(typeof(EventModel), "_combatSynchronizer");

    public static EventOption CreateAncientRelicOption(
        AncientEventModel ancient,
        RelicModel relic,
        string pageName = "INITIAL")
    {
#if STS2_V111_1
        return CallAncientRelicOptionCore(ancient, relic, pageName, null);
#else
        return CallAncientRelicOptionCore(ancient, relic, pageName);
#endif
    }

    public static void SetCardBaseStarCost(CardModel card, int cost)
    {
        SetBaseStarCostDelegate(card, cost);
    }

    public static CombatState GetEventCombatLayoutState(EventModel eventModel)
    {
        var sync = EventCombatSyncField?.GetValue(eventModel) as EventCombatSynchronizer
            ?? throw new InvalidOperationException($"{eventModel.Id} combat synchronizer is not initialized.");
        return sync.CombatStateForLayout
            ?? throw new InvalidOperationException($"{eventModel.Id} combat layout state is not initialized.");
    }

    public static void ClearPooledCardExtraTextures(NCard card)
    {
        if (GetCardAncientPortraitRef(card) is { } ancientPortrait)
            ancientPortrait.Texture = null;
        if (GetCardEnchantmentIconRef(card) is { } enchantmentIcon)
            enchantmentIcon.Texture = null;
    }

    public static Dictionary<Control, NHoverTipSet>? GetActiveHoverTipsDictionary()
    {
        return ActiveHoverTipsField?.GetValue(null) as Dictionary<Control, NHoverTipSet>;
    }

    public static (VFlowContainer? TextContainer, NHoverTipCardContainer? CardContainer) GetHoverTipContainers(NHoverTipSet tipSet)
    {
        return (GetTextHoverTipContainerRef(tipSet), GetCardHoverTipContainerRef(tipSet));
    }

    public static void FlashPower(PowerModel power)
    {
        CallPowerFlash(power);
    }

    public static void UnregisterLeakedLobbyGameModeHandler(StartRunLobby lobby)
    {
#if STS2_V113_1
        if (HandleLobbyGameModeChangedMethod == null || lobby.NetService == null)
            return;
        try
        {
            var handler = Delegate.CreateDelegate(
                typeof(MessageHandlerDelegate<LobbyGameModeChangedMessage>),
                lobby,
                HandleLobbyGameModeChangedMethod,
                throwOnBindFailure: false) as MessageHandlerDelegate<LobbyGameModeChangedMessage>;
            if (handler != null)
                lobby.NetService.UnregisterMessageHandler<LobbyGameModeChangedMessage>(handler);
        }
        catch (Exception ex)
        {
            Log.Debug($"[STS2_Things] StartRunLobby handler cleanup skipped: {ex.Message}");
        }
#else
        _ = lobby;
#endif
    }

    private static Action<CardModel, int> BuildBaseStarCostSetter()
    {
        MethodInfo setter = AccessTools.PropertySetter(typeof(CardModel), nameof(CardModel.BaseStarCost))
            ?? throw new MissingMethodException(typeof(CardModel).FullName, $"set_{nameof(CardModel.BaseStarCost)}");
        return setter.CreateDelegate<Action<CardModel, int>>();
    }
}
