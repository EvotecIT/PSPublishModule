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
        var runtimeInputs = command.RuntimeInputs ?? new List<DocumentationTypeHelp>();
        if (inputs.Count == 0)
        {
            command.Inputs = runtimeInputs;
            command.RuntimeInputs = new List<DocumentationTypeHelp>();
            return;
        }

        var normalized = new List<DocumentationTypeHelp>(inputs.Count);
        foreach (var input in inputs)
        {
            if (input is null || !TryParsePowerShellInputAggregate(input, runtimeInputs, out var parsedInputs))
            {
                if (input is not null) normalized.Add(input);
                continue;
            }

            normalized.AddRange(parsedInputs);
        }

        command.Inputs = normalized;
        command.RuntimeInputs = new List<DocumentationTypeHelp>();
    }

    private static bool TryParsePowerShellInputAggregate(
        DocumentationTypeHelp input,
        IReadOnlyList<DocumentationTypeHelp> runtimeInputs,
        out IReadOnlyList<DocumentationTypeHelp> parsedInputs)
    {
        parsedInputs = Array.Empty<DocumentationTypeHelp>();
        if (!string.IsNullOrWhiteSpace(input.Description) ||
            !string.IsNullOrEmpty(input.CanonicalTypeName) ||
            !string.IsNullOrEmpty(input.RuntimeIdentity) ||
            !string.IsNullOrEmpty(input.AssemblyQualifiedName) ||
            runtimeInputs.Count == 0)
            return false;

        var lines = SplitPowerShellHelpLines(input.Name);
        if (lines.Length < 2 || lines.Any(string.IsNullOrEmpty)) return false;

        var usedRuntimeInputs = new HashSet<int>();
        var result = new List<DocumentationTypeHelp>();
        DocumentationTypeHelp? current = null;
        List<string>? descriptionLines = null;

        foreach (var line in lines)
        {
            if (TryFindRuntimeInput(line, runtimeInputs, usedRuntimeInputs, out var runtimeIndex))
            {
                CompleteCurrent(result, current, descriptionLines);
                var runtimeInput = runtimeInputs[runtimeIndex];
                var clrTypeName = string.IsNullOrEmpty(runtimeInput.ClrTypeName)
                    ? line
                    : runtimeInput.ClrTypeName;
                current = new DocumentationTypeHelp
                {
                    Name = line,
                    ClrTypeName = clrTypeName,
                    LookupName = line,
                    LookupClrTypeName = clrTypeName
                };
                descriptionLines = new List<string>();
                usedRuntimeInputs.Add(runtimeIndex);
                continue;
            }

            if (current is null) return false;
            descriptionLines!.Add(line);
        }

        CompleteCurrent(result, current, descriptionLines);
        if (result.Count == 0) return false;
        parsedInputs = result;
        return true;
    }

    private static bool TryFindRuntimeInput(
        string line,
        IReadOnlyList<DocumentationTypeHelp> runtimeInputs,
        ISet<int> usedRuntimeInputs,
        out int runtimeIndex)
    {
        for (var index = 0; index < runtimeInputs.Count; index++)
        {
            if (usedRuntimeInputs.Contains(index)) continue;
            var runtimeInput = runtimeInputs[index];
            if (string.Equals(line, runtimeInput.Name, StringComparison.Ordinal) ||
                string.Equals(line, runtimeInput.ClrTypeName, StringComparison.Ordinal))
            {
                runtimeIndex = index;
                return true;
            }
        }

        runtimeIndex = -1;
        return false;
    }

    private static void CompleteCurrent(
        ICollection<DocumentationTypeHelp> result,
        DocumentationTypeHelp? current,
        IReadOnlyCollection<string>? descriptionLines)
    {
        if (current is null) return;
        current.Description = string.Join("\n", descriptionLines ?? Array.Empty<string>());
        result.Add(current);
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
