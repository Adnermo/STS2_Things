using Godot;

namespace STS2_Things.Diagnostics;

/// <summary>Native engine/managed scene errors bypass MegaCrit's Log callback.</summary>
public partial class BugEngineLogger : Godot.Logger
{
    public override void _LogError(string function, string file, int line, string code,
        string rationale, bool editorNotify, int errorType,
        Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (errorType == (int)ErrorType.Warning) return;
        try
        {
            var functions = new List<string>();
            foreach (var trace in scriptBacktraces)
                for (int i = 0; i < Math.Min(20, trace.GetFrameCount()); i++) functions.Add(trace.GetFrameFunction(i));
            if (functions.Count == 0) functions.Add(function);
            BugRuntimeFaults.Capture(code + "\n" + rationale, 0, functions);
        }
        catch (Exception) { /* Never log from an engine logger callback. */ }
    }

}
