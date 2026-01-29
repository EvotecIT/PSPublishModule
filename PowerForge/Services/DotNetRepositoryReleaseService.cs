using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Repository-wide release workflow for .NET packages (discover, version, pack, publish).
/// </summary>
public sealed class DotNetRepositoryReleaseService
{
    private readonly ILogger _logger;
    private readonly NuGetPackageVersionResolver _resolver;

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

            var enumeration = new ProjectEnumeration(
                rootPath: root,
                kind: ProjectKind.CSharp,
                customExtensions: new[] { "*.csproj" },
                excludeDirectories: spec.ExcludeDirectories);

            var projectFiles = ProjectFileEnumerator.Enumerate(enumeration)
                .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var projects = new List<DotNetRepositoryProjectResult>();
            foreach (var csproj in projectFiles)
            {
                var name = Path.GetFileNameWithoutExtension(csproj) ?? csproj;
                if (include.Count > 0 && !include.Contains(name)) continue;
                if (exclude.Contains(name)) continue;

                projects.Add(new DotNetRepositoryProjectResult
                {
                    ProjectName = name,
                    CsprojPath = csproj,
                    IsPackable = IsPackable(csproj)
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

            var expected = string.IsNullOrWhiteSpace(spec.ExpectedVersion) ? null : spec.ExpectedVersion!.Trim();
            var resolvedVersion = ResolveVersion(expected, packable, spec);
            if (string.IsNullOrWhiteSpace(resolvedVersion))
            {
                result.Success = false;
                result.ErrorMessage = "Unable to resolve a version for the release.";
                return result;
            }

            result.ResolvedVersion = resolvedVersion;

            foreach (var project in packable)
            {
                if (CsprojVersionEditor.TryGetVersion(project.CsprojPath, out var oldV))
                    project.OldVersion = oldV;

                project.NewVersion = resolvedVersion;
                if (spec.WhatIf) continue;

                var content = File.ReadAllText(project.CsprojPath);
                var updated = CsprojVersionEditor.UpdateVersionText(content, resolvedVersion, out _);

                if (!string.Equals(content, updated, StringComparison.Ordinal))
                    File.WriteAllText(project.CsprojPath, updated);
            }

            if (spec.Pack)
            {
                foreach (var project in packable)
                {
                    if (spec.WhatIf)
                    {
                        if (!string.IsNullOrWhiteSpace(resolvedVersion))
                        {
                            var planned = ResolvePackagePath(spec, project, resolvedVersion);
                            if (!string.IsNullOrWhiteSpace(planned))
                                project.Packages.Add(planned!);
                        }
                        continue;
                    }

                    var packResult = PackProject(project, spec);
                    if (!packResult.Success)
                    {
                        project.ErrorMessage = packResult.ErrorMessage;
                        result.Success = false;
                        continue;
                    }

                    foreach (var pkg in packResult.Packages)
                        project.Packages.Add(pkg);
                }
            }

            if (spec.Publish)
            {
                if (string.IsNullOrWhiteSpace(spec.PublishApiKey))
                {
                    result.Success = false;
                    result.ErrorMessage = "PublishApiKey is required when Publish is enabled.";
                    return result;
                }

                var source = string.IsNullOrWhiteSpace(spec.PublishSource)
                    ? "https://api.nuget.org/v3/index.json"
                    : spec.PublishSource!.Trim();

                var packages = projects.SelectMany(p => p.Packages)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var pkg in packages)
                {
                    if (spec.WhatIf)
                    {
                        result.PublishedPackages.Add(pkg);
                        continue;
                    }

                    var push = PushPackage(pkg, spec.PublishApiKey!, source, spec.SkipDuplicate, out var error);
                    if (push)
                        result.PublishedPackages.Add(pkg);
                    else
                    {
                        result.Success = false;
                        _logger.Warn($"NuGet push failed for {pkg}: {error}");
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private string ResolveVersion(string? expectedVersion, DotNetRepositoryProjectResult[] projects, DotNetRepositoryReleaseSpec spec)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            var first = projects.FirstOrDefault();
            if (first != null && CsprojVersionEditor.TryGetVersion(first.CsprojPath, out var v))
                return v;
            return string.Empty;
        }

        if (Version.TryParse(expectedVersion, out var exact))
            return exact.ToString();

        Version? current = null;
        foreach (var p in projects)
        {
            var latest = _resolver.ResolveLatest(
                packageId: p.ProjectName,
                sources: spec.VersionSources,
                credential: spec.VersionSourceCredential,
                includePrerelease: spec.IncludePrerelease);

            if (latest is null) continue;
            if (current is null || latest > current) current = latest;
        }

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
