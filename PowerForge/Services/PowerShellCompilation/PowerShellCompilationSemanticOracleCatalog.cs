using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace PowerForge;

/// <summary>Canonical immutable semantic profiles and provenance used by PowerForge differential validation.</summary>
public static class PowerShellCompilationSemanticOracleCatalog
{
    /// <summary>Windows PowerShell 5.1 profile identity.</summary>
    public const string WindowsPowerShell51ProfileId = "PowerForge.Oracle.WindowsPowerShell/5.1";

    /// <summary>PowerShell 7.4 long-term-support profile identity.</summary>
    public const string PowerShell74ProfileId = "PowerForge.Oracle.PowerShell/7.4";

    /// <summary>PowerShell 7.6 profile identity.</summary>
    public const string PowerShell76ProfileId = "PowerForge.Oracle.PowerShell/7.6";

    private static readonly IReadOnlyDictionary<string, PowerShellCompilationSemanticOracleProfile> KnownProfiles =
        new ReadOnlyDictionary<string, PowerShellCompilationSemanticOracleProfile>(
            CreateProfiles().ToDictionary(static profile => profile.ProfileId, StringComparer.Ordinal));

    /// <summary>All compiler-owned semantic-oracle profiles, ordered by stable identity.</summary>
    public static IReadOnlyList<PowerShellCompilationSemanticOracleProfile> Profiles { get; } =
        new ReadOnlyCollection<PowerShellCompilationSemanticOracleProfile>(KnownProfiles.Values
            .OrderBy(static profile => profile.ProfileId, StringComparer.Ordinal)
            .ToArray());

    /// <summary>Returns one known semantic-oracle profile or fails closed for an unknown identity.</summary>
    public static PowerShellCompilationSemanticOracleProfile Get(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("A semantic profile identity is required.", nameof(profileId));
        return KnownProfiles.TryGetValue(profileId.Trim(), out var profile)
            ? profile
            : throw new KeyNotFoundException($"Unknown PowerForge semantic-oracle profile '{profileId}'.");
    }

    private static IEnumerable<PowerShellCompilationSemanticOracleProfile> CreateProfiles()
    {
        yield return new PowerShellCompilationSemanticOracleProfile(
            WindowsPowerShell51ProfileId,
            PowerShellCompilationSemanticHostFamily.WindowsPowerShell51,
            "powershell.exe",
            "Desktop",
            "[5.1,5.2)",
            "Windows",
            "x64",
            "invariant-per-case",
            new[] { "DesktopEdition", "FullFramework", "WindowsOnly", "WmiV1Hosted" },
            "Microsoft Windows PowerShell 5.1 product source",
            string.Empty,
            "https://learn.microsoft.com/powershell/scripting/windows-powershell/starting-windows-powershell");

        yield return new PowerShellCompilationSemanticOracleProfile(
            PowerShell74ProfileId,
            PowerShellCompilationSemanticHostFamily.PowerShell7,
            "pwsh",
            "Core",
            "[7.4,7.5)",
            "Any",
            "Any",
            "invariant-per-case",
            new[] { "CoreEdition", "CrossPlatform", "CimCmdlets" },
            "https://github.com/PowerShell/PowerShell",
            "4f5b7eb097060ccff3037ced9cd5c75d69cf74a1",
            "https://learn.microsoft.com/powershell/scripting/whats-new/what-s-new-in-powershell-74");

        yield return new PowerShellCompilationSemanticOracleProfile(
            PowerShell76ProfileId,
            PowerShellCompilationSemanticHostFamily.PowerShell7,
            "pwsh",
            "Core",
            "[7.6,7.7)",
            "Any",
            "Any",
            "invariant-per-case",
            new[] { "CoreEdition", "CrossPlatform", "CimCmdlets" },
            "https://github.com/PowerShell/PowerShell",
            "7acb29279dd64e646d821f75d1cc8ad59455a9a6",
            "https://learn.microsoft.com/powershell/scripting/whats-new/what-s-new-in-powershell-76");
    }
}

/// <summary>Compares normalized semantic observations while excluding host-identification metadata.</summary>
public static class PowerShellCompilationSemanticOracleComparer
{
    /// <summary>
    /// Compares semantic effects and fails closed unless every observed difference is explicitly allowed.
    /// Allowed paths are exact property paths returned by this method.
    /// </summary>
    public static IReadOnlyList<PowerShellCompilationSemanticOracleDifference> Compare(
        PowerShellCompilationSemanticOracleEnvelope expected,
        PowerShellCompilationSemanticOracleEnvelope actual,
        IEnumerable<string>? allowedDifferencePaths = null)
    {
        if (expected is null) throw new ArgumentNullException(nameof(expected));
        if (actual is null) throw new ArgumentNullException(nameof(actual));

        var allowed = new HashSet<string>(allowedDifferencePaths ?? Array.Empty<string>(), StringComparer.Ordinal);
        var differences = new List<PowerShellCompilationSemanticOracleDifference>();
        Add(differences, allowed, "Success", expected.Success, actual.Success);
        Add(differences, allowed, "Information", expected.Information, actual.Information);
        Add(differences, allowed, "Warnings", expected.Warnings, actual.Warnings);
        Add(differences, allowed, "Verbose", expected.Verbose, actual.Verbose);
        Add(differences, allowed, "Debug", expected.Debug, actual.Debug);
        Add(differences, allowed, "Errors", expected.Errors, actual.Errors);
        Add(differences, allowed, "ExitCode", expected.ExitCode, actual.ExitCode);
        Add(differences, allowed, "FileSystemEffects", expected.FileSystemEffects, actual.FileSystemEffects);
        Add(differences, allowed, "ProcessEffects", expected.ProcessEffects, actual.ProcessEffects);
        return new ReadOnlyCollection<PowerShellCompilationSemanticOracleDifference>(differences);
    }

    private static void Add<T>(
        ICollection<PowerShellCompilationSemanticOracleDifference> differences,
        ISet<string> allowed,
        string path,
        T expected,
        T actual)
    {
        var expectedJson = JsonSerializer.Serialize(expected);
        var actualJson = JsonSerializer.Serialize(actual);
        if (string.Equals(expectedJson, actualJson, StringComparison.Ordinal) || allowed.Contains(path))
            return;
        differences.Add(new PowerShellCompilationSemanticOracleDifference(path, expectedJson, actualJson));
    }
}
