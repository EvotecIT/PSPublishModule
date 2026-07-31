using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Reconstructs target-host runtime types as deterministic PowerShell expressions.
/// </summary>
internal static class PowerShellRuntimeTypeFormatter
{
    private static readonly Regex SafeTypeLiteralName = new(
        @"^[A-Za-z_][A-Za-z0-9_.+`]*(?:\[[A-Za-z0-9_.+`,\[\]]+\])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Formats a canonical type identity and its optional structural runtime shape.
    /// </summary>
    public static string Format(
        string canonicalTypeName,
        string runtimeTypeName,
        string assemblyName,
        string? runtimeTypeShape = null)
    {
        if (!SafeTypeLiteralName.IsMatch(canonicalTypeName) &&
            !string.IsNullOrWhiteSpace(runtimeTypeShape))
            return FormatRuntimeTypeShape(runtimeTypeShape!);
        if (canonicalTypeName.EndsWith("*", StringComparison.Ordinal))
            return FormatModifiedType(canonicalTypeName.Substring(0, canonicalTypeName.Length - 1), runtimeTypeName, assemblyName, ".MakePointerType()");
        if (canonicalTypeName.EndsWith("&", StringComparison.Ordinal))
            return FormatModifiedType(canonicalTypeName.Substring(0, canonicalTypeName.Length - 1), runtimeTypeName, assemblyName, ".MakeByRefType()");
        if (canonicalTypeName.EndsWith("[*]", StringComparison.Ordinal))
            return FormatModifiedType(canonicalTypeName.Substring(0, canonicalTypeName.Length - 3), runtimeTypeName, assemblyName, ".MakeArrayType(1)");
        if (canonicalTypeName.EndsWith("[]", StringComparison.Ordinal))
            return FormatModifiedType(canonicalTypeName.Substring(0, canonicalTypeName.Length - 2), runtimeTypeName, assemblyName, ".MakeArrayType()");
        if (!SafeTypeLiteralName.IsMatch(canonicalTypeName))
            return FormatExactLoadedType(runtimeTypeName, assemblyName);
        return "[" + canonicalTypeName + "]";
    }

    private static string FormatRuntimeTypeShape(string shape)
    {
        var tokens = shape.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        var index = 0;
        var expression = ParseRuntimeTypeShape(tokens, ref index);
        if (index != tokens.Length)
            throw new FormatException("The runtime type shape contains trailing tokens.");
        return expression;
    }

    private static string ParseRuntimeTypeShape(IReadOnlyList<string> tokens, ref int index)
    {
        if (index >= tokens.Count)
            throw new FormatException("The runtime type shape ended unexpectedly.");
        var token = tokens[index++];
        if (token.Equals("P", StringComparison.Ordinal))
            return AppendTypeModifier(ParseRuntimeTypeShape(tokens, ref index), ".MakePointerType()");
        if (token.Equals("R", StringComparison.Ordinal))
            return AppendTypeModifier(ParseRuntimeTypeShape(tokens, ref index), ".MakeByRefType()");
        if (token.StartsWith("A:", StringComparison.Ordinal))
        {
            var parts = token.Split(':');
            if (parts.Length != 3 ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rank) ||
                rank < 1 ||
                (parts[2] != "0" && parts[2] != "1"))
                throw new FormatException("The runtime type shape contains invalid array metadata.");
            var modifier = parts[2] == "1"
                ? ".MakeArrayType()"
                : ".MakeArrayType(" + rank.ToString(CultureInfo.InvariantCulture) + ")";
            return AppendTypeModifier(ParseRuntimeTypeShape(tokens, ref index), modifier);
        }
        if (token.StartsWith("G:", StringComparison.Ordinal))
        {
            if (!int.TryParse(token.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out var arity) || arity < 1)
                throw new FormatException("The runtime type shape contains invalid generic metadata.");
            var definition = ParenthesizeScriptType(ParseRuntimeTypeShape(tokens, ref index));
            var arguments = new List<string>(arity);
            for (var argumentIndex = 0; argumentIndex < arity; argumentIndex++)
                arguments.Add(ParenthesizeScriptType(ParseRuntimeTypeShape(tokens, ref index)));
            return definition + ".MakeGenericType([type[]]@(" + string.Join(", ", arguments) + "))";
        }
        if (!token.StartsWith("N:", StringComparison.Ordinal))
            throw new FormatException("The runtime type shape contains an unknown token.");
        var namedParts = token.Split(new[] { ':' }, 3);
        if (namedParts.Length != 3)
            throw new FormatException("The runtime type shape contains invalid named-type metadata.");
        var typeName = DecodeRuntimeTypeText(namedParts[1]);
        var assemblyName = DecodeRuntimeTypeText(namedParts[2]);
        if (SafeTypeLiteralName.IsMatch(typeName)) return "[" + typeName + "]";
        return FormatExactLoadedType(typeName, assemblyName);
    }

    private static string AppendTypeModifier(string expression, string modifier)
        => ParenthesizeScriptType(expression) + modifier;

    private static string ParenthesizeScriptType(string expression)
        => expression.StartsWith("& {", StringComparison.Ordinal) ? "(" + expression + ")" : expression;

    private static string DecodeRuntimeTypeText(string value)
    {
        try { return Encoding.Unicode.GetString(Convert.FromBase64String(value)); }
        catch (FormatException exception)
        {
            throw new FormatException("The runtime type shape contains invalid UTF-16 metadata.", exception);
        }
    }

    private static string FormatExactLoadedType(string runtimeTypeName, string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(runtimeTypeName) || string.IsNullOrWhiteSpace(assemblyName))
            return string.Empty;
        var formattedRuntimeTypeName = PowerShellDefaultValueFormatter.FormatString(runtimeTypeName, preserveCharacterType: false);
        return "& { $assembly = [System.AppDomain]::CurrentDomain.GetAssemblies() | " +
               "Where-Object { $_.FullName -eq " + PowerShellDefaultValueFormatter.FormatString(assemblyName, preserveCharacterType: false) +
               " } | Select-Object -First 1; if ($null -eq $assembly) { throw 'Type assembly is not loaded.' }; " +
               "$type = $assembly.GetType(" + formattedRuntimeTypeName + ", $false, $false); " +
               "if ($null -eq $type) { $type = $assembly.GetTypes() | Where-Object { $_.FullName -ceq " +
               formattedRuntimeTypeName + " } | Select-Object -First 1 }; " +
               "if ($null -eq $type) { throw 'Type is not available in the loaded assembly.' }; return $type }";
    }

    private static string FormatModifiedType(string elementTypeName, string runtimeTypeName, string assemblyName, string modifier)
    {
        var elementExpression = Format(elementTypeName, runtimeTypeName, assemblyName);
        if (elementExpression.Length == 0) return string.Empty;
        return ParenthesizeScriptType(elementExpression) + modifier;
    }
}
