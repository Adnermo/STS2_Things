using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Godot;
using HarmonyLib;
using LinqExpression = System.Linq.Expressions.Expression;

namespace STS2_Things.Compatibility.Internal;

/// <summary>Repair the verified default-texture cache contract, preserving each mod's renderer.</summary>
internal static class PortraitCacheGuard
{
    private sealed record CacheShape(
        Func<object, Texture2D?> GetTexture,
        Func<object, TextureRect.StretchModeEnum> GetStretchMode,
        Func<object, CanvasItem.TextureFilterEnum>? GetTextureFilter);

    private static readonly ConcurrentDictionary<MethodBase, CacheShape> CacheShapes = new();

    private static Func<object, TValue> CompilePropertyGetter<TValue>(Type stateType, PropertyInfo property)
    {
        ParameterExpression instanceParam = LinqExpression.Parameter(typeof(object), "state");
        UnaryExpression typedInstance = LinqExpression.Convert(instanceParam, stateType);
        MemberExpression propertyAccess = LinqExpression.Property(typedInstance, property);
        return LinqExpression.Lambda<Func<object, TValue>>(propertyAccess, instanceParam).Compile();
    }

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
        if (state == null || texture?.PropertyType != typeof(Texture2D) || stretch?.PropertyType != typeof(TextureRect.StretchModeEnum)
            || (filter != null && filter.PropertyType != typeof(CanvasItem.TextureFilterEnum))
            || !MatchesCacheHelper(remember, state) || !MatchesCacheHelper(restore, state))
            throw new NotSupportedException("Portrait cache API differs from the verified contract.");

        // The private state type belongs to its particular mod/assembly context.
        // Never share compiled getters across independently loaded card-art mods.
        CacheShapes[restore!] = new CacheShape(
            CompilePropertyGetter<Texture2D?>(state, texture),
            CompilePropertyGetter<TextureRect.StretchModeEnum>(state, stretch),
            filter != null ? CompilePropertyGetter<CanvasItem.TextureFilterEnum>(state, filter) : null);
        try
        {
            harmony.Patch(remember!, prefix: new HarmonyMethod(typeof(PortraitCacheGuard), nameof(BeforeRemember)));
            harmony.Patch(restore!, prefix: new HarmonyMethod(typeof(PortraitCacheGuard), nameof(BeforeRestore)));
            if (MatchesReplacementResolver4(resolve))
                harmony.Patch(resolve!, postfix: new HarmonyMethod(typeof(PortraitCacheGuard), nameof(AfterResolveReplacement4)));
            else if (MatchesReplacementResolver5(resolve))
                harmony.Patch(resolve!, postfix: new HarmonyMethod(typeof(PortraitCacheGuard), nameof(AfterResolveReplacement5)));
        }
        catch (Exception)
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

    private static bool MatchesReplacementResolver4(MethodInfo? method)
    {
        if (method?.ReturnType != typeof(bool)) return false;
        ParameterInfo[] args = method.GetParameters();
        return args.Length == 4 && args[0].ParameterType == typeof(string)
            && args[1].ParameterType == typeof(ulong) && args[2].ParameterType == typeof(int)
            && args[3].IsOut && args[3].ParameterType == typeof(Texture2D).MakeByRefType();
    }

    private static bool MatchesReplacementResolver5(MethodInfo? method)
    {
        if (method?.ReturnType != typeof(bool)) return false;
        ParameterInfo[] args = method.GetParameters();
        return args.Length == 5
            && args[0].ParameterType == typeof(MegaCrit.Sts2.Core.Models.CardModel)
            && args[1].ParameterType == typeof(string)
            && args[2].ParameterType == typeof(ulong) && args[3].ParameterType == typeof(int)
            && args[4].IsOut && args[4].ParameterType == typeof(Texture2D).MakeByRefType();
    }

    private static void AfterResolveReplacement4(ref bool __result, ref Texture2D? __3)
    {
        if (__result && __3 != null && !GodotObject.IsInstanceValid(__3))
        {
            __result = false;
            __3 = null;
        }
    }

    private static void AfterResolveReplacement5(ref bool __result, ref Texture2D? __4)
    {
        if (__result && __4 != null && !GodotObject.IsInstanceValid(__4))
        {
            __result = false;
            __4 = null;
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
        if (string.IsNullOrWhiteSpace(__0)) return true;
        object? state = __2[__0];
        if (state == null || !CacheShapes.TryGetValue(__originalMethod, out CacheShape? shape)
            || shape.GetTexture(state) is not Texture2D texture || GodotObject.IsInstanceValid(texture)) return true;

        // UpdateVisuals has already loaded the current model's native artwork.
        // Keep it; evict the dead wrapper, and still restore the cached style.
        __2.Remove(__0);
        __1.StretchMode = shape.GetStretchMode(state);
        if (shape.GetTextureFilter != null) __1.TextureFilter = shape.GetTextureFilter(state);
        return false;
    }
}
