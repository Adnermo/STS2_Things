using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
#if STS2_V113_1
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Helpers;
#endif
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
#if STS2_V113_1
using MegaCrit.Sts2.Core.Models.Powers;
#endif
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
#if STS2_V113_1
using MegaCrit.Sts2.Core.Nodes.Vfx;
#endif
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2_Things.Monsters;

namespace STS2_Things.Platform;

/// <summary>
/// Single authoritative compatibility seam between STS2 v0.111.1 (main) and v0.113.1 (beta).
/// Content models (Cards, Events, Monsters, Powers, Relics, Enchantments, Acts) call through
/// this seam or <c>STS2_Things.Core.Models</c> base classes and never embed <c>#if</c> directives.
/// </summary>
public static class Sts2VersionSeam
{
    public static readonly HashSet<Type> SplitKnownNumericVarTypes =
    [
        typeof(BlockVar),
        typeof(CalculationBaseVar),
        typeof(CalculationExtraVar),
        typeof(CardsVar),
        typeof(DamageVar),
        typeof(EnergyVar),
        typeof(ExtraDamageVar),
        typeof(ForgeVar),
        typeof(GoldVar),
        typeof(HealVar),
#if STS2_V113_1
        typeof(HitCountVar),
#endif
        typeof(HpLossVar),
        typeof(IntVar),
        typeof(MaxHpVar),
        typeof(OstyDamageVar),
        typeof(RepeatVar),
        typeof(StarsVar),
        typeof(SummonVar),
    ];

    public static string TargetTag =>
#if STS2_V111_1
        "v111.1";
#else
        "v113.1";
#endif

    public static void InitializeBeforeModelDatabase()
    {
    }

    public static void EnsureSavedPropertyTypeRegistered(Type owner)
    {
        _ = owner;
    }

    public static IDisposable? TrackEntryScope(MegaTrackEntry? entry, out MegaTrackEntry? track)
    {
        if (entry?.BoundObject == null || !GodotObject.IsInstanceValid(entry.BoundObject))
        {
            track = null;
            return null;
        }
        track = entry;
        return (object?)entry as IDisposable;
    }

#if STS2_V113_1
    public static void ToggleIsInteractable(this NCreature node, bool on)
        => node.SetIsInteractable(on, changeVisibilityImmediately: true);

    public static void AnimEnableUi(this NCreature node)
        => node.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar")
            ?.AnimateIn(MegaCrit.Sts2.Core.Entities.UI.HealthBarAnimMode.FromHidden);
#endif

    public static AttackCommand AttackFromCard(decimal damage, CardModel card, CardPlay cardPlay)
    {
        return DamageCmd.Attack(damage).FromCard(card, cardPlay);
    }

    public static Player GetCardPlayActor(CardPlay play)
    {
        return play.Player;
    }

    public static Task LoseMaxHp(PlayerChoiceContext context, Creature creature, decimal amount)
    {
#if STS2_V113_1
        return CreatureCmd.LoseMaxHp(context, creature, amount, (CardModel?)null);
#else
        return CreatureCmd.LoseMaxHp(context, creature, amount, isFromCard: false);
#endif
    }

    public static NCreature? GetCreatureNode(Creature creature)
    {
        return NCombatRoom.Instance?.GetCreatureNode(creature);
    }

    public static void SetCreatureNodeVisible(Creature creature, bool visible)
    {
        creature.SetNodeVisible(visible);
    }

    public static void SetTowerFaceNodeActive(Creature creature, bool active)
    {
        var node = GetCreatureNode(creature);
        if (active)
        {
            SetCreatureNodeVisible(creature, true);
            if (node != null && GodotObject.IsInstanceValid(node))
            {
                if (node.IntentContainer != null)
                    node.IntentContainer.Visible = true;
                node.ToggleIsInteractable(true);
                node.Visible = true;
            }
            return;
        }

        if (node != null && GodotObject.IsInstanceValid(node))
        {
            node.ToggleIsInteractable(false);
            if (node.IntentContainer != null)
                node.IntentContainer.Visible = false;

            Node[] activeArrowVfx = GetActiveArrowVfxNodes(node);
            if (activeArrowVfx.Length > 0)
            {
                // Keep NCreature visible until NImpaledArrowVfx finishes its 0.5s RetrieveAndFree() arc,
                // while %HealthBar, %Intents, and %Hitbox are already hidden above.
                node.Visible = true;
                foreach (Node vfx in activeArrowVfx)
                {
                    Creature capturedCreature = creature;
                    NCreature capturedNode = node;
                    vfx.TreeExited += () =>
                    {
                        if (!GodotObject.IsInstanceValid(capturedNode))
                            return;
                        bool shouldRemainHidden = capturedCreature.IsDead ||
                            capturedCreature.Monster is ThingsHumanFaceTowerFace { IsActiveFace: false };
                        if (shouldRemainHidden && GetActiveArrowVfxNodes(capturedNode).Length == 0)
                        {
                            SetCreatureNodeVisible(capturedCreature, false);
                            capturedNode.Visible = false;
                        }
                    };
                }
                return;
            }

            node.Visible = false;
        }

        SetCreatureNodeVisible(creature, false);
    }

    private static Node[] GetActiveArrowVfxNodes(NCreature node)
    {
        var list = new List<Node>();
        void Collect(Node? parent)
        {
            if (parent == null || !GodotObject.IsInstanceValid(parent))
                return;
            foreach (Node child in parent.GetChildren())
            {
                if (!GodotObject.IsInstanceValid(child) || child.IsQueuedForDeletion())
                    continue;
#if STS2_V113_1
                if (child is NImpaledArrowVfx)
                {
                    list.Add(child);
                    continue;
                }
#endif
                if (child.Name.ToString().Contains("ImpaledArrowVfx", StringComparison.Ordinal))
                    list.Add(child);
            }
        }

        Collect(node.Visuals);
        Collect(node);
        return list.ToArray();
    }

    public static async Task DislodgeImpaledArrowsAsync(
        Creature creature,
        PlayerChoiceContext? choiceContext = null,
        CardModel? source = null)
    {
#if STS2_V113_1
        if (CombatManager.Instance.IsEnding)
            return;

        PlayerChoiceContext ctx = choiceContext ?? new BlockingPlayerChoiceContext();
        foreach (ImpaledPower impaled in creature.GetPowerInstances<ImpaledPower>().ToList())
        {
            if (impaled.Applier is { IsDead: true })
                continue;
            await impaled.DislodgeAllArrows(ctx, source);
            if (creature.Powers.Contains(impaled))
                await PowerCmd.Remove(impaled);
        }
#else
        _ = creature;
        _ = choiceContext;
        _ = source;
        await Task.CompletedTask;
#endif
    }

    public static void RefreshMonsterIntentsUi(Creature creature)
    {
        var node = GetCreatureNode(creature);
        if (node != null && GodotObject.IsInstanceValid(node) && creature.CombatState?.IsLiveCombat() == true)
        {
            _ = MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(node.RefreshIntents());
        }
    }

    public static void RemoveCreatureWithoutDeathOrEscape(Creature creature)
    {
        if (creature.IsDead)
            return;

        var combatState = creature.CombatState;
        if (combatState == null || !combatState.IsLiveCombat() || !combatState.ContainsCreature(creature))
            return;

#if STS2_V113_1
        foreach (ImpaledPower impaled in creature.GetPowerInstances<ImpaledPower>().ToList())
        {
            while (impaled.Arrows.Count > 0)
            {
                ArrowSlot arrow = impaled.RemoveFirstArrow();
                if (arrow.Status == ArrowStatus.Away)
                {
                    arrow.Status = arrow.DestroyAfterImpaled ? ArrowStatus.Destroyed : ArrowStatus.Retrieved;
                    arrow.DestroyAfterImpaled = false;
                }
            }
        }
#endif

        creature.RemoveAllPowersInternalExcept();

        var node = GetCreatureNode(creature);
        if (node != null)
        {
            NCombatRoom.Instance?.RemoveCreatureNode(node);
            node.ToggleIsInteractable(on: false);
            node.Visible = false;
        }

        CombatManager.Instance.RemoveCreature(creature);
        combatState.RemoveCreature(creature);
    }

    public static bool GetPlayerCanModifyPotions(Player player)
    {
        return player.CanUseOrRemovePotions;
    }

    public static void SetPlayerCanModifyPotions(Player player, bool allowed)
    {
        player.CanUseOrRemovePotions = allowed;
    }

    public static IEnumerable<PotionModel> GetPlayerPotionOptions(Player player)
    {
        return PotionFactory.GetPotionOptions(player);
    }

    public static void AimThiefSpineBone(Creature thief, IReadOnlyList<Creature> targets)
    {
        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(thief);
        if (creatureNode == null || targets.Count == 0)
            return;

        Creature target = LocalContext.GetMe(targets) ?? targets[0];
        NCreature? targetNode = GetCreatureNode(target);
        Node2D? spineBone = creatureNode.GetSpecialNode<Node2D>("Visuals/SpineBoneNode");
        if (spineBone == null || targetNode == null)
            return;

        float targetOffset = 900f * creatureNode.Visuals.Scale.X;
        spineBone.GlobalPosition = new Vector2(targetNode.GlobalPosition.X + targetOffset, spineBone.GlobalPosition.Y);
    }

    public static string ResolveMigratedSplitVarName(
        CardModel card,
        DynamicVar value,
        IReadOnlyDictionary<string, decimal> savedUnsplitValues)
    {
        string savedName = value.Name;
#if STS2_V113_1
        if (value is HitCountVar && value.Name == HitCountVar.defaultName &&
            !card.DynamicVars.ContainsKey("Repeat") &&
            !savedUnsplitValues.ContainsKey(savedName) &&
            savedUnsplitValues.ContainsKey("Repeat"))
        {
            savedName = "Repeat";
        }
#endif
        return savedName;
    }

    public static Task LoseBlock(
        PlayerChoiceContext choiceContext,
        Creature target,
        decimal amount,
        Creature? remover)
    {
        return CreatureCmd.LoseBlock(choiceContext, target, amount, remover);
    }

    public static Task LoseUnblockableHpFromCardAsync(
        PlayerChoiceContext choiceContext,
        Creature target,
        decimal amount,
        CardModel? cardSource,
        CardPlay? cardPlay)
    {
        return CreatureCmd.Damage(
            choiceContext,
            target,
            amount,
            ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move,
            cardSource,
            cardPlay);
    }

    public static void PlayCardCostFeedback(CardModel card)
    {
        NCard.FindOnTable(card)?.PlayRandomizeCostAnim();
    }

    private static readonly Func<CardModel, string?>? CardPortraitPngPathGetter =
        CreateCardPortraitPngPathGetter();

    private static Func<CardModel, string?>? CreateCardPortraitPngPathGetter()
    {
        MethodInfo? getter = AccessTools.PropertyGetter(typeof(CardModel), "PortraitPngPath");
        if (getter == null || getter.ReturnType != typeof(string))
            return null;
        return (Func<CardModel, string?>)Delegate.CreateDelegate(typeof(Func<CardModel, string?>), getter);
    }

    /// <summary>
    /// Loads a card's standalone portrait PNG directly via <see cref="ResourceLoader"/> on both
    /// v111.1 and v113.1. Intentionally avoids <c>CardModel.BigPortrait</c> and
    /// <c>PreloadManager.Cache.GetTexture2D</c> so unpreloaded portraits are never registered in
    /// <c>_missedCacheAssets</c> and disposed on room transitions.
    /// </summary>
    public static Texture2D? TryGetBigCardPortrait(CardModel? card)
    {
        if (card == null || !card.HasPortrait)
            return null;

        string? pngPath = CardPortraitPngPathGetter?.Invoke(card);
        if (string.IsNullOrWhiteSpace(pngPath) || !ResourceLoader.Exists(pngPath))
            return null;

        Texture2D? texture = ResourceLoader.Load<Texture2D>(pngPath, null, ResourceLoader.CacheMode.Reuse);
        return texture != null && GodotObject.IsInstanceValid(texture) ? texture : null;
    }

    public static bool IsDread()
    {
#if STS2_V113_1
        return AscensionHelper.IsDread();
#else
        return false;
#endif
    }

    public static bool IsBlitz(IRunState? runState)
    {
#if STS2_V113_1
        return runState?.GameMode == GameMode.Blitz;
#else
        _ = runState;
        return false;
#endif
    }

    public static bool IsCustomGame(IRunState? runState)
    {
#if STS2_V113_1
        return runState?.IsCustomGame == true;
#else
        return runState?.GameMode == GameMode.Custom;
#endif
    }

    public static bool HasDoubleBossAscension(IRunState? runState)
    {
        if (runState == null)
            return false;
#if STS2_V113_1
        return runState.GameMode != GameMode.Blitz && runState.AscensionLevel >= (int)AscensionLevel.DoubleBoss;
#else
        return runState.AscensionLevel >= (int)AscensionLevel.DoubleBoss;
#endif
    }

    public static string? GetActiveDominionId(IRunState? runState)
    {
#if STS2_V113_1
        return runState?.Dominion?.Id.ToString();
#else
        _ = runState;
        return null;
#endif
    }

    public static string[] GetRunModelIds(IRunState? runState)
    {
#if STS2_V113_1
        return runState?.RunModels?.Select(m => m.Id.ToString()).ToArray() ?? [];
#else
        _ = runState;
        return [];
#endif
    }
}
