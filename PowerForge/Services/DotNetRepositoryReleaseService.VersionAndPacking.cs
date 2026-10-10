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
    // Compute package items without recopying signed staging files. The SDK keeps
    // its no-build prerequisites for references and satellite resources, while
    // ComputeFilesToPublish includes PrepareForPublish itself.
    private const string PreparedToolPublishTargetsProperty = "_CorePublishTargets=ComputeFilesToPublish";

    private string ResolveVersion(
        DotNetRepositoryProjectResult project,
        string? expectedVersion,
        DotNetRepositoryReleaseSpec spec,
        out string? warning,
        string? evaluatedPackageVersion = null)
    {
        warning = null;

        if (!spec.UpdateVersions)
            return ResolveCurrentProjectVersion(project, spec, out warning, evaluatedPackageVersion);

        if (string.IsNullOrWhiteSpace(expectedVersion))
            return ResolveCurrentProjectVersion(project, spec, out warning, evaluatedPackageVersion);

        if (PackageVersionUtility.TryNormalizeExact(expectedVersion, out var exact))
            return exact;

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

    private string ResolveCurrentProjectVersion(
        DotNetRepositoryProjectResult project,
        DotNetRepositoryReleaseSpec spec,
        out string? warning,
        string? evaluatedPackageVersion = null)
    {
        warning = null;
        // Reuse only metadata from this execution. Version-binding refreshes call without it.
        if (!string.IsNullOrWhiteSpace(evaluatedPackageVersion) &&
            PackageVersionUtility.TryNormalizeExact(evaluatedPackageVersion, out var metadataVersion))
            return metadataVersion;
        string? declaredVersion = null;
        string? declaredExactVersion = null;
        if (CsprojVersionEditor.TryGetVersion(project.CsprojPath, out var candidate))
        {
            declaredVersion = candidate;
            if (PackageVersionUtility.TryNormalizeExact(candidate, out var exact))
                declaredExactVersion = exact;
        }

        var projectDirectory = Path.GetDirectoryName(project.CsprojPath) ?? spec.RootPath;
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var exitCode = RunDotnetMsBuildGetProperty(
            project.CsprojPath,
            projectDirectory,
            configuration,
            targetFramework: null,
            propertyName: "PackageVersion",
            project.ProjectName,
            _logger,
            out var evaluatedVersion,
            out var stdErr,
            out var stdOut,
            out _);

        if (exitCode == 0 &&
            !string.IsNullOrWhiteSpace(evaluatedVersion) &&
            PackageVersionUtility.TryNormalizeExact(evaluatedVersion, out var evaluatedExact))
        {
            return evaluatedExact;
        }

        if (exitCode != 0)
        {
            var detail = SummarizeProcessFailureOutput(stdErr, stdOut);
            warning = !string.IsNullOrWhiteSpace(declaredExactVersion)
                ? $"MSBuild PackageVersion evaluation failed; using declared project version '{declaredExactVersion}'. {detail}".Trim()
                : string.IsNullOrWhiteSpace(declaredVersion)
                    ? $"No literal project version was found and MSBuild PackageVersion evaluation failed. {detail}".Trim()
                    : $"Project version '{declaredVersion}' requires MSBuild evaluation, but PackageVersion evaluation failed. {detail}".Trim();
        }
        else if (!string.IsNullOrWhiteSpace(evaluatedVersion))
        {
            warning = !string.IsNullOrWhiteSpace(declaredExactVersion)
                ? $"MSBuild evaluated PackageVersion to unsupported value '{evaluatedVersion}'; using declared project version '{declaredExactVersion}'."
                : $"MSBuild evaluated PackageVersion to unsupported value '{evaluatedVersion}'.";
        }
        else
        {
            warning = !string.IsNullOrWhiteSpace(declaredExactVersion)
                ? $"MSBuild did not evaluate PackageVersion; using declared project version '{declaredExactVersion}'."
                : string.IsNullOrWhiteSpace(declaredVersion)
                    ? "No project version was found after evaluating MSBuild PackageVersion."
                    : $"Project version '{declaredVersion}' did not evaluate to a package version.";
        }

        if (!string.IsNullOrWhiteSpace(declaredExactVersion))
            return declaredExactVersion!;

        return spec.WhatIf && !string.IsNullOrWhiteSpace(declaredVersion)
            ? declaredVersion!
            : string.Empty;
    }

    private bool TryRefreshEffectiveVersionsAfterBindings(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseResult result,
        DotNetRepositoryReleaseSpec spec,
        IReadOnlyList<ProjectVersionBinding>? bindings,
        out string? error)
    {
        error = null;
        var bindingSources = new HashSet<string>(
            (bindings ?? Array.Empty<ProjectVersionBinding>())
                .Where(static binding => binding is not null && !string.IsNullOrWhiteSpace(binding.Project))
                .Select(static binding => binding.Project.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var sourceVersionsBeforeRefresh = result.ResolvedVersionsByProject
            .Where(pair => bindingSources.Contains(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            var effectiveVersion = ResolveCurrentProjectVersion(project, spec, out var warning);
            if (!string.IsNullOrWhiteSpace(warning))
                _logger.Warn($"{project.ProjectName}: {warning}");
            if (string.IsNullOrWhiteSpace(effectiveVersion) ||
                !PackageVersionUtility.TryNormalizeExact(effectiveVersion, out var normalizedVersion))
            {
                project.ErrorMessage = "Unable to re-evaluate the effective package version after applying version bindings.";
                error = $"{project.ProjectName}: {project.ErrorMessage}";
                _logger.Warn(error);
                return false;
            }

            if (!string.Equals(project.NewVersion, normalizedVersion, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info(
                    $"{project.ProjectName}: effective package version changed from {project.NewVersion} to {normalizedVersion} after version bindings.");
            }

            result.ResolvedVersionsByProject[project.ProjectName] = normalizedVersion;
            project.NewVersion = normalizedVersion;
        }

        foreach (var source in sourceVersionsBeforeRefresh)
        {
            if (result.ResolvedVersionsByProject.TryGetValue(source.Key, out var refreshed) &&
                string.Equals(source.Value, refreshed, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var project = projects.First(item =>
                string.Equals(item.ProjectName, source.Key, StringComparison.OrdinalIgnoreCase));
            project.ErrorMessage =
                $"Version binding source '{source.Key}' changed its effective package version after the binding was applied.";
            error = project.ErrorMessage;
            _logger.Warn(error);
            return false;
        }

        return true;
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
                var onlyPackToolProjects = new HashSet<DotNetRepositoryProjectResult>();
                if (spec.SignDependencyAssemblies)
                {
                    if (!TryPreparePackToolPublishOutputs(new[] { project }, spec, outputPath, logger, out var preparationDuration, out var preparationError, out onlyPackToolProjects))
                    {
                        result.Duration += preparationDuration;
                        result.ErrorMessage = preparationError;
                        return result;
                    }

                    result.Duration += preparationDuration;
                }

                var includePatterns = ResolveAssemblySigningIncludePatterns(project, spec, csprojDir, configuration, logger);
                var resolveOutputWatch = Stopwatch.StartNew();
                var outputDirectories = ResolveBuildOutputDirectories(project.CsprojPath, csprojDir, configuration, project.ProjectName, logger, includePatterns);
                var signingPlan = BuildAssemblySigningPlan(
                    outputDirectories,
                    includePatterns,
                    ResolvePackToolIntermediateAssemblyPaths(
                        project.CsprojPath,
                        csprojDir,
                        configuration,
                        project.ProjectName,
                        logger,
                        includePatterns,
                        spec.SignDependencyAssemblies,
                        outputPath),
                    recursiveBuildOutputs: !onlyPackToolProjects.Contains(project));
                if (signingPlan.Files.Length == 0 && !spec.SignDependencyAssemblies)
                {
                    var evaluatedIncludePatterns = ResolveAssemblySigningIncludePatterns(project, spec, csprojDir, configuration, logger);
                    if (!SamePatterns(includePatterns, evaluatedIncludePatterns))
                    {
                        includePatterns = evaluatedIncludePatterns;
                        outputDirectories = ResolveBuildOutputDirectories(project.CsprojPath, csprojDir, configuration, project.ProjectName, logger, includePatterns);
                        signingPlan = BuildAssemblySigningPlan(
                            outputDirectories,
                            includePatterns,
                            ResolvePackToolIntermediateAssemblyPaths(
                                project.CsprojPath,
                                csprojDir,
                                configuration,
                                project.ProjectName,
                                logger,
                                includePatterns,
                                includePreparedPublishOutput: false,
                                outputPath));
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
                        OverwriteSigned = spec.OverwriteSignedAssemblies,
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

        var exitCode = RunDotnetPack(project.CsprojPath, csprojDir, configuration, outputPath, project.ProjectName, logger, noBuild: true, includeSymbols: spec.IncludeSymbols,
            usePreparedToolPublishOutput: shouldSignAssemblies && spec.SignDependencyAssemblies, out var stdErr, out var stdOut, out var duration);
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
        bool usePreparedToolPublishOutput,
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
        if (usePreparedToolPublishOutput)
            args.Add("-p:" + PreparedToolPublishTargetsProperty);
        if (noBuild)
        {
            args.Add("--no-build");
            args.Add("-p:_IsPacking=true");
            args.Add("-p:BuildProjectReferences=false");
        }
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
        if (usePreparedToolPublishOutput)
            psi.ArgumentList.Add("-p:" + PreparedToolPublishTargetsProperty);
        if (noBuild)
        {
            psi.ArgumentList.Add("--no-build");
            psi.ArgumentList.Add("-p:_IsPacking=true");
            psi.ArgumentList.Add("-p:BuildProjectReferences=false");
        }
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
