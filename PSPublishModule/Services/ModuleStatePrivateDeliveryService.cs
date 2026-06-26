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
            .GroupBy(action => new DeliveryGroupKey(action.Kind, ResolveActionRepository(action, options), ResolveActionForce(action, options)), DeliveryGroupKeyComparer.Instance)
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
            var request = CreateRequest(group.Key.Kind, group.Key.Repository, group.Key.Force, groupActions, options);
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
        bool force,
        IReadOnlyList<ModuleStatePlanAction> actions,
        ModuleStatePrivateDeliveryOptions options)
    {
        var request = new PrivateModuleWorkflowRequest
        {
            Operation = actionKind == ModuleStatePlanActionKind.Update
                ? PrivateModuleWorkflowOperation.Update
                : PrivateModuleWorkflowOperation.Install,
            ModuleNames = actions.Select(static action => action.ModuleName).ToArray(),
            RequiredVersions = CreateVersionDictionary(actions, static constraint => constraint.RequiredVersion),
            MinimumVersions = CreateVersionDictionary(actions, static constraint => constraint.MinimumVersion),
            MinimumVersionInclusivity = CreateVersionInclusivityDictionary(actions, static constraint => constraint.MinimumVersion, static constraint => constraint.MinimumVersionInclusive),
            MaximumVersions = CreateVersionDictionary(actions, static constraint => constraint.MaximumVersion),
            MaximumVersionInclusivity = CreateVersionInclusivityDictionary(actions, static constraint => constraint.MaximumVersion, static constraint => constraint.MaximumVersionInclusive),
            InstallScopes = actions
                .Where(static action => !string.IsNullOrWhiteSpace(action.TargetScope))
                .GroupBy(static action => action.ModuleName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First().TargetScope!, StringComparer.OrdinalIgnoreCase),
            RepositoryName = repository ?? string.Empty,
            InstallPrerequisites = options.InstallPrerequisites,
            Prerelease = options.Prerelease,
            Force = force,
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

    private static bool ResolveActionForce(ModuleStatePlanAction action, ModuleStatePrivateDeliveryOptions options)
        => action.Force || (options.Force && action.Kind == ModuleStatePlanActionKind.Install);

    private static Dictionary<string, string> CreateVersionDictionary(
        IEnumerable<ModuleStatePlanAction> actions,
        Func<ModuleStateVersionConstraint, string?> selector)
        => actions
            .Select(action => new { action.ModuleName, Version = selector(ParseVersionConstraint(action.ModuleName, action.VersionPolicy)) })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Version))
            .GroupBy(static item => item.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Version!, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, bool> CreateVersionInclusivityDictionary(
        IEnumerable<ModuleStatePlanAction> actions,
        Func<ModuleStateVersionConstraint, string?> boundarySelector,
        Func<ModuleStateVersionConstraint, bool> inclusivitySelector)
        => actions
            .Select(action =>
            {
                var constraint = ParseVersionConstraint(action.ModuleName, action.VersionPolicy);
                return new
                {
                    action.ModuleName,
                    Boundary = boundarySelector(constraint),
                    Inclusive = inclusivitySelector(constraint)
                };
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Boundary))
            .GroupBy(static item => item.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Inclusive, StringComparer.OrdinalIgnoreCase);

    private static ModuleStateVersionConstraint ParseVersionConstraint(string moduleName, string? versionPolicy)
    {
        if (string.IsNullOrWhiteSpace(versionPolicy))
            return ModuleStateVersionConstraint.Empty;

        var trimmed = versionPolicy!.Trim();
        if (trimmed.Length == 0 || string.Equals(trimmed, "*", StringComparison.Ordinal))
            return ModuleStateVersionConstraint.Empty;

        string? requiredVersion = null;
        string? minimumVersion = null;
        var minimumVersionInclusive = true;
        string? maximumVersion = null;
        var maximumVersionInclusive = true;
        foreach (var token in trimmed.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith(">=", StringComparison.Ordinal))
            {
                minimumVersion = token.Substring(2).Trim();
                minimumVersionInclusive = true;
            }
            else if (token.StartsWith(">", StringComparison.Ordinal))
            {
                minimumVersion = token.Substring(1).Trim();
                minimumVersionInclusive = false;
            }
            else if (token.StartsWith("<=", StringComparison.Ordinal))
            {
                maximumVersion = token.Substring(2).Trim();
                maximumVersionInclusive = true;
            }
            else if (token.StartsWith("<", StringComparison.Ordinal))
            {
                maximumVersion = token.Substring(1).Trim();
                maximumVersionInclusive = false;
            }
            else if (token.StartsWith("=", StringComparison.Ordinal))
            {
                requiredVersion = token.Substring(1).Trim();
            }
            else
            {
                requiredVersion = token.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(requiredVersion) &&
            (!string.IsNullOrWhiteSpace(minimumVersion) || !string.IsNullOrWhiteSpace(maximumVersion)))
        {
            throw new InvalidOperationException($"Module '{moduleName}' combines exact and range version policies. Private module delivery requires one version policy shape per module.");
        }

        return new ModuleStateVersionConstraint(requiredVersion, minimumVersion, minimumVersionInclusive, maximumVersion, maximumVersionInclusive);
    }
}

internal readonly struct ModuleStateVersionConstraint
{
    internal static readonly ModuleStateVersionConstraint Empty = new(null, null, true, null, true);

    internal ModuleStateVersionConstraint(
        string? requiredVersion,
        string? minimumVersion,
        bool minimumVersionInclusive,
        string? maximumVersion,
        bool maximumVersionInclusive)
    {
        RequiredVersion = string.IsNullOrWhiteSpace(requiredVersion) ? null : requiredVersion!.Trim();
        MinimumVersion = string.IsNullOrWhiteSpace(minimumVersion) ? null : minimumVersion!.Trim();
        MinimumVersionInclusive = minimumVersionInclusive;
        MaximumVersion = string.IsNullOrWhiteSpace(maximumVersion) ? null : maximumVersion!.Trim();
        MaximumVersionInclusive = maximumVersionInclusive;
    }

    internal string? RequiredVersion { get; }

    internal string? MinimumVersion { get; }

    internal bool MinimumVersionInclusive { get; }

    internal string? MaximumVersion { get; }

    internal bool MaximumVersionInclusive { get; }
}

internal readonly struct DeliveryGroupKey
{
    internal DeliveryGroupKey(ModuleStatePlanActionKind kind, string? repository, bool force)
    {
        Kind = kind;
        Repository = string.IsNullOrWhiteSpace(repository) ? null : repository!.Trim();
        Force = force;
    }

    internal ModuleStatePlanActionKind Kind { get; }

    internal string? Repository { get; }

    internal bool Force { get; }
}

internal sealed class DeliveryGroupKeyComparer : IEqualityComparer<DeliveryGroupKey>
{
    internal static readonly DeliveryGroupKeyComparer Instance = new();

    public bool Equals(DeliveryGroupKey x, DeliveryGroupKey y)
        => x.Kind == y.Kind &&
           x.Force == y.Force &&
           string.Equals(x.Repository, y.Repository, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(DeliveryGroupKey obj)
    {
        unchecked
        {
            var hash = ((int)obj.Kind * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Repository ?? string.Empty);
            return (hash * 397) ^ obj.Force.GetHashCode();
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
