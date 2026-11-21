using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using STS2_Things.Enchantments.Split;
using STS2_Things.Platform;

namespace STS2_Things.Enchantments;

/// <summary>
/// Splits a card's fixed energy/star costs and known numeric gameplay values in half.
/// Costs round down and numeric values round up.
/// </summary>
public sealed class ThingsSplit : EnchantmentModel
{
    private Dictionary<string, decimal>? _unsplitDynamicValues;
    private int? _unsplitEnergyCost;
    private int? _unsplitStarCost;
    private int _internalWriteDepth;
    private DowngradeMutation? _pendingDowngrade;
    private DynamicUpgradeMutation? _pendingDynamicUpgrade;
    private EnergyUpgradeMutation? _pendingEnergyUpgrade;
    private StarUpgradeMutation? _pendingStarUpgrade;
    private SplitSerializedState? _pendingSerializedState;

    static ThingsSplit()
    {
        Sts2VersionSeam.EnsureSavedPropertyTypeRegistered(typeof(ThingsSplit));
    }

    /// <summary>
    /// Persists both the logical unsplit baseline and the currently visible
    /// split values. The latter matters for cards that permanently grow after
    /// being split: their growth applies to the half-card, not to a temporarily
    /// restored full card.
    /// </summary>
    [SavedProperty]
    public string SplitState
    {
        get => !HasCard || _unsplitDynamicValues == null
            ? string.Empty
            : SplitStateCodec.Serialize(Card, _unsplitDynamicValues, _unsplitEnergyCost, _unsplitStarCost);
        set
        {
            AssertMutable();
            _pendingSerializedState = SplitStateCodec.TryDeserialize(value);
        }
    }

    public override bool CanEnchant(CardModel card)
    {
        return base.CanEnchant(card) &&
               SplitValueCalculator.HasFixedEnergyCost(card) &&
               !card.HasStarCostX;
    }

    protected override void OnEnchant()
    {
        if (_pendingDowngrade != null)
        {
            CompleteDowngradeTransition();
            return;
        }

        if (_unsplitDynamicValues == null)
        {
            CaptureCurrentValuesAsUnsplit();
            ApplySplitValues();
        }
    }

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _unsplitDynamicValues = _unsplitDynamicValues == null
            ? null
            : new Dictionary<string, decimal>(_unsplitDynamicValues, StringComparer.Ordinal);
        _internalWriteDepth = 0;
        _pendingDowngrade = null;
        _pendingDynamicUpgrade = null;
        _pendingEnergyUpgrade = null;
        _pendingStarUpgrade = null;
        _pendingSerializedState = null;
    }

    private void EnsureUnsplitSnapshot()
    {
        if (_unsplitDynamicValues == null)
            CaptureCurrentValuesAsUnsplit();
    }

    private void CaptureCurrentValuesAsUnsplit()
    {
        _unsplitDynamicValues = Card.DynamicVars.Values
            .Where(SplitValueCalculator.IsKnownGameplayNumericVar)
            .ToDictionary(value => value.Name, value => value.BaseValue, StringComparer.Ordinal);
        _unsplitEnergyCost = Card.EnergyCost.CostsX
            ? null
            : Card.EnergyCost.GetWithModifiers(CostModifiers.None);
        _unsplitStarCost = Card.HasStarCostX || Card.BaseStarCost < 0
            ? null
            : Card.BaseStarCost;
    }

    internal void RestoreUnsplitValues()
    {
        EnsureUnsplitSnapshot();
        RunInternalWrite(() =>
        {
            foreach (DynamicVar value in Card.DynamicVars.Values)
            {
                if (_unsplitDynamicValues!.TryGetValue(value.Name, out decimal unsplitValue) &&
                    SplitValueCalculator.IsKnownGameplayNumericVar(value))
                {
                    value.BaseValue = unsplitValue;
                }
            }

            Card.DynamicVars.RecalculateForUpgradeOrEnchant();
            if (_unsplitEnergyCost.HasValue && !Card.EnergyCost.CostsX)
                Card.EnergyCost.SetCustomBaseCost(_unsplitEnergyCost.Value);

            if (_unsplitStarCost.HasValue && !Card.HasStarCostX)
                EngineAccess.SetCardBaseStarCost(Card, _unsplitStarCost.Value);
        });
    }

    private void ApplySplitValues()
    {
        RunInternalWrite(() =>
        {
            foreach (DynamicVar value in Card.DynamicVars.Values)
            {
                if (_unsplitDynamicValues!.TryGetValue(value.Name, out decimal unsplitValue) &&
                    SplitValueCalculator.IsKnownGameplayNumericVar(value))
                {
                    value.BaseValue = SplitValueCalculator.SplitDynamicValue(unsplitValue);
                }
            }

            Card.DynamicVars.RecalculateForUpgradeOrEnchant();
            if (_unsplitEnergyCost.HasValue && !Card.EnergyCost.CostsX)
                Card.EnergyCost.SetCustomBaseCost(SplitValueCalculator.SplitEnergyCost(_unsplitEnergyCost.Value));

            if (_unsplitStarCost.HasValue && !Card.HasStarCostX)
                EngineAccess.SetCardBaseStarCost(Card, SplitValueCalculator.SplitEnergyCost(_unsplitStarCost.Value));
        });
    }

    private void RunInternalWrite(Action action)
    {
        _internalWriteDepth++;
        try
        {
            action();
        }
        finally
        {
            _internalWriteDepth--;
        }
    }

    internal void BeforeDynamicBaseValueWrite(DynamicVar value, decimal newValue)
    {
        if (_internalWriteDepth > 0 || _pendingDowngrade != null ||
            !SplitValueCalculator.IsKnownGameplayNumericVar(value))
        {
            return;
        }

        if (_pendingDynamicUpgrade?.Value == value)
        {
            _pendingDynamicUpgrade.WriteConsumed = true;
            return;
        }

        EnsureUnsplitSnapshot();
        if (_unsplitDynamicValues!.TryGetValue(value.Name, out decimal unsplitValue))
        {
            decimal clampedNewValue = decimal.Min(newValue, SplitValueCalculator.DynamicVarMaximum);
            _unsplitDynamicValues[value.Name] = unsplitValue + clampedNewValue - value.BaseValue;
        }
    }

    internal FullValueSetterMutation? PrepareFullValueSetter(
        string dynamicVarName,
        decimal intendedFullValue)
    {
        if (_internalWriteDepth > 0 || _pendingDowngrade != null)
            return null;

        EnsureUnsplitSnapshot();
        if (!_unsplitDynamicValues!.TryGetValue(dynamicVarName, out decimal previousUnsplitValue) ||
            !Card.DynamicVars.TryGetValue(dynamicVarName, out DynamicVar? value) ||
            !SplitValueCalculator.IsKnownGameplayNumericVar(value))
        {
            return null;
        }

        return new FullValueSetterMutation(
            this,
            value,
            dynamicVarName,
            previousUnsplitValue,
            value.BaseValue,
            decimal.Min(intendedFullValue, SplitValueCalculator.DynamicVarMaximum));
    }

    internal static void CompleteFullValueSetter(FullValueSetterMutation? mutation)
    {
        if (mutation == null)
            return;

        ThingsSplit split = mutation.Split;
        decimal visibleValue = mutation.PreviousVisibleValue +
            mutation.IntendedFullValue - mutation.PreviousUnsplitValue;
        split.RunInternalWrite(() =>
        {
            split._unsplitDynamicValues![mutation.Name] = mutation.IntendedFullValue;
            mutation.Value.BaseValue = decimal.Min(visibleValue, SplitValueCalculator.DynamicVarMaximum);
            split.Card.DynamicVars.RecalculateForUpgradeOrEnchant();
        });
    }

    internal decimal NormalizeStoredFullValue(string dynamicVarName, decimal visibleValue)
    {
        if (_internalWriteDepth > 0 || _pendingDowngrade != null)
            return visibleValue;

        EnsureUnsplitSnapshot();
        return _unsplitDynamicValues!.GetValueOrDefault(dynamicVarName, visibleValue);
    }

    internal DynamicUpgradeMutation? PrepareDynamicUpgrade(DynamicVar value, ref decimal addend)
    {
        if (_internalWriteDepth > 0 || _pendingDowngrade != null ||
            _pendingDynamicUpgrade != null || !SplitValueCalculator.IsKnownGameplayNumericVar(value))
        {
            return null;
        }

        EnsureUnsplitSnapshot();
        if (!_unsplitDynamicValues!.TryGetValue(value.Name, out decimal oldUnsplitValue))
            return null;

        decimal newUnsplitValue = decimal.Min(oldUnsplitValue + addend, SplitValueCalculator.DynamicVarMaximum);
        decimal expectedVisibleValue = SplitValueCalculator.SplitDynamicValue(newUnsplitValue);
        var mutation = new DynamicUpgradeMutation(
            this,
            value,
            value.Name,
            oldUnsplitValue,
            expectedVisibleValue);
        _pendingDynamicUpgrade = mutation;
        _unsplitDynamicValues[value.Name] = newUnsplitValue;
        addend = expectedVisibleValue - value.BaseValue;
        return mutation;
    }

    internal static void CompleteDynamicUpgrade(DynamicUpgradeMutation? mutation, Exception? exception)
    {
        if (mutation == null)
            return;

        ThingsSplit split = mutation.Split;
        if (!ReferenceEquals(split._pendingDynamicUpgrade, mutation))
            return;

        split._pendingDynamicUpgrade = null;
        if (!mutation.WriteConsumed ||
            (exception != null && mutation.Value.BaseValue != mutation.ExpectedVisibleValue))
        {
            split._unsplitDynamicValues![mutation.Name] = mutation.PreviousUnsplitValue;
        }
    }

    internal void BeforeCustomEnergyCostWrite(CardEnergyCost energyCost, int newBaseCost)
    {
        if (_internalWriteDepth > 0 || _pendingDowngrade != null)
            return;

        if (_pendingEnergyUpgrade != null)
        {
            _pendingEnergyUpgrade.WriteConsumed = true;
            return;
        }

        EnsureUnsplitSnapshot();
        if (_unsplitEnergyCost.HasValue)
        {
            int oldVisibleCost = energyCost.GetWithModifiers(CostModifiers.None);
            _unsplitEnergyCost += newBaseCost - oldVisibleCost;
        }
    }

    internal EnergyUpgradeMutation? PrepareEnergyUpgrade(CardEnergyCost energyCost, ref int addend)
    {
        if (_internalWriteDepth > 0 || _pendingDowngrade != null ||
            _pendingEnergyUpgrade != null || energyCost.CostsX || addend == 0)
        {
            return null;
        }

        EnsureUnsplitSnapshot();
        if (!_unsplitEnergyCost.HasValue)
            return null;

        int oldUnsplitCost = _unsplitEnergyCost.Value;
        int oldVisibleCost = energyCost.GetWithModifiers(CostModifiers.None);
        int newUnsplitCost = Math.Max(oldUnsplitCost + addend, 0);
        int expectedVisibleCost = SplitValueCalculator.SplitEnergyCost(newUnsplitCost);
        var mutation = new EnergyUpgradeMutation(
            this,
            energyCost,
            oldUnsplitCost,
            oldVisibleCost,
            expectedVisibleCost);
        _pendingEnergyUpgrade = mutation;
        _unsplitEnergyCost = newUnsplitCost;
        addend = expectedVisibleCost - oldVisibleCost;
        return mutation;
    }

    internal static void CompleteEnergyUpgrade(EnergyUpgradeMutation? mutation, Exception? exception)
    {
        _ = exception;
        if (mutation == null)
            return;

        ThingsSplit split = mutation.Split;
        if (!ReferenceEquals(split._pendingEnergyUpgrade, mutation))
            return;

        split._pendingEnergyUpgrade = null;
        int actualCost = mutation.EnergyCost.GetWithModifiers(CostModifiers.None);
        if (actualCost != mutation.ExpectedVisibleCost)
            split._unsplitEnergyCost = mutation.PreviousUnsplitCost;
    }

    internal void BeforeBaseStarCostWrite(int newBaseCost)
    {
        if (_internalWriteDepth > 0 || _pendingDowngrade != null || Card.HasStarCostX)
            return;

        if (_pendingStarUpgrade != null)
        {
            _pendingStarUpgrade.WriteConsumed = true;
            return;
        }

        EnsureUnsplitSnapshot();
        if (_unsplitStarCost.HasValue)
            _unsplitStarCost += newBaseCost - Card.BaseStarCost;
    }

    internal StarUpgradeMutation? PrepareStarUpgrade(ref int addend)
    {
        if (_internalWriteDepth > 0 || _pendingDowngrade != null ||
            _pendingStarUpgrade != null || Card.HasStarCostX || addend == 0)
        {
            return null;
        }

        EnsureUnsplitSnapshot();
        if (!_unsplitStarCost.HasValue)
            return null;

        int oldUnsplitCost = _unsplitStarCost.Value;
        int oldVisibleCost = Card.BaseStarCost;
        int newUnsplitCost = oldUnsplitCost + addend;
        int expectedVisibleCost = SplitValueCalculator.SplitEnergyCost(newUnsplitCost);
        var mutation = new StarUpgradeMutation(
            this,
            oldUnsplitCost,
            oldVisibleCost,
            expectedVisibleCost);
        _pendingStarUpgrade = mutation;
        _unsplitStarCost = newUnsplitCost;
        addend = expectedVisibleCost - oldVisibleCost;
        return mutation;
    }

    internal static void CompleteStarUpgrade(StarUpgradeMutation? mutation, Exception? exception)
    {
        _ = exception;
        if (mutation == null)
            return;

        ThingsSplit split = mutation.Split;
        if (!ReferenceEquals(split._pendingStarUpgrade, mutation))
            return;

        split._pendingStarUpgrade = null;
        int actualCost = split.Card.BaseStarCost;
        if (actualCost != mutation.ExpectedVisibleCost)
            split._unsplitStarCost = mutation.PreviousUnsplitCost;
    }

    internal void BeginDowngrade()
    {
        if (_internalWriteDepth > 0 || _pendingDowngrade != null || !HasCard)
            return;

        EnsureUnsplitSnapshot();
        var visibleDynamicValues = Card.DynamicVars.Values
            .Where(SplitValueCalculator.IsKnownGameplayNumericVar)
            .ToDictionary(value => value.Name, value => value.BaseValue, StringComparer.Ordinal);
        _pendingDowngrade = new DowngradeMutation(
            new Dictionary<string, decimal>(_unsplitDynamicValues!, StringComparer.Ordinal),
            visibleDynamicValues,
            _unsplitEnergyCost,
            Card.EnergyCost.CostsX ? null : Card.EnergyCost.GetWithModifiers(CostModifiers.None),
            _unsplitStarCost,
            Card.HasStarCostX || Card.BaseStarCost < 0 ? null : Card.BaseStarCost);
    }

    private void CompleteDowngradeTransition()
    {
        DowngradeMutation? mutation = _pendingDowngrade;
        if (mutation == null || !HasCard)
            return;

        var postDowngradeDynamicValues = Card.DynamicVars.Values
            .Where(SplitValueCalculator.IsKnownGameplayNumericVar)
            .ToDictionary(value => value.Name, value => value.BaseValue, StringComparer.Ordinal);
        int? postDowngradeEnergyCost = Card.EnergyCost.CostsX
            ? null
            : Card.EnergyCost.GetWithModifiers(CostModifiers.None);
        int? postDowngradeStarCost = Card.HasStarCostX || Card.BaseStarCost < 0
            ? null
            : Card.BaseStarCost;

        RunInternalWrite(() =>
        {
            foreach (DynamicVar value in Card.DynamicVars.Values)
            {
                if (!SplitValueCalculator.IsKnownGameplayNumericVar(value) ||
                    !postDowngradeDynamicValues.TryGetValue(value.Name, out decimal postDowngradeValue))
                {
                    continue;
                }

                decimal visibleValue = SplitValueCalculator.SplitDynamicValue(postDowngradeValue);
                if (mutation.VisibleDynamicValues.TryGetValue(value.Name, out decimal previousVisibleValue) &&
                    mutation.UnsplitDynamicValues.TryGetValue(value.Name, out decimal previousUnsplitValue))
                {
                    visibleValue = previousVisibleValue +
                        SplitValueCalculator.SplitDynamicValue(postDowngradeValue) -
                        SplitValueCalculator.SplitDynamicValue(previousUnsplitValue);
                }

                value.BaseValue = visibleValue;
            }

            _unsplitDynamicValues = postDowngradeDynamicValues;
            Card.DynamicVars.RecalculateForUpgradeOrEnchant();

            _unsplitEnergyCost = postDowngradeEnergyCost;
            if (postDowngradeEnergyCost.HasValue)
            {
                int visibleEnergyCost = SplitValueCalculator.SplitEnergyCost(postDowngradeEnergyCost.Value);
                if (mutation.VisibleEnergyCost.HasValue && mutation.UnsplitEnergyCost.HasValue)
                {
                    visibleEnergyCost = mutation.VisibleEnergyCost.Value +
                        SplitValueCalculator.SplitEnergyCost(postDowngradeEnergyCost.Value) -
                        SplitValueCalculator.SplitEnergyCost(mutation.UnsplitEnergyCost.Value);
                }

                Card.EnergyCost.SetCustomBaseCost(visibleEnergyCost);
            }

            _unsplitStarCost = postDowngradeStarCost;
            if (postDowngradeStarCost.HasValue)
            {
                int visibleStarCost = SplitValueCalculator.SplitEnergyCost(postDowngradeStarCost.Value);
                if (mutation.VisibleStarCost.HasValue && mutation.UnsplitStarCost.HasValue)
                {
                    visibleStarCost = mutation.VisibleStarCost.Value +
                        SplitValueCalculator.SplitEnergyCost(postDowngradeStarCost.Value) -
                        SplitValueCalculator.SplitEnergyCost(mutation.UnsplitStarCost.Value);
                }

                EngineAccess.SetCardBaseStarCost(Card, visibleStarCost);
            }
        });

        _pendingDowngrade = null;
    }

    internal void CompleteDowngrade(Exception? exception)
    {
        DowngradeMutation? mutation = _pendingDowngrade;
        if (mutation == null)
            return;

        if (exception == null)
        {
            CompleteDowngradeTransition();
            return;
        }

        try
        {
            RunInternalWrite(() =>
            {
                foreach (DynamicVar value in Card.DynamicVars.Values)
                {
                    if (SplitValueCalculator.IsKnownGameplayNumericVar(value) &&
                        mutation.VisibleDynamicValues.TryGetValue(value.Name, out decimal visibleValue))
                    {
                        value.BaseValue = visibleValue;
                    }
                }

                _unsplitDynamicValues = new Dictionary<string, decimal>(
                    mutation.UnsplitDynamicValues, StringComparer.Ordinal);
                Card.DynamicVars.RecalculateForUpgradeOrEnchant();
                _unsplitEnergyCost = mutation.UnsplitEnergyCost;
                if (mutation.VisibleEnergyCost.HasValue && !Card.EnergyCost.CostsX)
                    Card.EnergyCost.SetCustomBaseCost(mutation.VisibleEnergyCost.Value);

                _unsplitStarCost = mutation.UnsplitStarCost;
                if (mutation.VisibleStarCost.HasValue && !Card.HasStarCostX)
                    EngineAccess.SetCardBaseStarCost(Card, mutation.VisibleStarCost.Value);
            });
        }
        catch (Exception)
        {
            // Preserve the original exception that interrupted CardModel.DowngradeInternal.
        }
        finally
        {
            _pendingDowngrade = null;
        }
    }

    internal void ApplyPendingSerializedState()
    {
        SplitSerializedState? state = _pendingSerializedState;
        if (state == null || !HasCard)
            return;

        RunInternalWrite(() =>
        {
            var mergedUnsplitValues = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (DynamicVar value in Card.DynamicVars.Values)
            {
                if (!SplitValueCalculator.IsKnownGameplayNumericVar(value))
                    continue;

                decimal currentUnsplitValue =
                    _unsplitDynamicValues?.GetValueOrDefault(value.Name, value.BaseValue) ??
                    value.BaseValue;
                string savedName = Sts2VersionSeam.ResolveMigratedSplitVarName(
                    Card,
                    value,
                    state.UnsplitDynamicValues);
                mergedUnsplitValues[value.Name] =
                    state.UnsplitDynamicValues.GetValueOrDefault(savedName, currentUnsplitValue);
                if (state.VisibleDynamicValues.TryGetValue(savedName, out decimal visibleValue))
                    value.BaseValue = visibleValue;
            }

            _unsplitDynamicValues = mergedUnsplitValues;
            Card.DynamicVars.RecalculateForUpgradeOrEnchant();
            _unsplitEnergyCost = state.UnsplitEnergyCost;
            if (state.VisibleEnergyCost.HasValue && !Card.EnergyCost.CostsX)
                Card.EnergyCost.SetCustomBaseCost(state.VisibleEnergyCost.Value);

            if (state.HasStarCostState)
            {
                _unsplitStarCost = state.UnsplitStarCost;
                if (state.VisibleStarCost.HasValue && !Card.HasStarCostX)
                    EngineAccess.SetCardBaseStarCost(Card, state.VisibleStarCost.Value);
            }
        });
        _pendingSerializedState = null;
    }

    internal sealed class DynamicUpgradeMutation(
        ThingsSplit split,
        DynamicVar value,
        string name,
        decimal previousUnsplitValue,
        decimal expectedVisibleValue)
    {
        public ThingsSplit Split { get; } = split;
        public DynamicVar Value { get; } = value;
        public string Name { get; } = name;
        public decimal PreviousUnsplitValue { get; } = previousUnsplitValue;
        public decimal ExpectedVisibleValue { get; } = expectedVisibleValue;
        public bool WriteConsumed { get; set; }
    }

    internal sealed class FullValueSetterMutation(
        ThingsSplit split,
        DynamicVar value,
        string name,
        decimal previousUnsplitValue,
        decimal previousVisibleValue,
        decimal intendedFullValue)
    {
        public ThingsSplit Split { get; } = split;
        public DynamicVar Value { get; } = value;
        public string Name { get; } = name;
        public decimal PreviousUnsplitValue { get; } = previousUnsplitValue;
        public decimal PreviousVisibleValue { get; } = previousVisibleValue;
        public decimal IntendedFullValue { get; } = intendedFullValue;
    }

    internal sealed class EnergyUpgradeMutation(
        ThingsSplit split,
        CardEnergyCost energyCost,
        int previousUnsplitCost,
        int oldVisibleCost,
        int expectedVisibleCost)
    {
        public ThingsSplit Split { get; } = split;
        public CardEnergyCost EnergyCost { get; } = energyCost;
        public int PreviousUnsplitCost { get; } = previousUnsplitCost;
        public int OldVisibleCost { get; } = oldVisibleCost;
        public int ExpectedVisibleCost { get; } = expectedVisibleCost;
        public bool WriteConsumed { get; set; }
    }

    internal sealed class StarUpgradeMutation(
        ThingsSplit split,
        int previousUnsplitCost,
        int oldVisibleCost,
        int expectedVisibleCost)
    {
        public ThingsSplit Split { get; } = split;
        public int PreviousUnsplitCost { get; } = previousUnsplitCost;
        public int OldVisibleCost { get; } = oldVisibleCost;
        public int ExpectedVisibleCost { get; } = expectedVisibleCost;
        public bool WriteConsumed { get; set; }
    }

    private sealed record DowngradeMutation(
        Dictionary<string, decimal> UnsplitDynamicValues,
        Dictionary<string, decimal> VisibleDynamicValues,
        int? UnsplitEnergyCost,
        int? VisibleEnergyCost,
        int? UnsplitStarCost,
        int? VisibleStarCost);
}
