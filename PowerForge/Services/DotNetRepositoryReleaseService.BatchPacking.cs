using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
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
            var forceBatchRebuild = false;
            foreach (var project in projects)
            {
                if (!TryRemoveStalePrimaryPackageOutputs(
                        project,
                        string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim(),
                        logger,
                        out _,
                        out var removedIntermediatePrimaryOutput,
                        out var cleanupDuration,
                        out var cleanupError))
                {
                    result.Duration += cleanupDuration;
                    result.ErrorMessage = cleanupError;
                    return result;
                }

                result.Duration += cleanupDuration;
                forceBatchRebuild |= !removedIntermediatePrimaryOutput;
            }

            if (forceBatchRebuild)
                logger.Info("MSBuild batch freshness could not prove every intermediate output was removed; using the Rebuild safety target.");

            WritePackTraversalProject(traversalPath, projects, spec, outputPath, forceBatchRebuild);

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

            if (spec.IncludeSymbols)
            {
                var symbolPackages = Directory.EnumerateFiles(outputPath, "*.snupkg", SearchOption.AllDirectories)
                    .Where(p => WasPackageCreatedOrChanged(existingPackages, p))
                    .ToArray();
                result.SymbolPackages.AddRange(symbolPackages);
            }

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
        string outputPath,
        bool forceRebuild = false)
    {
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var buildProperties = $"Configuration={EscapeMsBuildPropertyValue(configuration)}";
        var packProperties = new List<string>
        {
            buildProperties,
            $"PackageOutputPath={EscapeMsBuildPropertyValue(outputPath)}",
            "NoBuild=true",
            "BuildProjectReferences=false"
        };
        if (spec.IncludeSymbols)
        {
            packProperties.Add("IncludeSymbols=true");
            packProperties.Add("SymbolPackageFormat=snupkg");
        }
        var joinedPackProperties = string.Join(";", packProperties);

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
                        // Freshness cleanup normally permits an incremental Build. Rebuild remains
                        // the safety target when an intermediate compiler output was not removed.
                        new XAttribute("Targets", forceRebuild ? "Rebuild" : "Build"),
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
                        new XAttribute("Properties", joinedPackProperties))),
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
}
