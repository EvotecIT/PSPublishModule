namespace PowerForge;

internal enum PowerShellCommandResolutionStatus
{
    Resolved,
    Missing,
    Ambiguous
}

internal sealed class PowerShellCommandSemanticResolution
{
    internal PowerShellCommandSemanticResolution(
        PowerShellCommandResolutionStatus status,
        string requestedName,
        PowerShellCompilationCommandProviderContract? contract,
        IReadOnlyList<PowerShellCompilationCommandProviderContract>? candidates = null)
    {
        Status = status;
        RequestedName = requestedName;
        Contract = contract;
        Candidates = candidates ?? Array.Empty<PowerShellCompilationCommandProviderContract>();
    }

    internal PowerShellCommandResolutionStatus Status { get; }
    internal string RequestedName { get; }
    internal PowerShellCompilationCommandProviderContract? Contract { get; }
    internal IReadOnlyList<PowerShellCompilationCommandProviderContract> Candidates { get; }
}

/// <summary>Canonical, deterministic registry for compile-time-only command semantics.</summary>
internal sealed class PowerShellCommandSemanticRegistry
{
    private readonly IReadOnlyDictionary<string, PowerShellCompilationCommandProviderContract> _qualified;
    private readonly IReadOnlyDictionary<string, PowerShellCompilationCommandProviderContract[]> _unqualified;

    internal PowerShellCommandSemanticRegistry(IEnumerable<PowerShellCompilationCommandProviderContract> contracts)
    {
        if (contracts is null) throw new ArgumentNullException(nameof(contracts));
        Contracts = contracts.Select(Snapshot)
            .OrderBy(static contract => contract.ProviderId, StringComparer.Ordinal)
            .ToArray();
        ValidateContracts(Contracts);

        var qualified = new Dictionary<string, PowerShellCompilationCommandProviderContract>(StringComparer.OrdinalIgnoreCase);
        var unqualified = new Dictionary<string, List<PowerShellCompilationCommandProviderContract>>(StringComparer.OrdinalIgnoreCase);
        foreach (var contract in Contracts)
        {
            var names = new[] { contract.CommandName }.Concat(contract.Aliases)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var name in names)
            {
                AddUnqualified(unqualified, name, contract);
                foreach (var module in contract.ModuleNames)
                    AddQualified(qualified, module + "\\" + name, contract);
            }
        }
        _qualified = qualified;
        _unqualified = unqualified.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.GroupBy(static contract => contract.ProviderId, StringComparer.Ordinal)
                .Select(static group => group.First())
                .OrderBy(static contract => contract.ProviderId, StringComparer.Ordinal).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    internal static PowerShellCommandSemanticRegistry Default { get; } = new(CreateBuiltIns());

    internal static PowerShellCommandSemanticRegistry Create(IEnumerable<PowerShellCompilationCommandProviderContract>? extensions)
        => new(CreateBuiltIns().Concat(extensions ?? Array.Empty<PowerShellCompilationCommandProviderContract>()));

    internal IReadOnlyList<PowerShellCompilationCommandProviderContract> Contracts { get; }

    internal PowerShellCommandSemanticResolution Resolve(string? commandName)
    {
        var requested = commandName?.Trim() ?? string.Empty;
        if (requested.Length == 0)
            return new PowerShellCommandSemanticResolution(PowerShellCommandResolutionStatus.Missing, requested, null);
        var separator = requested.LastIndexOf('\\');
        if (separator >= 0)
        {
            var module = requested.Substring(0, separator);
            var leaf = requested.Substring(separator + 1);
            if (module.Length == 0 || leaf.Length == 0)
                return new PowerShellCommandSemanticResolution(PowerShellCommandResolutionStatus.Missing, requested, null);
            return _qualified.TryGetValue(module + "\\" + leaf, out var qualified)
                ? new PowerShellCommandSemanticResolution(PowerShellCommandResolutionStatus.Resolved, requested, qualified)
                : new PowerShellCommandSemanticResolution(PowerShellCommandResolutionStatus.Missing, requested, null);
        }

        if (!_unqualified.TryGetValue(requested, out var candidates))
            return new PowerShellCommandSemanticResolution(PowerShellCommandResolutionStatus.Missing, requested, null);
        return candidates.Length == 1
            ? new PowerShellCommandSemanticResolution(PowerShellCommandResolutionStatus.Resolved, requested, candidates[0])
            : new PowerShellCommandSemanticResolution(PowerShellCommandResolutionStatus.Ambiguous, requested, null, candidates);
    }

    internal static PowerShellCompilationCommandProviderContract HostedRegionContract(string commandName)
        => Contract(
            "powerforge.command.hosted-region",
            PowerShellCompilationCommandFamily.HostedRegion,
            commandName,
            Array.Empty<string>(),
            PowerShellCompilationCommandOutput.Unknown,
            PowerShellCompilationCommandCardinality.Unknown,
            "Success+PowerShell",
            PowerShellCompilationCommandErrors.PowerShellHost,
            runtimeFree: false);

    private static IEnumerable<PowerShellCompilationCommandProviderContract> CreateBuiltIns()
    {
        yield return Stream("output", "Write-Output", "Success", PowerShellCompilationCommandOutput.Enumerated, PowerShellCompilationCommandCardinality.Collection, PowerShellCompilationCommandErrors.None);
        yield return Stream("verbose", "Write-Verbose", "Verbose");
        yield return Stream("debug", "Write-Debug", "Debug");
        yield return Stream("warning", "Write-Warning", "Warning");
        yield return Stream("information", "Write-Information", "Information");
        yield return Stream("host", "Write-Host", "Information");
        yield return Stream("error", "Write-Error", "Error", PowerShellCompilationCommandOutput.None, PowerShellCompilationCommandCardinality.None, PowerShellCompilationCommandErrors.NonTerminating);
        yield return Contract(
            "powerforge.command.projection.select-object",
            PowerShellCompilationCommandFamily.Projection,
            "Select-Object",
            new[] { "select" },
            PowerShellCompilationCommandOutput.Projected,
            PowerShellCompilationCommandCardinality.Collection,
            "Success",
            PowerShellCompilationCommandErrors.PowerShellHost,
            runtimeFree: false);
        yield return Contract(
            "powerforge.command.filtering.where-object",
            PowerShellCompilationCommandFamily.Filtering,
            "Where-Object",
            new[] { "where", "?" },
            PowerShellCompilationCommandOutput.Filtered,
            PowerShellCompilationCommandCardinality.Collection,
            "Success",
            PowerShellCompilationCommandErrors.PowerShellHost,
            runtimeFree: false);
        yield return Contract(
            "powerforge.command.mapping.foreach-object",
            PowerShellCompilationCommandFamily.Mapping,
            "ForEach-Object",
            new[] { "foreach", "%" },
            PowerShellCompilationCommandOutput.Enumerated,
            PowerShellCompilationCommandCardinality.Collection,
            "Success",
            PowerShellCompilationCommandErrors.PowerShellHost,
            runtimeFree: false);
        yield return Contract(
            "powerforge.command.sorting.sort-object",
            PowerShellCompilationCommandFamily.Sorting,
            "Sort-Object",
            new[] { "sort" },
            PowerShellCompilationCommandOutput.Sorted,
            PowerShellCompilationCommandCardinality.Collection,
            "Success",
            PowerShellCompilationCommandErrors.PowerShellHost,
            runtimeFree: false);
    }

    private static PowerShellCompilationCommandProviderContract Stream(
        string id,
        string commandName,
        string stream,
        PowerShellCompilationCommandOutput output = PowerShellCompilationCommandOutput.None,
        PowerShellCompilationCommandCardinality cardinality = PowerShellCompilationCommandCardinality.None,
        PowerShellCompilationCommandErrors errors = PowerShellCompilationCommandErrors.Terminating)
        => new()
        {
            ProviderId = "powerforge.command.stream." + id,
            ProviderVersion = "1.0",
            FeatureId = PowerShellCompilationFeatureIds.ForCommand(commandName),
            Family = PowerShellCompilationCommandFamily.Stream,
            CommandName = commandName,
            ModuleNames = new[] { "Microsoft.PowerShell.Utility" },
            Output = output,
            Cardinality = cardinality,
            Stream = stream,
            Errors = errors,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                Operation = stream.Equals("Success", StringComparison.Ordinal) ? "WriteOutput" : "Write" + stream,
                SemanticProfile = PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" + PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion,
                RuntimeFree = true,
                AotCompatible = true
            }
        };

    private static PowerShellCompilationCommandProviderContract Contract(
        string providerId,
        PowerShellCompilationCommandFamily family,
        string commandName,
        string[] aliases,
        PowerShellCompilationCommandOutput output,
        PowerShellCompilationCommandCardinality cardinality,
        string stream,
        PowerShellCompilationCommandErrors errors,
        bool runtimeFree)
        => new()
        {
            ProviderId = providerId,
            ProviderVersion = "1.0",
            FeatureId = PowerShellCompilationFeatureIds.ForCommand(commandName),
            Family = family,
            CommandName = commandName,
            ModuleNames = new[] { "Microsoft.PowerShell.Utility" },
            Aliases = aliases,
            Output = output,
            Cardinality = cardinality,
            Stream = stream,
            Errors = errors,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                SemanticProfile = runtimeFree
                    ? PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" + PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion
                    : "PowerShell.Hosted/1.0",
                RuntimeFree = runtimeFree,
                AotCompatible = runtimeFree,
                Dependencies = runtimeFree ? Array.Empty<string>() : new[] { "System.Management.Automation" }
            }
        };

    private static void ValidateContracts(IReadOnlyList<PowerShellCompilationCommandProviderContract> contracts)
    {
        var duplicateProvider = contracts.GroupBy(static contract => contract.ProviderId, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateProvider is not null)
            throw new InvalidOperationException($"Command semantic provider '{duplicateProvider.Key}' is registered more than once.");
        foreach (var contract in contracts)
        {
            if (contract.SchemaVersion != 1 || string.IsNullOrWhiteSpace(contract.ProviderId) ||
                string.IsNullOrWhiteSpace(contract.ProviderVersion) || string.IsNullOrWhiteSpace(contract.CommandName))
                throw new InvalidOperationException("Command semantic providers require schema 1 plus non-empty provider, version, and command identities.");
            if (!contract.CompileTimeOnly || contract.MayImportSourceModules || contract.MayExecuteSource)
                throw new InvalidOperationException($"Command semantic provider '{contract.ProviderId}' violates the compile-time-only execution boundary.");
            if (contract.Adapter.RuntimeFree)
            {
                if (contract.Family != PowerShellCompilationCommandFamily.Stream ||
                    contract.Stream is not ("Success" or "Verbose" or "Debug" or "Warning" or "Information" or "Error"))
                    throw new InvalidOperationException($"Runtime-free provider '{contract.ProviderId}' must use one supported stream adapter contract.");
                var expectedProfile = PowerShellCompilationSemanticProfile.RuntimeFreeStrictName + "/" + PowerShellCompilationSemanticProfile.RuntimeFreeStrictVersion;
                if (!contract.Adapter.SemanticProfile.Equals(expectedProfile, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Runtime-free provider '{contract.ProviderId}' targets semantic profile '{contract.Adapter.SemanticProfile}' instead of '{expectedProfile}'.");
                var expectedOperation = contract.Stream.Equals("Success", StringComparison.Ordinal)
                    ? "WriteOutput"
                    : "Write" + contract.Stream;
                if (!contract.Adapter.Operation.Equals(expectedOperation, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Runtime-free provider '{contract.ProviderId}' operation '{contract.Adapter.Operation}' does not match stream '{contract.Stream}' operation '{expectedOperation}'.");
                if (contract.Adapter.Dependencies.Length > 0)
                    throw new InvalidOperationException($"Runtime-free provider '{contract.ProviderId}' declares adapter dependencies that cannot yet be locked and certified. Dependency-bearing providers require the Milestone 16 provider package contract.");
            }
            else if (contract.Adapter.Dependencies.Any(static dependency =>
                         !dependency.Equals("System.Management.Automation", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Hosted provider '{contract.ProviderId}' declares an adapter dependency outside the current PowerShell host contract.");
            }
            var duplicateName = new[] { contract.CommandName }.Concat(contract.Aliases)
                .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(static group => group.Count() > 1);
            if (duplicateName is not null)
                throw new InvalidOperationException($"Command semantic provider '{contract.ProviderId}' registers duplicate command or alias '{duplicateName.Key}'.");
        }
    }

    private static void AddQualified(
        IDictionary<string, PowerShellCompilationCommandProviderContract> lookup,
        string key,
        PowerShellCompilationCommandProviderContract contract)
    {
        if (lookup.TryGetValue(key, out var existing) && !existing.ProviderId.Equals(contract.ProviderId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Qualified command semantic registration '{key}' is owned by both '{existing.ProviderId}' and '{contract.ProviderId}'.");
        lookup[key] = contract;
    }

    private static void AddUnqualified(
        IDictionary<string, List<PowerShellCompilationCommandProviderContract>> lookup,
        string key,
        PowerShellCompilationCommandProviderContract contract)
    {
        if (!lookup.TryGetValue(key, out var providers)) lookup[key] = providers = new List<PowerShellCompilationCommandProviderContract>();
        providers.Add(contract);
    }

    private static PowerShellCompilationCommandProviderContract Snapshot(PowerShellCompilationCommandProviderContract source)
        => new()
        {
            SchemaVersion = source.SchemaVersion,
            ProviderId = source.ProviderId ?? string.Empty,
            ProviderVersion = source.ProviderVersion ?? string.Empty,
            FeatureId = source.FeatureId ?? string.Empty,
            Family = source.Family,
            CommandName = source.CommandName ?? string.Empty,
            ModuleNames = (source.ModuleNames ?? Array.Empty<string>()).ToArray(),
            Aliases = (source.Aliases ?? Array.Empty<string>()).ToArray(),
            Output = source.Output,
            Cardinality = source.Cardinality,
            Stream = source.Stream ?? string.Empty,
            Errors = source.Errors,
            CompileTimeOnly = source.CompileTimeOnly,
            MayImportSourceModules = source.MayImportSourceModules,
            MayExecuteSource = source.MayExecuteSource,
            Adapter = new PowerShellCompilationCommandAdapterContract
            {
                Operation = source.Adapter?.Operation ?? string.Empty,
                SemanticProfile = source.Adapter?.SemanticProfile ?? string.Empty,
                RuntimeFree = source.Adapter?.RuntimeFree == true,
                AotCompatible = source.Adapter?.AotCompatible == true,
                Dependencies = source.Adapter?.Dependencies?.ToArray() ?? Array.Empty<string>()
            }
        };
}
