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
            .Where(static action => action.Kind is ModuleStatePlanActionKind.Install or ModuleStatePlanActionKind.Update or ModuleStatePlanActionKind.Save)
            .ToArray();
        if (actions.Length == 0)
            return Array.Empty<ModuleStateDeliveryExecutionResult>();

        var logger = new CmdletLogger(_cmdlet, _cmdlet.MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var installService = new ManagedModuleInstallService(logger);
        var updateService = new ManagedModuleUpdateService(logger);
        var results = new List<ModuleStateDeliveryExecutionResult>(actions.Length);

        foreach (var action in actions)
        {
            var repository = ResolveRepository(action, options);
            results.Add(action.Kind switch
            {
                ModuleStatePlanActionKind.Update => ExecuteUpdate(updateService, repository, action, options),
                ModuleStatePlanActionKind.Save => ExecuteSave(installService, repository, action, options),
                _ => ExecuteInstall(installService, repository, action, options)
            });
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
            RequestedTransport = ModuleStateDeliveryTransport.ManagedModule,
            EffectiveTransport = ModuleStateDeliveryTransport.ManagedModule,
            DeliveryTransportReason = "Managed module delivery was requested explicitly for ModuleState execution.",
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
            RequestedTransport = ModuleStateDeliveryTransport.ManagedModule,
            EffectiveTransport = ModuleStateDeliveryTransport.ManagedModule,
            DeliveryTransportReason = "Managed module delivery was requested explicitly for ModuleState execution.",
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

    private ModuleStateDeliveryExecutionResult ExecuteSave(
        ManagedModuleInstallService service,
        ManagedModuleRepository repository,
        ModuleStatePlanAction action,
        ModuleStateManagedDeliveryOptions options)
    {
        var request = CreateSaveRequest(repository, action, options);
        if (!_cmdlet.ShouldProcess(action.ModuleName, $"Save managed module from repository '{repository.Name}'"))
            return CreateSkippedResult("Save", repository.Name, action);

        var result = service.InstallAsync(request).GetAwaiter().GetResult();
        return new ModuleStateDeliveryExecutionResult
        {
            Operation = "Save",
            OperationPerformed = result.Status == ManagedModuleInstallStatus.Installed,
            RepositoryName = repository.Name,
            RequestedTransport = ModuleStateDeliveryTransport.ManagedModule,
            EffectiveTransport = ModuleStateDeliveryTransport.ManagedModule,
            DeliveryTransportReason = "Managed module delivery was requested explicitly for ModuleState execution.",
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
            ExpectedPackageSha256 = action.ExpectedPackageSha256,
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
            ExpectedPackageSha256 = action.ExpectedPackageSha256,
            Credential = options.Credential,
            Force = options.Force || action.Force,
            AllowClobber = options.AllowClobber,
            AcceptLicense = options.AcceptLicense,
            LoadedModules = options.LoadedModules,
            SourcePolicy = action.IsRepair ? new ManagedModuleSourcePolicy() : null
        };
    }

    private ManagedModuleInstallRequest CreateSaveRequest(
        ManagedModuleRepository repository,
        ModuleStatePlanAction action,
        ModuleStateManagedDeliveryOptions options)
    {
        var versionPolicy = ResolveVersionPolicy(action.VersionPolicy);
        var moduleRoot = ResolveModuleRoot(action, options)
            ?? throw new InvalidOperationException("Managed module save delivery requires an action target path.");
        return new ManagedModuleInstallRequest
        {
            Repository = repository,
            Name = action.ModuleName,
            Version = versionPolicy.ExactVersion,
            VersionPolicy = versionPolicy.RangePolicy,
            IncludePrerelease = options.Prerelease,
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot,
            ExpectedPackageSha256 = action.ExpectedPackageSha256,
            Credential = options.Credential,
            Force = options.Force || action.Force,
            AllowClobber = options.AllowClobber,
            AcceptLicense = options.AcceptLicense
        };
    }

    private ManagedModuleRepository ResolveRepository(
        ModuleStatePlanAction action,
        ModuleStateManagedDeliveryOptions options)
    {
        if (!string.IsNullOrWhiteSpace(action.TargetRepository))
            return ManagedModuleCommandSupport.CreateRepository(
                _cmdlet,
                ManagedModuleCommandSupport.DefaultRepositoryName,
                action.TargetRepository!);

        if (!string.IsNullOrWhiteSpace(options.ProfileName))
            return ManagedModuleCommandSupport.CreateRepository(
                _cmdlet,
                ManagedModuleCommandSupport.DefaultRepositoryName,
                ManagedModuleCommandSupport.DefaultRepositorySource,
                options.ProfileName,
                repositoryWasBound: false);

        if (!string.IsNullOrWhiteSpace(options.Repository))
            return ManagedModuleCommandSupport.CreateRepository(
                _cmdlet,
                ManagedModuleCommandSupport.DefaultRepositoryName,
                options.Repository!);

        throw new InvalidOperationException("Managed module delivery requires Repository, ProfileName, or action target repository.");
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
            RequestedTransport = ModuleStateDeliveryTransport.ManagedModule,
            EffectiveTransport = ModuleStateDeliveryTransport.ManagedModule,
            DeliveryTransportReason = "Managed module delivery was requested explicitly for ModuleState execution.",
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

    internal IReadOnlyList<ManagedModuleLoadedModule> LoadedModules { get; set; } = Array.Empty<ManagedModuleLoadedModule>();
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
