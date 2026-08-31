using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Immutable result of provider metadata conformance validation.</summary>
public sealed class PowerShellCompilationProviderConformanceReport
{
    /// <summary>Conformance report schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Canonical identity of the validated provider contract set.</summary>
    public string ContractSha256 { get; set; } = string.Empty;

    /// <summary>Stable names of the conformance checks that passed.</summary>
    public string[] PassedChecks { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Validates portable provider metadata without loading assemblies, importing modules, or executing authored source.
/// The compiler remains the only semantic eligibility owner; this kit validates the external contract boundary.
/// </summary>
public sealed class PowerShellCompilationProviderConformanceKit
{
    /// <summary>Validates one provider package manifest and returns deterministic conformance evidence.</summary>
    public PowerShellCompilationProviderConformanceReport Validate(
        PowerShellCompilationProviderPackageManifest manifest)
    {
        PowerShellCompilationProviderContractValidator.Validate(manifest);
        var contracts = manifest.Providers;

        var forward = ComputeContractHash(contracts);
        var reverse = ComputeContractHash(contracts.AsEnumerable().Reverse());
        if (!forward.Equals(reverse, StringComparison.Ordinal))
            throw new InvalidOperationException("Provider registration order changes the canonical contract identity.");

        return new PowerShellCompilationProviderConformanceReport
        {
            ContractSha256 = forward,
            PassedChecks = new[]
            {
                "analysis-no-execution",
                "aot-and-runtime-dependency-claims",
                "cancellation-and-cleanup-contract-shape",
                "diagnostics-and-error-contract",
                "module-qualification-and-alias-ambiguity",
                "output-cardinality-and-value-state",
                "registration-order-independence",
                "stream-contract"
            }
        };
    }

    private static string ComputeContractHash(IEnumerable<PowerShellCompilationCommandProviderContract> contracts)
    {
        var canonical = contracts
            .OrderBy(static contract => contract.ProviderId, StringComparer.Ordinal)
            .Select(static contract => new
            {
                contract.SchemaVersion,
                contract.ProviderId,
                contract.ProviderVersion,
                contract.FeatureId,
                Family = contract.Family.ToString(),
                contract.CommandName,
                ModuleNames = (contract.ModuleNames ?? Array.Empty<string>()).OrderBy(static value => value, StringComparer.Ordinal),
                Aliases = (contract.Aliases ?? Array.Empty<string>()).OrderBy(static value => value, StringComparer.Ordinal),
                Parameters = (contract.Parameters ?? Array.Empty<PowerShellCompilationCommandParameterContract>())
                    .OrderBy(static parameter => parameter.Position)
                    .ThenBy(static parameter => parameter.Name, StringComparer.Ordinal)
                    .Select(static parameter => new
                    {
                        parameter.Name,
                        parameter.Position,
                        Aliases = (parameter.Aliases ?? Array.Empty<string>()).OrderBy(static value => value, StringComparer.Ordinal)
                    }),
                Output = contract.Output.ToString(),
                Cardinality = contract.Cardinality.ToString(),
                contract.Stream,
                Errors = contract.Errors.ToString(),
                contract.Adapter.Operation,
                contract.Adapter.SemanticProfile,
                contract.Adapter.RuntimeFree,
                contract.Adapter.AotCompatible,
                Cancellation = contract.Adapter.Cancellation.ToString(),
                Cleanup = contract.Adapter.Cleanup.ToString(),
                Dependencies = (contract.Adapter.Dependencies ?? Array.Empty<string>()).OrderBy(static value => value, StringComparer.Ordinal),
                EntryPoint = contract.Adapter.EntryPoint is null
                    ? null
                    : new
                    {
                        contract.Adapter.EntryPoint.AssemblyPath,
                        contract.Adapter.EntryPoint.TypeName,
                        contract.Adapter.EntryPoint.MethodName,
                        contract.Adapter.EntryPoint.ResultType
                    }
            });
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical)));
        return string.Concat(bytes.Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
