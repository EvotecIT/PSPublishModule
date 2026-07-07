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
            .GroupBy(action => new DeliveryGroupKey(
                action.Kind,
                ResolveActionRepository(action, options),
                ResolveActionForce(action, options),
                ResolveManagedAllowClobber(action, options),
                ResolveManagedAcceptLicense(action, options),
                ResolveManagedSkipDependencyCheck(action, options),
                action.ModuleName,
                action.TargetScope,
                action.TargetPath),
                DeliveryGroupKeyComparer.Instance)
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
            var request = CreateRequest(
                group.Key.Kind,
                group.Key.Repository,
                group.Key.Force,
                group.Key.ManagedAllowClobber,
                group.Key.ManagedAcceptLicense,
                group.Key.ManagedSkipDependencyCheck,
                groupActions,
                options);
            var workflowResult = service.Execute(request, ShouldProcess);
            results.Add(new ModuleStateDeliveryExecutionResult
            {
                Operation = group.Key.Kind.ToString(),
                OperationPerformed = workflowResult.OperationPerformed,
                RepositoryName = workflowResult.RepositoryName,
                RequestedTransport = workflowResult.RequestedTransport,
                EffectiveTransport = workflowResult.EffectiveTransport,
                DeliveryTransportReason = workflowResult.DeliveryTransportReason,
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

    private bool ShouldProcess(string target, string action)
        => _cmdlet is AsyncPSCmdlet asyncCmdlet
            ? asyncCmdlet.ShouldProcess(target, action)
            : _cmdlet.ShouldProcess(target, action);

    private static PrivateModuleWorkflowRequest CreateRequest(
        ModuleStatePlanActionKind actionKind,
        string? repository,
        bool force,
        bool managedAllowClobber,
        bool managedAcceptLicense,
        bool managedSkipDependencyCheck,
        IReadOnlyList<ModuleStatePlanAction> actions,
        ModuleStatePrivateDeliveryOptions options)
    {
        ValidateNoConflictingDuplicateActions(actions);

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
            ManagedRepositorySource = ResolveManagedRepositorySource(repository),
            InstallPrerequisites = options.InstallPrerequisites,
            Prerelease = options.Prerelease || RequiresPrereleaseDelivery(actions),
            Force = force,
            DeliveryTransport = options.DeliveryTransport,
            CredentialUserName = options.CredentialUserName,
            CredentialSecret = options.CredentialSecret,
            CredentialSecretFilePath = options.CredentialSecretFilePath,
            PromptForCredential = options.PromptForCredential
        };
        ApplyManagedDeliveryOptions(
            request,
            actions,
            options,
            managedAllowClobber,
            managedAcceptLicense,
            managedSkipDependencyCheck);

        if (!string.IsNullOrWhiteSpace(options.ProfileName))
        {
            var profile = ModuleRepositoryProfileCommandSupport.ResolveRequired(options.ProfileName!);
            if (!ShouldApplyProfile(repository, profile))
                return request;

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
            request.ApiVersion = profile.ApiVersion;
            request.BootstrapMode = profile.BootstrapMode;
            request.Trusted = profile.Trusted;
            request.Priority = profile.Priority;
        }

        return request;
    }

    private static void ApplyManagedDeliveryOptions(
        PrivateModuleWorkflowRequest request,
        IReadOnlyList<ModuleStatePlanAction> actions,
        ModuleStatePrivateDeliveryOptions options,
        bool managedAllowClobber,
        bool managedAcceptLicense,
        bool managedSkipDependencyCheck)
    {
        request.ManagedAllowClobber = managedAllowClobber;
        request.ManagedAcceptLicense = managedAcceptLicense;
        request.ManagedSkipDependencyCheck = managedSkipDependencyCheck;
        request.ManagedModuleRoot = ResolveManagedModuleRoot(actions, options);
        request.ManagedScope = ResolveManagedScope(actions);
        request.ManagedLoadedModules = options.LoadedModules;
    }

    private static string? ResolveManagedModuleRoot(
        IReadOnlyList<ModuleStatePlanAction> actions,
        ModuleStatePrivateDeliveryOptions options)
        => actions
            .Select(static action => action.TargetPath)
            .FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path))
           ?? options.ManagedModuleRoot;

    private static ManagedModuleInstallScope ResolveManagedScope(IReadOnlyList<ModuleStatePlanAction> actions)
    {
        var scope = actions
            .Select(static action => action.TargetScope)
            .FirstOrDefault(static scope => !string.IsNullOrWhiteSpace(scope));

        return string.Equals(scope, "AllUsers", StringComparison.OrdinalIgnoreCase)
            ? ManagedModuleInstallScope.AllUsers
            : ManagedModuleInstallScope.CurrentUser;
    }

    private static bool ShouldApplyProfile(string? repository, ModuleRepositoryProfile profile)
        => string.IsNullOrWhiteSpace(repository) ||
           string.Equals(repository, profile.RepositoryName, StringComparison.OrdinalIgnoreCase);

    private static string? ResolveManagedRepositorySource(string? repository)
        => IsRepositorySource(repository) ? repository!.Trim() : null;

    private static bool IsRepositorySource(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return false;

        var value = repository!.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Scheme))
            return true;

        return value.StartsWith(".", StringComparison.Ordinal) ||
               value.IndexOf('\\') >= 0 ||
               value.IndexOf('/') >= 0;
    }

    private static void ValidateNoConflictingDuplicateActions(IReadOnlyList<ModuleStatePlanAction> actions)
    {
        foreach (var group in actions
                     .GroupBy(static action => string.Join("|", action.ModuleName, action.TargetScope ?? string.Empty), StringComparer.OrdinalIgnoreCase))
        {
            var policies = group
                .Select(static action => action.VersionPolicy ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (policies.Length > 1)
            {
                throw new InvalidOperationException(
                    $"ModuleState delivery contains conflicting version policies for module '{group.First().ModuleName}' in scope '{group.First().TargetScope ?? "<default>"}'.");
            }
        }
    }

    private static string? ResolveActionRepository(ModuleStatePlanAction action, ModuleStatePrivateDeliveryOptions options)
    {
        if (!string.IsNullOrWhiteSpace(action.TargetRepositorySource))
            return action.TargetRepositorySource;
        if (!string.IsNullOrWhiteSpace(action.TargetRepository))
            return action.TargetRepository;
        if (!string.IsNullOrWhiteSpace(options.ProfileName))
            return options.Repository;
        if (!string.IsNullOrWhiteSpace(options.Repository))
            return options.Repository;

        return null;
    }

    private static bool ResolveActionForce(ModuleStatePlanAction action, ModuleStatePrivateDeliveryOptions options)
        => action.Force ||
           (options.Force && action.Kind is ModuleStatePlanActionKind.Install or ModuleStatePlanActionKind.Update);

    private static bool RequiresPrereleaseDelivery(IEnumerable<ModuleStatePlanAction> actions)
        => actions.Any(static action =>
            action.IncludePrerelease ||
            ConstraintHasPrerelease(ParseVersionConstraint(action.ModuleName, action.VersionPolicy)));

    private static bool ResolveManagedAllowClobber(ModuleStatePlanAction action, ModuleStatePrivateDeliveryOptions options)
        => options.ManagedAllowClobber || action.AllowClobber;

    private static bool ResolveManagedAcceptLicense(ModuleStatePlanAction action, ModuleStatePrivateDeliveryOptions options)
        => options.ManagedAcceptLicense || action.AcceptLicense;

    private static bool ResolveManagedSkipDependencyCheck(ModuleStatePlanAction action, ModuleStatePrivateDeliveryOptions options)
        => options.ManagedSkipDependencyCheck || action.SkipDependencyCheck;

    private static bool ConstraintHasPrerelease(ModuleStateVersionConstraint constraint)
        => ContainsPrereleaseBoundary(constraint.RequiredVersion) ||
           ContainsPrereleaseBoundary(constraint.MinimumVersion) ||
           ContainsPrereleaseBoundary(constraint.MaximumVersion);

    private static bool ContainsPrereleaseBoundary(string? version)
        => ModuleStateVersion.TryParse(version, out var parsed) && parsed.IsPrerelease;

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

        if (HasNuGetRangeDelimiters(trimmed))
        {
            var range = ManagedModuleVersionRange.Parse(trimmed);
            return range.IsUnbounded
                ? ModuleStateVersionConstraint.Empty
                : new ModuleStateVersionConstraint(
                    range.ExactVersion,
                    range.MinimumVersion,
                    range.IncludeMinimum,
                    range.MaximumVersion,
                    range.IncludeMaximum);
        }

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

    private static bool HasNuGetRangeDelimiters(string value)
        => value.StartsWith("[", StringComparison.Ordinal) ||
           value.StartsWith("(", StringComparison.Ordinal) ||
           value.EndsWith("]", StringComparison.Ordinal) ||
           value.EndsWith(")", StringComparison.Ordinal);
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
    internal DeliveryGroupKey(
        ModuleStatePlanActionKind kind,
        string? repository,
        bool force,
        bool managedAllowClobber,
        bool managedAcceptLicense,
        bool managedSkipDependencyCheck,
        string moduleName,
        string? targetScope,
        string? targetPath)
    {
        Kind = kind;
        Repository = string.IsNullOrWhiteSpace(repository) ? null : repository!.Trim();
        Force = force;
        ManagedAllowClobber = managedAllowClobber;
        ManagedAcceptLicense = managedAcceptLicense;
        ManagedSkipDependencyCheck = managedSkipDependencyCheck;
        ModuleName = moduleName;
        TargetScope = string.IsNullOrWhiteSpace(targetScope) ? null : targetScope!.Trim();
        TargetPath = string.IsNullOrWhiteSpace(targetPath) ? null : targetPath!.Trim();
    }

    internal ModuleStatePlanActionKind Kind { get; }

    internal string? Repository { get; }

    internal bool Force { get; }

    internal bool ManagedAllowClobber { get; }

    internal bool ManagedAcceptLicense { get; }

    internal bool ManagedSkipDependencyCheck { get; }

    internal string ModuleName { get; }

    internal string? TargetScope { get; }

    internal string? TargetPath { get; }
}

internal sealed class DeliveryGroupKeyComparer : IEqualityComparer<DeliveryGroupKey>
{
    internal static readonly DeliveryGroupKeyComparer Instance = new();

    public bool Equals(DeliveryGroupKey x, DeliveryGroupKey y)
        => x.Kind == y.Kind &&
            x.Force == y.Force &&
            x.ManagedAllowClobber == y.ManagedAllowClobber &&
            x.ManagedAcceptLicense == y.ManagedAcceptLicense &&
            x.ManagedSkipDependencyCheck == y.ManagedSkipDependencyCheck &&
            string.Equals(x.Repository, y.Repository, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ModuleName, y.ModuleName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.TargetScope, y.TargetScope, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.TargetPath, y.TargetPath, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(DeliveryGroupKey obj)
    {
        unchecked
        {
            var hash = ((int)obj.Kind * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Repository ?? string.Empty);
            hash = (hash * 397) ^ obj.Force.GetHashCode();
            hash = (hash * 397) ^ obj.ManagedAllowClobber.GetHashCode();
            hash = (hash * 397) ^ obj.ManagedAcceptLicense.GetHashCode();
            hash = (hash * 397) ^ obj.ManagedSkipDependencyCheck.GetHashCode();
            hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ModuleName ?? string.Empty);
            hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TargetScope ?? string.Empty);
            return (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TargetPath ?? string.Empty);
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

    internal ModuleStateDeliveryTransport DeliveryTransport { get; set; } = ModuleStateDeliveryTransport.PrivateModule;

    internal string? CredentialUserName { get; set; }

    internal string? CredentialSecret { get; set; }

    internal string? CredentialSecretFilePath { get; set; }

    internal bool PromptForCredential { get; set; }

    internal string? ManagedModuleRoot { get; set; }

    internal bool ManagedAllowClobber { get; set; }

    internal bool ManagedAcceptLicense { get; set; }

    internal bool ManagedSkipDependencyCheck { get; set; }

    internal IReadOnlyList<ManagedModuleLoadedModule> LoadedModules { get; set; } = Array.Empty<ManagedModuleLoadedModule>();
}
