using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>Resolves aggregate requiredness without losing parameter-set-specific optionality.</summary>
internal static class DocumentationParameterRequiredResolver
{
    /// <summary>Returns true only when the parameter is required in every parameter set where it appears.</summary>
    public static bool IsAlwaysRequired(DocumentationParameterHelp parameter)
    {
        if (parameter is null) throw new ArgumentNullException(nameof(parameter));

        var requiredBySet = parameter.ParameterSetRequired;
        if (requiredBySet is null || requiredBySet.Count == 0)
            return parameter.Required;

        var parameterSets = (parameter.ParameterSets ?? new List<string>())
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (parameterSets.Length == 0)
            return parameter.Required;

        foreach (var parameterSet in parameterSets)
        {
            if (TryGetRequired(requiredBySet, parameterSet, out var required))
            {
                if (!required) return false;
                continue;
            }

            if (!parameter.Required) return false;
        }

        return true;
    }

    private static bool TryGetRequired(
        IReadOnlyDictionary<string, bool> requiredBySet,
        string parameterSet,
        out bool required)
    {
        foreach (var entry in requiredBySet)
        {
            if (string.Equals(entry.Key, parameterSet, StringComparison.OrdinalIgnoreCase))
            {
                required = entry.Value;
                return true;
            }
        }

        foreach (var entry in requiredBySet)
        {
            if (string.Equals(entry.Key, "(All)", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Key, "__AllParameterSets", StringComparison.OrdinalIgnoreCase))
            {
                required = entry.Value;
                return true;
            }
        }

        required = false;
        return false;
    }
}
