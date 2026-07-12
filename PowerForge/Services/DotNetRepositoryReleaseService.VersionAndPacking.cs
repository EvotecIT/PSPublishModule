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

        var exitCode = RunDotnetPack(project.CsprojPath, csprojDir, configuration, outputPath, project.ProjectName, logger, noBuild: true, includeSymbols: spec.IncludeSymbols, out var stdErr, out var stdOut, out var duration);
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

            if (spec.IncludeSymbols)
            {
                var symbolPackages = Directory.EnumerateFiles(packageRoot, "*.snupkg", SearchOption.AllDirectories)
                    .Where(p => WasPackageCreatedOrChanged(existingPackages, p))
                    .ToArray();
                result.SymbolPackages.AddRange(symbolPackages);
            }
        }
        packageDiscoveryWatch.Stop();
        result.Duration += packageDiscoveryWatch.Elapsed;
        logger.Success($"{project.ProjectName}: package discovery found {result.Packages.Count} package(s) and {result.SymbolPackages.Count} symbol package(s) in {FormatDuration(packageDiscoveryWatch.Elapsed)}.");

        if (!TryValidateProjectPackagePayloads(project, spec, result.Packages, logger, out var validationError))
        {
            result.ErrorMessage = validationError;
            return result;
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
        var packageId = string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId;
        return Path.Combine(outputPath, $"{packageId}.{version}.nupkg");
    }

    private static string? ResolveSymbolPackagePath(DotNetRepositoryReleaseSpec spec, DotNetRepositoryProjectResult project, string version)
    {
        var packagePath = ResolvePackagePath(spec, project, version);
        return string.IsNullOrWhiteSpace(packagePath)
            ? null
            : Path.ChangeExtension(packagePath, ".snupkg");
    }

    private static int RunDotnetPack(
        string csproj,
        string workingDirectory,
        string configuration,
        string? outputPath,
        string projectName,
        ILogger logger,
        bool noBuild,
        bool includeSymbols,
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
        if (includeSymbols)
        {
            args.Add("-p:IncludeSymbols=true");
            args.Add("-p:SymbolPackageFormat=snupkg");
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
        if (includeSymbols)
        {
            psi.ArgumentList.Add("-p:IncludeSymbols=true");
            psi.ArgumentList.Add("-p:SymbolPackageFormat=snupkg");
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
        bool forceNonIncremental,
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
        var args = new List<string> { "build", csproj, "--configuration", configuration };
        if (forceNonIncremental)
            args.Add("--no-incremental");
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(csproj);
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(configuration);
        if (forceNonIncremental)
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

            if (Directory.EnumerateFiles(directory, includePattern, SearchOption.TopDirectoryOnly).Any())
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

}
