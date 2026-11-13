using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

/// <summary>Test-fixture adapters only. Production code uses version-specific native calls.</summary>
internal static class ProbeRuntimeApi
{
    public static RunState CreateForNewRun(IReadOnlyList<Player> players, IReadOnlyList<ActModel> acts,
        IReadOnlyList<ModifierModel> modifiers, GameMode mode, int ascension, string seed)
        => CreateForNewRun(players, acts, modifiers, mode, false, ascension, seed);

    public static RunState CreateForNewRun(IReadOnlyList<Player> players, IReadOnlyList<ActModel> acts,
        IReadOnlyList<ModifierModel> modifiers, GameMode mode, bool isCustom, int ascension, string seed)
    {
        var create = typeof(RunState).GetMethod(nameof(RunState.CreateForNewRun))!;
        var run = (RunState)create.Invoke(null, create.GetParameters().Length == 7
            ? [players, acts, modifiers, mode, isCustom, ascension, seed]
            : new object[] { players, acts, modifiers, mode, ascension, seed })!;
        PrepareSelectors(run);
        return run;
    }

    public static void PrepareSelectors(IRunState run)
    {
        if (typeof(RunManager).GetProperty("CardSelectorStack") is { } property)
            property.SetValue(RunManager.Instance, Activator.CreateInstance(property.PropertyType, [run]));
    }

    public static AscensionManager Ascension(int level)
    {
        var type = typeof(AscensionManager);
        return (AscensionManager)(type.GetConstructor([typeof(int)]) is { } legacy
            ? legacy.Invoke([level])
            : type.GetConstructor([typeof(GameMode), typeof(int)])!.Invoke([GameMode.Standard, level]));
    }

    public static void EnterCombat(CombatState state)
    {
        var scaling = state.GetType().GetProperty("MultiplayerScalingModel")?.GetValue(state);
        scaling?.GetType().GetMethod("OnCombatEntered")?.Invoke(scaling, [state]);
        if (state.RunState.GetType().GetProperty("RunModels")?.GetValue(state.RunState) is not IEnumerable models) return;
        foreach (object model in models)
        {
            if (model.GetType().GetProperty("RunState", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(model) is null)
                model.GetType().GetMethod("OnRunCreated")!.Invoke(model, [state.RunState]);
            model.GetType().GetMethod("OnCombatEntered")!.Invoke(model, [state]);
        }
    }

    public static Task AfterDeath(AbstractModel model, PlayerChoiceContext context, Creature creature,
        bool wasRemovalPrevented, float deathAnimLength)
    {
        var hook = model.GetType().GetMethod(nameof(AbstractModel.AfterDeath))!;
        return (Task)hook.Invoke(model, hook.GetParameters().Length == 5
            ? new object?[] { context, creature, null, wasRemovalPrevented, deathAnimLength }
            : new object?[] { context, creature, wasRemovalPrevented, deathAnimLength })!;
    }

    public static IDisposable UseSelector(MegaCrit.Sts2.Core.TestSupport.ICardSelector selector)
    {
        var method = typeof(MegaCrit.Sts2.Core.Commands.CardSelectCmd).GetMethod("UseSelector",
            BindingFlags.Static | BindingFlags.Public);
        method ??= typeof(AbstractModel).Assembly.GetType("MegaCrit.Sts2.Core.CardSelection.CardSelectorStack")!
            .GetMethod("SetTestSelector", BindingFlags.Static | BindingFlags.Public);
        return (IDisposable)method!.Invoke(null, [selector])!;
    }

    public static decimal MultiplayerScaling(EncounterModel? encounter, int actIndex)
    {
        var game = typeof(AbstractModel).Assembly;
        var type = game.GetType("MegaCrit.Sts2.Core.Models.MultiplayerScalingModel")
            ?? game.GetType("MegaCrit.Sts2.Core.Models.Singleton.MultiplayerScalingModel")!;
        return (decimal)type.GetMethod("GetMultiplayerScaling")!.Invoke(null, [encounter, actIndex])!;
    }
}
