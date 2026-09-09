using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private sealed class ScriptPackageDestinationNamespace
    {
        internal ScriptPackageDestinationNamespace(string[] files, string[] directories)
        {
            Files = files;
            Directories = directories;
        }

        internal string[] Files { get; }

        internal string[] Directories { get; }
    }

    private static ScriptPackageDestinationNamespace ResolveScriptPackageDestinationNamespace(
        string stagingPath,
        string scriptRoot,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders,
        IReadOnlyList<string>? finalizedPayloadFiles)
    {
        string fullScriptRoot = Path.GetFullPath(scriptRoot);
        string[] sourceFiles = ResolveModulePackageSourceFiles(
            stagingPath,
            information,
            delivery,
            includeScriptFolders,
            finalizedPayloadFiles);
        string[] destinationFiles = CreateModulePackageCopyPlan(
                Path.GetFullPath(stagingPath),
                fullScriptRoot,
                sourceFiles)
            .Select(static entry => entry.DestinationPath)
            .ToArray();
        string[] destinationDirectories = finalizedPayloadFiles is { Count: > 0 }
            ? Array.Empty<string>()
            : EnumerateModulePackageDirectories(
                    stagingPath,
                    ResolvePackagingInformation(information, delivery, includeScriptFolders))
                .Select(sourceDirectory => Path.GetFullPath(Path.Combine(
                    fullScriptRoot,
                    ComputeRelativePath(stagingPath, sourceDirectory))))
                .ToArray();

        return new ScriptPackageDestinationNamespace(destinationFiles, destinationDirectories);
    }

    private static void ValidateScriptEntryPointDoesNotConflictWithPackage(
        string scriptRoot,
        string scriptName,
        ScriptPackageDestinationNamespace packageNamespace)
    {
        string scriptPath = Path.GetFullPath(Path.Combine(scriptRoot, scriptName));
        string? fileConflict = packageNamespace.Files.FirstOrDefault(destination =>
            ScriptPathsOverlap(destination, scriptPath));
        string? directoryConflict = packageNamespace.Directories.FirstOrDefault(destination =>
            IsSameOrBelowPath(destination, scriptPath));
        string? conflict = fileConflict ?? directoryConflict;
        if (conflict is null)
            return;

        throw new InvalidOperationException(
            $"ScriptName '{scriptName}' conflicts with packaged payload destination '{conflict}' on the output filesystem. " +
            "Choose an entry point name that does not replace included module content.");
    }

    private static void ValidateScriptPackageDestinationsDoNotTraverseReparsePoints(
        string scriptRoot,
        string stagingPath,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders,
        IReadOnlyList<string>? finalizedPayloadFiles)
    {
        string fullScriptRoot = Path.GetFullPath(scriptRoot);
        foreach (string sourcePath in ResolveModulePackageSourceFiles(
                     stagingPath,
                     information,
                     delivery,
                     includeScriptFolders,
                     finalizedPayloadFiles))
        {
            string relativePath = ComputeRelativePath(stagingPath, sourcePath);
            string destination = Path.GetFullPath(Path.Combine(fullScriptRoot, relativePath));
            ValidateScriptDestinationDoesNotTraverseReparsePoint(
                destination,
                fullScriptRoot,
                "module package destination");
        }
    }

    /// <summary>
    /// Rejects existing reparse-point components from a destination through its trusted boundary,
    /// or through the filesystem root when no validated boundary contains the destination.
    /// </summary>
    internal static void ValidateScriptDestinationDoesNotTraverseReparsePoint(
        string destination,
        string? trustedBoundary,
        string description)
    {
        string fullDestination = NormalizeScriptValidationPath(destination);
        string? fullTrustedBoundary = string.IsNullOrWhiteSpace(trustedBoundary)
            ? null
            : NormalizeScriptValidationPath(trustedBoundary!);
        if (fullTrustedBoundary is not null &&
            !IsSameOrBelowPath(fullDestination, fullTrustedBoundary))
        {
            fullTrustedBoundary = null;
        }

        string current = fullDestination;
        while (true)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Script artefact {description} '{fullDestination}' traverses a symbolic link or reparse point at '{current}'.");
                }
            }
            catch (FileNotFoundException)
            {
                // Non-existing path components are created only after this preflight succeeds.
            }
            catch (DirectoryNotFoundException)
            {
                // Non-existing path components are created only after this preflight succeeds.
            }

            if (fullTrustedBoundary is not null &&
                string.Equals(current, fullTrustedBoundary, GetPathComparison(current, fullTrustedBoundary)))
            {
                return;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, GetPathComparison(parent, current)))
            {
                return;
            }

            current = parent;
        }
    }

    private static string NormalizeScriptValidationPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string pathRoot = Path.GetPathRoot(fullPath) ?? string.Empty;
        return fullPath.Length > pathRoot.Length
            ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : fullPath;
    }

    private static void ValidateScriptBuildRootsDoNotOverlapStaging(
        string stagingPath,
        string outputRoot,
        string projectRoot,
        string scriptRoot,
        string? requiredModulesRoot,
        string? temporaryBuildRoot,
        bool rejectOutputRootContainingProject)
    {
        string fullOutputRoot = Path.GetFullPath(outputRoot);
        string fullProjectRoot = Path.GetFullPath(projectRoot);
        ValidateScriptBuildRootIsOutsideRepositoryMetadata(fullOutputRoot, "output root");
        if (rejectOutputRootContainingProject && IsSameOrBelowPath(fullProjectRoot, fullOutputRoot))
        {
            throw new InvalidOperationException(
                $"Script artefact output root '{fullOutputRoot}' contains project root '{fullProjectRoot}'. " +
                "Use a dedicated artefact directory inside the project so output cleanup cannot modify project sources.");
        }

        string fullStagingPath = Path.GetFullPath(stagingPath);
        foreach ((string Label, string Path) candidate in new[]
                 {
                     ("output root", outputRoot),
                     ("generated script root", scriptRoot),
                     ("required modules root", requiredModulesRoot ?? string.Empty)
                 })
        {
            if (string.IsNullOrWhiteSpace(candidate.Path))
                continue;

            string fullCandidate = Path.GetFullPath(candidate.Path);
            if (!IsSameOrBelowPath(fullStagingPath, fullCandidate) &&
                !IsSameOrBelowPath(fullCandidate, fullStagingPath))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Script artefact {candidate.Label} '{fullCandidate}' overlaps staging source '{fullStagingPath}'. " +
                "Keep staging and artefact destination trees separate.");
        }

        string fullScriptRoot = Path.GetFullPath(scriptRoot);
        ValidateScriptBuildRootIsOutsideRepositoryMetadata(fullScriptRoot, "generated script root");
        if (!string.Equals(
                fullScriptRoot,
                fullOutputRoot,
                GetPathComparison(fullScriptRoot, fullOutputRoot)) &&
            IsSameOrBelowPath(fullOutputRoot, fullScriptRoot))
        {
            throw new InvalidOperationException(
                $"Script artefact generated script root '{fullScriptRoot}' contains output root '{fullOutputRoot}' and would erase the output tree while preparing the script layout. " +
                "Keep the generated script root equal to or inside the artefact output root.");
        }

        if (ScriptPathsOverlap(fullScriptRoot, fullProjectRoot) &&
            !IsSameOrBelowPath(fullScriptRoot, fullOutputRoot))
        {
            throw new InvalidOperationException(
                $"Script artefact generated script root '{fullScriptRoot}' overlaps project root '{fullProjectRoot}' and would erase or modify project sources.");
        }

        ValidateScriptDestinationDoesNotTraverseReparsePoint(
            fullOutputRoot,
            ResolveTrustedSystemTemporaryBoundary(fullOutputRoot),
            "output root");
        string? fullTemporaryBuildRoot = null;
        if (!string.IsNullOrWhiteSpace(temporaryBuildRoot))
        {
            fullTemporaryBuildRoot = Path.GetFullPath(temporaryBuildRoot!);
            ValidateScriptDestinationDoesNotTraverseReparsePoint(
                fullTemporaryBuildRoot,
                ResolveTrustedSystemTemporaryBoundary(fullTemporaryBuildRoot),
                "temporary build root");
        }
        ValidateScriptDestinationDoesNotTraverseReparsePoint(
            fullScriptRoot,
            ResolveTrustedScriptDestinationBoundary(fullScriptRoot, fullOutputRoot, fullTemporaryBuildRoot),
            "generated script root");
        if (!string.IsNullOrWhiteSpace(requiredModulesRoot))
        {
            string fullRequiredModulesRoot = Path.GetFullPath(requiredModulesRoot!);
            ValidateScriptBuildRootIsOutsideRepositoryMetadata(fullRequiredModulesRoot, "required modules root");
            ValidateScriptDestinationDoesNotTraverseReparsePoint(
                fullRequiredModulesRoot,
                ResolveTrustedScriptDestinationBoundary(fullRequiredModulesRoot, fullOutputRoot, fullTemporaryBuildRoot),
                "required modules root");
        }
    }

    private static string? ResolveTrustedScriptDestinationBoundary(
        string destination,
        string outputRoot,
        string? temporaryBuildRoot)
    {
        if (IsSameOrBelowPath(destination, outputRoot))
            return outputRoot;
        if (!string.IsNullOrWhiteSpace(temporaryBuildRoot) &&
            IsSameOrBelowPath(destination, temporaryBuildRoot!))
        {
            return temporaryBuildRoot;
        }

        return ResolveTrustedSystemTemporaryBoundary(destination);
    }

    private static string? ResolveTrustedSystemTemporaryBoundary(string destination)
    {
        string systemTemporaryRoot = Path.GetFullPath(Path.GetTempPath());
        return IsSameOrBelowPath(destination, systemTemporaryRoot)
            ? systemTemporaryRoot
            : null;
    }

    private static void ValidateScriptBuildRootIsOutsideRepositoryMetadata(
        string path,
        string description)
    {
        string current = NormalizeScriptValidationPath(path);
        while (true)
        {
            string name = Path.GetFileName(current);
            if (IsRepositoryMetadataDirectoryName(name))
            {
                throw new InvalidOperationException(
                    $"Script artefact {description} '{Path.GetFullPath(path)}' is inside repository metadata '{current}'. " +
                    "Choose an artefact path outside version-control metadata directories.");
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, GetPathComparison(parent, current)))
            {
                return;
            }

            current = parent;
        }
    }

    private static bool IsRepositoryMetadataDirectoryName(string name)
        => string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, ".hg", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, ".svn", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, ".bzr", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, "_darcs", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, ".pijul", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name, ".fossil-settings", StringComparison.OrdinalIgnoreCase);
}
