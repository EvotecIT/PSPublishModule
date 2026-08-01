using System;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Validates canonical CLR names that PowerShell can safely use as type literals.
/// </summary>
internal static class PowerShellTypeLiteralName
{
    /// <summary>Returns whether every namespace, nested-type, and generic argument segment is literal-safe.</summary>
    public static bool IsSafe(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || !string.Equals(typeName, typeName!.Trim(), StringComparison.Ordinal))
            return false;
        return IsSafeType(typeName!);
    }

    private static bool IsSafeType(string typeName)
    {
        var lastOpen = typeName.LastIndexOf('[');
        if (lastOpen > 0 && typeName.EndsWith("]", StringComparison.Ordinal))
        {
            var suffix = typeName.Substring(lastOpen + 1, typeName.Length - lastOpen - 2);
            if (suffix.Length == 0 || suffix == "*" || IsCommaOnly(suffix))
                return IsSafeType(typeName.Substring(0, lastOpen));
        }

        var genericOpen = typeName.IndexOf('[');
        if (genericOpen < 0) return IsSafeSimpleName(typeName);
        if (!typeName.EndsWith("]", StringComparison.Ordinal) ||
            !IsSafeSimpleName(typeName.Substring(0, genericOpen)))
            return false;

        var arguments = SplitGenericArguments(typeName, genericOpen);
        if (arguments is null || arguments.Count == 0) return false;
        foreach (var argument in arguments)
        {
            if (!IsSafeType(argument)) return false;
        }
        return true;
    }

    private static List<string>? SplitGenericArguments(string typeName, int genericOpen)
    {
        var arguments = new List<string>();
        var depth = 0;
        var start = genericOpen + 1;
        for (var index = start; index < typeName.Length - 1; index++)
        {
            var character = typeName[index];
            if (character == '[') { depth++; continue; }
            if (character == ']')
            {
                if (depth == 0) return null;
                depth--;
                continue;
            }
            if (character != ',' || depth != 0) continue;
            arguments.Add(typeName.Substring(start, index - start));
            start = index + 1;
        }
        if (depth != 0) return null;
        arguments.Add(typeName.Substring(start, typeName.Length - start - 1));
        return arguments;
    }

    private static bool IsSafeSimpleName(string typeName)
    {
        var segments = typeName.Split(new[] { '.', '+' }, StringSplitOptions.None);
        foreach (var segment in segments)
        {
            if (!IsSafeSegment(segment)) return false;
        }
        return segments.Length > 0;
    }

    private static bool IsSafeSegment(string segment)
    {
        if (segment.Length == 0 || !IsAsciiIdentifierStart(segment[0])) return false;
        var tick = segment.IndexOf('`');
        var identifierLength = tick < 0 ? segment.Length : tick;
        for (var index = 1; index < identifierLength; index++)
        {
            if (!IsAsciiIdentifierPart(segment[index])) return false;
        }
        if (tick < 0) return true;
        if (tick == segment.Length - 1 || segment.IndexOf('`', tick + 1) >= 0) return false;
        for (var index = tick + 1; index < segment.Length; index++)
        {
            if (segment[index] < '0' || segment[index] > '9') return false;
        }
        return true;
    }

    private static bool IsCommaOnly(string value)
    {
        foreach (var character in value)
        {
            if (character != ',') return false;
        }
        return value.Length > 0;
    }

    private static bool IsAsciiIdentifierStart(char value)
        => value == '_' || value >= 'A' && value <= 'Z' || value >= 'a' && value <= 'z';

    private static bool IsAsciiIdentifierPart(char value)
        => IsAsciiIdentifierStart(value) || value >= '0' && value <= '9';
}
