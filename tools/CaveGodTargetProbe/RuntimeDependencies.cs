using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class RuntimeDependencies
{
    // The game's v111 module initializer loads Sentry before the probe's _Ready.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Godot hosts this standalone probe application as a library.")]
    [ModuleInitializer]
    internal static void Initialize()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string directory = System.Environment.GetEnvironmentVariable("THINGS_PROBE_RUNTIME_DIR") ?? AppContext.BaseDirectory;
        AssemblyLoadContext.GetLoadContext(assembly)!.Resolving += (context, name) => {
            string path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }
}
