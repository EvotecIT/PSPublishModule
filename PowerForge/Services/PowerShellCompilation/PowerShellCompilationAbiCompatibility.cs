using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

/// <summary>Checks whether a generated PowerShell library preserves an earlier public CLR ABI.</summary>
public static class PowerShellCompilationAbiCompatibility
{
    /// <summary>
    /// Compares a baseline ABI with a candidate ABI. New methods are compatible; removing or
    /// changing an existing method, generated type identity, or module lifetime is not.
    /// </summary>
    public static PowerShellCompilationAbiCompatibilityResult Compare(
        PowerShellCompilationAbiManifest baseline,
        PowerShellCompilationAbiManifest candidate)
    {
        if (baseline is null) throw new ArgumentNullException(nameof(baseline));
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));

        var issues = new List<PowerShellCompilationAbiCompatibilityIssue>();
        ValidateManifest(baseline, "baseline", issues);
        ValidateManifest(candidate, "candidate", issues);
        if (issues.Count != 0) return new PowerShellCompilationAbiCompatibilityResult(issues.ToArray());

        CompareValue(
            baseline.NamespaceName,
            candidate.NamespaceName,
            PowerShellCompilationAbiCompatibilityIssueKind.NamespaceChanged,
            "namespace",
            "The generated CLR namespace changed.",
            issues);
        CompareValue(
            baseline.TypeName,
            candidate.TypeName,
            PowerShellCompilationAbiCompatibilityIssueKind.TypeNameChanged,
            "type",
            "The generated public CLR type name changed.",
            issues);

        if (!LifetimeContract(baseline.ModuleLifetime).Equals(
                LifetimeContract(candidate.ModuleLifetime), StringComparison.Ordinal))
        {
            issues.Add(new PowerShellCompilationAbiCompatibilityIssue(
                PowerShellCompilationAbiCompatibilityIssueKind.ModuleLifetimeChanged,
                "moduleLifetime",
                "The public construction, reset, state, or disposal contract changed."));
        }

        var candidateMethods = candidate.Methods
            .GroupBy(static method => method.ClrName, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        foreach (var method in baseline.Methods.OrderBy(static method => method.ClrName, StringComparer.Ordinal))
        {
            if (!candidateMethods.TryGetValue(method.ClrName, out var matches) || matches.Length == 0)
            {
                issues.Add(new PowerShellCompilationAbiCompatibilityIssue(
                    PowerShellCompilationAbiCompatibilityIssueKind.MethodRemoved,
                    $"methods.{method.ClrName}",
                    $"Public method '{method.ClrName}' was removed."));
                continue;
            }

            if (matches.Length != 1 || !MethodContract(method).Equals(MethodContract(matches[0]), StringComparison.Ordinal))
            {
                issues.Add(new PowerShellCompilationAbiCompatibilityIssue(
                    PowerShellCompilationAbiCompatibilityIssueKind.MethodChanged,
                    $"methods.{method.ClrName}",
                    $"Public method '{method.ClrName}' changed its signature or semantic contract."));
            }
        }

        return new PowerShellCompilationAbiCompatibilityResult(issues.ToArray());
    }

    /// <summary>Throws when the candidate ABI does not preserve the baseline public contract.</summary>
    public static void EnsureCompatible(
        PowerShellCompilationAbiManifest baseline,
        PowerShellCompilationAbiManifest candidate)
    {
        var result = Compare(baseline, candidate);
        if (result.IsCompatible) return;
        throw new InvalidOperationException(
            "The generated PowerShell library contains breaking public ABI changes: " +
            string.Join(" ", result.Issues.Select(static issue => $"{issue.Path}: {issue.Message}")));
    }

    private static void ValidateManifest(
        PowerShellCompilationAbiManifest manifest,
        string name,
        ICollection<PowerShellCompilationAbiCompatibilityIssue> issues)
    {
        if (manifest.SchemaVersion is not 4 and not 5 || string.IsNullOrWhiteSpace(manifest.NamespaceName) ||
            string.IsNullOrWhiteSpace(manifest.TypeName) || manifest.Methods is null ||
            manifest.Methods.Any(static method => !IsValidMethod(method)) ||
            manifest.ModuleLifetime is { } lifetime && !IsValidLifetime(lifetime))
        {
            issues.Add(new PowerShellCompilationAbiCompatibilityIssue(
                PowerShellCompilationAbiCompatibilityIssueKind.InvalidManifest,
                name,
                $"The {name} ABI manifest is incomplete."));
            return;
        }

        foreach (var duplicate in manifest.Methods.GroupBy(static method => method.ClrName, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            issues.Add(new PowerShellCompilationAbiCompatibilityIssue(
                PowerShellCompilationAbiCompatibilityIssueKind.InvalidManifest,
                $"{name}.methods.{duplicate.Key}",
                $"The {name} ABI contains more than one method named '{duplicate.Key}'."));
        }
    }

    private static bool IsValidMethod(PowerShellCompilationAbiMethod? method)
    {
        if (method is null || string.IsNullOrWhiteSpace(method.ClrName) || HasNull(method.OutputValueStates) ||
            HasNull(method.Aliases) || method.Parameters is null || method.CommandProviders is null)
            return false;
        if (method.Parameters.Any(static parameter => parameter is null || HasNull(parameter.Aliases) ||
                parameter.Bindings is null || parameter.Validations is null ||
                parameter.Bindings.Any(static binding => binding is null) ||
                parameter.Validations.Any(static validation => validation is null || HasNull(validation.Arguments))))
            return false;
        return !method.CommandProviders.Any(static provider => provider is null || HasNull(provider.Aliases) ||
            HasNull(provider.ModuleNames) || provider.Parameters is null || provider.Adapter is null ||
            HasNull(provider.Adapter.Dependencies) ||
            provider.Parameters.Any(static parameter => parameter is null || HasNull(parameter.Aliases)));
    }

    private static bool IsValidLifetime(PowerShellRuntimeFreeModuleContract lifetime)
        => lifetime.Parameters is not null && lifetime.SupportedParameterCounts is not null && lifetime.Fields is not null &&
           lifetime.Parameters.All(static parameter => parameter is not null &&
               !string.IsNullOrWhiteSpace(parameter.Name) && !string.IsNullOrWhiteSpace(parameter.TypeName) &&
               !HasNull(parameter.Aliases) && parameter.Bindings is not null &&
               parameter.Bindings.All(static binding => binding is not null) && parameter.Validations is not null &&
               parameter.Validations.All(static validation => validation is not null && !HasNull(validation.Arguments))) &&
           lifetime.Fields.All(static field => field is not null && !string.IsNullOrWhiteSpace(field.Name) &&
               !string.IsNullOrWhiteSpace(field.TypeName));

    private static bool HasNull<T>(IEnumerable<T>? values) where T : class
        => values is null || values.Any(static value => value is null);

    private static string MethodContract(PowerShellCompilationAbiMethod method)
        => PowerShellCompilationAbiBuilder.GetNormalizedText(new PowerShellCompilationAbiManifest
        {
            SchemaVersion = 5,
            NamespaceName = "Compatibility",
            TypeName = "Contract",
            Methods = new[] { method }
        });

    private static string LifetimeContract(PowerShellRuntimeFreeModuleContract? lifetime)
        => lifetime is null
            ? string.Empty
            : PowerShellCompilationAbiBuilder.GetNormalizedText(new PowerShellCompilationAbiManifest
            {
                SchemaVersion = 5,
                NamespaceName = "Compatibility",
                TypeName = "Contract",
                ModuleLifetime = lifetime,
                Methods = Array.Empty<PowerShellCompilationAbiMethod>()
            });

    private static void CompareValue(
        string baseline,
        string candidate,
        PowerShellCompilationAbiCompatibilityIssueKind kind,
        string path,
        string message,
        ICollection<PowerShellCompilationAbiCompatibilityIssue> issues)
    {
        if (!string.Equals(baseline, candidate, StringComparison.Ordinal))
            issues.Add(new PowerShellCompilationAbiCompatibilityIssue(kind, path, message));
    }
}
