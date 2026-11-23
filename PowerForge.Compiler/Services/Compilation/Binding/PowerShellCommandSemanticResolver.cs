using System.Management.Automation.Language;

namespace PowerForge;

internal enum PowerShellCommandSemanticOrigin
{
    Dynamic,
    Missing,
    Ambiguous,
    LocalFunction,
    ProviderQualified,
    ProviderUnqualified,
    PowerShellRuntime
}

internal sealed class PowerShellCommandInvocationResolution
{
    internal PowerShellCommandInvocationResolution(
        PowerShellCommandSemanticOrigin origin,
        string requestedName,
        PowerShellCompilationCommandProviderContract? contract = null,
        IReadOnlyList<PowerShellCompilationCommandProviderContract>? candidates = null)
    {
        Origin = origin;
        RequestedName = requestedName;
        Contract = contract;
        Candidates = candidates ?? Array.Empty<PowerShellCompilationCommandProviderContract>();
    }

    internal PowerShellCommandSemanticOrigin Origin { get; }
    internal string RequestedName { get; }
    internal PowerShellCompilationCommandProviderContract? Contract { get; }
    internal IReadOnlyList<PowerShellCompilationCommandProviderContract> Candidates { get; }
    internal bool IsProvider => Origin is PowerShellCommandSemanticOrigin.ProviderQualified or PowerShellCommandSemanticOrigin.ProviderUnqualified;
}

/// <summary>
/// Owns the compiler's single decision about what an authored command name means for one target host.
/// The registry catalogs providers; this resolver applies source precedence and target-host safety.
/// </summary>
internal sealed class PowerShellCommandSemanticResolver
{
    private readonly PowerShellCommandSemanticRegistry _registry;

    internal PowerShellCommandSemanticResolver(PowerShellCommandSemanticRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    internal PowerShellCommandInvocationResolution Resolve(
        CommandAst command,
        ISet<string>? localFunctionNames,
        PowerShellCompilationCapability capabilities)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        var requestedName = command.GetCommandName()?.Trim() ?? string.Empty;
        if (requestedName.Length == 0)
            return new PowerShellCommandInvocationResolution(PowerShellCommandSemanticOrigin.Dynamic, requestedName);

        var separator = requestedName.LastIndexOf('\\');
        if (separator < 0 &&
            command.InvocationOperator == TokenKind.Unknown &&
            localFunctionNames?.Contains(requestedName) == true)
            return new PowerShellCommandInvocationResolution(PowerShellCommandSemanticOrigin.LocalFunction, requestedName);

        var catalog = _registry.Resolve(requestedName);
        if (catalog.Status == PowerShellCommandResolutionStatus.Missing)
            return new PowerShellCommandInvocationResolution(PowerShellCommandSemanticOrigin.Missing, requestedName);
        if (catalog.Status == PowerShellCommandResolutionStatus.Ambiguous)
            return new PowerShellCommandInvocationResolution(
                PowerShellCommandSemanticOrigin.Ambiguous,
                requestedName,
                candidates: catalog.Candidates);

        var contract = catalog.Contract!;
        if (separator >= 0)
        {
            var moduleName = requestedName.Substring(0, separator);
            var leafName = requestedName.Substring(separator + 1);
            if (contract.ModuleNames.Any(module => module.Equals(moduleName, StringComparison.OrdinalIgnoreCase)) &&
                contract.CommandName.Equals(leafName, StringComparison.OrdinalIgnoreCase))
                return new PowerShellCommandInvocationResolution(
                    PowerShellCommandSemanticOrigin.ProviderQualified,
                    requestedName,
                    contract);

            return new PowerShellCommandInvocationResolution(
                PowerShellCommandSemanticOrigin.PowerShellRuntime,
                requestedName,
                contract);
        }

        // A generated PowerShell host participates in normal command precedence. Rewriting an
        // unqualified command directly to a provider would bypass local, imported-module,
        // alias, function, and session-state shadowing. Preserve that lookup at runtime unless
        // the source names the provider's canonical module and command explicitly.
        if (capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes))
            return new PowerShellCommandInvocationResolution(
                PowerShellCommandSemanticOrigin.PowerShellRuntime,
                requestedName,
                contract);

        return new PowerShellCommandInvocationResolution(
            PowerShellCommandSemanticOrigin.ProviderUnqualified,
            requestedName,
            contract);
    }

    internal bool IsRuntimeFreeCompilerIntrinsic(
        CommandAst command,
        ISet<string>? localFunctionNames,
        PowerShellCompilationCapability capabilities)
    {
        var resolution = Resolve(command, localFunctionNames, capabilities);
        if (!resolution.IsProvider || resolution.Contract is null)
            return false;
        return resolution.Contract.Family switch
        {
            PowerShellCompilationCommandFamily.RuntimeState =>
                PowerShellRuntimeStateCommandSemanticBinder.IsSupportedShape(command, resolution.Contract, capabilities),
            PowerShellCompilationCommandFamily.ClrConstruction =>
                PowerShellNewObjectSemanticBinder.IsSupportedShape(command),
            _ => false
        };
    }

    /// <summary>
    /// Resolves a compiler-synthesized command contract without applying authored PowerShell
    /// command precedence. Synthetic stream/error behavior still obtains its provider identity
    /// through this resolver so the registry never becomes a second semantic decision surface.
    /// </summary>
    internal PowerShellCompilationCommandProviderContract? ResolveSynthesizedProvider(
        string canonicalCommandName,
        PowerShellCompilationCommandFamily expectedFamily)
    {
        if (string.IsNullOrWhiteSpace(canonicalCommandName))
            throw new ArgumentException("A canonical synthesized command name is required.", nameof(canonicalCommandName));

        var catalog = _registry.Resolve(canonicalCommandName);
        return catalog.Status == PowerShellCommandResolutionStatus.Resolved &&
               catalog.Contract!.CommandName.Equals(canonicalCommandName, StringComparison.OrdinalIgnoreCase) &&
               catalog.Contract.Family == expectedFamily
            ? catalog.Contract
            : null;
    }
}
