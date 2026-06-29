namespace PowerForge;

/// <summary>
/// Updates installed PowerShell modules using managed repository and install services.
/// </summary>
public sealed class ManagedModuleUpdateService
{
    private readonly ILogger _logger;
    private readonly ManagedModuleRepositoryClient _repositoryClient;
    private readonly ManagedModuleInstallService _installService;
    private readonly ManagedModuleReceiptStore _receiptStore;

    /// <summary>
    /// Creates a managed module update service.
    /// </summary>
    /// <param name="logger">Logger used for diagnostics.</param>
    /// <param name="repositoryClient">Optional repository client.</param>
    /// <param name="installService">Optional install service.</param>
    public ManagedModuleUpdateService(
        ILogger logger,
        ManagedModuleRepositoryClient? repositoryClient = null,
        ManagedModuleInstallService? installService = null)
    {
        _logger = logger ?? new NullLogger();
        _repositoryClient = repositoryClient ?? new ManagedModuleRepositoryClient(_logger);
        _installService = installService ?? new ManagedModuleInstallService(_logger, _repositoryClient);
        _receiptStore = new ManagedModuleReceiptStore();
    }

    /// <summary>
    /// Updates a module in the selected scope.
    /// </summary>
    /// <param name="request">Update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update result.</returns>
    public async Task<ManagedModuleUpdateResult> UpdateAsync(
        ManagedModuleUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        ManagedModuleTrustEvaluator.ThrowIfRepositoryRejected(request.Repository, request.TrustPolicy);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var requestScope = _repositoryClient.BeginRequestScope();

        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        var targetVersionInfo = await ResolveSelectedVersionInfoAsync(request, cancellationToken).ConfigureAwait(false);
        var targetVersion = targetVersionInfo.Version;
        var installedVersions = GetInstalledVersions(moduleRoot, request.Name);
        var currentVersion = installedVersions.LastOrDefault();
        var currentModulePath = currentVersion is null ? null : Path.Combine(moduleRoot, request.Name.Trim(), currentVersion);
        var sourceEvaluation = EvaluateSourcePolicy(request, currentModulePath);
        var action = ResolvePlanAction(currentVersion, targetVersion, request.Force, sourceEvaluation);
        ThrowIfSourcePolicyBlocked(action, sourceEvaluation);
        var targetWouldWrite = ActionWritesFiles(action);
        var familyActions = await PlanFamilyActionsAsync(moduleRoot, request, targetVersion, cancellationToken).ConfigureAwait(false);
        ThrowIfFamilyPlanBlocked(familyActions);

        if (!targetWouldWrite && !familyActions.Any(static action => action.WouldWriteFiles))
        {
            _logger.Verbose($"Managed module update skipped '{request.Name}' because {currentVersion} satisfies target {targetVersion}.");
            return new ManagedModuleUpdateResult
            {
                Name = request.Name.Trim(),
                TargetVersion = targetVersion,
                PreviousVersion = currentVersion,
                Status = ManagedModuleUpdateStatus.UpToDate,
                RepositoryName = request.Repository.Name,
                RepositorySource = request.Repository.Source,
                RequestedVersion = request.Version,
                MinimumVersion = request.MinimumVersion,
                MaximumVersion = request.MaximumVersion,
                VersionPolicy = request.VersionPolicy,
                ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256),
                AuthenticodeCheck = request.AuthenticodeCheck,
                RequireTrustedRepository = request.TrustPolicy?.RequireTrustedRepository == true,
                AllowedAuthors = ManagedModuleTrustEvaluator.NormalizeAuthors(request.TrustPolicy?.AllowedAuthors),
                ModuleRoot = moduleRoot,
                ModulePath = Path.Combine(moduleRoot, request.Name.Trim(), currentVersion!),
                Elapsed = stopwatch.Elapsed,
                RepositoryRequestCount = requestScope.Count,
                SourcePolicySatisfied = sourceEvaluation.IsSatisfied,
                SourcePolicyReason = sourceEvaluation.Reason,
                InstalledReceipt = sourceEvaluation.Receipt,
                FamilyResults = MapFamilyResults(familyActions)
            };
        }

        ThrowIfLoadedModuleBlocksUpdate(request, targetWouldWrite, familyActions);

        var modulePath = targetWouldWrite
            ? Path.Combine(moduleRoot, request.Name.Trim(), targetVersion)
            : Path.Combine(moduleRoot, request.Name.Trim(), currentVersion!);
        var install = targetWouldWrite
            ? await InstallSelectedVersionAsync(request, request.Name, targetVersion, cancellationToken).ConfigureAwait(false)
            : null;
        if (install is not null && install.Status == ManagedModuleInstallStatus.Installed)
            _receiptStore.WriteReceipt(request.Repository, install, currentVersion, "Update");
        var familyResults = await ApplyFamilyActionsAsync(request, familyActions, cancellationToken).ConfigureAwait(false);

        return new ManagedModuleUpdateResult
        {
            Name = request.Name.Trim(),
            TargetVersion = targetVersion,
            PreviousVersion = currentVersion,
            Status = ResolveUpdateStatus(action),
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            RequestedVersion = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256),
            AuthenticodeCheck = request.AuthenticodeCheck,
            RequireTrustedRepository = request.TrustPolicy?.RequireTrustedRepository == true,
            AllowedAuthors = ManagedModuleTrustEvaluator.NormalizeAuthors(request.TrustPolicy?.AllowedAuthors),
            ModuleRoot = moduleRoot,
            ModulePath = modulePath,
            Elapsed = stopwatch.Elapsed,
            RepositoryRequestCount = requestScope.Count,
            InstallResult = install,
            Receipt = install?.Receipt,
            ReceiptPath = install?.ReceiptPath,
            SourcePolicySatisfied = sourceEvaluation.IsSatisfied,
            SourcePolicyReason = sourceEvaluation.Reason,
            InstalledReceipt = sourceEvaluation.Receipt,
            FamilyResults = familyResults
        };
    }

    /// <summary>
    /// Creates a non-mutating update plan for the requested module.
    /// </summary>
    /// <param name="request">Update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update plan.</returns>
    public async Task<ManagedModuleUpdatePlan> PlanUpdateAsync(
        ManagedModuleUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        ManagedModuleTrustEvaluator.ThrowIfRepositoryRejected(request.Repository, request.TrustPolicy);

        var moduleRoot = ManagedModuleInstallRootResolver.Resolve(request.Scope, request.ShellEdition, request.ModuleRoot);
        var targetVersionInfo = await ResolveSelectedVersionInfoAsync(request, cancellationToken, resolveExactMetadata: true).ConfigureAwait(false);
        var targetVersion = targetVersionInfo.Version;
        var installedVersions = GetInstalledVersions(moduleRoot, request.Name);
        var currentVersion = installedVersions.LastOrDefault();
        var currentModulePath = currentVersion is null ? null : Path.Combine(moduleRoot, request.Name.Trim(), currentVersion);
        var sourceEvaluation = EvaluateSourcePolicy(request, currentModulePath);
        var action = ResolvePlanAction(currentVersion, targetVersion, request.Force, sourceEvaluation);
        var familyActions = await PlanFamilyActionsAsync(moduleRoot, request, targetVersion, cancellationToken).ConfigureAwait(false);
        var selectedPathVersion = action is ManagedModuleUpdatePlanAction.SkipUpToDate or ManagedModuleUpdatePlanAction.SourceMismatchBlocked && currentVersion is not null
            ? currentVersion
            : targetVersion;

        return new ManagedModuleUpdatePlan
        {
            Name = request.Name.Trim(),
            TargetVersion = targetVersion,
            PreviousVersion = currentVersion,
            InstalledVersions = installedVersions,
            Action = action,
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            ModuleRoot = moduleRoot,
            ModulePath = Path.Combine(moduleRoot, request.Name.Trim(), selectedPathVersion),
            WouldWriteFiles = ActionWritesFiles(action) ||
                              familyActions.Any(static familyAction => familyAction.WouldWriteFiles),
            RequestedVersion = request.Version,
            MinimumVersion = request.MinimumVersion,
            MaximumVersion = request.MaximumVersion,
            VersionPolicy = request.VersionPolicy,
            ExpectedPackageSha256 = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256),
            AuthenticodeCheck = request.AuthenticodeCheck,
            RequireTrustedRepository = request.TrustPolicy?.RequireTrustedRepository == true,
            AllowedAuthors = ManagedModuleTrustEvaluator.NormalizeAuthors(request.TrustPolicy?.AllowedAuthors),
            SourcePolicySatisfied = sourceEvaluation.IsSatisfied,
            SourcePolicyReason = sourceEvaluation.Reason,
            InstalledReceipt = sourceEvaluation.Receipt,
            FamilyActions = familyActions,
            License = targetVersionInfo.License,
            LicenseAcceptanceRequired = targetVersionInfo.RequireLicenseAcceptance,
            LicenseAccepted = request.AcceptLicense
        };
    }

    private async Task<ManagedModuleInstallResult> InstallSelectedVersionAsync(
        ManagedModuleUpdateRequest request,
        string moduleName,
        string targetVersion,
        CancellationToken cancellationToken)
        => await _installService.InstallAsync(
            new ManagedModuleInstallRequest
            {
                Repository = request.Repository,
                Name = moduleName,
                Version = targetVersion,
                MinimumVersion = null,
                MaximumVersion = null,
                VersionPolicy = null,
                IncludePrerelease = request.IncludePrerelease || ContainsPrereleaseLabel(targetVersion),
                Scope = request.Scope,
                ShellEdition = request.ShellEdition,
                ModuleRoot = request.ModuleRoot,
                PackageCacheDirectory = request.PackageCacheDirectory,
                DependencyConcurrency = request.DependencyConcurrency,
                ExpectedPackageSha256 = string.Equals(moduleName, request.Name, StringComparison.OrdinalIgnoreCase)
                    ? request.ExpectedPackageSha256
                    : null,
                TrustPolicy = request.TrustPolicy,
                Credential = request.Credential,
                Force = true,
                AllowClobber = request.AllowClobber,
                AcceptLicense = request.AcceptLicense,
                AuthenticodeCheck = request.AuthenticodeCheck,
                SkipDependencyCheck = request.SkipDependencyCheck
            },
            cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<ManagedModuleFamilyUpdateResult>> ApplyFamilyActionsAsync(
        ManagedModuleUpdateRequest request,
        IReadOnlyList<ManagedModuleFamilyUpdatePlanItem> familyActions,
        CancellationToken cancellationToken)
    {
        if (familyActions.Count == 0)
            return Array.Empty<ManagedModuleFamilyUpdateResult>();

        var results = new List<ManagedModuleFamilyUpdateResult>(familyActions.Count);
        foreach (var action in familyActions)
        {
            if (!action.WouldWriteFiles)
            {
                results.Add(MapFamilyResult(action));
                continue;
            }

            var install = await InstallSelectedVersionAsync(request, action.Name, action.TargetVersion, cancellationToken).ConfigureAwait(false);
            if (install.Status == ManagedModuleInstallStatus.Installed)
                _receiptStore.WriteReceipt(request.Repository, install, action.PreviousVersion, "Update");
            results.Add(MapFamilyResult(action, install));
        }

        return results;
    }

    private async Task<IReadOnlyList<ManagedModuleFamilyUpdatePlanItem>> PlanFamilyActionsAsync(
        string moduleRoot,
        ManagedModuleUpdateRequest request,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        var policy = request.FamilyPolicy;
        if (policy is null || !policy.RequireSameVersion)
            return Array.Empty<ManagedModuleFamilyUpdatePlanItem>();

        var familyNames = GetInstalledFamilyModuleNames(moduleRoot, policy)
            .Where(name => !name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (familyNames.Length == 0)
            return Array.Empty<ManagedModuleFamilyUpdatePlanItem>();

        var actions = new List<ManagedModuleFamilyUpdatePlanItem>(familyNames.Length);
        foreach (var familyName in familyNames)
        {
            var installedVersions = GetInstalledVersions(moduleRoot, familyName);
            var currentVersion = installedVersions.LastOrDefault();
            if (currentVersion is null)
                continue;

            var repositoryVersion = await TryResolveRepositoryVersionAsync(
                request,
                familyName,
                targetVersion,
                cancellationToken).ConfigureAwait(false);
            var repositoryVersionAvailable = repositoryVersion is not null;
            var action = ResolveFamilyPlanAction(currentVersion, targetVersion, request.Force, repositoryVersionAvailable);
            var selectedPathVersion = action is ManagedModuleFamilyUpdatePlanAction.Update or ManagedModuleFamilyUpdatePlanAction.Reinstall
                ? targetVersion
                : currentVersion;

            actions.Add(new ManagedModuleFamilyUpdatePlanItem
            {
                Name = familyName,
                FamilyName = ResolveFamilyName(policy),
                TargetVersion = targetVersion,
                PreviousVersion = currentVersion,
                InstalledVersions = installedVersions,
                Action = action,
                ModulePath = Path.Combine(moduleRoot, familyName, selectedPathVersion),
                RepositoryVersionAvailable = repositoryVersionAvailable,
                WouldWriteFiles = action is ManagedModuleFamilyUpdatePlanAction.Update or ManagedModuleFamilyUpdatePlanAction.Reinstall,
                ConflictReason = ResolveFamilyConflictReason(familyName, currentVersion, targetVersion, action),
                License = repositoryVersion?.License,
                LicenseAcceptanceRequired = repositoryVersion?.RequireLicenseAcceptance == true,
                LicenseAccepted = request.AcceptLicense
            });
        }

        return actions;
    }

    private async Task<ManagedModuleVersionInfo?> TryResolveRepositoryVersionAsync(
        ManagedModuleUpdateRequest request,
        string moduleName,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        var versions = await _repositoryClient.GetVersionsAsync(
            request.Repository,
            moduleName,
            request.IncludePrerelease || ContainsPrereleaseLabel(targetVersion),
            request.Credential,
            cancellationToken).ConfigureAwait(false);
        return versions.FirstOrDefault(version => version.Version.Equals(targetVersion, StringComparison.OrdinalIgnoreCase));
    }

    private static void ThrowIfLoadedModuleBlocksUpdate(
        ManagedModuleUpdateRequest request,
        bool targetWouldWrite,
        IReadOnlyList<ManagedModuleFamilyUpdatePlanItem> familyActions)
    {
        if (request.AllowLoadedModuleUpdate)
            return;

        var updatedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (targetWouldWrite)
            updatedNames.Add(request.Name.Trim());
        foreach (var action in familyActions.Where(static action => action.WouldWriteFiles))
            updatedNames.Add(action.Name);

        if (updatedNames.Count == 0)
            return;

        var loaded = SelectLoadedModules(request, updatedNames).ToArray();
        if (loaded.Length == 0)
            return;

        var details = string.Join(", ", loaded.Select(FormatLoadedModule));
        throw new InvalidOperationException(
            $"One or more modules selected for managed update are already loaded and cannot be safely updated: {details}. Close the PowerShell session, unload the module, or set AllowLoadedModuleUpdate when you accept the risk.");
    }

    private static void ThrowIfFamilyPlanBlocked(IReadOnlyList<ManagedModuleFamilyUpdatePlanItem> familyActions)
    {
        var blocked = familyActions
            .Where(static action => action.Action is ManagedModuleFamilyUpdatePlanAction.MissingRepositoryVersion or ManagedModuleFamilyUpdatePlanAction.DowngradeBlocked)
            .ToArray();
        if (blocked.Length == 0)
            return;

        var details = string.Join("; ", blocked.Select(static action => action.ConflictReason ?? $"{action.Name}: {action.Action}"));
        throw new InvalidOperationException($"Managed module family update cannot be applied safely: {details}.");
    }

    private async Task<ManagedModuleVersionInfo> ResolveSelectedVersionInfoAsync(
        ManagedModuleUpdateRequest request,
        CancellationToken cancellationToken,
        bool resolveExactMetadata = false)
    {
        if (!string.IsNullOrWhiteSpace(request.Version))
        {
            var exactVersion = request.Version!.Trim();
            if (resolveExactMetadata)
            {
                var exactMatch = await TryResolveRepositoryVersionAsync(
                    request,
                    request.Name,
                    exactVersion,
                    cancellationToken).ConfigureAwait(false);
                if (exactMatch is not null)
                    return exactMatch;
            }

            return CreateRequestedVersionInfo(request, exactVersion);
        }

        var range = ResolveVersionRange(request.VersionPolicy, request.MinimumVersion, request.MaximumVersion);
        var versions = await _repositoryClient.GetVersionsAsync(
            request.Repository,
            request.Name,
            request.IncludePrerelease || range.AllowsPrerelease,
            request.Credential,
            cancellationToken).ConfigureAwait(false);

        var latest = versions
            .Where(version => range.IsSatisfiedBy(version.Version))
            .LastOrDefault();
        if (latest is null)
            throw new InvalidOperationException($"No versions of '{request.Name}' satisfying range '{range}' were found in repository '{request.Repository.Name}'.");

        return latest;
    }

    private static ManagedModuleVersionInfo CreateRequestedVersionInfo(ManagedModuleUpdateRequest request, string version)
        => new()
        {
            Name = request.Name.Trim(),
            Version = version,
            RepositoryName = request.Repository.Name,
            RepositorySource = request.Repository.Source,
            IsPrerelease = ManagedModuleVersionComparer.IsPrerelease(version)
        };

    private static IReadOnlyList<string> GetInstalledVersions(string moduleRoot, string moduleName)
        => ManagedModuleInstallContext.EnumerateInstalledVersions(moduleRoot, moduleName);

    private static ManagedModuleUpdatePlanAction ResolvePlanAction(
        string? currentVersion,
        string targetVersion,
        bool force,
        SourcePolicyEvaluation sourceEvaluation)
    {
        if (currentVersion is null)
            return ManagedModuleUpdatePlanAction.InstallMissing;

        var comparison = ManagedModuleVersionComparer.Instance.Compare(currentVersion, targetVersion);
        if (comparison == 0)
        {
            if (force)
                return ManagedModuleUpdatePlanAction.Reinstall;
            return sourceEvaluation.IsSatisfied
                ? ManagedModuleUpdatePlanAction.SkipUpToDate
                : ManagedModuleUpdatePlanAction.RepairSource;
        }
        if (comparison > 0)
            return sourceEvaluation.IsSatisfied
                ? ManagedModuleUpdatePlanAction.SkipUpToDate
                : ManagedModuleUpdatePlanAction.SourceMismatchBlocked;

        return ManagedModuleUpdatePlanAction.Update;
    }

    private static bool ActionWritesFiles(ManagedModuleUpdatePlanAction action)
        => action is ManagedModuleUpdatePlanAction.InstallMissing
            or ManagedModuleUpdatePlanAction.Update
            or ManagedModuleUpdatePlanAction.Reinstall
            or ManagedModuleUpdatePlanAction.RepairSource;

    private static ManagedModuleUpdateStatus ResolveUpdateStatus(ManagedModuleUpdatePlanAction action)
        => action switch
        {
            ManagedModuleUpdatePlanAction.InstallMissing => ManagedModuleUpdateStatus.InstalledMissing,
            ManagedModuleUpdatePlanAction.RepairSource => ManagedModuleUpdateStatus.SourceRepaired,
            ManagedModuleUpdatePlanAction.SkipUpToDate => ManagedModuleUpdateStatus.UpToDate,
            _ => ManagedModuleUpdateStatus.Updated
        };

    private static void ThrowIfSourcePolicyBlocked(
        ManagedModuleUpdatePlanAction action,
        SourcePolicyEvaluation sourceEvaluation)
    {
        if (action != ManagedModuleUpdatePlanAction.SourceMismatchBlocked)
            return;

        throw new InvalidOperationException(
            $"Managed module source policy cannot be repaired safely without an explicit downgrade: {sourceEvaluation.Reason}");
    }

    private SourcePolicyEvaluation EvaluateSourcePolicy(ManagedModuleUpdateRequest request, string? modulePath)
    {
        if (request.SourcePolicy is null || string.IsNullOrWhiteSpace(modulePath))
            return SourcePolicyEvaluation.Satisfied();

        var receipt = _receiptStore.TryReadReceipt(modulePath!);
        if (receipt is null)
        {
            return request.SourcePolicy.RequireManagedReceipt
                ? SourcePolicyEvaluation.Failed("No managed module receipt was found for the installed version.", null)
                : SourcePolicyEvaluation.Satisfied();
        }

        if (request.SourcePolicy.RequireRepositoryNameMatch &&
            !receipt.RepositoryName.Equals(request.Repository.Name, StringComparison.OrdinalIgnoreCase))
            return SourcePolicyEvaluation.Failed($"Receipt repository name '{receipt.RepositoryName}' does not match requested repository '{request.Repository.Name}'.", receipt);

        if (request.SourcePolicy.RequireRepositorySourceMatch &&
            !NormalizeRepositorySource(receipt.RepositorySource).Equals(NormalizeRepositorySource(request.Repository.Source), StringComparison.OrdinalIgnoreCase))
            return SourcePolicyEvaluation.Failed("Receipt repository source does not match the requested repository source.", receipt);

        return SourcePolicyEvaluation.Satisfied(receipt);
    }

    private static string NormalizeRepositorySource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var trimmed = source!.Trim().Trim('"');
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return trimmed.TrimEnd('/', '\\');
    }

    private static ManagedModuleFamilyUpdatePlanAction ResolveFamilyPlanAction(
        string currentVersion,
        string targetVersion,
        bool force,
        bool repositoryVersionAvailable)
    {
        if (!repositoryVersionAvailable)
            return ManagedModuleFamilyUpdatePlanAction.MissingRepositoryVersion;

        var comparison = ManagedModuleVersionComparer.Instance.Compare(currentVersion, targetVersion);
        if (comparison == 0)
            return force ? ManagedModuleFamilyUpdatePlanAction.Reinstall : ManagedModuleFamilyUpdatePlanAction.SkipUpToDate;
        if (comparison < 0)
            return ManagedModuleFamilyUpdatePlanAction.Update;

        return ManagedModuleFamilyUpdatePlanAction.DowngradeBlocked;
    }

    private static string? ResolveFamilyConflictReason(
        string moduleName,
        string currentVersion,
        string targetVersion,
        ManagedModuleFamilyUpdatePlanAction action)
        => action switch
        {
            ManagedModuleFamilyUpdatePlanAction.MissingRepositoryVersion
                => $"Repository does not contain '{moduleName}' version {targetVersion}.",
            ManagedModuleFamilyUpdatePlanAction.DowngradeBlocked
                => $"'{moduleName}' has version {currentVersion}, which is newer than selected family target {targetVersion}.",
            _ => null
        };

    private static IReadOnlyList<ManagedModuleFamilyUpdateResult> MapFamilyResults(
        IReadOnlyList<ManagedModuleFamilyUpdatePlanItem> familyActions)
        => familyActions.Count == 0
            ? Array.Empty<ManagedModuleFamilyUpdateResult>()
            : familyActions.Select(static action => MapFamilyResult(action)).ToArray();

    private static ManagedModuleFamilyUpdateResult MapFamilyResult(
        ManagedModuleFamilyUpdatePlanItem action,
        ManagedModuleInstallResult? install = null)
        => new()
        {
            Name = action.Name,
            FamilyName = action.FamilyName,
            TargetVersion = action.TargetVersion,
            PreviousVersion = action.PreviousVersion,
            Action = action.Action,
            ModulePath = install?.ModulePath ?? action.ModulePath,
            InstallResult = install,
            Receipt = install?.Receipt,
            ReceiptPath = install?.ReceiptPath
        };

    private static IEnumerable<ManagedModuleLoadedModule> SelectLoadedModules(
        ManagedModuleUpdateRequest request,
        HashSet<string> updatedNames)
        => (request.LoadedModules ?? Array.Empty<ManagedModuleLoadedModule>())
            .Where(module => module is not null &&
                             updatedNames.Contains(module.Name));

    private static string FormatLoadedModule(ManagedModuleLoadedModule module)
    {
        var version = string.IsNullOrWhiteSpace(module.Version) ? "unknown version" : module.Version!.Trim();
        var path = FirstNonEmpty(module.ModuleBase, module.Path);
        return string.IsNullOrWhiteSpace(path)
            ? $"{module.Name} {version}"
            : $"{module.Name} {version} at {path}";
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static IEnumerable<string> GetInstalledFamilyModuleNames(string moduleRoot, ManagedModuleFamilyPolicy policy)
    {
        if (!Directory.Exists(moduleRoot))
            return Array.Empty<string>();

        var configuredNames = new HashSet<string>(
            policy.ModuleNames?.Where(static name => !string.IsNullOrWhiteSpace(name)).Select(static name => name.Trim()) ??
            Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var installedNames = Directory.EnumerateDirectories(moduleRoot)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .Where(name => configuredNames.Contains(name) || MatchesFamilyPrefix(name, policy.ModuleNamePrefix))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase);

        return installedNames.ToArray();
    }

    private static bool MatchesFamilyPrefix(string moduleName, string? prefix)
        => !string.IsNullOrWhiteSpace(prefix) &&
           moduleName.StartsWith(prefix!.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string ResolveFamilyName(ManagedModuleFamilyPolicy policy)
    {
        if (!string.IsNullOrWhiteSpace(policy.Name))
            return policy.Name!.Trim();
        if (!string.IsNullOrWhiteSpace(policy.ModuleNamePrefix))
            return policy.ModuleNamePrefix!.Trim();
        return "ManagedModuleFamily";
    }

    private static bool ContainsPrereleaseLabel(string version)
        => version.IndexOf("-", StringComparison.Ordinal) >= 0;

    private static void Validate(ManagedModuleUpdateRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (request.Repository is null)
            throw new ArgumentException("Repository is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Module name is required.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.Version) &&
            (!string.IsNullOrWhiteSpace(request.MinimumVersion) ||
             !string.IsNullOrWhiteSpace(request.MaximumVersion) ||
             !string.IsNullOrWhiteSpace(request.VersionPolicy)))
            throw new ArgumentException("Version cannot be combined with MinimumVersion, MaximumVersion, or VersionPolicy.", nameof(request));
        if (!string.IsNullOrWhiteSpace(request.VersionPolicy) &&
            (!string.IsNullOrWhiteSpace(request.MinimumVersion) || !string.IsNullOrWhiteSpace(request.MaximumVersion)))
            throw new ArgumentException("VersionPolicy cannot be combined with MinimumVersion or MaximumVersion.", nameof(request));
        if (request.Scope == ManagedModuleInstallScope.Custom && string.IsNullOrWhiteSpace(request.ModuleRoot))
            throw new ArgumentException("ModuleRoot is required when Scope is Custom.", nameof(request));
        if (request.DependencyConcurrency < 0 || request.DependencyConcurrency > ManagedModuleInstallService.MaximumDependencyInstallConcurrency)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.DependencyConcurrency,
                $"DependencyConcurrency must be between 0 and {ManagedModuleInstallService.MaximumDependencyInstallConcurrency}. Use 0 for the engine default.");
        if (request.FamilyPolicy is not null &&
            request.FamilyPolicy.RequireSameVersion &&
            string.IsNullOrWhiteSpace(request.FamilyPolicy.ModuleNamePrefix) &&
            !(request.FamilyPolicy.ModuleNames?.Any(static name => !string.IsNullOrWhiteSpace(name)) == true))
            throw new ArgumentException("FamilyPolicy requires ModuleNamePrefix or ModuleNames when RequireSameVersion is enabled.", nameof(request));

        _ = ManagedModulePackageIntegrity.NormalizeSha256(request.ExpectedPackageSha256);
    }

    private static ManagedModuleVersionRange ResolveVersionRange(string? versionPolicy, string? minimumVersion, string? maximumVersion)
        => string.IsNullOrWhiteSpace(versionPolicy)
            ? ManagedModuleVersionRange.FromBounds(minimumVersion, maximumVersion)
            : ManagedModuleVersionRange.Parse(versionPolicy);

    private sealed class SourcePolicyEvaluation
    {
        private SourcePolicyEvaluation(bool isSatisfied, string? reason, ManagedModuleReceipt? receipt)
        {
            IsSatisfied = isSatisfied;
            Reason = reason;
            Receipt = receipt;
        }

        public bool IsSatisfied { get; }

        public string? Reason { get; }

        public ManagedModuleReceipt? Receipt { get; }

        public static SourcePolicyEvaluation Satisfied(ManagedModuleReceipt? receipt = null)
            => new(true, null, receipt);

        public static SourcePolicyEvaluation Failed(string reason, ManagedModuleReceipt? receipt)
            => new(false, reason, receipt);
    }
}
