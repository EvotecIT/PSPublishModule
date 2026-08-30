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

    /// <summary>Promoted semantic-family provenance, cross-joined with every named host profile.</summary>
    public static IReadOnlyList<PowerShellCompilationSemanticFeatureProvenance> FeatureProvenance { get; } =
        new ReadOnlyCollection<PowerShellCompilationSemanticFeatureProvenance>(CreateFeatureProvenance()
            .OrderBy(static evidence => evidence.FeatureId, StringComparer.Ordinal)
            .ThenBy(static evidence => evidence.ProfileId, StringComparer.Ordinal)
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

    /// <summary>
    /// Proposes review for changed upstream identities without mutating the accepted profiles or provenance.
    /// Unknown profiles fail closed; profiles without public source commits are ignored until an explicit source identity exists.
    /// </summary>
    public static IReadOnlyList<PowerShellCompilationSemanticUpstreamChange> ReviewUpstreamChanges(
        IReadOnlyDictionary<string, string> observedCommits)
    {
        if (observedCommits is null) throw new ArgumentNullException(nameof(observedCommits));
        var unknown = observedCommits.Keys.FirstOrDefault(profileId => !KnownProfiles.ContainsKey(profileId));
        if (unknown is not null)
            throw new KeyNotFoundException($"Unknown PowerForge semantic-oracle profile '{unknown}'.");

        var changes = new List<PowerShellCompilationSemanticUpstreamChange>();
        foreach (var pair in observedCommits.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var profile = KnownProfiles[pair.Key];
            var observed = pair.Value?.Trim() ?? string.Empty;
            if (profile.UpstreamCommit.Length == 0 ||
                observed.Length == 0 ||
                string.Equals(profile.UpstreamCommit, observed, StringComparison.OrdinalIgnoreCase))
                continue;
            changes.Add(new PowerShellCompilationSemanticUpstreamChange(
                profile.ProfileId,
                profile.UpstreamCommit,
                observed,
                FeatureProvenance
                    .Where(evidence => evidence.ProfileId.Equals(profile.ProfileId, StringComparison.Ordinal))
                    .Select(static evidence => evidence.FeatureId)));
        }
        return new ReadOnlyCollection<PowerShellCompilationSemanticUpstreamChange>(changes);
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

    private static IEnumerable<PowerShellCompilationSemanticFeatureProvenance> CreateFeatureProvenance()
    {
        var families = new[]
        {
            new SemanticFamily(PowerShellCompilationFeatureIds.ParameterType, "Binding/PowerShellParameterBinder.cs", "test/powershell/Language/Scripting/ParameterBinding/ParameterBinding.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_functions_advanced_parameters"),
            new SemanticFamily(PowerShellCompilationFeatureIds.ParameterDefault, "Binding/PowerShellSemanticBinder.cs", "test/powershell/Language/Scripting/ParameterBinding/ParameterBinding.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_functions_advanced_parameters"),
            new SemanticFamily(PowerShellCompilationFeatureIds.ParameterMetadata, "Binding/PowerShellParameterSemanticValidator.cs", "test/powershell/Language/Scripting/ParameterBinding/ParameterBinding.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_functions_advanced_parameters"),
            new SemanticFamily(PowerShellCompilationFeatureIds.ParameterBinding, "Binding/PowerShellParameterSemanticValidator.cs", "test/powershell/Language/Scripting/ParameterBinding/ParameterBinding.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_parameter_binding"),
            new SemanticFamily(PowerShellCompilationFeatureIds.Conversion, "Binding/PowerShellSemanticBinder.Expressions.cs", "test/powershell/Language/Scripting/TypeConversion/TypeConversion.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_type_conversion"),
            new SemanticFamily(PowerShellCompilationFeatureIds.ExpandableString, "Binding/PowerShellSemanticBinder.Expressions.cs", "test/powershell/Language/Parser/Parsing.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_quoting_rules"),
            new SemanticFamily(PowerShellCompilationFeatureIds.AssignmentTarget, "Binding/PowerShellSemanticBinder.cs", "test/powershell/Language/Operators/AssignmentOperator.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_assignment_operators"),
            new SemanticFamily(PowerShellCompilationFeatureIds.SwitchFlags, "Binding/PowerShellSemanticBinder.cs", "test/powershell/Language/Flow-Control/Switch.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_switch"),
            new SemanticFamily(PowerShellCompilationFeatureIds.CatchFilter, "Binding/PowerShellSemanticBinder.cs", "test/powershell/Language/Scripting/ExceptionHandling/ExceptionHandling.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_try_catch_finally"),
            new SemanticFamily(PowerShellCompilationFeatureIds.PipelineLifecycle, "Lowering/PowerShellTypedLowerer.cs", "test/powershell/Language/Scripting/AdvancedFunctions/AdvancedFunction.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_functions_advanced_methods"),
            new SemanticFamily(PowerShellCompilationFeatureIds.FunctionGraph, "Binding/PowerShellSemanticCallGraph.cs", "test/powershell/Language/Parser/Function.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_functions"),
            new SemanticFamily(PowerShellCompilationFeatureIds.CommentBasedHelp, "PowerShellCompiledHelpWriter.cs", "test/powershell/Host/Help/HelpSystem.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_comment_based_help"),
            new SemanticFamily(PowerShellCompilationFeatureIds.RequiresDirective, "Binding/PowerShellScriptRequirementPolicy.cs", "test/powershell/Language/Parser/Requires.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_requires"),
            new SemanticFamily(PowerShellCompilationFeatureIds.DictionaryFlow, "Binding/PowerShellSemanticBinder.Expressions.cs", "test/powershell/Language/Scripting/HashTable.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_hash_tables"),
            new SemanticFamily("operator.arithmetic", "Binding/PowerShellOperatorSemanticBinder.cs", "test/powershell/Language/Operators/ArithmeticOperator.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_arithmetic_operators"),
            new SemanticFamily("operator.comparison", "Binding/PowerShellOperatorSemanticBinder.cs", "test/powershell/Language/Operators/ComparisonOperator.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_comparison_operators"),
            new SemanticFamily("operator.logical", "Binding/PowerShellOperatorSemanticBinder.cs", "test/powershell/Language/Operators/LogicalOperator.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_logical_operators"),
            new SemanticFamily("pipeline.enumeration", "BoundTree/PowerShellBoundPipeline.cs", "test/powershell/Language/Scripting/Pipeline/Pipeline.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_pipelines"),
            new SemanticFamily("runtime.read-only-state", "Binding/PowerShellRuntimeStateIntrinsicPolicy.cs", "test/powershell/Language/Scripting/AutomaticVariables/AutomaticVariables.Tests.ps1", "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_automatic_variables")
        };

        foreach (var family in families)
        foreach (var profile in Profiles)
        {
            yield return new PowerShellCompilationSemanticFeatureProvenance(
                family.FeatureId,
                profile.ProfileId,
                profile.UpstreamCommit,
                new[] { family.UpstreamTest },
                new[] { family.DocumentationUri },
                expectedVersionDifference: family.FeatureId == PowerShellCompilationFeatureIds.PipelineLifecycle && profile.Family == PowerShellCompilationSemanticHostFamily.WindowsPowerShell51
                    ? "PowerShell 5.1 does not expose the clean lifecycle block; the profile rejects or hosts that shape explicitly."
                    : string.Empty,
                contractVersion: "1.0",
                owningComponent: family.Owner);
        }
    }

    private sealed class SemanticFamily
    {
        internal SemanticFamily(string featureId, string owner, string upstreamTest, string documentationUri)
        {
            FeatureId = featureId;
            Owner = owner;
            UpstreamTest = upstreamTest;
            DocumentationUri = documentationUri;
        }

        internal string FeatureId { get; }
        internal string Owner { get; }
        internal string UpstreamTest { get; }
        internal string DocumentationUri { get; }
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
