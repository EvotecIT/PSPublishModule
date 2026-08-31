namespace PowerForge;

/// <summary>
/// Canonical metadata-only validation for provider package contracts. Both the SDK packer and
/// compiler package reader use this owner so a package cannot select a weaker validation route.
/// </summary>
public static class PowerShellCompilationProviderContractValidator
{
    private static readonly string[] KnownStreams =
    {
        "None", "Success", "Verbose", "Debug", "Warning", "Information", "Host", "Error"
    };

    /// <summary>Validates one provider package manifest without loading or executing provider code.</summary>
    public static void Validate(PowerShellCompilationProviderPackageManifest manifest)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (manifest.Providers is null || manifest.Providers.Length == 0)
            throw new InvalidOperationException("Provider conformance requires at least one command contract.");
        if (manifest.SchemaVersion != 3 || manifest.SourceSemanticProfiles is null || manifest.SourceSemanticProfiles.Length == 0)
            throw new InvalidOperationException("Provider conformance requires schema 3 and at least one named source semantic profile.");
        _ = manifest.SourceSemanticProfiles
            .Select(static profile => PowerShellCompilationSemanticOracleCatalog.Get(profile).ProfileId)
            .ToArray();

        EnsureUniqueRegistration(manifest.Providers);
        foreach (var contract in manifest.Providers)
            ValidateContract(contract, manifest);
    }

    /// <summary>Validates the executable command shape shared by package conformance and compiler registration.</summary>
    public static void ValidateExecutableContractShape(
        PowerShellCompilationCommandProviderContract contract,
        bool requireExecutableEntryPoint)
    {
        if (contract is null) throw new ArgumentNullException(nameof(contract));
        if (contract.Adapter is null)
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' requires an adapter contract.");

        if (contract.Adapter.RuntimeFree)
        {
            if (contract.Family is not (PowerShellCompilationCommandFamily.Stream or PowerShellCompilationCommandFamily.ExternalOperation) ||
                contract.Stream is not ("Success" or "Verbose" or "Debug" or "Warning" or "Information" or "Host" or "Error"))
                throw new InvalidOperationException($"Runtime-free provider '{contract.ProviderId}' must use one supported stream adapter contract.");
            var expectedProfile = PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" +
                                  PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion;
            if (!contract.Adapter.SemanticProfile.Equals(expectedProfile, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Runtime-free provider '{contract.ProviderId}' targets semantic profile '{contract.Adapter.SemanticProfile}' instead of '{expectedProfile}'.");
            if (contract.Family == PowerShellCompilationCommandFamily.Stream)
            {
                var expectedOperation = contract.Stream.Equals("Success", StringComparison.Ordinal)
                    ? "WriteOutput"
                    : "Write" + contract.Stream;
                if (!contract.Adapter.Operation.Equals(expectedOperation, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Runtime-free provider '{contract.ProviderId}' operation '{contract.Adapter.Operation}' does not match stream '{contract.Stream}' operation '{expectedOperation}'.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(contract.Adapter.Operation))
                    throw new InvalidOperationException($"Runtime-free external provider '{contract.ProviderId}' requires a named adapter operation.");
                if (contract.Adapter.EntryPoint is null)
                    throw new InvalidOperationException($"Runtime-free external provider '{contract.ProviderId}' requires an executable adapter entry point.");
            }
            if (requireExecutableEntryPoint && contract.Adapter.EntryPoint is null)
                throw new InvalidOperationException($"Runtime-free package provider '{contract.ProviderId}' requires an executable adapter entry point.");
            if ((contract.Parameters ?? Array.Empty<PowerShellCompilationCommandParameterContract>()).Length != 1)
                throw new InvalidOperationException($"Runtime-free provider '{contract.ProviderId}' must declare exactly one value parameter shape.");
        }
        else if (contract.Family == PowerShellCompilationCommandFamily.ExternalOperation)
        {
            throw new InvalidOperationException($"External provider '{contract.ProviderId}' must declare a runtime-free executable adapter.");
        }
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
        ValidateExecutableContractShape(contract, requireExecutableEntryPoint: true);
        if (contract.Adapter.EntryPoint is { } entryPoint &&
            !Enum.IsDefined(typeof(PowerShellCompilationProviderValueType), entryPoint.ResultType))
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' declares an unknown executable result type.");
        if (!(manifest.SemanticProfiles ?? Array.Empty<string>()).Contains(contract.Adapter.SemanticProfile, StringComparer.Ordinal))
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' targets an undeclared semantic profile.");
        if (!KnownStreams.Contains(contract.Stream, StringComparer.Ordinal))
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' declares unsupported stream '{contract.Stream}'.");
        if (!Enum.IsDefined(typeof(PowerShellCompilationCommandFamily), contract.Family) ||
            !Enum.IsDefined(typeof(PowerShellCompilationCommandOutput), contract.Output) ||
            !Enum.IsDefined(typeof(PowerShellCompilationCommandCardinality), contract.Cardinality) ||
            !Enum.IsDefined(typeof(PowerShellCompilationCommandErrors), contract.Errors) ||
            !Enum.IsDefined(typeof(PowerShellCompilationProviderCancellation), contract.Adapter.Cancellation) ||
            !Enum.IsDefined(typeof(PowerShellCompilationProviderCleanup), contract.Adapter.Cleanup))
            throw new InvalidOperationException($"Provider '{contract.ProviderId}' declares an unknown output, cardinality, error, cancellation, or cleanup contract.");
        if (contract.Stream.Equals("Success", StringComparison.Ordinal))
        {
            if (contract.Output == PowerShellCompilationCommandOutput.None ||
                contract.Cardinality == PowerShellCompilationCommandCardinality.None)
                throw new InvalidOperationException($"Success-stream provider '{contract.ProviderId}' must declare output and cardinality.");
        }
        else if (contract.Output != PowerShellCompilationCommandOutput.None ||
                 contract.Cardinality != PowerShellCompilationCommandCardinality.None)
        {
            throw new InvalidOperationException($"Non-success provider '{contract.ProviderId}' cannot declare success output or cardinality.");
        }
        if (!contract.Stream.Equals("Success", StringComparison.Ordinal) &&
            contract.Adapter.EntryPoint?.ResultType != PowerShellCompilationProviderValueType.String)
            throw new InvalidOperationException($"Non-success provider '{contract.ProviderId}' must return a string for its stream sink.");
        if (contract.Adapter.RuntimeFree && contract.Errors == PowerShellCompilationCommandErrors.PowerShellHost)
            throw new InvalidOperationException($"Runtime-free provider '{contract.ProviderId}' cannot delegate errors to a PowerShell host.");
        if (contract.Adapter.RuntimeFree && (contract.Adapter.Dependencies ?? Array.Empty<string>()).Any(static dependency =>
                dependency.Equals("System.Management.Automation", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Runtime-free provider '{contract.ProviderId}' cannot depend on System.Management.Automation.");
        if (contract.Adapter.AotCompatible && !contract.Adapter.RuntimeFree)
            throw new InvalidOperationException($"Hosted provider '{contract.ProviderId}' cannot claim runtime-free AOT compatibility.");
        if (!contract.Adapter.RuntimeFree &&
            (contract.Adapter.Cancellation is PowerShellCompilationProviderCancellation.Cooperative or
                 PowerShellCompilationProviderCancellation.PostInitializationCooperative ||
             contract.Adapter.Cleanup == PowerShellCompilationProviderCleanup.Deterministic))
            throw new InvalidOperationException($"Hosted provider '{contract.ProviderId}' cannot claim runtime-free cancellation or cleanup ownership.");
        var declaredDependencies = (manifest.Assemblies ?? Array.Empty<PowerShellCompilationProviderAssembly>())
            .Select(static assembly => assembly.AssemblyName)
            .Concat((manifest.Dependencies ?? Array.Empty<PowerShellCompilationProviderDependency>())
                .Select(static dependency => dependency.PackageId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingDependency = (contract.Adapter.Dependencies ?? Array.Empty<string>())
            .FirstOrDefault(dependency => !declaredDependencies.Contains(dependency));
        if (missingDependency is not null)
            throw new InvalidOperationException(
                $"Provider '{contract.ProviderId}' adapter dependency '{missingDependency}' is absent from the package's declared assembly/package closure.");

        var parameters = contract.Parameters ?? Array.Empty<PowerShellCompilationCommandParameterContract>();
        var names = parameters.SelectMany(static parameter =>
            new[] { parameter.Name }.Concat(parameter.Aliases ?? Array.Empty<string>()));
        if (parameters.Any(static parameter => parameter.Position < -1) ||
            names.Any(string.IsNullOrWhiteSpace) ||
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
}
