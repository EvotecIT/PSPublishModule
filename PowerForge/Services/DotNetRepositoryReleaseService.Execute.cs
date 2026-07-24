using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
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
        IProjectBuildProgressReporter? progress)
    {
        var result = new DotNetRepositoryReleaseResult();

        try
        {
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

            var include = BuildNameSet(spec.IncludeProjects);
            var exclude = BuildNameSet(spec.ExcludeProjects);
            var expectedMap = BuildExpectedVersionMap(spec.ExpectedVersionsByProject);
            if (include.Count > 0)
            {
                var includeList = string.Join(", ", include.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                _logger.Info($"Include projects: {includeList}");
            }
            if (exclude.Count > 0)
            {
                var excludeList = string.Join(", ", exclude.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                _logger.Info($"Exclude projects: {excludeList}");
            }

            var excludeDirectories = BuildExcludeDirectories(spec.ExcludeDirectories);
            var enumeration = new ProjectEnumeration(
                rootPath: root,
                kind: ProjectKind.CSharp,
                customExtensions: new[] { "*.csproj" },
                excludeDirectories: excludeDirectories);

            var projectFiles = ProjectFileEnumerator.Enumerate(enumeration)
                .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var projects = new List<DotNetRepositoryProjectResult>();
            var candidates = new List<(string Name, string Path)>();
            foreach (var csproj in projectFiles)
            {
                var name = Path.GetFileNameWithoutExtension(csproj) ?? csproj;
                if (include.Count > 0 && !include.Contains(name)) continue;
                if (exclude.Contains(name)) continue;

                candidates.Add((name, csproj));
            }

            if (spec.ExpectedVersionMapAsInclude)
            {
                if (expectedMap.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "ExpectedVersionMapAsInclude is set but ExpectedVersionMap is empty.";
                    return result;
                }

                var excludedByMap = new List<string>();
                var filtered = new List<(string Name, string Path)>();
                foreach (var candidate in candidates)
                {
                    if (MatchesExpectedMap(candidate.Name, expectedMap, spec.ExpectedVersionMapUseWildcards))
                        filtered.Add(candidate);
                    else
                        excludedByMap.Add(candidate.Name);
                }

                var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in expectedMap)
                {
                    var pattern = kvp.Key;
                    bool any = filtered.Any(c => MatchesPattern(c.Name, pattern, spec.ExpectedVersionMapUseWildcards));
                    if (any) matched.Add(pattern);
                    else _logger.Warn($"Expected version map entry '{pattern}' did not match any projects.");
                }

                candidates = filtered;
                _logger.Info($"Expected version map include-only: {candidates.Count} project(s) matched.");
                if (excludedByMap.Count > 0)
                {
                    var distinctExcluded = excludedByMap.Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
                    _logger.Info($"Excluded by ExpectedVersionMap: {string.Join(", ", distinctExcluded)}");
                }
            }

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
            progress?.PhaseStarted(ProjectBuildProgressPhase.Versioning, packable.Length, "Resolving project versions");
            var versionProgress = 0;
            foreach (var project in packable)
            {
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
                    if (alignedVersions.TryGetValue(project.ProjectName, out var alignedVersion))
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
                if (spec.WhatIf || !shouldUpdateProjectVersion)
                {
                    versionProgress++;
                    progress?.PhaseUpdated(ProjectBuildProgressPhase.Versioning, versionProgress, packable.Length, project.ProjectName);
                    continue;
                }

                var content = File.ReadAllText(project.CsprojPath);
                var updated = CsprojVersionEditor.UpdateVersionText(content, resolvedVersion, out _);

                if (!string.Equals(content, updated, StringComparison.Ordinal))
                {
                    File.WriteAllText(project.CsprojPath, updated);
                    if (!string.IsNullOrWhiteSpace(project.OldVersion))
                        _logger.Success($"{project.ProjectName}: {project.OldVersion} -> {resolvedVersion}");
                    else
                        _logger.Success($"{project.ProjectName}: set version {resolvedVersion}");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(project.OldVersion))
                        _logger.Info($"{project.ProjectName}: version unchanged ({project.OldVersion}).");
                }

                versionProgress++;
                progress?.PhaseUpdated(ProjectBuildProgressPhase.Versioning, versionProgress, packable.Length, project.ProjectName);
            }

            if (packable.Any(project => !string.IsNullOrWhiteSpace(project.ErrorMessage)))
                progress?.PhaseFailed(ProjectBuildProgressPhase.Versioning, "One or more project versions could not be resolved");
            else
                progress?.PhaseCompleted(ProjectBuildProgressPhase.Versioning, $"{packable.Length} project version(s) resolved");

            if (spec.Pack)
            {
                progress?.PhaseStarted(ProjectBuildProgressPhase.PackageBuild, packable.Length, "Building and packing projects");
                DotNetPackResult? batchPackResult = null;
                HashSet<DotNetRepositoryProjectResult>? batchCandidateSet = null;
                var batchPackRequested = spec.PackStrategy == DotNetRepositoryPackStrategy.MSBuild && !spec.WhatIf;
                if (batchPackRequested)
                {
                    var batchCandidates = packable
                        .Where(project =>
                            string.IsNullOrWhiteSpace(project.ErrorMessage) &&
                            !string.IsNullOrWhiteSpace(project.NewVersion))
                        .ToArray();
                    var missingVersionCandidates = packable
                        .Where(project =>
                            string.IsNullOrWhiteSpace(project.ErrorMessage) &&
                            string.IsNullOrWhiteSpace(project.NewVersion))
                        .ToArray();
                    batchCandidateSet = new HashSet<DotNetRepositoryProjectResult>(batchCandidates);

                    if (missingVersionCandidates.Length > 0)
                    {
                        var names = string.Join(", ", missingVersionCandidates.Select(project => project.ProjectName));
                        _logger.Warn($"MSBuild batch pack excluded {missingVersionCandidates.Length} project(s) without a resolved version; they will be skipped during pack: {names}");
                        foreach (var project in missingVersionCandidates)
                            project.ErrorMessage = "No resolved version; skipping pack.";
                    }

                    if (string.IsNullOrWhiteSpace(spec.OutputPath))
                    {
                        _logger.Warn("MSBuild pack strategy requires OutputPath/StagingPath; falling back to per-project dotnet pack.");
                    }
                    else if (batchCandidates.Length > 0)
                    {
                        _logger.Info($"Packing {batchCandidates.Length} project(s) with MSBuild batch strategy...");
                        batchPackResult = PackProjectsWithMsBuild(batchCandidates, spec, _logger, signAssemblyOutputs ? signAssemblies : null);
                        if (!batchPackResult.Success)
                        {
                            var batchError = $"{batchPackResult.ErrorMessage ?? "MSBuild batch pack failed."} (MSBuild batch failed; enable verbose logging to see per-project MSBuild output.)";
                            foreach (var project in batchCandidates)
                                project.ErrorMessage = batchError;

                            result.Success = false;
                            _logger.Warn(batchError);
                            if (spec.PublishFailFast)
                                return result;
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
                    progress?.PhaseUpdated(ProjectBuildProgressPhase.PackageBuild, packageProgress, packable.Length, project.ProjectName);
                    packageProgress++;
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
                            if (!TryCreateReleaseZip(project, spec.Configuration, zipPath, out var zipError, out var zippedFiles, out var zippedBytes))
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

                progress?.PhaseUpdated(ProjectBuildProgressPhase.PackageBuild, packable.Length, packable.Length, "Package workflow complete");
                if (packable.Any(project => !string.IsNullOrWhiteSpace(project.ErrorMessage)))
                    progress?.PhaseFailed(ProjectBuildProgressPhase.PackageBuild, "One or more project package workflows failed");
                else
                    progress?.PhaseCompleted(ProjectBuildProgressPhase.PackageBuild, $"{packable.Sum(project => project.Packages.Count)} package(s) produced");

                if (!spec.WhatIf && signNuGetPackages && signingSha256 is not null)
                {
                    var packagesToSign = packable
                        .Where(project => string.IsNullOrWhiteSpace(project.ErrorMessage))
                        .SelectMany(project => project.Packages.Concat(project.SymbolPackages))
                        .Where(package => !string.IsNullOrWhiteSpace(package))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    if (packagesToSign.Length > 0)
                    {
                        progress?.PhaseStarted(ProjectBuildProgressPhase.PackageSigning, packagesToSign.Length, "Signing NuGet packages");
                        _logger.Info($"Signing {packagesToSign.Length} NuGet package(s)...");
                        var signingWatch = Stopwatch.StartNew();
                        if (!_signPackages(packagesToSign, spec, signingSha256, out var signError))
                        {
                            signingWatch.Stop();
                            result.ErrorMessage = signError;
                            _logger.Warn(signError);
                            result.Success = false;
                            MarkPackageSigningFailure(packable, packagesToSign, signError);
                            progress?.PhaseFailed(ProjectBuildProgressPhase.PackageSigning, signError);
                            if (spec.PublishFailFast)
                                return result;
                        }
                        else
                        {
                            signingWatch.Stop();
                            _logger.Success($"Signed {packagesToSign.Length} NuGet package(s) in {FormatDuration(signingWatch.Elapsed)}.");
                            progress?.PhaseCompleted(ProjectBuildProgressPhase.PackageSigning, $"{packagesToSign.Length} package(s) signed");
                        }
                    }
                }
            }

            if (spec.Publish)
            {
                var preflight = ValidatePublishPreflight(packable, spec);
                if (!preflight.Success)
                {
                    result.Success = false;
                    result.ErrorMessage = preflight.ErrorMessage;
                    return result;
                }

                if (string.IsNullOrWhiteSpace(spec.PublishApiKey))
                {
                    result.Success = false;
                    result.ErrorMessage = "PublishApiKey is required when Publish is enabled.";
                    return result;
                }

                var source = string.IsNullOrWhiteSpace(spec.PublishSource)
                    ? "https://api.nuget.org/v3/index.json"
                    : spec.PublishSource!.Trim();
                result.PublishSource = source;

                var orderedProjects = SortProjectsForPublish(packable);
                var publishSymbolsSeparately = spec.IncludeSymbols && IsLocalPublishSource(source);
                var packages = GetPackagesForPublish(orderedProjects, publishSymbolsSeparately).ToArray();

                var packageLookup = orderedProjects
                    .SelectMany(p => (publishSymbolsSeparately
                            ? p.Packages.Concat(p.SymbolPackages)
                            : p.Packages)
                        .Select(pkg => new { Package = pkg, Project = p }))
                    .GroupBy(x => x.Package, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Project, StringComparer.OrdinalIgnoreCase);

                var publishWatch = Stopwatch.StartNew();
                progress?.PhaseStarted(ProjectBuildProgressPhase.NuGetPublish, packages.Length, "Publishing package artifacts");
                var publishProgress = 0;
                foreach (var pkg in packages)
                {
                    progress?.PhaseUpdated(ProjectBuildProgressPhase.NuGetPublish, publishProgress, packages.Length, Path.GetFileName(pkg));
                    packageLookup.TryGetValue(pkg, out var project);
                    var publishedArtifacts = GetPublishedArtifacts(
                        project,
                        pkg,
                        includeCompanionSymbols: !publishSymbolsSeparately);
                    if (spec.WhatIf)
                    {
                        result.PublishedPackages.AddRange(publishedArtifacts);
                        continue;
                    }

                    if (publishSymbolsSeparately &&
                        !CanPublishSymbolPackage(
                            pkg,
                            (IEnumerable<string>?)project?.Packages ?? Array.Empty<string>(),
                            primaryPackage =>
                                result.PublishedPackages.Contains(primaryPackage, StringComparer.OrdinalIgnoreCase) ||
                                result.SkippedDuplicatePackages.Contains(primaryPackage, StringComparer.OrdinalIgnoreCase),
                            out var primaryPackage))
                    {
                        var blockedResult = CreateBlockedCompanionResult(pkg, primaryPackage);
                        result.Success = false;
                        result.FailedPackages.Add(pkg);
                        _logger.Warn(blockedResult.Message!);
                        if (project is not null && string.IsNullOrWhiteSpace(project.ErrorMessage))
                            project.ErrorMessage = blockedResult.Message;
                        if (spec.PublishFailFast)
                        {
                            result.ErrorMessage = blockedResult.Message;
                            return result;
                        }

                        continue;
                    }

                    _logger.Info($"Publishing {Path.GetFileName(pkg)}...");
                    var packagePublishWatch = Stopwatch.StartNew();
                    spec.RemotePublishAttempted?.Invoke();
                    var pushResult = PushPackage(
                        pkg,
                        spec.PublishApiKey!,
                        source,
                        spec.SkipDuplicate,
                        suppressCompanionSymbols: !spec.IncludeSymbols || publishSymbolsSeparately,
                        workingDirectory: root);
                    packagePublishWatch.Stop();
                    var artifactOutcomes = ClassifyPublishedArtifacts(
                        publishedArtifacts,
                        pushResult,
                        spec.SkipDuplicate);
                    foreach (var artifact in publishedArtifacts)
                    {
                        switch (artifactOutcomes[artifact])
                        {
                            case PackagePushOutcome.SkippedDuplicate:
                                result.SkippedDuplicatePackages.Add(artifact);
                                _logger.Info($"Skipped duplicate {Path.GetFileName(artifact)} in {FormatDuration(packagePublishWatch.Elapsed)}; package already exists in the feed.");
                                break;
                            case PackagePushOutcome.Published:
                                result.PublishedPackages.Add(artifact);
                                _logger.Success($"Published {Path.GetFileName(artifact)} in {FormatDuration(packagePublishWatch.Elapsed)}.");
                                break;
                            default:
                                result.FailedPackages.Add(artifact);
                                _logger.Warn($"NuGet push failed for {artifact} after {FormatDuration(packagePublishWatch.Elapsed)}: {pushResult.Message}");
                                break;
                        }
                    }

                    var failedArtifacts = publishedArtifacts
                        .Where(artifact => artifactOutcomes[artifact] == PackagePushOutcome.Failed)
                        .ToArray();
                    if (failedArtifacts.Length > 0)
                    {
                        result.Success = false;
                        var error = pushResult.Message;
                        if (project is not null && string.IsNullOrWhiteSpace(project.ErrorMessage))
                            project.ErrorMessage = $"Publish failed for {string.Join(", ", failedArtifacts.Select(Path.GetFileName))}: {error}";
                        if (spec.PublishFailFast)
                        {
                            if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                                result.ErrorMessage = $"Publish failed for {string.Join(", ", failedArtifacts.Select(Path.GetFileName))}.";
                            return result;
                        }
                    }
                    publishProgress++;
                }
                publishWatch.Stop();
                var publishSummary = spec.WhatIf
                    ? $"NuGet publish plan prepared in {FormatDuration(publishWatch.Elapsed)} ({result.PublishedPackages.Count} package artifact(s) would be published)."
                    : $"NuGet publish phase completed in {FormatDuration(publishWatch.Elapsed)} ({result.PublishedPackages.Count} published, {result.SkippedDuplicatePackages.Count} skipped duplicate, {result.FailedPackages.Count} failed).";
                if (result.FailedPackages.Count == 0)
                {
                    _logger.Success(publishSummary);
                    progress?.PhaseCompleted(ProjectBuildProgressPhase.NuGetPublish, publishSummary);
                }
                else
                {
                    _logger.Warn(publishSummary);
                    progress?.PhaseFailed(ProjectBuildProgressPhase.NuGetPublish, publishSummary);
                }
            }

            if (result.ResolvedVersionsByProject.Count > 0)
            {
                var distinct = result.ResolvedVersionsByProject.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (distinct.Count == 1)
                    result.ResolvedVersion = distinct[0];
            }

            var projectErrors = projects
                .Where(p => !string.IsNullOrWhiteSpace(p.ErrorMessage))
                .Select(p => $"{p.ProjectName}: {p.ErrorMessage}")
                .ToArray();
            if (projectErrors.Length > 0 && string.IsNullOrWhiteSpace(result.ErrorMessage))
                result.ErrorMessage = "One or more projects failed: " + string.Join("; ", projectErrors);

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private static void MarkPackageSigningFailure(
        IEnumerable<DotNetRepositoryProjectResult> projects,
        IReadOnlyList<string> packagesToSign,
        string signError)
    {
        var failedPackages = new HashSet<string>(
            packagesToSign.Where(package => !string.IsNullOrWhiteSpace(package)),
            StringComparer.OrdinalIgnoreCase);

        if (failedPackages.Count == 0)
            return;

        var message = string.IsNullOrWhiteSpace(signError)
            ? "Package signing failed."
            : $"Package signing failed: {signError}";

        foreach (var project in projects)
        {
            if (!string.IsNullOrWhiteSpace(project.ErrorMessage))
                continue;

            if (project.Packages.Concat(project.SymbolPackages).Any(package => failedPackages.Contains(package)))
                project.ErrorMessage = message;
        }
    }

}
