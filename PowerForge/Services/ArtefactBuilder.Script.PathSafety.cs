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
        string projectRoot,
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
                "module package destination",
                projectRoot,
                stagingPath);
        }
    }

    private static void ValidateScriptDestinationDoesNotTraverseReparsePoint(
        string destination,
        string boundary,
        string description,
        string projectRoot,
        string stagingPath)
    {
        string fullDestination = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullBoundary = Path.GetFullPath(boundary)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!IsSameOrBelowPath(fullDestination, fullBoundary))
            return;

        string current = fullDestination;
        while (true)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    // A shared outer alias (for example macOS /var) keeps every protected path in
                    // the same lexical namespace. An unshared ancestor can redirect only the output
                    // boundary and bypass the project/staging overlap checks.
                    bool isSharedAncestorOutsideBoundary =
                        !IsSameOrBelowPath(current, fullBoundary) &&
                        IsSameOrBelowPath(projectRoot, current) &&
                        IsSameOrBelowPath(stagingPath, current);
                    if (!isSharedAncestorOutsideBoundary)
                    {
                        throw new InvalidOperationException(
                            $"Script artefact {description} '{fullDestination}' traverses a symbolic link or reparse point at '{current}'.");
                    }
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
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, GetPathComparison(parent, current)))
            {
                return;
            }

            current = parent;
        }
    }
}
