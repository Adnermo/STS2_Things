using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

public partial class DepthsProbeNode
{
    private static RunState NativeRun(IReadOnlyList<Player> party, IReadOnlyList<ActModel> acts,
        GameMode mode, int ascension, string seed)
    {
        var create = typeof(RunState).GetMethod(nameof(RunState.CreateForNewRun))!;
        object[] args = create.GetParameters().Length == 7
            ? [party, acts, Array.Empty<ModifierModel>(), mode, false, ascension, seed]
            : [party, acts, Array.Empty<ModifierModel>(), mode, ascension, seed];
        return (RunState)create.Invoke(null, args)!;
    }

    private static object NativeAscension(int level)
    {
        var type = typeof(RunManager).GetProperty("AscensionManager")!.PropertyType;
        if (type.GetConstructor([typeof(int)]) is { } legacy)
            return legacy.Invoke([level]);
        return type.GetConstructor([typeof(GameMode), typeof(int)])!.Invoke([GameMode.Standard, level]);
    }

    private static SerializableRun NativeEndedRun(object result)
        => result as SerializableRun ?? (SerializableRun?)result.GetType().GetField("run")?.GetValue(result)
            ?? throw new InvalidOperationException("Native victory did not return a serialized run.");
}
