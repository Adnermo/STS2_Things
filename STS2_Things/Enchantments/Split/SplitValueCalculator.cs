using System;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using STS2_Things.Platform;

namespace STS2_Things.Enchantments.Split;

/// <summary>
/// Pure classification and halving math for <see cref="ThingsSplit"/>.
/// </summary>
internal static class SplitValueCalculator
{
    public const decimal DynamicVarMaximum = 999999999m;

    public static bool IsKnownGameplayNumericVar(DynamicVar value)
    {
        Type type = value.GetType();
        if (value is StringVar or BoolVar or IfUpgradedVar or CalculatedVar)
            return false;

        // A plain DynamicVar is the game's general-purpose numeric variable.
        // Subclasses are accepted only through an explicitly numeric base type;
        // this safely supports other mods' DamageVar/IntVar/etc. subclasses
        // without treating arbitrary text, boolean, conditional or calculated
        // variables as values that should be split.
        if (type == typeof(DynamicVar) ||
            Sts2VersionSeam.SplitKnownNumericVarTypes.Any(baseType => baseType.IsAssignableFrom(type)))
        {
            return true;
        }

        for (Type? current = type; current != null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(PowerVar<>))
                return true;
        }

        return false;
    }

    public static bool HasFixedEnergyCost(CardModel card)
    {
        return !card.EnergyCost.CostsX &&
               card.EnergyCost.GetWithModifiers(CostModifiers.None) >= 0;
    }

    public static decimal SplitDynamicValue(decimal value)
    {
        return decimal.Ceiling(value / 2m);
    }

    public static int SplitEnergyCost(int value)
    {
        return value < 0 ? value : value / 2;
    }
}
