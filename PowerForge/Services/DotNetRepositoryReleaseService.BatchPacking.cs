using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static DotNetRepositoryProjectResult[] PrepareMsBuildBatchCandidates(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        ILogger logger)
    {
        var candidates = projects
            .Where(project =>
                string.IsNullOrWhiteSpace(project.ErrorMessage) &&
                !string.IsNullOrWhiteSpace(project.NewVersion))
            .ToArray();
        var missingVersions = projects
            .Where(project =>
                string.IsNullOrWhiteSpace(project.ErrorMessage) &&
                string.IsNullOrWhiteSpace(project.NewVersion))
            .ToArray();
        if (missingVersions.Length == 0)
            return candidates;

        var names = string.Join(", ", missingVersions.Select(project => project.ProjectName));
        logger.Warn($"MSBuild batch pack excluded {missingVersions.Length} project(s) without a resolved version; they will be skipped during pack: {names}");
        foreach (var project in missingVersions)
            project.ErrorMessage = "No resolved version; skipping pack.";
        return candidates;
    }

    private static DotNetPackResult PackProjectsWithMsBuildAndTrackProgress(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        ILogger logger,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies,
        IReadOnlyDictionary<DotNetRepositoryProjectResult, ProjectBuildProgressItem> items,
        IReadOnlyDictionary<DotNetRepositoryProjectResult, Stopwatch> watches,
        IProjectBuildProgressReporterV2? detailedProgress,
        Action<string>? heartbeatDetail = null)
    {
        try
        {
            return PackProjectsWithMsBuild(projects, spec, logger, signAssemblies, heartbeatDetail);
        }
        finally
        {
            PauseMsBuildBatchProgress(projects, items, watches, detailedProgress);
        }
    }

    internal const string RestoreEvaluationProperties = "MSBuildRestoreSessionId=powerforge-batch-restore;ExcludeRestorePackageImports=true";

    private static DotNetPackResult PackProjectsWithMsBuild(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        ILogger logger,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies,
        Action<string>? heartbeatDetail = null)
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
            var cleanup = RemoveStaleBatchPackageOutputs(projects, spec, logger);
            result.Duration += cleanup.Duration;
            if (cleanup.Error is not null)
            {
                result.ErrorMessage = cleanup.Error;
                return result;
            }

            var forceBatchRebuild = cleanup.ForceRebuild;

            if (forceBatchRebuild)
                logger.Info("MSBuild batch freshness could not prove every intermediate output was removed; cleaning every project before the build.");

            var shouldSignAssemblies = signAssemblies is not null && !string.IsNullOrWhiteSpace(spec.CertificateThumbprint);
            WritePackTraversalProject(traversalPath, projects, spec, outputPath, forceBatchRebuild,
                usePreparedToolPublishOutput: shouldSignAssemblies && spec.SignDependencyAssemblies);
            int exitCode;
            string stdErr;
            string stdOut;
            TimeSpan duration;

            var hasRidSpecificTools = projects.Any(static project => project.ToolPackageRuntimeIdentifiers.Count > 0);
            if (shouldSignAssemblies && !TryRejectRidSpecificToolsWithoutDependencySigning(projects, spec, out var ridSigningError))
            {
                result.ErrorMessage = ridSigningError;
                return result;
            }

            if (shouldSignAssemblies || hasRidSpecificTools)
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
                    out duration,
                    heartbeatDetail);
                result.Duration += duration;

                if (exitCode != 0)
                {
                    result.ErrorMessage = $"dotnet msbuild batch build failed (exit {exitCode}). {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
                    return result;
                }

                // Dependency signing prepares every pack tool; otherwise only RID-specific tools need staging.
                var prepareAllPackTools = shouldSignAssemblies && spec.SignDependencyAssemblies;
                if (!TryPreparePackToolPublishOutputs(projects, spec, outputPath, logger, ridSpecificToolsOnly: !prepareAllPackTools, out var preparationDuration, out var preparationError, out var onlyPackToolProjects))
                {
                    result.Duration += preparationDuration;
                    result.ErrorMessage = preparationError;
                    return result;
                }

                result.Duration += preparationDuration;

                if (shouldSignAssemblies)
                {
                    var signing = SignBatchBuildOutputs(projects, spec, outputPath, logger, signAssemblies!, onlyPackToolProjects);
                    result.Duration += signing.Duration;
                    if (!signing.Success)
                    {
                        result.SigningFailed = signing.SigningFailed;
                        result.ErrorMessage = signing.ErrorMessage;
                        return result;
                    }
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
                    out duration,
                    heartbeatDetail);
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
                    out duration,
                    heartbeatDetail);
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

            if (!TryValidatePackagePayloads(projects, spec, result.Packages, logger, out var validationError,
                    usePreparedToolPublishOutput: shouldSignAssemblies && spec.SignDependencyAssemblies,
                    packageOutputPath: outputPath))
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
        bool forceRebuild = false,
        bool usePreparedToolPublishOutput = false)
    {
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var buildProperties = $"Configuration={EscapeMsBuildPropertyValue(configuration)}";
        var packProperties = new List<string>
        {
            buildProperties,
            $"PackageOutputPath={EscapeMsBuildPropertyValue(outputPath)}",
            "_IsPacking=true",
            "NoBuild=true",
            "BuildProjectReferences=false"
        };
        // Retain the SDK's no-build reference/resource preparation, then compute
        // package items without copying over the signed staging files.
        if (usePreparedToolPublishOutput)
            packProperties.Add(PreparedToolPublishTargetsProperty);
        if (spec.IncludeSymbols)
        {
            packProperties.Add("IncludeSymbols=true");
            packProperties.Add("SymbolPackageFormat=snupkg");
        }
        var joinedPackProperties = string.Join(";", packProperties);
        // RID-specific tools always pack from their prepared per-runtime publish output. Pass that to their pack
        // only, so restore and build keep one evaluation of every referenced project.
        var packOnlyRidSpecificTools = !usePreparedToolPublishOutput &&
                                       projects.Any(static project => project.ToolPackageRuntimeIdentifiers.Count > 0);

        // Keep this a classic MSBuild project: it only fans out to concrete SDK projects
        // and does not require Microsoft.Build.Traversal to be installed.
        var document = new XDocument(
            new XElement("Project",
                new XElement("ItemGroup",
                    projects.Select(project => new XElement("PackProject",
                        new XAttribute("Include", Path.GetFullPath(project.CsprojPath)),
                        packOnlyRidSpecificTools && project.ToolPackageRuntimeIdentifiers.Count > 0
                            ? new XElement(PackAdditionalPropertiesMetadata, PreparedToolPublishTargetsProperty)
                            : null))),
                new XElement("Target",
                    new XAttribute("Name", "RestoreSelected"),
                    new XElement("MSBuild",
                        new XAttribute("Projects", "@(PackProject)"),
                        // Each root restore walks its dependency graph and can rewrite shared obj files.
                        // Finish one restore graph before starting another; build and pack stay parallel.
                        new XAttribute("Targets", "Restore"),
                        new XAttribute("BuildInParallel", "false"),
                        new XAttribute("StopOnFirstFailure", "true"),
                        // Restore under its own global properties, as msbuild -restore does. Otherwise the build
                        // reuses the evaluation cached before restore wrote the NuGet imports, and a project whose
                        // references come from packages (netstandard2.0, for example) compiles without them.
                        new XAttribute("Properties", buildProperties + ";" + RestoreEvaluationProperties))),
                new XElement("Target",
                    new XAttribute("Name", "CleanSelected"),
                    new XElement("MSBuild",
                        new XAttribute("Projects", "@(PackProject)"),
                        new XAttribute("Targets", "Clean"),
                        new XAttribute("BuildInParallel", "true"),
                        new XAttribute("StopOnFirstFailure", "true"),
                        new XAttribute("Properties", buildProperties))),
                new XElement("Target",
                    new XAttribute("Name", "BuildSelected"),
                    // Freshness cleanup normally permits an incremental Build. When an intermediate compiler output
                    // was not removed, every project is cleaned before any project builds. A parallel Rebuild would
                    // let one project's Clean delete outputs another project is compiling against.
                    new XAttribute("DependsOnTargets", forceRebuild ? "RestoreSelected;CleanSelected" : "RestoreSelected"),
                    new XElement("MSBuild",
                        new XAttribute("Projects", "@(PackProject)"),
                        new XAttribute("Targets", "Build"),
                        new XAttribute("BuildInParallel", "true"),
                        new XAttribute("StopOnFirstFailure", "true"),
                        new XAttribute("Properties", buildProperties))),
                new XElement("Target",
                    new XAttribute("Name", "PackOnlySelected"),
                    packOnlyRidSpecificTools
                        ? new XElement("ItemGroup",
                            new XElement("PackOnlyProject",
                                new XAttribute("Include", "@(PackProject)"),
                                new XAttribute("AdditionalProperties", $"%(PackProject.{PackAdditionalPropertiesMetadata})")))
                        : null,
                    new XElement("MSBuild",
                        new XAttribute("Projects", packOnlyRidSpecificTools ? "@(PackOnlyProject)" : "@(PackProject)"),
                        new XAttribute("Targets", "Pack"),
                        new XAttribute("BuildInParallel", "true"),
                        new XAttribute("StopOnFirstFailure", "true"),
                        new XAttribute("Properties", joinedPackProperties))),
                new XElement("Target",
                    new XAttribute("Name", "PackSelected"),
                    new XAttribute("DependsOnTargets", "BuildSelected;PackOnlySelected"))));

        document.Save(traversalPath);
    }
}
