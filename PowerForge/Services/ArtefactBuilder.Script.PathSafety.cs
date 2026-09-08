using System;
using System.Collections.Generic;
using System.IO;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
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
