using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Platform;

namespace STS2_Things.Core.Models;

/// <summary>
/// Shared v2.0 base class for all <c>STS2_Things</c> cards.
/// Unifies dual-version card attack/damage commands and resolves packed PNG portraits cleanly
/// without requiring <c>#if</c> directives in individual card implementations.
/// </summary>
public abstract class ThingsCardModel(
    int canonicalEnergyCost,
    CardType type,
    CardRarity rarity,
    TargetType targetType,
    bool shouldShowInCardLibrary = true)
    : CardModel(canonicalEnergyCost, type, rarity, targetType, shouldShowInCardLibrary)
{
    /// <summary>
    /// Override when a card stores its portrait outside the standard <c>packed/card_portraits/{pool}/{id}.png</c> layout.
    /// </summary>
    protected virtual string? CustomPortraitPath => null;

    public override string PortraitPath
    {
        get
        {
            if (!string.IsNullOrEmpty(CustomPortraitPath))
                return CustomPortraitPath;

            string atlasPath = base.PortraitPath;
            if (ResourceLoader.Exists(atlasPath))
                return atlasPath;

            return ImageHelper.GetImagePath(
                $"packed/card_portraits/{Pool.Title.ToLowerInvariant()}/{Id.Entry.ToLowerInvariant()}.png");
        }
    }

    protected override string PortraitPngPath => CustomPortraitPath ?? base.PortraitPngPath;

    protected AttackCommand AttackFromCard(decimal damage, CardPlay cardPlay)
        => Sts2VersionSeam.AttackFromCard(damage, this, cardPlay);

    protected Task DamageFromCardAsync(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageVar damage,
        CardPlay? cardPlay = null)
    {
        return CreatureCmd.Damage(choiceContext, target, damage, this, cardPlay);
    }
}
