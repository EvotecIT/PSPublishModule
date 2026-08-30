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
    private static readonly string[] KnownStreams =
    {
        "None", "Success", "Verbose", "Debug", "Warning", "Information", "Host", "Error"
    };

    /// <summary>Validates one provider package manifest and returns deterministic conformance evidence.</summary>
    public PowerShellCompilationProviderConformanceReport Validate(
        PowerShellCompilationProviderPackageManifest manifest)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (manifest.Providers is null || manifest.Providers.Length == 0)
            throw new InvalidOperationException("Provider conformance requires at least one command contract.");
        if (manifest.SchemaVersion != 2 || manifest.SourceSemanticProfiles is null || manifest.SourceSemanticProfiles.Length == 0)
            throw new InvalidOperationException("Provider conformance requires schema 2 and at least one named source semantic profile.");
        _ = manifest.SourceSemanticProfiles
            .Select(static profile => PowerShellCompilationSemanticOracleCatalog.Get(profile).ProfileId)
            .ToArray();

        var contracts = manifest.Providers;
        EnsureUniqueRegistration(contracts);
        foreach (var contract in contracts) ValidateContract(contract, manifest);

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
                "cancellation-and-cleanup",
                "diagnostics-and-error-contract",
                "module-qualification-and-alias-ambiguity",
                "output-cardinality-and-value-state",
                "registration-order-independence",
                "stream-contract"
            }
        };
    }

    private static void ValidateContract(
        PowerShellCompilationCommandProviderContract contract,
        PowerShellCompilationProviderPackageManifest manifest)
    {
        if (contract.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(contract.ProviderId) ||
            string.IsNullOrWhiteSpace(contract.ProviderVersion) ||
            string.IsNullOrWhiteSpace(contract.FeatureId) ||
            string.IsNullOrWhiteSpace(contract.CommandName))
            throw new InvalidOperationException("Provider contracts require schema, provider, feature, and command identities.");
        if (!contract.CompileTimeOnly || contract.MayExecuteSource || contract.MayImportSourceModules)
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' violates the no-execution analysis boundary.");
        if (contract.Adapter is null || string.IsNullOrWhiteSpace(contract.Adapter.Operation))
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' requires an adapter operation.");
        if (contract.Adapter.RuntimeFree && contract.Adapter.EntryPoint is null)
            throw new InvalidOperationException($"Runtime-free package provider '{contract.ProviderId}' requires an executable adapter entry point.");
        if (!manifest.SemanticProfiles.Contains(contract.Adapter.SemanticProfile, StringComparer.Ordinal))
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' targets an undeclared semantic profile.");
        if (!KnownStreams.Contains(contract.Stream, StringComparer.Ordinal))
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' declares unsupported stream '{contract.Stream}'.");
        if (!Enum.IsDefined(typeof(PowerShellCompilationCommandErrors), contract.Errors) ||
            !Enum.IsDefined(typeof(PowerShellCompilationProviderCancellation), contract.Adapter.Cancellation) ||
            !Enum.IsDefined(typeof(PowerShellCompilationProviderCleanup), contract.Adapter.Cleanup))
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' declares an unknown error, cancellation, or cleanup contract.");
        if (contract.Output == PowerShellCompilationCommandOutput.None &&
            contract.Cardinality != PowerShellCompilationCommandCardinality.None)
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' cannot declare output cardinality for no output.");
        if (contract.Output != PowerShellCompilationCommandOutput.None &&
            contract.Cardinality == PowerShellCompilationCommandCardinality.None)
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' must declare output cardinality.");
        if (contract.Adapter.RuntimeFree && contract.Errors == PowerShellCompilationCommandErrors.PowerShellHost)
            throw new InvalidOperationException($"Runtime-free provider '{contract.ProviderId}' cannot delegate errors to a PowerShell host.");
        if (contract.Adapter.RuntimeFree && contract.Adapter.Dependencies.Any(static dependency =>
                dependency.Equals("System.Management.Automation", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Runtime-free provider '{contract.ProviderId}' cannot depend on System.Management.Automation.");
        if (contract.Adapter.AotCompatible && !contract.Adapter.RuntimeFree)
            throw new InvalidOperationException($"Hosted provider '{contract.ProviderId}' cannot claim runtime-free AOT compatibility.");
        if (!contract.Adapter.RuntimeFree &&
            (contract.Adapter.Cancellation == PowerShellCompilationProviderCancellation.Cooperative ||
             contract.Adapter.Cleanup == PowerShellCompilationProviderCleanup.Deterministic))
            throw new InvalidOperationException($"Hosted provider '{contract.ProviderId}' cannot claim runtime-free cancellation or cleanup ownership.");

        var names = contract.Parameters.SelectMany(static parameter =>
            new[] { parameter.Name }.Concat(parameter.Aliases ?? Array.Empty<string>()));
        if (names.Any(string.IsNullOrWhiteSpace) ||
            names.GroupBy(static name => name, StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() > 1))
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' declares ambiguous parameter names or aliases.");
    }

    private static void EnsureUniqueRegistration(IEnumerable<PowerShellCompilationCommandProviderContract> contracts)
    {
        var providers = contracts.ToArray();
        var duplicateId = providers.GroupBy(static provider => provider.ProviderId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateId is not null)
            throw new InvalidOperationException($"Provider identity '{duplicateId.Key}' is declared more than once.");

        var registrations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        foreach (var name in new[] { provider.CommandName }.Concat(provider.Aliases ?? Array.Empty<string>()))
        {
            Add(name, provider.ProviderId);
            foreach (var module in provider.ModuleNames ?? Array.Empty<string>())
                Add(module + "\\" + name, provider.ProviderId);
        }

        void Add(string key, string providerId)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException($"Provider '{providerId}' declares an empty command, alias, or module qualification.");
            if (registrations.TryGetValue(key, out var owner) && !owner.Equals(providerId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Command registration '{key}' is ambiguous between providers '{owner}' and '{providerId}'.");
            registrations[key] = providerId;
        }
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
                        contract.Adapter.EntryPoint.MethodName
                    }
            });
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical)));
        return string.Concat(bytes.Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
