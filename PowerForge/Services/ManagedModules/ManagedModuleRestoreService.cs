namespace PowerForge;

internal sealed partial class ManagedModuleRestoreService(ILogger logger)
{
    internal const string DefaultSource = ManagedModuleCatalogDefaults.PowerShellGalleryV3;
    private const int LockSchemaVersion = 1;

    internal async Task<ManagedModuleRestoreResult> RestoreAsync(
        ManagedModuleRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        var requirementsPath = Path.GetFullPath(request.RequirementsPath);
        var lockPath = Path.GetFullPath(request.LockPath);
        var destination = ManagedModuleInstallRootResolver.Resolve(ManagedModuleInstallScope.Custom, customRoot: request.Destination);
        if (FrameworkCompatibility.PathComparer.Equals(FrameworkCompatibility.ResolvePhysicalPath(requirementsPath),
                                                       FrameworkCompatibility.ResolvePhysicalPath(lockPath)))
            throw new ArgumentException("The requirements and lock file must use different paths.");
        RequireOutsideDestination(requirementsPath, destination);
        RequireOutsideDestination(lockPath, destination);
        if (File.Exists(destination))
            throw new ArgumentException("The module destination must be a directory.");

        var requirements = ReadRequirements(requirementsPath);
        var repository = ResolveRepository(request.Source);
        var restoreLock = request.WriteLock ? null : ReadLock(lockPath, requirements, repository.Source);
        if (request.WriteLock && !request.Plan && !Directory.Exists(Path.GetDirectoryName(lockPath)))
            throw new DirectoryNotFoundException($"Create the parent directory of '{lockPath}' before writing a lock.");

        var scratch = Path.Combine(Path.GetTempPath(), "PFMM.R", Guid.NewGuid().ToString("N").Substring(0, 12));
        var cache = Path.Combine(scratch, "cache");
        var client = new ManagedModuleRepositoryClient(logger);
        var installer = new ManagedModuleInstallService(logger, client);
        try
        {
            var prepared = restoreLock is null
                ? await ResolveAsync(requirements, repository, client, cache, cancellationToken)
                    .ConfigureAwait(false)
                : await PrepareLockedAsync(restoreLock, repository, client, cache, cancellationToken)
                    .ConfigureAwait(false);

            var ordered = ValidateClosure(requirements, prepared, request.AcceptLicense);
            restoreLock ??= new ManagedModuleRestoreLock(
                LockSchemaVersion, repository.Source, requirements,
                prepared.Values.Select(static package => package.Pin)
                    .OrderBy(static pin => pin.Name, StringComparer.OrdinalIgnoreCase).ToArray());
            if (!request.Plan)
            {
                ValidateModuleDestinations(destination, ordered);
                using var restoreGate = ManagedModuleInstallLock.Acquire(destination, ".restore", cancellationToken);
                foreach (var package in ordered)
                {
                    await installer.InstallAsync(new ManagedModuleInstallRequest
                    {
                        Name = package.Pin.Name,
                        Version = package.Pin.Version,
                        Repository = repository,
                        Scope = ManagedModuleInstallScope.Custom,
                        ModuleRoot = destination,
                        PackageCacheDirectory = cache,
                        ExpectedPackageSha256 = package.Pin.Sha256,
                        AcceptLicense = request.AcceptLicense,
                        AllowClobber = request.AllowClobber,
                        RequireVersionedModulePath = true,
                        SkipDependencyCheck = true
                    }, cancellationToken).ConfigureAwait(false);
                }

                if (request.WriteLock)
                    WriteLock(lockPath, restoreLock);
            }

            return new ManagedModuleRestoreResult(destination, lockPath, request.Plan, request.WriteLock && !request.Plan, restoreLock);
        }
        finally
        {
            ManagedModuleExtractedPackageCache.DeleteDirectoryQuietly(scratch);
        }
    }

    private static async Task<Dictionary<string, PreparedPackage>> PrepareLockedAsync(
        ManagedModuleRestoreLock restoreLock,
        ManagedModuleRepository repository,
        ManagedModuleRepositoryClient client,
        string cache,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(ManagedModuleConcurrencyDefaults.ResolveDefault());
        var tasks = restoreLock.Packages.Select(async pin =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var download = await client.DownloadPackageAsync(repository, pin.Name, pin.Version, cache,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                ManagedModulePackageIntegrity.VerifyDownload(download, pin.Sha256);
                return PreparePackage(download);
            }
            finally
            {
                gate.Release();
            }
        });

        var packages = await Task.WhenAll(tasks).ConfigureAwait(false);
        return packages.ToDictionary(static package => package.Pin.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static PreparedPackage PreparePackage(ManagedModuleDownloadResult download)
    {
        var metadata = download.Metadata ??
            throw new InvalidOperationException($"Package metadata is missing for '{download.Name}' version '{download.Version}'.");
        return new PreparedPackage(
            new ManagedModuleRestorePackage(
                ValidateName(metadata.Id), download.Version,
                ManagedModulePackageIntegrity.NormalizeSha256(download.PackageSha256) ??
                    throw new InvalidOperationException($"Package hash is missing for '{download.Name}'.")),
            metadata);
    }

    private static List<PreparedPackage> ValidateClosure(
        ManagedModuleRestoreRequirement[] requirements,
        Dictionary<string, PreparedPackage> prepared,
        bool acceptLicense)
    {
        foreach (var requirement in requirements)
        {
            if (!prepared.TryGetValue(requirement.Name, out var package) || !SameVersion(requirement.Version, package.Pin.Version))
                throw new InvalidOperationException($"Restore is missing required module '{requirement.Name}' version '{requirement.Version}'.");
        }

        foreach (var package in prepared.Values)
        {
            var name = package.Pin.Name;
            if (package.Metadata.RequireLicenseAcceptance && !acceptLicense)
                throw new InvalidOperationException($"'{name}' requires license acceptance. Review the license and pass --accept-license.");
            foreach (var dependency in package.Dependencies)
            {
                if (!prepared.TryGetValue(dependency.Name, out var child) || !dependency.Range.IsSatisfiedBy(child.Pin.Version))
                    throw new InvalidOperationException($"'{name}' requires '{dependency.Name}' {dependency.Range}, but the restore lock has no matching package.");
            }
        }

        if (!TryOrderPackages(requirements, prepared, out var ordered, out var cycle))
            throw new InvalidOperationException($"Restore contains a dependency cycle involving '{cycle}'.");
        if (ordered.Count != prepared.Count)
            throw new InvalidOperationException("The restore lock contains packages outside the required dependency set.");
        return ordered;
    }

    private static bool TryOrderPackages(
        ManagedModuleRestoreRequirement[] requirements,
        Dictionary<string, PreparedPackage> packages,
        out List<PreparedPackage> ordered,
        out string? cycle)
    {
        var result = new List<PreparedPackage>();
        var complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? cycleMember = null;
        ordered = result;

        foreach (var requirement in requirements)
        {
            if (!Visit(requirement.Name))
            {
                cycle = cycleMember;
                return false;
            }
        }

        cycle = null;
        return true;

        bool Visit(string name)
        {
            if (complete.Contains(name))
                return true;
            if (!active.Add(name))
            {
                cycleMember = name;
                return false;
            }

            var package = packages[name];
            foreach (var dependency in package.Dependencies)
            {
                if (!Visit(dependency.Name))
                    return false;
            }

            active.Remove(name);
            complete.Add(name);
            result.Add(package);
            return true;
        }
    }

    private static bool SameVersion(string left, string right)
        => ManagedModuleVersionComparer.Instance.Compare(left, right) == 0;

    private sealed class PreparedPackage(ManagedModuleRestorePackage pin, ManagedModulePackageMetadata metadata)
    {
        public ManagedModuleRestorePackage Pin { get; } = pin;
        public ManagedModulePackageMetadata Metadata { get; } = metadata;
        public DependencyConstraint[] Dependencies { get; } = ManagedModuleInstallService.SelectDependencies(metadata)
            .Select(dependency => new DependencyConstraint(pin.Name, dependency.Id, ManagedModuleVersionRange.Parse(dependency.VersionRange)))
            .ToArray();
    }
}
