using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal sealed class ModuleStatePrivateDeliveryService
{
    private readonly PSCmdlet _cmdlet;

    internal ModuleStatePrivateDeliveryService(PSCmdlet cmdlet)
        => _cmdlet = cmdlet ?? throw new ArgumentNullException(nameof(cmdlet));

    internal ModuleStateDeliveryExecutionResult[] Execute(
        PowerForge.ModuleStateApplyResult applyResult,
        ModuleStatePrivateDeliveryOptions options)
    {
        if (applyResult is null)
            throw new ArgumentNullException(nameof(applyResult));
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var actionable = applyResult.Plan.Actions
            .Where(static action => action.Kind is ModuleStatePlanActionKind.Install or ModuleStatePlanActionKind.Update)
            .GroupBy(action => new DeliveryGroupKey(action.Kind, ResolveActionRepository(action, options)), DeliveryGroupKeyComparer.Instance)
            .OrderBy(static group => group.Key.Kind == ModuleStatePlanActionKind.Update ? 0 : 1)
            .ToArray();

        if (actionable.Length == 0)
            return Array.Empty<ModuleStateDeliveryExecutionResult>();

        var host = new CmdletPrivateGalleryHost(_cmdlet);
        var logger = new CmdletLogger(_cmdlet, _cmdlet.MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var service = new PrivateModuleWorkflowService(host, new PrivateGalleryService(host), logger);
        var results = new List<ModuleStateDeliveryExecutionResult>();
        foreach (var group in actionable)
        {
            var groupActions = group.ToArray();
            var request = CreateRequest(group.Key.Kind, group.Key.Repository, groupActions, options);
            var workflowResult = service.Execute(request, (target, action) => _cmdlet.ShouldProcess(target, action));
            results.Add(new ModuleStateDeliveryExecutionResult
            {
                Operation = group.Key.Kind.ToString(),
                OperationPerformed = workflowResult.OperationPerformed,
                RepositoryName = workflowResult.RepositoryName,
                DependencyResults = workflowResult.DependencyResults.Select(static dependency => new ModuleStateDependencyResult
                {
                    Name = dependency.Name,
                    InstalledVersion = dependency.InstalledVersion,
                    ResolvedVersion = dependency.ResolvedVersion,
                    RequestedVersion = dependency.RequestedVersion,
                    Status = dependency.Status.ToString(),
                    Installer = dependency.Installer,
                    Message = dependency.Message
                }).ToArray()
            });
        }

        return results.ToArray();
    }

    private static PrivateModuleWorkflowRequest CreateRequest(
        ModuleStatePlanActionKind actionKind,
        string? repository,
        IReadOnlyList<ModuleStatePlanAction> actions,
        ModuleStatePrivateDeliveryOptions options)
    {
        var request = new PrivateModuleWorkflowRequest
        {
            Operation = actionKind == ModuleStatePlanActionKind.Update
                ? PrivateModuleWorkflowOperation.Update
                : PrivateModuleWorkflowOperation.Install,
            ModuleNames = actions.Select(static action => action.ModuleName).ToArray(),
            RequiredVersions = actions
                .Select(static action => new { action.ModuleName, RequiredVersion = GetExactVersionPolicyValue(action.VersionPolicy) })
                .Where(static item => !string.IsNullOrWhiteSpace(item.RequiredVersion))
                .GroupBy(static item => item.ModuleName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First().RequiredVersion!, StringComparer.OrdinalIgnoreCase),
            InstallScopes = actions
                .Where(static action => !string.IsNullOrWhiteSpace(action.TargetScope))
                .GroupBy(static action => action.ModuleName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First().TargetScope!, StringComparer.OrdinalIgnoreCase),
            RepositoryName = repository ?? string.Empty,
            InstallPrerequisites = options.InstallPrerequisites,
            Prerelease = options.Prerelease,
            Force = options.Force && actionKind == ModuleStatePlanActionKind.Install,
            CredentialUserName = options.CredentialUserName,
            CredentialSecret = options.CredentialSecret,
            CredentialSecretFilePath = options.CredentialSecretFilePath,
            PromptForCredential = options.PromptForCredential
        };

        if (!string.IsNullOrWhiteSpace(options.ProfileName))
        {
            var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(options.ProfileName!);
            request.UseAzureArtifacts = true;
            request.ProfileName = options.ProfileName;
            request.Provider = profile.Provider;
            request.AzureDevOpsOrganization = profile.AzureDevOpsOrganization;
            request.AzureDevOpsProject = profile.AzureDevOpsProject;
            request.AzureArtifactsFeed = profile.AzureArtifactsFeed;
            request.RepositoryName = profile.RepositoryName;
            request.Repository = profile.Repository;
            request.RepositoryUri = profile.RepositoryUri;
            request.RepositorySourceUri = profile.RepositorySourceUri;
            request.RepositoryPublishUri = profile.RepositoryPublishUri;
            request.JFrogBaseUri = profile.JFrogBaseUri;
            request.JFrogRepository = profile.JFrogRepository;
            request.Tool = profile.Tool;
            request.BootstrapMode = profile.BootstrapMode;
            request.Trusted = profile.Trusted;
            request.Priority = profile.Priority;
        }

        return request;
    }

    private static string? ResolveActionRepository(ModuleStatePlanAction action, ModuleStatePrivateDeliveryOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ProfileName))
            return options.Repository;
        if (!string.IsNullOrWhiteSpace(options.Repository))
            return options.Repository;

        return action.TargetRepository;
    }

    private static string? GetExactVersionPolicyValue(string? versionPolicy)
    {
        if (string.IsNullOrWhiteSpace(versionPolicy))
            return null;

        var trimmed = versionPolicy!.Trim();
        if (trimmed.StartsWith("=", StringComparison.Ordinal))
            return trimmed.Substring(1).Trim();

        return null;
    }
}

internal readonly struct DeliveryGroupKey
{
    internal DeliveryGroupKey(ModuleStatePlanActionKind kind, string? repository)
    {
        Kind = kind;
        Repository = string.IsNullOrWhiteSpace(repository) ? null : repository!.Trim();
    }

    internal ModuleStatePlanActionKind Kind { get; }

    internal string? Repository { get; }
}

internal sealed class DeliveryGroupKeyComparer : IEqualityComparer<DeliveryGroupKey>
{
    internal static readonly DeliveryGroupKeyComparer Instance = new();

    public bool Equals(DeliveryGroupKey x, DeliveryGroupKey y)
        => x.Kind == y.Kind &&
           string.Equals(x.Repository, y.Repository, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(DeliveryGroupKey obj)
    {
        unchecked
        {
            return ((int)obj.Kind * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Repository ?? string.Empty);
        }
    }
}

internal sealed class ModuleStatePrivateDeliveryOptions
{
    internal string? ProfileName { get; set; }

    internal string? Repository { get; set; }

    internal bool InstallPrerequisites { get; set; }

    internal bool Prerelease { get; set; }

    internal bool Force { get; set; }

    internal string? CredentialUserName { get; set; }

    internal string? CredentialSecret { get; set; }

    internal string? CredentialSecretFilePath { get; set; }

    internal bool PromptForCredential { get; set; }
}
