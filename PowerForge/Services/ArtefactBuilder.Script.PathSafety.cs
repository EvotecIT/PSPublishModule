using System;
using System.Collections.Generic;
using System.IO;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
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

    private static void ValidateScriptDestinationDoesNotTraverseReparsePoint(
        string destination,
        string boundary,
        string description)
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

            if (string.Equals(current, fullBoundary, GetPathComparison(current, fullBoundary)))
                return;

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException(
                    $"Script artefact {description} '{fullDestination}' escapes validation boundary '{fullBoundary}'.");
        }
    }
}
