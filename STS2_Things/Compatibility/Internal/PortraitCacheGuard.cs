using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Godot;
using HarmonyLib;

namespace STS2_Things.Compatibility.Internal;

/// <summary>Repair the verified default-texture cache contract, preserving each mod's renderer.</summary>
internal static class PortraitCacheGuard
{
    private sealed record CacheShape(PropertyInfo Texture, PropertyInfo StretchMode, PropertyInfo? TextureFilter);
    private static readonly ConcurrentDictionary<MethodBase, CacheShape> CacheShapes = new();

    internal static void Install(Assembly assembly, string typeNamespace, Harmony harmony)
    {
        Type? patch = assembly.GetType(typeNamespace + ".PortraitPatch", throwOnError: false, ignoreCase: true);
        Type? state = patch?.GetNestedType("PortraitState", BindingFlags.NonPublic);
        PropertyInfo? texture = state?.GetProperty("Texture", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? stretch = state?.GetProperty("StretchMode", BindingFlags.Public | BindingFlags.Instance);
        PropertyInfo? filter = state?.GetProperty("TextureFilter", BindingFlags.Public | BindingFlags.Instance);
        MethodInfo? remember = patch?.GetMethod("RememberDefaultState", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? restore = patch?.GetMethod("RestoreDefaultState", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? resolve = patch?.GetMethod("TryResolveReplacementTexture", BindingFlags.NonPublic | BindingFlags.Static);
        if (texture?.PropertyType != typeof(Texture2D) || stretch?.PropertyType != typeof(TextureRect.StretchModeEnum)
            || (filter != null && filter.PropertyType != typeof(CanvasItem.TextureFilterEnum))
            || !MatchesCacheHelper(remember, state) || !MatchesCacheHelper(restore, state))
            throw new NotSupportedException("Portrait cache API differs from the verified contract.");

        // The private state type belongs to its particular mod/assembly context.
        // Never share a PropertyInfo across independently loaded card-art mods.
        CacheShapes[restore!] = new CacheShape(texture, stretch, filter);
        try
        {
            harmony.Patch(remember!, prefix: new HarmonyMethod(typeof(PortraitCacheGuard), nameof(BeforeRemember)));
            harmony.Patch(restore!, prefix: new HarmonyMethod(typeof(PortraitCacheGuard), nameof(BeforeRestore)));
            if (MatchesReplacementResolver(resolve))
                harmony.Patch(resolve!, postfix: new HarmonyMethod(typeof(PortraitCacheGuard), nameof(AfterResolveReplacement)));
        }
        catch
        {
            CacheShapes.TryRemove(restore!, out _);
            throw; // The manager rolls back only this adapter's own Harmony ID.
        }
    }

    private static bool MatchesCacheHelper(MethodInfo? method, Type? state)
    {
        if (method?.ReturnType != typeof(void)) return false;
        ParameterInfo[] args = method.GetParameters();
        return args.Length == 3 && args[0].ParameterType == typeof(string)
            && args[1].ParameterType == typeof(TextureRect)
            && args[2].ParameterType.IsGenericType
            && args[2].ParameterType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            && args[2].ParameterType.GenericTypeArguments.SequenceEqual(new[] { typeof(string), state });
    }

    private static bool MatchesReplacementResolver(MethodInfo? method)
    {
        if (method?.ReturnType != typeof(bool)) return false;
        ParameterInfo[] args = method.GetParameters();
        return args.Length == 4 && args[0].ParameterType == typeof(string)
            && args[1].ParameterType == typeof(ulong) && args[2].ParameterType == typeof(int)
            && args[3].IsOut && args[3].ParameterType == typeof(Texture2D).MakeByRefType();
    }

    private static void AfterResolveReplacement(ref bool __result, ref Texture2D? __3)
    {
        if (__result && __3 != null && !GodotObject.IsInstanceValid(__3))
        {
            __result = false;
            __3 = null;
        }
    }

    private static bool BeforeRemember(TextureRect? __1)
    {
        return __1 != null && GodotObject.IsInstanceValid(__1)
            && (__1.Texture is not { } texture || GodotObject.IsInstanceValid(texture));
    }

    private static bool BeforeRestore(string __0, TextureRect? __1, IDictionary __2, MethodBase __originalMethod)
    {
        if (__1 == null || !GodotObject.IsInstanceValid(__1)) return false;
        if (string.IsNullOrWhiteSpace(__0) || !__2.Contains(__0)) return true;
        object? state = __2[__0];
        if (state == null || !CacheShapes.TryGetValue(__originalMethod, out CacheShape? shape)
            || shape.Texture.GetValue(state) is not Texture2D texture || GodotObject.IsInstanceValid(texture)) return true;

        // UpdateVisuals has already loaded the current model's native artwork.
        // Keep it; evict the dead wrapper, and still restore the cached style.
        __2.Remove(__0);
        __1.StretchMode = (TextureRect.StretchModeEnum)shape.StretchMode.GetValue(state)!;
        if (shape.TextureFilter != null) __1.TextureFilter = (CanvasItem.TextureFilterEnum)shape.TextureFilter.GetValue(state)!;
        return false;
    }
}
