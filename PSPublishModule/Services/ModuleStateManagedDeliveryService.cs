using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal sealed class ModuleStateManagedDeliveryService
{
    private readonly PSCmdlet _cmdlet;

    internal ModuleStateManagedDeliveryService(PSCmdlet cmdlet)
        => _cmdlet = cmdlet ?? throw new ArgumentNullException(nameof(cmdlet));

    internal ModuleStateDeliveryExecutionResult[] Execute(
        PowerForge.ModuleStateApplyResult applyResult,
        ModuleStateManagedDeliveryOptions options)
    {
        if (applyResult is null)
            throw new ArgumentNullException(nameof(applyResult));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var actions = applyResult.Plan.Actions
            .Where(static action => action.Kind is ModuleStatePlanActionKind.Install or ModuleStatePlanActionKind.Update)
            .ToArray();
        if (actions.Length == 0)
            return Array.Empty<ModuleStateDeliveryExecutionResult>();

        var repository = ResolveRepository(actions, options);
        var logger = new CmdletLogger(_cmdlet, _cmdlet.MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var installService = new ManagedModuleInstallService(logger);
        var updateService = new ManagedModuleUpdateService(logger);
        var results = new List<ModuleStateDeliveryExecutionResult>(actions.Length);

        foreach (var action in actions)
        {
            results.Add(action.Kind == ModuleStatePlanActionKind.Update
                ? ExecuteUpdate(updateService, repository, action, options)
                : ExecuteInstall(installService, repository, action, options));
        }

        return results.ToArray();
    }

    private ModuleStateDeliveryExecutionResult ExecuteInstall(
        ManagedModuleInstallService service,
        ManagedModuleRepository repository,
        ModuleStatePlanAction action,
        ModuleStateManagedDeliveryOptions options)
    {
        var request = CreateInstallRequest(repository, action, options);
        if (!_cmdlet.ShouldProcess(action.ModuleName, $"Install managed module from repository '{repository.Name}'"))
            return CreateSkippedResult("Install", repository.Name, action);

        var result = service.InstallAsync(request).GetAwaiter().GetResult();
        return new ModuleStateDeliveryExecutionResult
        {
            Operation = "Install",
            OperationPerformed = result.Status == ManagedModuleInstallStatus.Installed,
            RepositoryName = repository.Name,
            DependencyResults = new[]
            {
                new ModuleStateDependencyResult
                {
                    Name = result.Name,
                    InstalledVersion = action.InstalledVersion,
                    ResolvedVersion = result.Version,
                    RequestedVersion = action.VersionPolicy,
                    Status = result.Status.ToString(),
                    Installer = "ManagedModule",
                    Message = result.ModulePath
                }
            }
        };
    }

    private ModuleStateDeliveryExecutionResult ExecuteUpdate(
        ManagedModuleUpdateService service,
        ManagedModuleRepository repository,
        ModuleStatePlanAction action,
        ModuleStateManagedDeliveryOptions options)
    {
        var request = CreateUpdateRequest(repository, action, options);
        if (!_cmdlet.ShouldProcess(action.ModuleName, $"Update managed module from repository '{repository.Name}'"))
            return CreateSkippedResult("Update", repository.Name, action);

        var result = service.UpdateAsync(request).GetAwaiter().GetResult();
        return new ModuleStateDeliveryExecutionResult
        {
            Operation = "Update",
            OperationPerformed = result.Status != ManagedModuleUpdateStatus.UpToDate,
            RepositoryName = repository.Name,
            DependencyResults = new[]
            {
                new ModuleStateDependencyResult
                {
                    Name = result.Name,
                    InstalledVersion = result.PreviousVersion,
                    ResolvedVersion = result.TargetVersion,
                    RequestedVersion = action.VersionPolicy,
                    Status = result.Status.ToString(),
                    Installer = "ManagedModule",
                    Message = result.ModulePath
                }
            }
        };
    }

    private ManagedModuleInstallRequest CreateInstallRequest(
        ManagedModuleRepository repository,
        ModuleStatePlanAction action,
        ModuleStateManagedDeliveryOptions options)
    {
        var versionPolicy = ResolveVersionPolicy(action.VersionPolicy);
        return new ManagedModuleInstallRequest
        {
            Repository = repository,
            Name = action.ModuleName,
            Version = versionPolicy.ExactVersion,
            VersionPolicy = versionPolicy.RangePolicy,
            IncludePrerelease = options.Prerelease,
            Scope = ResolveScope(action.TargetScope, action.TargetPath, options.ModuleRoot),
            ModuleRoot = ResolveModuleRoot(action, options),
            Credential = options.Credential,
            Force = options.Force || action.Force,
            AllowClobber = options.AllowClobber,
            AcceptLicense = options.AcceptLicense
        };
    }

    private ManagedModuleUpdateRequest CreateUpdateRequest(
        ManagedModuleRepository repository,
        ModuleStatePlanAction action,
        ModuleStateManagedDeliveryOptions options)
    {
        var versionPolicy = ResolveVersionPolicy(action.VersionPolicy);
        return new ManagedModuleUpdateRequest
        {
            Repository = repository,
            Name = action.ModuleName,
            Version = versionPolicy.ExactVersion,
            VersionPolicy = versionPolicy.RangePolicy,
            IncludePrerelease = options.Prerelease,
            Scope = ResolveScope(action.TargetScope, action.TargetPath, options.ModuleRoot),
            ModuleRoot = ResolveModuleRoot(action, options),
            Credential = options.Credential,
            Force = options.Force || action.Force,
            AllowClobber = options.AllowClobber,
            AcceptLicense = options.AcceptLicense,
            SourcePolicy = action.IsRepair ? new ManagedModuleSourcePolicy() : null
        };
    }

    private ManagedModuleRepository ResolveRepository(
        IReadOnlyList<ModuleStatePlanAction> actions,
        ModuleStateManagedDeliveryOptions options)
    {
        var source = ResolveRepositorySource(actions, options);
        var name = ResolveRepositoryName(actions, options, source);
        return new ManagedModuleRepository(name, source);
    }

    private string ResolveRepositorySource(
        IReadOnlyList<ModuleStatePlanAction> actions,
        ModuleStateManagedDeliveryOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Repository))
            return options.Repository!;

        if (!string.IsNullOrWhiteSpace(options.ProfileName))
        {
            var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(options.ProfileName!);
            return FirstNonEmpty(profile.RepositorySourceUri, profile.RepositoryUri, profile.Repository, profile.RepositoryName, options.ProfileName)
                ?? throw new InvalidOperationException($"Profile '{options.ProfileName}' does not define a repository source for managed module delivery.");
        }

        var actionRepository = actions
            .Select(static action => action.TargetRepository)
            .FirstOrDefault(static repository => !string.IsNullOrWhiteSpace(repository));
        if (!string.IsNullOrWhiteSpace(actionRepository))
            return actionRepository!;

        throw new InvalidOperationException("Managed module delivery requires Repository, ProfileName, or action target repository.");
    }

    private static string ResolveRepositoryName(
        IReadOnlyList<ModuleStatePlanAction> actions,
        ModuleStateManagedDeliveryOptions options,
        string source)
    {
        if (!string.IsNullOrWhiteSpace(options.ProfileName))
            return options.ProfileName!;

        var actionRepository = actions
            .Select(static action => action.TargetRepository)
            .FirstOrDefault(static repository => !string.IsNullOrWhiteSpace(repository));
        return actionRepository ?? source;
    }

    private static string? ResolveModuleRoot(ModuleStatePlanAction action, ModuleStateManagedDeliveryOptions options)
        => string.IsNullOrWhiteSpace(action.TargetPath) ? options.ModuleRoot : action.TargetPath;

    private static ManagedModuleInstallScope ResolveScope(string? scope, string? targetPath, string? moduleRoot)
        => !string.IsNullOrWhiteSpace(targetPath) || !string.IsNullOrWhiteSpace(moduleRoot)
            ? ManagedModuleInstallScope.Custom
            : string.Equals(scope, "AllUsers", StringComparison.OrdinalIgnoreCase)
            ? ManagedModuleInstallScope.AllUsers
            : string.Equals(scope, "Custom", StringComparison.OrdinalIgnoreCase)
                ? ManagedModuleInstallScope.Custom
                : ManagedModuleInstallScope.CurrentUser;

    private static ModuleStateManagedVersionPolicy ResolveVersionPolicy(string? versionPolicy)
    {
        if (string.IsNullOrWhiteSpace(versionPolicy) || string.Equals(versionPolicy!.Trim(), "*", StringComparison.Ordinal))
            return ModuleStateManagedVersionPolicy.Latest;

        var trimmed = versionPolicy!.Trim();
        if (trimmed.StartsWith("=", StringComparison.Ordinal))
            return new ModuleStateManagedVersionPolicy(trimmed.Substring(1).Trim(), null);
        if (ModuleStateVersion.TryParse(trimmed, out var exactVersion))
            return new ModuleStateManagedVersionPolicy(exactVersion.Normalized, null);

        return new ModuleStateManagedVersionPolicy(null, trimmed);
    }

    private static ModuleStateDeliveryExecutionResult CreateSkippedResult(
        string operation,
        string repositoryName,
        ModuleStatePlanAction action)
        => new()
        {
            Operation = operation,
            OperationPerformed = false,
            RepositoryName = repositoryName,
            DependencyResults = new[]
            {
                new ModuleStateDependencyResult
                {
                    Name = action.ModuleName,
                    InstalledVersion = action.InstalledVersion,
                    RequestedVersion = action.VersionPolicy,
                    Status = "Skipped",
                    Installer = "ManagedModule",
                    Message = "ShouldProcess declined the operation."
                }
            }
        };

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
}

internal sealed class ModuleStateManagedDeliveryOptions
{
    internal string? ProfileName { get; set; }

    internal string? Repository { get; set; }

    internal bool Prerelease { get; set; }

    internal bool Force { get; set; }

    internal bool AllowClobber { get; set; }

    internal bool AcceptLicense { get; set; }

    internal string? ModuleRoot { get; set; }

    internal RepositoryCredential? Credential { get; set; }
}

internal readonly struct ModuleStateManagedVersionPolicy
{
    internal static readonly ModuleStateManagedVersionPolicy Latest = new(null, null);

    internal ModuleStateManagedVersionPolicy(string? exactVersion, string? rangePolicy)
    {
        ExactVersion = string.IsNullOrWhiteSpace(exactVersion) ? null : exactVersion!.Trim();
        RangePolicy = string.IsNullOrWhiteSpace(rangePolicy) ? null : rangePolicy!.Trim();
    }

    internal string? ExactVersion { get; }

    internal string? RangePolicy { get; }
}
