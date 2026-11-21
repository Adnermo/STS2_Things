using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace STS2_Things.Enchantments.Split;

internal sealed record SplitSerializedState(
    Dictionary<string, decimal> UnsplitDynamicValues,
    Dictionary<string, decimal> VisibleDynamicValues,
    int? UnsplitEnergyCost,
    int? VisibleEnergyCost,
    bool HasStarCostState,
    int? UnsplitStarCost,
    int? VisibleStarCost);

/// <summary>
/// Versioned compact string codec for <see cref="ThingsSplit.SplitState"/> (supports v1 and v2).
/// </summary>
internal static class SplitStateCodec
{
    public const string CurrentVersion = "2";

    public static string Serialize(
        CardModel card,
        IReadOnlyDictionary<string, decimal> unsplitDynamicValues,
        int? unsplitEnergyCost,
        int? unsplitStarCost)
    {
        var entries = new List<string>();
        foreach ((string name, decimal unsplitValue) in
                 unsplitDynamicValues.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!card.DynamicVars.TryGetValue(name, out DynamicVar? value) ||
                !SplitValueCalculator.IsKnownGameplayNumericVar(value))
            {
                continue;
            }

            string encodedName = Convert.ToBase64String(Encoding.UTF8.GetBytes(name));
            entries.Add(string.Join(",",
                encodedName,
                unsplitValue.ToString("G29", CultureInfo.InvariantCulture),
                value.BaseValue.ToString("G29", CultureInfo.InvariantCulture)));
        }

        string unsplitEnergyText =
            unsplitEnergyCost?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        string visibleEnergyText = card.EnergyCost.CostsX
            ? string.Empty
            : card.EnergyCost.GetWithModifiers(CostModifiers.None)
                .ToString(CultureInfo.InvariantCulture);
        string unsplitStarText =
            unsplitStarCost?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        string visibleStarText = card.HasStarCostX || card.BaseStarCost < 0
            ? string.Empty
            : card.BaseStarCost.ToString(CultureInfo.InvariantCulture);

        return string.Join("|",
            CurrentVersion,
            unsplitEnergyText,
            visibleEnergyText,
            unsplitStarText,
            visibleStarText,
            string.Join(";", entries));
    }

    public static SplitSerializedState? TryDeserialize(string? serialized)
    {
        if (string.IsNullOrEmpty(serialized))
            return null;

        string[] sections = serialized.Split('|');
        bool isVersion1 = sections.Length == 4 &&
                          string.Equals(sections[0], "1", StringComparison.Ordinal);
        bool isVersion2 = sections.Length == 6 &&
                          string.Equals(sections[0], CurrentVersion, StringComparison.Ordinal);
        if (!isVersion1 && !isVersion2)
            return null;

        int? unsplitEnergyCost = ParseNullableInt(sections[1]);
        int? visibleEnergyCost = ParseNullableInt(sections[2]);
        if ((sections[1].Length > 0 && !unsplitEnergyCost.HasValue) ||
            (sections[2].Length > 0 && !visibleEnergyCost.HasValue))
        {
            return null;
        }

        int? unsplitStarCost = null;
        int? visibleStarCost = null;
        int entriesSectionIndex = 3;
        if (isVersion2)
        {
            unsplitStarCost = ParseNullableInt(sections[3]);
            visibleStarCost = ParseNullableInt(sections[4]);
            if ((sections[3].Length > 0 && !unsplitStarCost.HasValue) ||
                (sections[4].Length > 0 && !visibleStarCost.HasValue))
            {
                return null;
            }

            entriesSectionIndex = 5;
        }

        var unsplitValues = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var visibleValues = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (sections[entriesSectionIndex].Length > 0)
        {
            foreach (string entry in sections[entriesSectionIndex].Split(';'))
            {
                string[] fields = entry.Split(',');
                if (fields.Length != 3 ||
                    !TryDecodeName(fields[0], out string name) ||
                    !decimal.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal unsplitValue) ||
                    !decimal.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out decimal visibleValue) ||
                    !unsplitValues.TryAdd(name, unsplitValue) ||
                    !visibleValues.TryAdd(name, visibleValue))
                {
                    return null;
                }
            }
        }

        return new SplitSerializedState(
            unsplitValues,
            visibleValues,
            unsplitEnergyCost,
            visibleEnergyCost,
            isVersion2,
            unsplitStarCost,
            visibleStarCost);
    }

    private static int? ParseNullableInt(string value)
    {
        if (value.Length == 0)
            return null;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    private static bool TryDecodeName(string encodedName, out string name)
    {
        try
        {
            name = Encoding.UTF8.GetString(Convert.FromBase64String(encodedName));
            return name.Length > 0;
        }
        catch (FormatException)
        {
            name = string.Empty;
            return false;
        }
    }
}
