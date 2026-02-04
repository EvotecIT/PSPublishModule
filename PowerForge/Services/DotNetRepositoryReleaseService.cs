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

/// <summary>
/// Repository-wide release workflow for .NET packages (discover, version, pack, publish).
/// </summary>
public sealed class DotNetRepositoryReleaseService
{
    private readonly ILogger _logger;
    private readonly NuGetPackageVersionResolver _resolver;
    private static readonly string[] DefaultExcludeDirectories =
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules", "packages",
        "Artifacts", "Artefacts", "TestResults", "Ignore"
    };

    /// <summary>
    /// Creates a new repository release service.
    /// </summary>
    public DotNetRepositoryReleaseService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolver = new NuGetPackageVersionResolver(_logger);
    }

    /// <summary>
    /// Executes the repository release workflow.
    /// </summary>
    public DotNetRepositoryReleaseResult Execute(DotNetRepositoryReleaseSpec spec)
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

            string? signingSha256 = null;
            if (spec.Pack && !string.IsNullOrWhiteSpace(spec.CertificateThumbprint))
            {
                var stamp = string.IsNullOrWhiteSpace(spec.TimeStampServer)
                    ? "http://timestamp.digicert.com"
                    : spec.TimeStampServer!.Trim();
                spec.TimeStampServer = stamp;

                signingSha256 = GetCertificateSha256(spec.CertificateThumbprint!.Trim(), spec.CertificateStore);
                if (signingSha256 is null)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Certificate not found for signing (thumbprint {spec.CertificateThumbprint}).";
                    return result;
                }

                _logger.Info($"Package signing enabled (store {spec.CertificateStore}, thumbprint {spec.CertificateThumbprint}).");
            }

            var expectedGlobal = string.IsNullOrWhiteSpace(spec.ExpectedVersion) ? null : spec.ExpectedVersion!.Trim();
            if (!string.IsNullOrWhiteSpace(expectedGlobal))
                _logger.Info($"Expected version (global): {expectedGlobal}");
            if (expectedMap.Count > 0)
            {
                var mode = spec.ExpectedVersionMapAsInclude ? "include-only" : "override";
                var wildcard = spec.ExpectedVersionMapUseWildcards ? ", wildcards enabled" : string.Empty;
                _logger.Info($"Expected version map: {expectedMap.Count} project(s) ({mode}{wildcard}).");
            }
            foreach (var project in packable)
            {
                var expectedVersion = expectedGlobal;
                var expectedSource = "global";
                if (expectedMap.TryGetValue(project.ProjectName, out var overrideVersion) && !string.IsNullOrWhiteSpace(overrideVersion))
                {
                    expectedVersion = overrideVersion;
                    expectedSource = "per-project";
                }
                else if (string.IsNullOrWhiteSpace(expectedGlobal))
                {
                    expectedSource = "csproj";
                }

                if (!string.IsNullOrWhiteSpace(expectedVersion))
                    _logger.Info($"{project.ProjectName}: expected version {expectedVersion} ({expectedSource}).");
                else
                    _logger.Info($"{project.ProjectName}: no expected version; using csproj version.");

                string resolvedVersion;
                string? resolutionWarning;
                try
                {
                    resolvedVersion = ResolveVersion(project, expectedVersion, spec, out resolutionWarning);
                }
                catch (Exception ex)
                {
                    project.ErrorMessage = $"Version resolution failed: {ex.Message}";
                    _logger.Warn($"{project.ProjectName}: {project.ErrorMessage}");
                    result.Success = false;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(resolvedVersion))
                {
                    project.ErrorMessage = string.IsNullOrWhiteSpace(resolutionWarning)
                        ? "Unable to resolve a version for the project."
                        : resolutionWarning;
                    _logger.Warn($"{project.ProjectName}: {project.ErrorMessage}");
                    result.Success = false;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(resolutionWarning))
                    _logger.Warn($"{project.ProjectName}: {resolutionWarning}");

                result.ResolvedVersionsByProject[project.ProjectName] = resolvedVersion;

                if (CsprojVersionEditor.TryGetVersion(project.CsprojPath, out var oldV))
                    project.OldVersion = oldV;

                project.NewVersion = resolvedVersion;
                if (spec.WhatIf || !spec.UpdateVersions) continue;

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
            }

            if (spec.Pack)
            {
                foreach (var project in packable)
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
                        continue;
                    }

                    _logger.Info($"Packing {project.ProjectName}...");
                    var packResult = PackProject(project, spec);
                    if (!packResult.Success)
                    {
                        project.ErrorMessage = packResult.ErrorMessage;
                        _logger.Warn($"{project.ProjectName}: pack failed. {packResult.ErrorMessage}");
                        result.Success = false;
                        continue;
                    }

                    var filtered = FilterPackages(packResult.Packages, project.ProjectName, project.NewVersion!);
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

                    var ignored = packResult.Packages.Except(filtered, StringComparer.OrdinalIgnoreCase).ToArray();
                    if (ignored.Length > 0)
                        _logger.Verbose($"{project.ProjectName}: ignored {ignored.Length} package(s) from other versions.");

                    if (filtered.Count > 0)
                        _logger.Success($"{project.ProjectName}: packed {filtered.Count} package(s).");

                    if (signingSha256 is not null)
                    {
                        if (project.Packages.Count == 0)
                        {
                            project.ErrorMessage = "No packages to sign.";
                            _logger.Warn($"{project.ProjectName}: {project.ErrorMessage}");
                            result.Success = false;
                            if (spec.PublishFailFast)
                                return result;
                            continue;
                        }

                        _logger.Info($"Signing {project.ProjectName} package(s)...");
                        if (!SignPackages(project.Packages, spec, signingSha256, out var signError))
                        {
                            project.ErrorMessage = signError;
                            _logger.Warn($"{project.ProjectName}: {signError}");
                            result.Success = false;
                            if (spec.PublishFailFast)
                                return result;
                        }
                        else
                        {
                            _logger.Success($"{project.ProjectName}: signed {project.Packages.Count} package(s).");
                        }
                    }

                    if (spec.CreateReleaseZip && !string.IsNullOrWhiteSpace(project.NewVersion))
                    {
                        var zipPath = BuildReleaseZipPath(project, spec);
                        project.ReleaseZipPath = zipPath;
                        if (!spec.WhatIf)
                        {
                            if (!TryCreateReleaseZip(project, spec.Configuration, zipPath, out var zipError))
                            {
                                project.ErrorMessage = zipError;
                                _logger.Warn($"{project.ProjectName}: {zipError}");
                                result.Success = false;
                                if (spec.PublishFailFast)
                                    return result;
                            }
                            else
                            {
                                _logger.Success($"{project.ProjectName}: release zip created.");
                            }
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

                var orderedProjects = SortProjectsForPublish(packable);
                var packages = orderedProjects.SelectMany(p => p.Packages)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var packageLookup = orderedProjects
                    .SelectMany(p => p.Packages.Select(pkg => new { Package = pkg, Project = p }))
                    .GroupBy(x => x.Package, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Project, StringComparer.OrdinalIgnoreCase);

                foreach (var pkg in packages)
                {
                    if (spec.WhatIf)
                    {
                        result.PublishedPackages.Add(pkg);
                        continue;
                    }

                    _logger.Info($"Publishing {Path.GetFileName(pkg)}...");
                    var push = PushPackage(pkg, spec.PublishApiKey!, source, spec.SkipDuplicate, out var error);
                    if (push)
                    {
                        result.PublishedPackages.Add(pkg);
                        _logger.Success($"Published {Path.GetFileName(pkg)}.");
                    }
                    else
                    {
                        result.Success = false;
                        _logger.Warn($"NuGet push failed for {pkg}: {error}");
                        if (packageLookup.TryGetValue(pkg, out var project) && string.IsNullOrWhiteSpace(project.ErrorMessage))
                            project.ErrorMessage = $"Publish failed for {Path.GetFileName(pkg)}: {error}";
                        if (spec.PublishFailFast)
                        {
                            if (string.IsNullOrWhiteSpace(result.ErrorMessage))
                                result.ErrorMessage = $"Publish failed for {Path.GetFileName(pkg)}.";
                            return result;
                        }
                    }
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

    private string ResolveVersion(
        DotNetRepositoryProjectResult project,
        string? expectedVersion,
        DotNetRepositoryReleaseSpec spec,
        out string? warning)
    {
        warning = null;

        if (!spec.UpdateVersions)
        {
            if (CsprojVersionEditor.TryGetVersion(project.CsprojPath, out var v))
                return v;
            warning = "UpdateVersions is disabled and no version tags were found in the project file.";
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            if (CsprojVersionEditor.TryGetVersion(project.CsprojPath, out var v))
                return v;
            warning = "No expected version provided and no version tags were found in the project file.";
            return string.Empty;
        }

        if (Version.TryParse(expectedVersion, out var exact))
            return exact.ToString();

        var current = _resolver.ResolveLatest(
            packageId: project.ProjectName,
            sources: spec.VersionSources,
            credential: spec.VersionSourceCredential,
            includePrerelease: spec.IncludePrerelease);

        if (current is null)
            warning = $"No current package version found; using 0 baseline for '{expectedVersion}'.";

        return VersionPatternStepper.Step(expectedVersion!, current);
    }

    private static DotNetPackResult PackProject(DotNetRepositoryProjectResult project, DotNetRepositoryReleaseSpec spec)
    {
        var result = new DotNetPackResult();

        var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();

        string? outputPath = null;
        if (!string.IsNullOrWhiteSpace(spec.OutputPath))
        {
            outputPath = Path.IsPathRooted(spec.OutputPath)
                ? spec.OutputPath
                : Path.GetFullPath(Path.Combine(spec.RootPath, spec.OutputPath));
            Directory.CreateDirectory(outputPath);
        }

        var exitCode = RunDotnetPack(project.CsprojPath, csprojDir, configuration, outputPath, out var stdErr, out var stdOut);
        if (exitCode != 0)
        {
            result.ErrorMessage = $"dotnet pack failed for {project.ProjectName} (exit {exitCode}). {stdErr}".Trim();
            return result;
        }

        var packageRoot = outputPath ?? Path.Combine(csprojDir, "bin", configuration);
        if (Directory.Exists(packageRoot))
        {
            var pkgs = Directory.EnumerateFiles(packageRoot, "*.nupkg", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            result.Packages.AddRange(pkgs);
        }

        result.Success = true;
        return result;
    }

    private static string? ResolvePackagePath(DotNetRepositoryReleaseSpec spec, DotNetRepositoryProjectResult project, string version)
    {
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var outputPath = string.IsNullOrWhiteSpace(spec.OutputPath)
            ? Path.Combine(Path.GetDirectoryName(project.CsprojPath) ?? string.Empty, "bin", configuration)
            : (Path.IsPathRooted(spec.OutputPath)
                ? spec.OutputPath
                : Path.Combine(spec.RootPath, spec.OutputPath));

        if (string.IsNullOrWhiteSpace(outputPath)) return null;
        return Path.Combine(outputPath, $"{project.ProjectName}.{version}.nupkg");
    }

    private static int RunDotnetPack(string csproj, string workingDirectory, string configuration, string? outputPath, out string stdErr, out string stdOut)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

#if NET472
        var args = new List<string> { "pack", csproj, "--configuration", configuration };
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            args.Add("-o");
            args.Add(outputPath!);
        }
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("pack");
        psi.ArgumentList.Add(csproj);
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(configuration);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputPath!);
        }
#endif

        using var p = Process.Start(psi);
        if (p is null) return 1;
        stdOut = p.StandardOutput.ReadToEnd();
        stdErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    private static bool PushPackage(string packagePath, string apiKey, string source, bool skipDuplicate, out string error)
    {
        error = string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

#if NET472
        var args = new List<string>
        {
            "nuget", "push", packagePath,
            "--api-key", apiKey,
            "--source", source
        };
        if (skipDuplicate) args.Add("--skip-duplicate");
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("nuget");
        psi.ArgumentList.Add("push");
        psi.ArgumentList.Add(packagePath);
        psi.ArgumentList.Add("--api-key");
        psi.ArgumentList.Add(apiKey);
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(source);
        if (skipDuplicate) psi.ArgumentList.Add("--skip-duplicate");
#endif

        using var p = Process.Start(psi);
        if (p is null) { error = "Failed to start dotnet."; return false; }
        var stdOut = p.StandardOutput.ReadToEnd();
        var stdErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode == 0) return true;

        error = string.Join(Environment.NewLine, stdErr, stdOut).Trim();
        return false;
    }

    private static HashSet<string> BuildNameSet(IEnumerable<string>? items)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (items is null) return set;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            set.Add(item.Trim());
        }
        return set;
    }

    private static IReadOnlyList<string> BuildExcludeDirectories(IEnumerable<string>? items)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in DefaultExcludeDirectories)
            set.Add(dir);

        if (items is not null)
        {
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                set.Add(item.Trim());
            }
        }

        return set.ToArray();
    }

    private (bool Success, string? ErrorMessage) ValidatePublishPreflight(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec)
    {
        foreach (var project in projects)
        {
            if (!string.IsNullOrWhiteSpace(project.ErrorMessage))
                return (false, $"Publish preflight failed: {project.ProjectName} has errors: {project.ErrorMessage}");

            if (string.IsNullOrWhiteSpace(project.NewVersion))
                return (false, $"Publish preflight failed: {project.ProjectName} has no resolved version.");

            if (project.Packages.Count == 0)
                return (false, $"Publish preflight failed: {project.ProjectName} has no packages to publish.");

            foreach (var pkg in project.Packages)
            {
                if (!File.Exists(pkg))
                    return (false, $"Publish preflight failed: package not found: {pkg}");
            }

            var latest = _resolver.ResolveLatest(
                packageId: project.ProjectName,
                sources: spec.VersionSources,
                credential: spec.VersionSourceCredential,
                includePrerelease: spec.IncludePrerelease);

            if (latest is not null && Version.TryParse(project.NewVersion, out var target))
            {
                if (latest >= target && !spec.SkipDuplicate)
                    return (false, $"Publish preflight failed: {project.ProjectName} version {target} already exists (latest {latest}). Use -SkipDuplicate to allow.");
            }
        }

        return (true, null);
    }

    private IReadOnlyList<DotNetRepositoryProjectResult> SortProjectsForPublish(IReadOnlyList<DotNetRepositoryProjectResult> projects)
    {
        var byName = projects.ToDictionary(p => p.ProjectName, StringComparer.OrdinalIgnoreCase);
        var deps = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            deps[project.ProjectName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var doc = XDocument.Load(project.CsprojPath);
                foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase)))
                {
                    var include = pr.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(include)) continue;

                    var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
                    var depPath = Path.GetFullPath(Path.Combine(csprojDir, include));
                    var depName = Path.GetFileNameWithoutExtension(depPath);
                    if (string.IsNullOrWhiteSpace(depName)) continue;
                    if (byName.ContainsKey(depName))
                        deps[project.ProjectName].Add(depName);
                }
            }
            catch
            {
                // Ignore dependency parsing errors; fall back to original order.
            }
        }

        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
            inDegree[p.ProjectName] = 0;

        foreach (var kvp in deps)
        {
            foreach (var dep in kvp.Value)
            {
                if (inDegree.ContainsKey(dep))
                    inDegree[dep]++;
            }
        }

        var queue = new Queue<string>(inDegree.Where(k => k.Value == 0).Select(k => k.Key));
        var ordered = new List<DotNetRepositoryProjectResult>();

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (byName.TryGetValue(name, out var proj))
                ordered.Add(proj);

            if (!deps.TryGetValue(name, out var children)) continue;
            foreach (var child in children)
            {
                if (!inDegree.ContainsKey(child)) continue;
                inDegree[child]--;
                if (inDegree[child] == 0)
                    queue.Enqueue(child);
            }
        }

        if (ordered.Count != projects.Count)
        {
            _logger.Warn("Publish order dependency analysis detected a cycle; falling back to name order.");
            return projects.OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return ordered;
    }

    private static Dictionary<string, string> BuildExpectedVersionMap(Dictionary<string, string>? map)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (map is null) return result;

        foreach (var kvp in map)
        {
            if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
            if (string.IsNullOrWhiteSpace(kvp.Value)) continue;
            result[kvp.Key.Trim()] = kvp.Value.Trim();
        }

        return result;
    }

    private static bool IsPackable(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            var value = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("IsPackable", StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrWhiteSpace(value)) return true;
            return !string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static string BuildReleaseZipPath(DotNetRepositoryProjectResult project, DotNetRepositoryReleaseSpec spec)
    {
        var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var cfg = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var releasePath = string.IsNullOrWhiteSpace(spec.ReleaseZipOutputPath)
            ? Path.Combine(csprojDir, "bin", cfg)
            : spec.ReleaseZipOutputPath!;
        var version = string.IsNullOrWhiteSpace(project.NewVersion) ? "0.0.0" : project.NewVersion;
        return Path.Combine(releasePath, $"{project.ProjectName}.{version}.zip");
    }

    private static bool TryCreateReleaseZip(
        DotNetRepositoryProjectResult project,
        string configuration,
        string zipPath,
        out string error)
    {
        error = string.Empty;
        var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var cfg = string.IsNullOrWhiteSpace(configuration) ? "Release" : configuration.Trim();
        var releasePath = Path.Combine(csprojDir, "bin", cfg);

        if (!Directory.Exists(releasePath))
        {
            error = $"Release path not found: {releasePath}";
            return false;
        }

        try
        {
            var zipDir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrWhiteSpace(zipDir))
                Directory.CreateDirectory(zipDir);

            if (File.Exists(zipPath)) File.Delete(zipPath);
            var zipFull = Path.GetFullPath(zipPath);

            using var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);

            foreach (var file in Directory.EnumerateFiles(releasePath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(file), zipFull, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                catch { }

                if (file.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rel = ComputeRelativePath(releasePath, file);
                var entry = archive.CreateEntry(rel, System.IO.Compression.CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                fileStream.CopyTo(entryStream);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to create release zip: {ex.Message}";
            return false;
        }
    }

    private static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(baseDir)));
            var pathUri = new Uri(Path.GetFullPath(fullPath));
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return Path.GetFileName(fullPath) ?? fullPath;
        }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static List<string> FilterPackages(IEnumerable<string> packages, string projectName, string version)
    {
        var list = new List<string>();
        if (packages is null) return list;
        if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(version))
            return list;

        var prefix = $"{projectName}.{version}.";
        foreach (var pkg in packages)
        {
            var name = Path.GetFileName(pkg) ?? string.Empty;
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                list.Add(pkg);
        }

        return list;
    }

    private static bool SignPackages(
        IReadOnlyList<string> packages,
        DotNetRepositoryReleaseSpec spec,
        string sha256,
        out string error)
    {
        error = string.Empty;
        if (packages is null || packages.Count == 0) return true;

        var store = spec.CertificateStore == CertificateStoreLocation.LocalMachine ? "LocalMachine" : "CurrentUser";
        var timeStampServer = string.IsNullOrWhiteSpace(spec.TimeStampServer) ? "http://timestamp.digicert.com" : spec.TimeStampServer!.Trim();

        foreach (var pkg in packages)
        {
            var exitCode = RunDotnetSign(pkg, sha256, store, timeStampServer, out var stdErr, out var stdOut);
            if (exitCode == 0) continue;

            var msg = string.Join(Environment.NewLine, stdErr, stdOut).Trim();
            error = $"Signing failed for {Path.GetFileName(pkg)}. {msg}".Trim();
            return false;
        }

        return true;
    }

    private static int RunDotnetSign(
        string packagePath,
        string sha256,
        string store,
        string timeStampServer,
        out string stdErr,
        out string stdOut)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

#if NET472
        var args = new List<string>
        {
            "nuget", "sign", packagePath,
            "--certificate-fingerprint", sha256,
            "--certificate-store-location", store,
            "--certificate-store-name", "My",
            "--timestamper", timeStampServer,
            "--overwrite"
        };
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("nuget");
        psi.ArgumentList.Add("sign");
        psi.ArgumentList.Add(packagePath);
        psi.ArgumentList.Add("--certificate-fingerprint");
        psi.ArgumentList.Add(sha256);
        psi.ArgumentList.Add("--certificate-store-location");
        psi.ArgumentList.Add(store);
        psi.ArgumentList.Add("--certificate-store-name");
        psi.ArgumentList.Add("My");
        psi.ArgumentList.Add("--timestamper");
        psi.ArgumentList.Add(timeStampServer);
        psi.ArgumentList.Add("--overwrite");
#endif

        using var p = Process.Start(psi);
        if (p is null) return 1;
        stdOut = p.StandardOutput.ReadToEnd();
        stdErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    private static string? GetCertificateSha256(string thumbprint, CertificateStoreLocation storeLocation)
    {
        try
        {
            var loc = storeLocation == CertificateStoreLocation.LocalMachine ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;
            using var store = new X509Store(StoreName.My, loc);
            store.Open(OpenFlags.ReadOnly);
            var cert = store.Certificates.Cast<X509Certificate2>()
                .FirstOrDefault(c => NormalizeThumbprint(c.Thumbprint) == NormalizeThumbprint(thumbprint));
            if (cert is null) return null;
#if NET472
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(cert.RawData);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
#else
            return cert.GetCertHashString(HashAlgorithmName.SHA256);
#endif
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeThumbprint(string? thumbprint)
        => (thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

    private static bool MatchesExpectedMap(string projectName, Dictionary<string, string> expectedMap, bool allowWildcards)
    {
        foreach (var kvp in expectedMap)
        {
            if (MatchesPattern(projectName, kvp.Key, allowWildcards))
                return true;
        }

        return false;
    }

    private static bool MatchesPattern(string value, string pattern, bool allowWildcards)
    {
        if (!allowWildcards || string.IsNullOrWhiteSpace(pattern))
            return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);

        if (!ContainsWildcard(pattern))
            return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
    }

    private static bool ContainsWildcard(string value)
        => value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;

    private sealed class DotNetPackResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> Packages { get; } = new();
    }

#if NET472
    private static string BuildWindowsArgumentString(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(EscapeWindowsArgument));

    // Based on .NET's internal ProcessStartInfo quoting behavior for Windows CreateProcess.
    private static string EscapeWindowsArgument(string arg)
    {
        if (arg is null) return "\"\"";
        if (arg.Length == 0) return "\"\"";

        bool needsQuotes = arg.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!needsQuotes) return arg;

        var sb = new System.Text.StringBuilder();
        sb.Append('"');

        int backslashCount = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                sb.Append('\\', backslashCount);
                backslashCount = 0;
            }

            sb.Append(ch);
        }

        if (backslashCount > 0)
            sb.Append('\\', backslashCount * 2);

        sb.Append('"');
        return sb.ToString();
    }
#endif
}
