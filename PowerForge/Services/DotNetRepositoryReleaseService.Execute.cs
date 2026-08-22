using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    /// <summary>
    /// Executes the repository release workflow.
    /// </summary>
    public DotNetRepositoryReleaseResult Execute(DotNetRepositoryReleaseSpec spec)
        => Execute(spec, signAssemblies: null, validateAssemblySigning: null);

    /// <summary>
    /// Executes the repository release workflow with optional assembly signing callbacks.
    /// </summary>
    public DotNetRepositoryReleaseResult Execute(
        DotNetRepositoryReleaseSpec spec,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies,
        Action<DotNetReleaseBuildAssemblySigningPreflightRequest>? validateAssemblySigning)
        => Execute(spec, signAssemblies, validateAssemblySigning, progress: null);

    internal DotNetRepositoryReleaseResult Execute(
        DotNetRepositoryReleaseSpec spec,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies,
        Action<DotNetReleaseBuildAssemblySigningPreflightRequest>? validateAssemblySigning,
        IProjectBuildProgressReporter? progress,
        CancellationToken cancellationToken = default)
    {
        var result = new DotNetRepositoryReleaseResult();
        var previousCancellationToken = ActiveCancellationToken.Value;
        ActiveCancellationToken.Value = cancellationToken;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (spec is null) throw new ArgumentNullException(nameof(spec));
            if (string.IsNullOrWhiteSpace(spec.RootPath))
            {
                result.Success = false;
                result.ErrorMessage = "RootPath is required.";
                return result;
            }

            var root = Path.GetFullPath(spec.RootPath.Trim().Trim('"'));
            if (!Directory.Exists(root))
            {
                result.Success = false;
                result.ErrorMessage = $"RootPath not found: {root}";
                return result;
            }
            spec.RootPath = root;
            if (!string.IsNullOrWhiteSpace(spec.PublishSource))
                spec.PublishSource = ResolvePublishSource(spec.PublishSource!, root);

            if (!TryResolveSelectedProjectCandidates(spec, _logger, out var candidates, out string? selectionError))
            {
                result.Success = false;
                result.ErrorMessage = selectionError;
                return result;
            }

            var expectedMap = BuildExpectedVersionMap(spec.ExpectedVersionsByProject);
            var projects = new List<DotNetRepositoryProjectResult>();

            foreach (var group in candidates.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Count() > 1)
                {
                    var dupPaths = string.Join("; ", group.Select(g => g.Path));
                    foreach (var item in group)
                    {
                        projects.Add(new DotNetRepositoryProjectResult
                        {
                            ProjectName = item.Name,
                            CsprojPath = item.Path,
                            PackageId = ResolvePackageId(item.Path, item.Name),
                            IsPackable = IsPackable(item.Path),
                            ErrorMessage = $"Duplicate project name found in multiple paths: {dupPaths}. Exclude directories or rename projects."
                        });
                    }
                    result.Success = false;
                    _logger.Warn($"Duplicate project name '{group.Key}' found in multiple paths: {dupPaths}");
                    continue;
                }

                var entry = group.First();
                projects.Add(new DotNetRepositoryProjectResult
                {
                    ProjectName = entry.Name,
                    CsprojPath = entry.Path,
                    PackageId = ResolvePackageId(entry.Path, entry.Name),
                    IsPackable = IsPackable(entry.Path)
                });
            }

            if (projects.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No .csproj files matched the selection criteria.";
                return result;
            }

            foreach (var p in projects)
                result.Projects.Add(p);

            var packable = projects.Where(p => p.IsPackable).ToArray();
            if (packable.Length == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No packable projects were found (IsPackable=false).";
                return result;
            }

            _logger.Info($"Discovered {projects.Count} project(s), {packable.Length} packable.");

            var hasSigningCertificate = spec.Pack && !string.IsNullOrWhiteSpace(spec.CertificateThumbprint);
            var signAssemblyOutputs = hasSigningCertificate && spec.SignAssemblies;
            var signNuGetPackages = hasSigningCertificate && spec.SignPackages;
            string? signingSha256 = null;
            if (signAssemblyOutputs || signNuGetPackages)
            {
                var stamp = string.IsNullOrWhiteSpace(spec.TimeStampServer)
                    ? "http://timestamp.digicert.com"
                    : spec.TimeStampServer!.Trim();
                spec.TimeStampServer = stamp;
            }

            if (signAssemblyOutputs && signAssemblies is null)
            {
                result.Success = false;
                result.ErrorMessage = "Assembly signing was requested, but no assembly signing handler was provided.";
                return result;
            }

            if (signAssemblyOutputs && validateAssemblySigning is null)
            {
                result.Success = false;
                result.ErrorMessage = "Assembly signing was requested, but no assembly signing preflight handler was provided.";
                return result;
            }

            if (signAssemblyOutputs)
            {
                try
                {
                    validateAssemblySigning!(new DotNetReleaseBuildAssemblySigningPreflightRequest
                    {
                        LocalStore = spec.CertificateStore,
                        CertificateThumbprint = spec.CertificateThumbprint!.Trim(),
                        TimeStampServer = spec.TimeStampServer!
                    });
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Assembly signing preflight failed. {ex.Message}";
                    return result;
                }
            }

            if (signNuGetPackages)
            {
                signingSha256 = _getCertificateSha256(spec.CertificateThumbprint!.Trim(), spec.CertificateStore);
                if (signingSha256 is null)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Certificate not found for signing (thumbprint {spec.CertificateThumbprint}).";
                    return result;
                }

                _logger.Info($"Package signing enabled (store {spec.CertificateStore}, thumbprint {spec.CertificateThumbprint}).");
            }
            if (signAssemblyOutputs)
                _logger.Info($"Assembly signing enabled (store {spec.CertificateStore}, thumbprint {spec.CertificateThumbprint}).");

            var expectedGlobal = string.IsNullOrWhiteSpace(spec.ExpectedVersion) ? null : spec.ExpectedVersion!.Trim();
            if (!string.IsNullOrWhiteSpace(expectedGlobal))
                _logger.Info($"Expected version (global): {expectedGlobal}");
            if (expectedMap.Count > 0)
            {
                var mode = spec.ExpectedVersionMapAsInclude ? "include-only" : "override";
                var wildcard = spec.ExpectedVersionMapUseWildcards ? ", wildcards enabled" : string.Empty;
                _logger.Info($"Expected version map: {expectedMap.Count} project(s) ({mode}{wildcard}).");
            }

            PrepareReleaseVersionFloor(packable, expectedGlobal, expectedMap, spec);
            var alignedVersions = ResolveAlignedPackageVersions(packable, expectedGlobal, expectedMap, spec);
            var detailedProgress = progress as IProjectBuildProgressReporterV2;
            var versionItems = CreateVersionProgressItems(packable, detailedProgress);
            progress?.PhaseStarted(ProjectBuildProgressPhase.Versioning, packable.Length, "Resolving project versions");
            var versionProgress = 0;
            var pendingVersionUpdates = new List<KeyValuePair<DotNetRepositoryProjectResult, RepositoryTextFileUpdate>>();
            foreach (var project in packable)
            {
                var versionItem = versionItems[project];
                detailedProgress?.ItemUpdated(versionItem, ProjectBuildProgressItemState.Started, "resolving version");
                progress?.PhaseUpdated(ProjectBuildProgressPhase.Versioning, versionProgress, packable.Length, project.ProjectName);
                var expectedVersion = ResolveExpectedVersion(
                    project.ProjectName,
                    expectedGlobal,
                    expectedMap,
                    spec.ExpectedVersionMapUseWildcards,
                    out var expectedSource);

                if (!string.IsNullOrWhiteSpace(expectedVersion))
                    _logger.Info($"{project.ProjectName}: expected version {expectedVersion} ({expectedSource}).");
                else
                    _logger.Info($"{project.ProjectName}: no expected version; using csproj version.");

                string resolvedVersion;
                string? resolutionWarning;
                try
                {
                    if (spec.PlannedVersionsByProject is not null &&
                        spec.PlannedVersionsByProject.TryGetValue(project.ProjectName, out var plannedVersion))
                    {
                        resolvedVersion = plannedVersion;
                        resolutionWarning = null;
                    }
                    else if (alignedVersions.TryGetValue(project.ProjectName, out var alignedVersion))
                    {
                        resolvedVersion = alignedVersion;
                        resolutionWarning = null;
                    }
                    else
                    {
                        resolvedVersion = ResolveVersion(project, expectedVersion, spec, out resolutionWarning);
                    }

                    resolvedVersion = ApplyReleaseVersionFloor(
                        project,
                        expectedVersion,
                        resolvedVersion,
                        spec);
                }
                catch (Exception ex)
                {
                    project.ErrorMessage = $"Version resolution failed: {ex.Message}";
                    _logger.Warn($"{project.ProjectName}: {project.ErrorMessage}");
                    result.Success = false;
                    detailedProgress?.ItemUpdated(versionItem, ProjectBuildProgressItemState.Failed, project.ErrorMessage);
                    versionProgress++;
                    progress?.PhaseUpdated(ProjectBuildProgressPhase.Versioning, versionProgress, packable.Length, project.ProjectName);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(resolvedVersion))
                {
                    project.ErrorMessage = string.IsNullOrWhiteSpace(resolutionWarning)
                        ? "Unable to resolve a version for the project."
                        : resolutionWarning;
                    _logger.Warn($"{project.ProjectName}: {project.ErrorMessage}");
                    result.Success = false;
                    detailedProgress?.ItemUpdated(versionItem, ProjectBuildProgressItemState.Failed, project.ErrorMessage);
                    versionProgress++;
                    progress?.PhaseUpdated(ProjectBuildProgressPhase.Versioning, versionProgress, packable.Length, project.ProjectName);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(resolutionWarning))
                    _logger.Warn($"{project.ProjectName}: {resolutionWarning}");

                result.ResolvedVersionsByProject[project.ProjectName] = resolvedVersion;
                var shouldUpdateProjectVersion = spec.UpdateVersions &&
                    (alignedVersions.ContainsKey(project.ProjectName) || !string.IsNullOrWhiteSpace(expectedVersion));

                if (CsprojVersionEditor.TryGetVersion(project.CsprojPath, out var oldV) &&
                    PackageVersionUtility.TryNormalizeExact(oldV, out var normalizedOldVersion))
                {
                    project.OldVersion = normalizedOldVersion;
                }
                else if (!shouldUpdateProjectVersion)
                {
                    project.OldVersion = resolvedVersion;
                }
                else if (!string.IsNullOrWhiteSpace(oldV))
                {
                    project.OldVersion = oldV;
                }

                project.NewVersion = resolvedVersion;
                if (!shouldUpdateProjectVersion)
                {
                    detailedProgress?.ItemUpdated(versionItem, ProjectBuildProgressItemState.Completed, resolvedVersion);
                    versionProgress++;
                    progress?.PhaseUpdated(ProjectBuildProgressPhase.Versioning, versionProgress, packable.Length, project.ProjectName);
                    continue;
                }

                var content = File.ReadAllText(project.CsprojPath);
                var updated = CsprojVersionEditor.UpdateVersionText(content, resolvedVersion, out _);

                if (!string.Equals(content, updated, StringComparison.Ordinal))
                {
                    pendingVersionUpdates.Add(new KeyValuePair<DotNetRepositoryProjectResult, RepositoryTextFileUpdate>(
                        project,
                        new RepositoryTextFileUpdate(project.CsprojPath, content, updated)));
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(project.OldVersion))
                        _logger.Info($"{project.ProjectName}: version unchanged ({project.OldVersion}).");
                }

                detailedProgress?.ItemUpdated(versionItem, ProjectBuildProgressItemState.Completed, resolvedVersion);
                versionProgress++;
                progress?.PhaseUpdated(ProjectBuildProgressPhase.Versioning, versionProgress, packable.Length, project.ProjectName);
            }

            if (packable.Any(project => !string.IsNullOrWhiteSpace(project.ErrorMessage)))
                progress?.PhaseFailed(ProjectBuildProgressPhase.Versioning, "One or more project versions could not be resolved");
            else
            {
                var versionBindingService = new ProjectVersionBindingService(_logger);
                var pathComparer = FrameworkCompatibility.GetPathStringComparison(root) == StringComparison.OrdinalIgnoreCase
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal;
                var plannedProjectContents = pendingVersionUpdates.ToDictionary(
                    static item => Path.GetFullPath(item.Value.FilePath),
                    static item => item.Value.UpdatedContent,
                    pathComparer);
                var versionBindingPlan = spec.UpdateVersions
                    ? versionBindingService.Plan(
                        root,
                        result.ResolvedVersionsByProject,
                        spec.VersionBindings,
                        plannedProjectContents)
                    : Array.Empty<ProjectVersionBindingFileUpdate>();

                if (spec.WhatIf)
                {
                    versionBindingService.LogPlanned(versionBindingPlan);
                }
                else
                {
                    var boundPaths = new HashSet<string>(
                        versionBindingPlan.Select(static item => Path.GetFullPath(item.Update.FilePath)),
                        pathComparer);
                    var fileUpdates = pendingVersionUpdates
                        .Where(item => !boundPaths.Contains(Path.GetFullPath(item.Value.FilePath)))
                        .Select(static item => item.Value)
                        .Concat(versionBindingPlan.Select(static item => item.Update))
                        .ToArray();
                    new RepositoryTextFileTransactionService().Apply(fileUpdates);

                    foreach (var pendingUpdate in pendingVersionUpdates)
                    {
                        var project = pendingUpdate.Key;
                        if (!string.IsNullOrWhiteSpace(project.OldVersion))
                            _logger.Success($"{project.ProjectName}: {project.OldVersion} -> {project.NewVersion}");
                        else
                            _logger.Success($"{project.ProjectName}: set version {project.NewVersion}");
                    }

                    versionBindingService.LogApplied(versionBindingPlan);
                }

                progress?.PhaseCompleted(ProjectBuildProgressPhase.Versioning, $"{packable.Length} project version(s) resolved");
            }
            if (spec.Pack)
            {
                progress?.PhaseStarted(ProjectBuildProgressPhase.PackageBuild, packable.Length, "Building and packing projects");
                var packageProgressItems = CreatePackageProgressItems(packable, detailedProgress);
                var packageWatches = new Dictionary<DotNetRepositoryProjectResult, Stopwatch>();
                DotNetPackResult? batchPackResult = null;
                HashSet<DotNetRepositoryProjectResult>? batchCandidateSet = null;
                var batchPackRequested = spec.PackStrategy == DotNetRepositoryPackStrategy.MSBuild && !spec.WhatIf;
                if (batchPackRequested)
                {
                    var batchCandidates = PrepareMsBuildBatchCandidates(packable, _logger);
                    batchCandidateSet = new HashSet<DotNetRepositoryProjectResult>(batchCandidates);
                    if (string.IsNullOrWhiteSpace(spec.OutputPath))
                    {
                        _logger.Warn("MSBuild pack strategy requires OutputPath/StagingPath; falling back to per-project dotnet pack.");
                    }
                    else if (batchCandidates.Length > 0)
                    {
                        _logger.Info($"Packing {batchCandidates.Length} project(s) with MSBuild batch strategy...");
                        StartMsBuildBatchProgress(
                            batchCandidates,
                            packageProgressItems,
                            packageWatches,
                            detailedProgress,
                            progress,
                            packable.Length);
                        batchPackResult = PackProjectsWithMsBuildAndTrackProgress(
                            batchCandidates, spec, _logger, signAssemblyOutputs ? signAssemblies : null,
                            packageProgressItems, packageWatches, detailedProgress);
                        if (!batchPackResult.Success)
                        {
                            var batchError = $"{batchPackResult.ErrorMessage ?? "MSBuild batch pack failed."} (MSBuild batch failed; enable verbose logging to see per-project MSBuild output.)";
                            foreach (var project in batchCandidates)
                                project.ErrorMessage = batchError;

                            result.Success = false;
                            _logger.Warn(batchError);
                            if (spec.PublishFailFast)
                            {
                                CompleteFailedMsBuildBatchProgress(
                                    batchCandidates, packageProgressItems, packageWatches, spec.WhatIf,
                                    detailedProgress, progress, packable.Length);
                                progress?.PhaseFailed(ProjectBuildProgressPhase.PackageBuild, batchError);
                                return result;
                            }
                        }
                        else
                        {
                            _logger.Success($"MSBuild batch pack produced {batchPackResult.Packages.Count} package(s) and {batchPackResult.SymbolPackages.Count} symbol package(s) in {FormatDuration(batchPackResult.Duration)}.");
                        }
                    }
                }
                var packageProgress = 0;
                foreach (var project in packable)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var progressItem = packageProgressItems[project];
                    var packageWatch = GetProjectPackageProgressWatch(
                        project,
                        progressItem,
                        packageWatches,
                        detailedProgress,
                        progress,
                        packageProgress,
                        packable.Length);
                    packageProgress++;
                    try
                    {
                    if (!string.IsNullOrWhiteSpace(project.ErrorMessage))
                    {
                        result.Success = false;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(project.NewVersion))
                    {
                        project.ErrorMessage = "No resolved version for project.";
                        _logger.Warn($"{project.ProjectName}: no resolved version; skipping pack.");
                        result.Success = false;
                        continue;
                    }

                    if (spec.WhatIf)
                    {
                        var planned = ResolvePackagePath(spec, project, project.NewVersion!);
                        if (!string.IsNullOrWhiteSpace(planned))
                            project.Packages.Add(planned!);
                        if (spec.IncludeSymbols)
                        {
                            var plannedSymbols = ResolveSymbolPackagePath(spec, project, project.NewVersion!);
                            if (!string.IsNullOrWhiteSpace(plannedSymbols))
                                project.SymbolPackages.Add(plannedSymbols!);
                        }
                        continue;
                    }

                    var useBatchPackResult = batchPackResult is not null && batchCandidateSet?.Contains(project) == true;

                    if (useBatchPackResult)
                        _logger.Info($"Collecting {project.ProjectName} package(s) from MSBuild batch...");
                    else
                        _logger.Info($"Packing {project.ProjectName}...");

                    var packResult = useBatchPackResult ? batchPackResult! : PackProject(project, spec, _logger, signAssemblyOutputs ? signAssemblies : null);
                    if (!useBatchPackResult && !packResult.Success)
                    {
                        project.ErrorMessage = packResult.ErrorMessage;
                        _logger.Warn($"{project.ProjectName}: pack failed. {packResult.ErrorMessage}");
                        result.Success = false;
                        continue;
                    }

                    // A successful batch result contains all produced packages; narrow it to this project/version.
                    var filtered = FilterPackages(packResult.Packages, project.PackageId, project.NewVersion!);
                    if (filtered.Count == 0)
                    {
                        project.ErrorMessage = $"No packages found for version {project.NewVersion}.";
                        _logger.Warn($"{project.ProjectName}: {project.ErrorMessage}");
                        result.Success = false;
                        if (spec.PublishFailFast)
                            return result;
                        continue;
                    }

                    foreach (var pkg in filtered)
                        project.Packages.Add(pkg);

                    if (spec.IncludeSymbols)
                    {
                        var filteredSymbols = FilterPackages(packResult.SymbolPackages, project.PackageId, project.NewVersion!);
                        foreach (var symbolPackage in filteredSymbols)
                            project.SymbolPackages.Add(symbolPackage);

                        if (filteredSymbols.Count == 0)
                        {
                            project.ErrorMessage = $"No symbol package found for version {project.NewVersion}.";
                            _logger.Warn($"{project.ProjectName}: {project.ErrorMessage}");
                            result.Success = false;
                            if (spec.PublishFailFast)
                                return result;
                            continue;
                        }
                    }

                    var ignored = packResult.Packages.Except(filtered, StringComparer.OrdinalIgnoreCase).ToArray();
                    // In batch mode, ignored packages are normally packages for other batched projects.
                    if (ignored.Length > 0 && batchPackResult is null)
                        _logger.Verbose($"{project.ProjectName}: ignored {ignored.Length} package(s) from other versions.");

                    if (filtered.Count > 0)
                    {
                        var packTiming = batchPackResult is null
                            ? $" in {FormatDuration(packResult.Duration)}"
                            : " from MSBuild batch";
                        _logger.Success($"{project.ProjectName}: package workflow produced {filtered.Count} package(s) and {project.SymbolPackages.Count} symbol package(s){packTiming}.");
                    }

                    if (spec.CreateReleaseZip && !string.IsNullOrWhiteSpace(project.NewVersion))
                    {
                        var zipPath = BuildReleaseZipPath(project, spec);
                        project.ReleaseZipPath = zipPath;
                        if (!spec.WhatIf)
                        {
                            _logger.Info($"Creating {project.ProjectName} release zip...");
                            var zipWatch = Stopwatch.StartNew();
                            if (!TryCreateReleaseZip(project, spec.Configuration, zipPath, _logger, out var zipError, out var zippedFiles, out var zippedBytes))
                            {
                                zipWatch.Stop();
                                project.ErrorMessage = zipError;
                                _logger.Warn($"{project.ProjectName}: {zipError}");
                                result.Success = false;
                                if (spec.PublishFailFast)
                                    return result;
                            }
                            else
                            {
                                zipWatch.Stop();
                                var zipSize = File.Exists(zipPath) ? new FileInfo(zipPath).Length : 0;
                                _logger.Success($"{project.ProjectName}: release zip created in {FormatDuration(zipWatch.Elapsed)} ({zippedFiles} file(s), {FormatBytes(zippedBytes)} input, {FormatBytes(zipSize)} zip).");
                            }
                        }
                    }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (string.IsNullOrWhiteSpace(project.ErrorMessage))
                            project.ErrorMessage = ex.Message;
                        throw;
                    }
                    finally
                    {
                        packageWatch.Stop();
                        CompleteProjectPackageProgress(
                            project,
                            progressItem,
                            packageWatch.Elapsed,
                            spec.WhatIf,
                            cancellationToken.IsCancellationRequested,
                            detailedProgress,
                            progress,
                            packageProgress,
                            packable.Length);
                    }
                }

                progress?.PhaseUpdated(ProjectBuildProgressPhase.PackageBuild, packable.Length, packable.Length, "Package workflow complete");
                if (packable.Any(project => !string.IsNullOrWhiteSpace(project.ErrorMessage)))
                    progress?.PhaseFailed(ProjectBuildProgressPhase.PackageBuild, "One or more project package workflows failed");
                else
                    progress?.PhaseCompleted(ProjectBuildProgressPhase.PackageBuild, $"{packable.Sum(project => project.Packages.Count)} package(s) produced");

                if (!spec.WhatIf &&
                    signNuGetPackages &&
                    signingSha256 is not null &&
                    ExecutePackageSigning(spec, result, packable, signingSha256, progress, detailedProgress))
                {
                    return result;
                }
            }

            if (spec.Publish && ExecuteNuGetPublishing(spec, result, packable, root, progress, detailedProgress))
            {
                return result;
            }

            if (result.ResolvedVersionsByProject.Count > 0)
            {
                var distinct = result.ResolvedVersionsByProject.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (distinct.Count == 1)
                    result.ResolvedVersion = distinct[0];
            }

            SetAggregateProjectError(result, projects);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
        finally
        {
            ActiveCancellationToken.Value = previousCancellationToken;
        }
    }

    internal static string[] ResolveSelectedProjectPaths(DotNetRepositoryReleaseSpec spec)
    {
        if (!TryResolveSelectedProjectCandidates(spec, logger: null, out var candidates, out string? error))
            throw new InvalidOperationException(error);
        return candidates
            .Where(static candidate => IsPackable(candidate.Path))
            .Select(static candidate => candidate.Path)
            .ToArray();
    }

    private static bool TryResolveSelectedProjectCandidates(
        DotNetRepositoryReleaseSpec spec,
        ILogger? logger,
        out List<(string Name, string Path)> candidates,
        out string? error)
    {
        candidates = new List<(string Name, string Path)>();
        error = null;
        var include = BuildNameSet(spec.IncludeProjects);
        var exclude = BuildNameSet(spec.ExcludeProjects);
        var expectedMap = BuildExpectedVersionMap(spec.ExpectedVersionsByProject);
        if (include.Count > 0)
        {
            var includeList = string.Join(", ", include.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            logger?.Info($"Include projects: {includeList}");
        }
        if (exclude.Count > 0)
        {
            var excludeList = string.Join(", ", exclude.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            logger?.Info($"Exclude projects: {excludeList}");
        }

        var enumeration = new ProjectEnumeration(
            rootPath: spec.RootPath,
            kind: ProjectKind.CSharp,
            customExtensions: new[] { "*.csproj" },
            excludeDirectories: BuildExcludeDirectories(spec.ExcludeDirectories));
        foreach (string csproj in ProjectFileEnumerator.Enumerate(enumeration)
                     .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(csproj) ?? csproj;
            if (include.Count > 0 && !include.Contains(name))
                continue;
            if (!exclude.Contains(name))
                candidates.Add((name, csproj));
        }

        if (!spec.ExpectedVersionMapAsInclude)
            return true;
        if (expectedMap.Count == 0)
        {
            error = "ExpectedVersionMapAsInclude is set but ExpectedVersionMap is empty.";
            return false;
        }

        var excludedByMap = new List<string>();
        candidates = candidates.Where(candidate =>
            {
                bool included = MatchesExpectedMap(
                    candidate.Name,
                    expectedMap,
                    spec.ExpectedVersionMapUseWildcards);
                if (!included)
                    excludedByMap.Add(candidate.Name);
                return included;
            })
            .ToList();
        foreach (string pattern in expectedMap.Keys)
        {
            bool any = candidates.Any(candidate => MatchesPattern(
                candidate.Name,
                pattern,
                spec.ExpectedVersionMapUseWildcards));
            if (!any)
                logger?.Warn($"Expected version map entry '{pattern}' did not match any projects.");
        }

        logger?.Info($"Expected version map include-only: {candidates.Count} project(s) matched.");
        if (excludedByMap.Count > 0)
        {
            var distinctExcluded = excludedByMap.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
            logger?.Info($"Excluded by ExpectedVersionMap: {string.Join(", ", distinctExcluded)}");
        }
        return true;
    }

}
