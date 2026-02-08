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

}
