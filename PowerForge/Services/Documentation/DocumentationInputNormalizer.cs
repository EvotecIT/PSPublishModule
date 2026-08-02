using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Converts PowerShell's newline-aggregated Get-Help input type snapshot into
/// individual documentation type entries without weakening identity escaping.
/// </summary>
internal static class DocumentationInputNormalizer
{
    /// <summary>Normalizes collected input metadata for one command.</summary>
    internal static void Normalize(DocumentationCommandHelp command)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var inputs = command.Inputs ?? new List<DocumentationTypeHelp>();
        if (inputs.Count == 0) return;

        var normalized = new List<DocumentationTypeHelp>(inputs.Count);
        foreach (var input in inputs)
        {
            if (input is null || !TrySplitPowerShellInputAggregate(input, out var splitInputs))
            {
                if (input is not null) normalized.Add(input);
                continue;
            }

            normalized.AddRange(splitInputs);
        }

        command.Inputs = normalized;
    }

    private static bool TrySplitPowerShellInputAggregate(
        DocumentationTypeHelp input,
        out IReadOnlyList<DocumentationTypeHelp> splitInputs)
    {
        splitInputs = Array.Empty<DocumentationTypeHelp>();
        if (!string.IsNullOrWhiteSpace(input.Description) ||
            !string.IsNullOrEmpty(input.CanonicalTypeName) ||
            !string.IsNullOrEmpty(input.RuntimeIdentity) ||
            !string.IsNullOrEmpty(input.AssemblyQualifiedName))
            return false;

        var names = SplitPowerShellHelpLines(input.Name);
        if (names.Length < 2 || names.Any(string.IsNullOrEmpty)) return false;

        string[] clrTypeNames;
        if (string.IsNullOrEmpty(input.ClrTypeName))
        {
            clrTypeNames = Enumerable.Repeat(string.Empty, names.Length).ToArray();
        }
        else
        {
            clrTypeNames = SplitPowerShellHelpLines(input.ClrTypeName);
            if (clrTypeNames.Length != names.Length || clrTypeNames.Any(string.IsNullOrEmpty)) return false;
        }

        splitInputs = names
            .Select((name, index) => new DocumentationTypeHelp
            {
                Name = name,
                ClrTypeName = clrTypeNames[index],
                LookupName = name,
                LookupClrTypeName = clrTypeNames[index]
            })
            .ToArray();
        return true;
    }

    private static string[] SplitPowerShellHelpLines(string value)
    {
        var parts = (value ?? string.Empty).Split(
            new[] { "\r\n", "\r", "\n" },
            StringSplitOptions.None);
        var count = parts.Length;
        while (count > 0 && parts[count - 1].Length == 0) count--;
        return count == parts.Length ? parts : parts.Take(count).ToArray();
    }
}
