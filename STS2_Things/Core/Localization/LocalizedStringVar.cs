using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace STS2_Things.Core.Localization;

/// <summary>
/// Locale-reactive <see cref="StringVar"/> that re-evaluates its localized text whenever
/// formatted or when the player switches languages mid-run.
/// </summary>
public sealed class LocalizedStringVar : StringVar
{
    private const int InitialPruneThreshold = 64;
    private static readonly object Sync = new();
    private static readonly List<WeakReference<LocalizedStringVar>> LiveInstances = [];
    private static int _nextPruneThreshold = InitialPruneThreshold;
    private static bool _localeSubscribed;

    private readonly Func<AbstractModel?, string> _resolver;
    private LocalizedStringVar? _registeredSelf;

    public LocalizedStringVar(string name, Func<AbstractModel?, string> resolver)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
        RegisterInstance(this);
        TryRefresh();
    }

    public override void SetOwner(AbstractModel owner)
    {
        base.SetOwner(owner);
        RegisterInstance(this);
        TryRefresh();
    }

    public override string ToString()
    {
        TryRefresh();
        return StringValue;
    }

    public void TryRefresh()
    {
        EnsureSubscribed();
        try
        {
            if (LocManager.Instance != null)
                StringValue = _resolver(_owner);
        }
        catch (Exception)
        {
            // Keep existing StringValue if localization tables are not yet initialized during bootstrap.
        }
    }

    private static void RegisterInstance(LocalizedStringVar instance)
    {
        lock (Sync)
        {
            if (ReferenceEquals(instance._registeredSelf, instance))
                return;
            instance._registeredSelf = instance;

            if (LiveInstances.Count >= _nextPruneThreshold)
            {
                for (int i = LiveInstances.Count - 1; i >= 0; i--)
                {
                    if (!LiveInstances[i].TryGetTarget(out _))
                        LiveInstances.RemoveAt(i);
                }
                _nextPruneThreshold = Math.Max(InitialPruneThreshold, LiveInstances.Count * 2);
            }

            LiveInstances.Add(new WeakReference<LocalizedStringVar>(instance));
        }
    }

    private static void EnsureSubscribed()
    {
        if (_localeSubscribed || LocManager.Instance == null)
            return;

        lock (Sync)
        {
            if (_localeSubscribed || LocManager.Instance == null)
                return;
            LocString.SubscribeToLocaleChange(OnLocaleChanged);
            _localeSubscribed = true;
        }
    }

    private static void OnLocaleChanged()
    {
        List<LocalizedStringVar> alive = [];
        lock (Sync)
        {
            for (int i = LiveInstances.Count - 1; i >= 0; i--)
            {
                if (LiveInstances[i].TryGetTarget(out var target))
                    alive.Add(target);
                else
                    LiveInstances.RemoveAt(i);
            }
            _nextPruneThreshold = Math.Max(InitialPruneThreshold, LiveInstances.Count * 2);
        }

        foreach (LocalizedStringVar item in alive)
            item.TryRefresh();
    }
}
