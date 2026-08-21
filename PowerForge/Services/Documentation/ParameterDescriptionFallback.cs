using System;
using System.Collections.Generic;
using System.Text;

namespace PowerForge;

/// <summary>
/// Produces conservative generated help when authored parameter documentation is missing.
/// Validation still evaluates the authored value; writers use this only as a presentation fallback.
/// </summary>
internal static class ParameterDescriptionFallback
{
    private static readonly HashSet<string> CollectionTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Array",
        "Collection",
        "Dictionary",
        "HashSet",
        "ICollection",
        "IDictionary",
        "IEnumerable",
        "IList",
        "IReadOnlyCollection",
        "IReadOnlyDictionary",
        "IReadOnlyList",
        "List",
        "Queue",
        "Stack"
    };

    internal static string Resolve(string? authoredDescription, string parameterName, string? parameterType)
    {
        if (!IsMissingOrPlaceholder(authoredDescription))
            return authoredDescription!.Trim();

        return Create(parameterName, parameterType);
    }

    internal static bool IsMissingOrPlaceholder(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return true;

        var value = description!.Trim();
        return value.StartsWith("{{", StringComparison.Ordinal) &&
               value.EndsWith("}}", StringComparison.Ordinal) &&
               value.IndexOf("Fill", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string Create(string parameterName, string? parameterType)
    {
        var words = SplitIdentifier(parameterName).ToLowerInvariant();
        var type = parameterType ?? string.Empty;

        if (type.IndexOf("SwitchParameter", StringComparison.OrdinalIgnoreCase) >= 0)
            return $"Specifies the {words} switch.";

        if (type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Bool", StringComparison.OrdinalIgnoreCase))
            return $"Specifies a Boolean value for {words}.";

        if (IsCollectionType(type))
            return $"Specifies one or more values for {words}.";

        return $"Specifies a value for {words}.";
    }

    private static bool IsCollectionType(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal))
            return true;

        var simpleNameIndex = type.LastIndexOf('.');
        var simpleName = simpleNameIndex >= 0 ? type.Substring(simpleNameIndex + 1) : type;
        var genericMarker = simpleName.IndexOfAny(new[] { '`', '<' });
        var baseName = genericMarker > 0 ? simpleName.Substring(0, genericMarker) : simpleName;
        return CollectionTypeNames.Contains(baseName);
    }

    private static string SplitIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "parameter";

        var builder = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current))
            {
                var previous = value[index - 1];
                var nextIsLower = index + 1 < value.Length && char.IsLower(value[index + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                    builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
