using System;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using STS2_Things.Core.Models;
using STS2_Things.Features.MerchantBargain;
using STS2_Things.Platform;

namespace STS2_Things.Modifiers;

/// <summary>
/// Run-state hook subscriber that applies temporary negotiated prices during Merchant Bargain
/// through native <see cref="AbstractModel.ModifyMerchantPrice"/> instead of patching <c>MerchantEntry.get_Cost</c>.
/// </summary>
public sealed class ThingsMerchantBargainPricePolicy : ThingsModifierModel
{
    public const string SubscriptionId = "STS2_Things.MerchantBargainPrice";
    private static bool _subscribed;
    private static AbstractModel[]? _cachedPolicy;

    public override LocString Title => new("settings_ui", "STS2_THINGS-MERCHANT_BARGAIN.title");

    public override LocString Description => new("settings_ui", "STS2_THINGS-FEATURE_MERCHANT_BARGAIN_ENABLED.title");

    public static void EnsureSubscribed()
    {
        if (_subscribed)
            return;

        ModHelper.SubscribeForRunStateHooks(
            SubscriptionId,
            static runState => runState.CurrentRoom is MerchantRoom
                ? _cachedPolicy ??= [ModelDb.Modifier<ThingsMerchantBargainPricePolicy>()]
                : Array.Empty<AbstractModel>());
        _subscribed = true;
    }

    public override decimal ModifyMerchantPrice(Player player, MerchantEntry entry, decimal cost)
    {
        if (Sts2VersionSeam.IsBlitz(player?.RunState))
            return cost;

        return MerchantBargainManager.TryGetPriceOverride(entry, out int negotiatedPrice)
            ? negotiatedPrice
            : cost;
    }
}
