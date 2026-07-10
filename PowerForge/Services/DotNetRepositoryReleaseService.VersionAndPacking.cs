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
            packageId: string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId,
            sources: spec.VersionSources,
            credential: spec.VersionSourceCredential,
            credentialsBySource: spec.VersionSourceCredentials,
            includePrerelease: spec.IncludePrerelease);

        if (current is null)
            warning = $"No current package version found; using 0 baseline for '{expectedVersion}'.";

        return VersionPatternStepper.Step(expectedVersion!, current);
    }

    private static DotNetPackResult PackProject(
        DotNetRepositoryProjectResult project,
        DotNetRepositoryReleaseSpec spec,
        ILogger logger,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies)
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

        var packageRoot = outputPath ?? Path.Combine(csprojDir, "bin", configuration);
        var existingPackages = SnapshotPackages(packageRoot);

        if (!TryRunFreshReleaseBuild(project, configuration, logger, out var buildDuration, out var buildError))
        {
            result.ErrorMessage = buildError;
            return result;
        }
        result.Duration += buildDuration;

        var shouldSignAssemblies = signAssemblies is not null && !string.IsNullOrWhiteSpace(spec.CertificateThumbprint);
        if (shouldSignAssemblies)
        {
            try
            {
                var includePatterns = ResolveAssemblySigningIncludePatterns(project, spec, csprojDir, configuration, logger);
                var resolveOutputWatch = Stopwatch.StartNew();
                var outputDirectories = ResolveBuildOutputDirectories(project.CsprojPath, csprojDir, configuration, project.ProjectName, logger, includePatterns);
                var signingPlan = BuildAssemblySigningPlan(outputDirectories, includePatterns);
                if (signingPlan.Files.Length == 0 && !spec.SignDependencyAssemblies)
                {
                    var evaluatedIncludePatterns = ResolveAssemblySigningIncludePatterns(project, spec, csprojDir, configuration, logger);
                    if (!SamePatterns(includePatterns, evaluatedIncludePatterns))
                    {
                        includePatterns = evaluatedIncludePatterns;
                        outputDirectories = ResolveBuildOutputDirectories(project.CsprojPath, csprojDir, configuration, project.ProjectName, logger, includePatterns);
                        signingPlan = BuildAssemblySigningPlan(outputDirectories, includePatterns);
                    }
                }
                resolveOutputWatch.Stop();
                result.Duration += resolveOutputWatch.Elapsed;
                logger.Success($"{project.ProjectName}: resolved {outputDirectories.Length} signing output directorie(s) in {FormatDuration(resolveOutputWatch.Elapsed)}.");

                var assemblySigningWatch = Stopwatch.StartNew();
                logger.Info($"{project.ProjectName}: assembly signing include pattern(s): {string.Join(", ", includePatterns)}.");
                if (signingPlan.Files.Length > 0)
                {
                    signAssemblies!(new DotNetReleaseBuildAssemblySigningRequest
                    {
                        ReleasePath = outputDirectories.Length == 1 ? outputDirectories[0] : csprojDir,
                        LocalStore = spec.CertificateStore,
                        CertificateThumbprint = spec.CertificateThumbprint!.Trim(),
                        TimeStampServer = string.IsNullOrWhiteSpace(spec.TimeStampServer) ? "http://timestamp.digicert.com" : spec.TimeStampServer!.Trim(),
                        IncludePatterns = includePatterns,
                        FilePaths = signingPlan.Files
                    });
                }
                assemblySigningWatch.Stop();
                result.Duration += assemblySigningWatch.Elapsed;
                logger.Success($"{project.ProjectName}: assembly signing completed for {signingPlan.OutputDirectoryCount} output directorie(s), {signingPlan.Files.Length} file(s), in {FormatDuration(assemblySigningWatch.Elapsed)}.");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Assembly signing failed for {project.ProjectName}. {ex.Message}";
                return result;
            }
        }

        var exitCode = RunDotnetPack(project.CsprojPath, csprojDir, configuration, outputPath, project.ProjectName, logger, noBuild: true, out var stdErr, out var stdOut, out var duration);
        result.Duration += duration;
        if (exitCode != 0)
        {
            result.ErrorMessage = $"dotnet pack failed for {project.ProjectName} (exit {exitCode}). {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
            return result;
        }
        logger.Success($"{project.ProjectName}: dotnet pack completed in {FormatDuration(duration)}.");

        var packageDiscoveryWatch = Stopwatch.StartNew();
        if (Directory.Exists(packageRoot))
        {
            var pkgs = Directory.EnumerateFiles(packageRoot, "*.nupkg", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .Where(p => WasPackageCreatedOrChanged(existingPackages, p))
                .ToArray();
            result.Packages.AddRange(pkgs);
        }
        packageDiscoveryWatch.Stop();
        result.Duration += packageDiscoveryWatch.Elapsed;
        logger.Success($"{project.ProjectName}: package discovery found {result.Packages.Count} package(s) in {FormatDuration(packageDiscoveryWatch.Elapsed)}.");

        if (!TryValidateProjectPackagePayloads(project, spec, result.Packages, logger, out var validationError))
        {
            result.ErrorMessage = validationError;
            return result;
        }

        result.Success = true;
        return result;
    }

    private static DotNetPackResult PackProjectsWithMsBuild(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        ILogger logger,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies)
    {
        var result = new DotNetPackResult();
        if (projects.Count == 0)
        {
            result.Success = true;
            return result;
        }

        if (string.IsNullOrWhiteSpace(spec.OutputPath))
        {
            result.Success = false;
            result.ErrorMessage = "MSBuild batch pack requires OutputPath.";
            return result;
        }

        var outputPath = Path.IsPathRooted(spec.OutputPath!)
            ? spec.OutputPath!
            : Path.GetFullPath(Path.Combine(spec.RootPath, spec.OutputPath!));
        Directory.CreateDirectory(outputPath);
        var existingPackages = SnapshotPackages(outputPath);

        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "project-build", $"pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var traversalPath = Path.Combine(tempRoot, "pack.proj");
        try
        {
            WritePackTraversalProject(traversalPath, projects, spec, outputPath);

            var shouldSignAssemblies = signAssemblies is not null && !string.IsNullOrWhiteSpace(spec.CertificateThumbprint);
            int exitCode;
            string stdErr;
            string stdOut;
            TimeSpan duration;

            if (shouldSignAssemblies)
            {
                exitCode = RunDotnetMsBuildTarget(
                    traversalPath,
                    tempRoot,
                    "BuildSelected",
                    "build",
                    "dotnet msbuild build",
                    projects.Count,
                    logger,
                    out stdErr,
                    out stdOut,
                    out duration);
                result.Duration += duration;

                if (exitCode != 0)
                {
                    result.ErrorMessage = $"dotnet msbuild batch build failed (exit {exitCode}). {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
                    return result;
                }

                var signing = SignBatchBuildOutputs(projects, spec, logger, signAssemblies!);
                result.Duration += signing.Duration;
                if (!signing.Success)
                {
                    result.ErrorMessage = signing.ErrorMessage;
                    return result;
                }

                exitCode = RunDotnetMsBuildTarget(
                    traversalPath,
                    tempRoot,
                    "PackOnlySelected",
                    "pack",
                    "dotnet msbuild pack",
                    projects.Count,
                    logger,
                    out stdErr,
                    out stdOut,
                    out duration);
                result.Duration += duration;
            }
            else
            {
                exitCode = RunDotnetMsBuildTarget(
                    traversalPath,
                    tempRoot,
                    "PackSelected",
                    "pack",
                    "dotnet msbuild pack",
                    projects.Count,
                    logger,
                    out stdErr,
                    out stdOut,
                    out duration);
                result.Duration += duration;
            }

            if (exitCode != 0)
            {
                result.ErrorMessage = $"dotnet msbuild batch pack failed (exit {exitCode}). {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
                return result;
            }

            var pkgs = Directory.EnumerateFiles(outputPath, "*.nupkg", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .Where(p => WasPackageCreatedOrChanged(existingPackages, p))
                .ToArray();
            result.Packages.AddRange(pkgs);

            if (!TryValidatePackagePayloads(projects, spec, result.Packages, logger, out var validationError))
            {
                result.Success = false;
                result.ErrorMessage = validationError;
                return result;
            }

            result.Success = true;
            return result;
        }
        finally
        {
            TryDeleteDirectory(tempRoot, logger);
        }
    }

    internal static void WritePackTraversalProject(
        string traversalPath,
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        string outputPath)
    {
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var buildProperties = $"Configuration={EscapeMsBuildPropertyValue(configuration)}";
        var packProperties = string.Join(";",
            buildProperties,
            $"PackageOutputPath={EscapeMsBuildPropertyValue(outputPath)}",
            "NoBuild=true",
            "BuildProjectReferences=false");

        // Keep this a classic MSBuild project: it only fans out to concrete SDK projects
        // and does not require Microsoft.Build.Traversal to be installed.
        var document = new XDocument(
            new XElement("Project",
                new XElement("ItemGroup",
                    projects.Select(project => new XElement("PackProject",
                        new XAttribute("Include", Path.GetFullPath(project.CsprojPath))))),
                new XElement("Target",
                    new XAttribute("Name", "RestoreSelected"),
                    new XElement("MSBuild",
                        new XAttribute("Projects", "@(PackProject)"),
                        // Restore and build all selected projects first, then pack without walking project
                        // references. Otherwise packable project references can be packed twice in parallel.
                        new XAttribute("Targets", "Restore"),
                        new XAttribute("BuildInParallel", "true"),
                        new XAttribute("StopOnFirstFailure", "true"),
                        new XAttribute("Properties", buildProperties))),
                new XElement("Target",
                    new XAttribute("Name", "BuildSelected"),
                    new XAttribute("DependsOnTargets", "RestoreSelected"),
                    new XElement("MSBuild",
                        new XAttribute("Projects", "@(PackProject)"),
                        new XAttribute("Targets", "Rebuild"),
                        new XAttribute("BuildInParallel", "true"),
                        new XAttribute("StopOnFirstFailure", "true"),
                        new XAttribute("Properties", buildProperties))),
                new XElement("Target",
                    new XAttribute("Name", "PackOnlySelected"),
                    new XElement("MSBuild",
                        new XAttribute("Projects", "@(PackProject)"),
                        new XAttribute("Targets", "Pack"),
                        new XAttribute("BuildInParallel", "true"),
                        new XAttribute("StopOnFirstFailure", "true"),
                        new XAttribute("Properties", packProperties))),
                new XElement("Target",
                    new XAttribute("Name", "PackSelected"),
                    new XAttribute("DependsOnTargets", "BuildSelected;PackOnlySelected"))));

        document.Save(traversalPath);
    }

    private static DotNetPackResult SignBatchBuildOutputs(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        ILogger logger,
        Action<DotNetReleaseBuildAssemblySigningRequest> signAssemblies)
    {
        var result = new DotNetPackResult();
        var watch = Stopwatch.StartNew();

        try
        {
            var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
            var signedOutputCount = 0;
            var files = new List<string>();

            foreach (var project in projects)
            {
                var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
                var includePatterns = ResolveAssemblySigningIncludePatterns(project, spec, csprojDir, configuration, logger);
                var outputDirectories = ResolveBuildOutputDirectories(project.CsprojPath, csprojDir, configuration, project.ProjectName, logger, includePatterns);
                var signingPlan = BuildAssemblySigningPlan(outputDirectories, includePatterns);
                if (signingPlan.Files.Length == 0 && !spec.SignDependencyAssemblies)
                {
                    var evaluatedIncludePatterns = ResolveAssemblySigningIncludePatterns(project, spec, csprojDir, configuration, logger);
                    if (!SamePatterns(includePatterns, evaluatedIncludePatterns))
                    {
                        includePatterns = evaluatedIncludePatterns;
                        outputDirectories = ResolveBuildOutputDirectories(project.CsprojPath, csprojDir, configuration, project.ProjectName, logger, includePatterns);
                        signingPlan = BuildAssemblySigningPlan(outputDirectories, includePatterns);
                    }
                }
                logger.Info($"{project.ProjectName}: assembly signing include pattern(s): {string.Join(", ", includePatterns)}.");
                files.AddRange(signingPlan.Files);
                signedOutputCount += signingPlan.OutputDirectoryCount;
            }

            var filePaths = files
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (filePaths.Length > 0)
                signAssemblies(new DotNetReleaseBuildAssemblySigningRequest
                {
                    ReleasePath = spec.RootPath,
                    LocalStore = spec.CertificateStore,
                    CertificateThumbprint = spec.CertificateThumbprint!.Trim(),
                    TimeStampServer = string.IsNullOrWhiteSpace(spec.TimeStampServer) ? "http://timestamp.digicert.com" : spec.TimeStampServer!.Trim(),
                    IncludePatterns = Array.Empty<string>(),
                    FilePaths = filePaths
                });

            watch.Stop();
            result.Duration = watch.Elapsed;
            result.Success = true;
            logger.Success($"MSBuild batch assembly signing completed for {signedOutputCount} output directorie(s), {filePaths.Length} file(s), in {FormatDuration(watch.Elapsed)}.");
            return result;
        }
        catch (Exception ex)
        {
            watch.Stop();
            result.Duration = watch.Elapsed;
            result.ErrorMessage = $"MSBuild batch assembly signing failed. {ex.Message}";
            return result;
        }
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
        var packageId = string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId;
        return Path.Combine(outputPath, $"{packageId}.{version}.nupkg");
    }

    private static int RunDotnetPack(
        string csproj,
        string workingDirectory,
        string configuration,
        string? outputPath,
        string projectName,
        ILogger logger,
        bool noBuild,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;
        duration = TimeSpan.Zero;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        var args = new List<string> { "pack", csproj, "--configuration", configuration };
        if (noBuild)
            args.Add("--no-build");
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
        if (noBuild)
            psi.ArgumentList.Add("--no-build");
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputPath!);
        }
#endif

        var exitCode = RunProcessWithHeartbeat(
            psi,
            logger,
            elapsed => $"{projectName}: dotnet pack still running ({FormatDuration(elapsed)} elapsed).",
            out stdErr,
            out stdOut,
            out duration);
        LogProcessOutput(logger, projectName, "dotnet pack", stdOut, stdErr);
        return exitCode;
    }

    private static int RunDotnetBuild(
        string csproj,
        string workingDirectory,
        string configuration,
        string projectName,
        ILogger logger,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;
        duration = TimeSpan.Zero;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        psi.Arguments = BuildWindowsArgumentString(new[] { "build", csproj, "--configuration", configuration, "--no-incremental" });
#else
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(csproj);
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(configuration);
        psi.ArgumentList.Add("--no-incremental");
#endif

        var exitCode = RunProcessWithHeartbeat(
            psi,
            logger,
            elapsed => $"{projectName}: dotnet build still running ({FormatDuration(elapsed)} elapsed).",
            out stdErr,
            out stdOut,
            out duration);
        LogProcessOutput(logger, projectName, "dotnet build", stdOut, stdErr);
        return exitCode;
    }

    private static string[] ResolveBuildOutputDirectories(
        string csproj,
        string workingDirectory,
        string configuration,
        string projectName,
        ILogger logger,
        IReadOnlyList<string>? includePatterns = null)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetFrameworks = ResolveConfiguredTargetFrameworks(csproj, workingDirectory, configuration, projectName, logger);
        foreach (var directory in ResolveConventionalBuildOutputDirectories(csproj, workingDirectory, configuration, targetFrameworks))
        {
            if (!Directory.Exists(directory))
                continue;

            if (includePatterns is { Count: > 0 } && !ContainsSignableFiles(directory, includePatterns))
                continue;

            directories.Add(Path.GetFullPath(directory));
        }

        foreach (var targetFramework in targetFrameworks)
        {
            if (directories.Count > 0 && HasOutputDirectoryForTargetFramework(directories, targetFramework))
                continue;

            var exitCode = RunDotnetMsBuildGetProperty(
                csproj,
                workingDirectory,
                configuration,
                targetFramework,
                "TargetDir",
                projectName,
                logger,
                out var value,
                out var stdErr,
                out var stdOut,
                out var duration);

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(value))
            {
                var resolved = Path.GetFullPath(value!.Trim().Trim('"'));
                if (Directory.Exists(resolved))
                    directories.Add(resolved);
            }
            else
            {
                logger.Verbose($"{projectName}: unable to resolve MSBuild TargetDir for signing in {FormatDuration(duration)}. {SummarizeProcessFailureOutput(stdErr, stdOut)}");
            }
        }

        if (directories.Count == 0)
        {
            var fallback = Path.Combine(workingDirectory, "bin", configuration);
            if (Directory.Exists(fallback))
                directories.Add(fallback);
        }

        if (directories.Count == 0)
            throw new DirectoryNotFoundException($"No build output directory found for {projectName}.");

        return directories.ToArray();
    }

    private static bool HasOutputDirectoryForTargetFramework(IEnumerable<string> directories, string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(targetFramework))
            return directories.Any();

        var normalizedTargetFramework = targetFramework!.Trim();
        return directories.Any(directory =>
        {
            var parts = directory.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Any(part => string.Equals(part, normalizedTargetFramework, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static string[] ResolveConventionalBuildOutputDirectories(
        string csproj,
        string workingDirectory,
        string configuration,
        IReadOnlyList<string?> targetFrameworkValues)
    {
        var directories = new List<string>();
        var targetFrameworks = targetFrameworkValues
            .Where(static framework => !string.IsNullOrWhiteSpace(framework))
            .Select(static framework => framework!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var targetFramework in targetFrameworks)
            directories.Add(Path.Combine(workingDirectory, "bin", configuration, targetFramework));

        foreach (var outputPath in ReadOutputPaths(csproj))
        {
            var resolved = Path.IsPathRooted(outputPath)
                ? outputPath
                : Path.GetFullPath(Path.Combine(workingDirectory, outputPath));
            directories.Add(resolved);

            foreach (var targetFramework in targetFrameworks)
                directories.Add(Path.Combine(resolved, targetFramework));
        }

        if (targetFrameworks.Length == 0)
            directories.Add(Path.Combine(workingDirectory, "bin", configuration));

        return directories
            .Where(static directory => !string.IsNullOrWhiteSpace(directory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ContainsSignableFiles(string directory, IReadOnlyList<string> includePatterns)
    {
        foreach (var includePattern in includePatterns)
        {
            if (string.IsNullOrWhiteSpace(includePattern))
                continue;

            if (Directory.EnumerateFiles(directory, includePattern, SearchOption.AllDirectories).Any())
                return true;
        }

        return false;
    }

    private static string[] ResolveAssemblySigningIncludePatterns(
        DotNetRepositoryProjectResult project,
        DotNetRepositoryReleaseSpec spec,
        string? workingDirectory = null,
        string? configuration = null,
        ILogger? logger = null)
    {
        if (spec.SignDependencyAssemblies)
            return new[] { "*.dll", "*.exe" };

        var assemblyNames = ResolveAssemblyNames(project.CsprojPath, project.ProjectName, workingDirectory, configuration, logger)
            .Concat(new[] { project.ProjectName })
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return assemblyNames
            .SelectMany(static assemblyName => new[]
            {
                $"{assemblyName}.dll",
                $"{assemblyName}.exe"
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AssemblySigningPlan BuildAssemblySigningPlan(
        IReadOnlyList<string> outputDirectories,
        IReadOnlyList<string> includePatterns)
    {
        var files = new List<string>();
        var outputDirectoryCount = 0;

        foreach (var outputDirectory in outputDirectories)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                continue;

            outputDirectoryCount++;
            foreach (var includePattern in includePatterns)
            {
                if (string.IsNullOrWhiteSpace(includePattern))
                    continue;

                files.AddRange(Directory.EnumerateFiles(outputDirectory, includePattern, SearchOption.AllDirectories));
            }
        }

        return new AssemblySigningPlan
        {
            IncludePatterns = includePatterns.ToArray(),
            Files = files
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            OutputDirectoryCount = outputDirectoryCount
        };
    }

    private static bool SamePatterns(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => left.Count == right.Count && left
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(right.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    private static string[] ResolveAssemblyNames(
        string csproj,
        string projectName,
        string? workingDirectory = null,
        string? configuration = null,
        ILogger? logger = null)
    {
        var assemblyNames = new List<string>();
        var evaluatedAssemblyNames = ResolveEvaluatedAssemblyNames(csproj, workingDirectory, configuration, projectName, logger);
        assemblyNames.AddRange(evaluatedAssemblyNames);

        if (evaluatedAssemblyNames.Length == 0)
        {
            try
            {
                var document = XDocument.Load(csproj);
                assemblyNames.AddRange(document.Descendants()
                    .Where(element => string.Equals(element.Name.LocalName, "AssemblyName", StringComparison.OrdinalIgnoreCase))
                    .Select(static element => element.Value)
                    .Where(IsUsableAssemblyName)
                    .Select(static assemblyName => assemblyName!.Trim()));
            }
            catch
            {
                // Project file metadata is best-effort here; the csproj name is the SDK default.
            }
        }

        assemblyNames.Add(string.IsNullOrWhiteSpace(projectName)
            ? Path.GetFileNameWithoutExtension(csproj) ?? "Project"
            : projectName.Trim());

        return assemblyNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ResolveEvaluatedAssemblyNames(
        string csproj,
        string? workingDirectory,
        string? configuration,
        string projectName,
        ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(configuration) || logger is null)
            return Array.Empty<string>();

        var assemblyNames = new List<string>();
        foreach (var targetFramework in ReadTargetFrameworks(csproj))
        {
            var exitCode = RunDotnetMsBuildGetProperty(
                csproj,
                workingDirectory!,
                configuration!,
                targetFramework,
                "AssemblyName",
                projectName,
                logger,
                out var value,
                out _,
                out _,
                out _);

            if (exitCode == 0 && IsUsableAssemblyName(value))
                assemblyNames.Add(value!.Trim());
        }

        return assemblyNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUsableAssemblyName(string? assemblyName)
        => !string.IsNullOrWhiteSpace(assemblyName) &&
           assemblyName!.IndexOf("$(", StringComparison.Ordinal) < 0 &&
           assemblyName.IndexOf(';') < 0;

    private static string?[] ReadTargetFrameworks(string csproj)
    {
        try
        {
            var document = XDocument.Load(csproj);
            var targetFrameworks = document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(targetFrameworks))
            {
                return targetFrameworks!
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(static value => value.Trim())
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string?>()
                    .ToArray();
            }

            var targetFramework = document.Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(targetFramework))
                return new string?[] { targetFramework!.Trim() };
        }
        catch
        {
            // Fall back to a configuration-level output directory when project XML cannot be read.
        }

        return new string?[] { null };
    }

    private static string[] ReadOutputPaths(string csproj)
    {
        try
        {
            var document = XDocument.Load(csproj);
            return document.Descendants()
                .Where(element =>
                    string.Equals(element.Name.LocalName, "OutputPath", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(element.Name.LocalName, "OutDir", StringComparison.OrdinalIgnoreCase))
                .Select(static element => element.Value?.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static int RunDotnetMsBuildTarget(
        string traversalProject,
        string workingDirectory,
        string targetName,
        string heartbeatOperation,
        string logOperation,
        int projectCount,
        ILogger logger,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;
        duration = TimeSpan.Zero;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        psi.Arguments = BuildWindowsArgumentString(new[]
        {
            "msbuild",
            traversalProject,
            $"/t:{targetName}",
            "/m",
            "/nr:false",
            logger.IsVerbose ? "/v:n" : "/v:m"
        });
#else
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add(traversalProject);
        psi.ArgumentList.Add($"/t:{targetName}");
        psi.ArgumentList.Add("/m");
        psi.ArgumentList.Add("/nr:false");
        psi.ArgumentList.Add(logger.IsVerbose ? "/v:n" : "/v:m");
#endif

        var exitCode = RunProcessWithHeartbeat(
            psi,
            logger,
            elapsed => $"MSBuild batch {heartbeatOperation} still running ({projectCount} project(s), {FormatDuration(elapsed)} elapsed).",
            out stdErr,
            out stdOut,
            out duration);
        LogProcessOutput(logger, "MSBuild batch", logOperation, stdOut, stdErr);
        return exitCode;
    }

    private static int RunDotnetMsBuildGetProperty(
        string csproj,
        string workingDirectory,
        string configuration,
        string? targetFramework,
        string propertyName,
        string projectName,
        ILogger logger,
        out string? value,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
    {
        value = null;
        stdErr = string.Empty;
        stdOut = string.Empty;
        duration = TimeSpan.Zero;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

        var args = new List<string>
        {
            "msbuild",
            csproj,
            "-nologo",
            $"-getProperty:{propertyName}",
            $"-p:Configuration={configuration}"
        };
        if (!string.IsNullOrWhiteSpace(targetFramework))
            args.Add($"-p:TargetFramework={targetFramework!.Trim()}");

#if NET472
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
#endif

        var exitCode = RunProcessWithHeartbeat(
            psi,
            logger,
            elapsed => $"{projectName}: dotnet msbuild {propertyName} still running ({FormatDuration(elapsed)} elapsed).",
            out stdErr,
            out stdOut,
            out duration);
        LogProcessOutput(logger, projectName, $"dotnet msbuild {propertyName}", stdOut, stdErr);
        value = ExtractMsBuildPropertyValue(stdOut, propertyName);
        return exitCode;
    }

    private static string? ExtractMsBuildPropertyValue(string stdOut, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(stdOut))
            return null;

        var lines = stdOut.Replace("\r\n", "\n").Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
            return null;

        var xmlStart = Array.FindIndex(lines, static line => line.StartsWith("<", StringComparison.Ordinal));
        if (xmlStart >= 0)
        {
            try
            {
                var xml = string.Join(Environment.NewLine, lines.Skip(xmlStart));
                var document = XDocument.Parse(xml);
                return document.Descendants()
                    .FirstOrDefault(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
                    ?.Value;
            }
            catch
            {
                // Fall back to the last non-empty line for older MSBuild output shapes.
            }
        }

        return lines[lines.Length - 1];
    }

    internal static PackagePushResult ClassifyNuGetPushOutcome(int exitCode, bool skipDuplicate, string stdErr, string stdOut)
    {
        var combined = string.Join(Environment.NewLine, stdErr, stdOut).Trim();

        if (exitCode != 0)
        {
            return new PackagePushResult
            {
                Outcome = PackagePushOutcome.Failed,
                Message = combined
            };
        }

        if (skipDuplicate && LooksLikeSkippedDuplicate(combined))
        {
            return new PackagePushResult
            {
                Outcome = PackagePushOutcome.SkippedDuplicate,
                Message = combined
            };
        }

        return new PackagePushResult
        {
            Outcome = PackagePushOutcome.Published,
            Message = combined
        };
    }

    private static bool PushPackage(string packagePath, string apiKey, string source, bool skipDuplicate, out PackagePushResult result)
    {
        result = new PackagePushResult();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

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
        if (p is null)
        {
            result = new PackagePushResult
            {
                Outcome = PackagePushOutcome.Failed,
                Message = "Failed to start dotnet."
            };
            return false;
        }
        // Start both stream reads before waiting to avoid pipe-buffer deadlocks.
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        var stdOut = stdoutTask.GetAwaiter().GetResult();
        var stdErr = stderrTask.GetAwaiter().GetResult();
        result = ClassifyNuGetPushOutcome(p.ExitCode, skipDuplicate, stdErr, stdOut);
        return result.Outcome != PackagePushOutcome.Failed;
    }

    private static void LogProcessOutput(ILogger logger, string projectName, string operation, string stdOut, string stdErr)
    {
        if (!logger.IsVerbose)
            return;

        if (!string.IsNullOrWhiteSpace(stdOut))
            logger.Verbose($"{projectName}: {operation} stdout:{Environment.NewLine}{stdOut.TrimEnd()}");
        if (!string.IsNullOrWhiteSpace(stdErr))
            logger.Verbose($"{projectName}: {operation} stderr:{Environment.NewLine}{stdErr.TrimEnd()}");
    }

    private static string SummarizeProcessFailureOutput(string stdErr, string stdOut)
    {
        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(stdErr))
            sections.Add("stderr:" + Environment.NewLine + SummarizeProcessOutputLines(stdErr));
        if (!string.IsNullOrWhiteSpace(stdOut))
            sections.Add("stdout:" + Environment.NewLine + SummarizeProcessOutputLines(stdOut));

        return sections.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, sections);
    }

    internal static string SummarizeProcessOutputLines(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        const int firstLines = 10;
        const int lastLines = 40;
        const int maxDiagnosticLines = 20;
        if (lines.Length <= firstLines + lastLines)
            return string.Join(Environment.NewLine, lines);

        var middle = lines.Skip(firstLines).Take(lines.Length - firstLines - lastLines).ToArray();
        var diagnostics = middle
            .Where(IsDiagnosticOutputLine)
            .Distinct(StringComparer.Ordinal)
            .Take(maxDiagnosticLines)
            .ToArray();

        var summary = new List<string>();
        summary.AddRange(lines.Take(firstLines));
        summary.Add($"... omitted {middle.Length} line(s); diagnostic lines from that range are shown below when detected ...");
        if (diagnostics.Length > 0)
        {
            summary.Add("diagnostic lines:");
            summary.AddRange(diagnostics);
        }

        summary.Add($"last {lastLines} line(s):");
        summary.AddRange(lines.Skip(lines.Length - lastLines));
        return string.Join(Environment.NewLine, summary);
    }

    private static bool IsDiagnosticOutputLine(string line)
    {
        return line.Contains(": error", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("failed", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(": failed", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("unable to", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static int RunProcessWithHeartbeat(
        ProcessStartInfo psi,
        ILogger logger,
        Func<TimeSpan, string> heartbeatMessage,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;
        duration = TimeSpan.Zero;

        using var p = Process.Start(psi);
        if (p is null) return 1;

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stopwatch = Stopwatch.StartNew();
        var nextProgress = HeartbeatInterval;

        while (!p.WaitForExit(1000))
        {
            if (stopwatch.Elapsed < nextProgress)
                continue;

            logger.Info(heartbeatMessage(stopwatch.Elapsed));
            nextProgress += HeartbeatInterval;
        }

        // On .NET Framework, WaitForExit(int) returning true does not guarantee async
        // output callbacks have completed; the no-arg overload does.
        p.WaitForExit();
        stdOut = stdoutTask.GetAwaiter().GetResult();
        stdErr = stderrTask.GetAwaiter().GetResult();
        stopwatch.Stop();
        duration = stopwatch.Elapsed;
        return p.ExitCode;
    }

    internal static Dictionary<string, (DateTime LastWriteUtc, long Length)> SnapshotPackages(string packageRoot)
    {
        var snapshot = new Dictionary<string, (DateTime LastWriteUtc, long Length)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(packageRoot))
            return snapshot;

        foreach (var path in Directory.EnumerateFiles(packageRoot, "*.nupkg", SearchOption.AllDirectories))
        {
            if (path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                continue;

            var info = new FileInfo(path);
            snapshot[path] = (info.LastWriteTimeUtc, info.Length);
        }

        return snapshot;
    }

    internal static bool WasPackageCreatedOrChanged(
        IReadOnlyDictionary<string, (DateTime LastWriteUtc, long Length)> existingPackages,
        string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            return false;

        if (!existingPackages.TryGetValue(path, out var existing))
            return true;

        return existing.LastWriteUtc != info.LastWriteTimeUtc || existing.Length != info.Length;
    }

    private static string EscapeMsBuildPropertyValue(string value)
        => value.Replace("%", "%25")
            .Replace(";", "%3B")
            .Replace("=", "%3D")
            .Replace("$", "%24")
            .Replace("@", "%40");

    private static bool LooksLikeSkippedDuplicate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("409 (Conflict)", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("cannot be modified", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("skip duplicate", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal enum PackagePushOutcome
    {
        Published,
        SkippedDuplicate,
        Failed
    }

    internal sealed class PackagePushResult
    {
        public PackagePushOutcome Outcome { get; set; }
        public string? Message { get; set; }
    }

}
