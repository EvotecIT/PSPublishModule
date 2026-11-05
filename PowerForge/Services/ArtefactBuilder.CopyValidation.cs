using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    // Resolve against the final output namespace before any builder clears existing output.
    // Packed builders remap these relative destinations into their isolated staging directory.
    private void ValidateExtraCopyMappings(
        ArtefactConfiguration cfg, ArtefactType type, string projectRoot, string outputRoot,
        string stagingPath, string moduleName, string moduleVersion, string? preRelease,
        IReadOnlyList<RequiredModuleReference> requiredModules,
        InformationConfiguration? information, DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders, IReadOnlyList<string>? finalizedPayloadFiles)
    {
        if (cfg.DirectoryOutput is not { Length: > 0 } && cfg.FilesOutput is not { Length: > 0 })
            return;

        bool packed = type is ArtefactType.Packed or ArtefactType.ScriptPacked;
        var sources = new List<(string Path, bool IsDirectory)>();
        var treeDestinations = new List<string>();
        var fileDestinations = new List<string>();
        var allFiles = new List<string>();
        var allDirectories = new List<string>();

        void Validate(ArtefactCopyMapping mapping, bool copyTree)
        {
            string source = ResolveInputPath(mapping.Source, projectRoot, moduleName, moduleVersion, preRelease);
            string destination = ResolveOutputPath(mapping.Destination, outputRoot,
                copyTree ? cfg.DestinationDirectoriesRelative == true : cfg.DestinationFilesRelative == true,
                packed, moduleName, moduleVersion, preRelease);
            bool directory = copyTree || Directory.Exists(source);
            try
            {
                ValidateCopyMappingSource(source, directory, cfg.DoNotClear != true, outputRoot, type, copyTree);
            }
            catch (InvalidOperationException error)
            {
                string parameter = copyTree ? "CopyDirectories" : "CopyFiles";
                throw new InvalidOperationException(
                    $"{type} artefact {parameter} mapping '{mapping.Source}' -> '{mapping.Destination}': {error.Message}", error);
            }
            sources.Add((source, copyTree));
            if (directory)
                allDirectories.Add(destination);
            else
            {
                fileDestinations.Add(destination);
                allFiles.Add(destination);
                allDirectories.Add(Path.GetDirectoryName(destination)!);
            }

            if (copyTree)
            {
                treeDestinations.Add(destination);
                foreach (string path in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                    allDirectories.Add(Path.Combine(destination, ComputeRelativePath(source, path)));
                foreach (string path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                    allFiles.Add(Path.Combine(destination, ComputeRelativePath(source, path)));
            }

            if (!packed)
            {
                // These existing guards also protect module artefacts from writing into sources or links.
                if (copyTree)
                    ValidateDirectoryCopyDestinationSafety(destination, outputRoot, outputRoot, projectRoot, stagingPath);
                else
                    ValidateFileCopyDestinationSafety(destination, outputRoot, outputRoot, projectRoot, stagingPath);
                if (cfg.DoNotClear == true || !IsSameOrBelowPath(destination, outputRoot))
                {
                    if (directory ? File.Exists(destination) : Directory.Exists(destination))
                        throw new InvalidOperationException(
                            $"{type} artefact {(directory ? "directory" : "file")} copy destination '{destination}' already exists as a {(directory ? "file" : "directory")}. Choose a destination of the correct kind.");
                }
            }
        }

        foreach (var mapping in cfg.DirectoryOutput ?? Array.Empty<ArtefactCopyMapping>())
            if (mapping is not null)
                Validate(mapping, copyTree: true);
        foreach (var mapping in cfg.FilesOutput ?? Array.Empty<ArtefactCopyMapping>())
            if (mapping is not null)
                Validate(mapping, copyTree: false);

        if (type is ArtefactType.Packed or ArtefactType.Unpacked)
        {
            string requiredRoot = ResolveRequiredModulesRootForUnpacked(cfg, outputRoot, projectRoot, moduleName, moduleVersion, preRelease);
            string modulesRoot = ResolveModulesRootForUnpacked(cfg, outputRoot, requiredRoot, projectRoot, moduleName, moduleVersion, preRelease);
            string moduleRoot = Path.Combine(modulesRoot, moduleName);
            var moduleRoots = new List<string> { moduleRoot };
            if (cfg.RequiredModules.Enabled == true)
                moduleRoots.AddRange(FilterRequiredModulesForArtefact(requiredModules, cfg.RequiredModules.ExcludeModuleName, logExclusions: false)
                    .Select(module => Path.Combine(requiredRoot, module.ModuleName.Trim())));
            var package = ResolveScriptPackageDestinationNamespace(stagingPath, moduleRoot,
                information, delivery, includeScriptFolders, finalizedPayloadFiles);
            allDirectories.AddRange(moduleRoots);
            allDirectories.AddRange(package.Directories.Where(path => !treeDestinations.Any(destination => IsSameOrBelowPath(path, destination))));
            allFiles.AddRange(package.Files.Where(path => !treeDestinations.Any(destination => IsSameOrBelowPath(path, destination))));
            allFiles.AddRange(ResolveDeliveryOutputPaths(cfg, stagingPath, outputRoot,
                information, delivery, includeScriptFolders, finalizedPayloadFiles)
                .Where(path => !treeDestinations.Any(destination => IsSameOrBelowPath(path, destination))));
            foreach (string destination in treeDestinations)
                if (moduleRoots.Any(root => IsSameOrBelowPath(root, destination)))
                    throw new InvalidOperationException(
                        $"{type} artefact directory copy destination '{destination}' would replace a bundled module directory. Choose a separate destination.");
        }

        allDirectories.AddRange(allFiles.Select(path => Path.GetDirectoryName(path)!));
        ValidateDirectoryCopyDestinationsDoNotOverlap(treeDestinations);
        ValidateFileCopyDestinationsDoNotConflict(allDirectories, fileDestinations);
        ValidateCopyDestinationKinds(type, allFiles, allDirectories);
        if (!packed)
            ValidateCopyMappingSourcesSurviveDestinations(sources, treeDestinations, fileDestinations);
    }

    private static void ValidateCopyDestinationKinds(
        ArtefactType type, IEnumerable<string> files, IEnumerable<string> directories)
    {
        // A broad lookup narrows candidates; the filesystem-aware comparison remains authoritative.
        var filesByPath = files.ToLookup(Path.GetFullPath, StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories)
        {
            for (string? ancestor = Path.GetFullPath(directory); ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            {
                foreach (string file in filesByPath[ancestor])
                    if (IsSameOrBelowPath(directory, file))
                        throw new InvalidOperationException(
                            $"{type} artefact file copy destination '{file}' conflicts with directory copy destination '{directory}'. Change one of the destinations.");
            }
        }
    }

    private static void ValidateDirectoryCopyDestinationsDoNotOverlap(
        IReadOnlyList<string> directoryDestinations)
    {
        for (int firstIndex = 0; firstIndex < directoryDestinations.Count; firstIndex++)
        {
            string first = directoryDestinations[firstIndex];
            for (int secondIndex = firstIndex + 1; secondIndex < directoryDestinations.Count; secondIndex++)
            {
                string second = directoryDestinations[secondIndex];
                if (!IsSameOrBelowPath(first, second) && !IsSameOrBelowPath(second, first))
                    continue;

                throw new InvalidOperationException(
                    $"Artefact directory copy destinations '{Path.GetFullPath(first)}' and '{Path.GetFullPath(second)}' overlap. " +
                    "Use separate destination trees so one mapping cannot erase another mapping's payload.");
            }
        }
    }

    private static void ValidateFileCopyDestinationsDoNotConflict(
        IReadOnlyList<string> directoryDestinations,
        IReadOnlyList<string> fileDestinations)
    {
        for (int firstIndex = 0; firstIndex < fileDestinations.Count; firstIndex++)
        {
            string first = fileDestinations[firstIndex];
            for (int secondIndex = firstIndex + 1; secondIndex < fileDestinations.Count; secondIndex++)
            {
                string second = fileDestinations[secondIndex];
                if (!CopyPathsOverlap(first, second))
                    continue;

                throw new InvalidOperationException(
                    $"Artefact file copy destinations '{Path.GetFullPath(first)}' and '{Path.GetFullPath(second)}' overlap. " +
                    "Use separate file paths so one mapping cannot block or overwrite another mapping.");
            }
        }

        foreach (string fileDestination in fileDestinations)
        {
            string? conflictingDirectory = directoryDestinations.FirstOrDefault(directoryDestination =>
                IsSameOrBelowPath(directoryDestination, fileDestination));
            if (conflictingDirectory is not null)
            {
                throw new InvalidOperationException(
                    $"Artefact file copy destination '{Path.GetFullPath(fileDestination)}' is equal to or contains directory copy destination '{Path.GetFullPath(conflictingDirectory)}'. " +
                    "Use separate destination paths so a file mapping cannot collide with a directory mapping.");
            }
        }
    }

    private static bool CopyPathsOverlap(string first, string second) =>
        IsSameOrBelowPath(first, second) || IsSameOrBelowPath(second, first);

    private static void ValidateCopyMappingSource(
        string source,
        bool sourceIsDirectory,
        bool clearsOutput,
        string outputRoot,
        ArtefactType artefactType,
        bool copyContents = true)
    {
        bool exists = sourceIsDirectory ? Directory.Exists(source) : File.Exists(source);
        if (!exists)
        {
            string kind = sourceIsDirectory ? "directory" : "file";
            if (sourceIsDirectory && File.Exists(source))
                throw new InvalidOperationException(
                    $"{artefactType} artefact directory copy source '{source}' is a file. Use CopyFiles for individual files.");
            throw new InvalidOperationException(
                $"Artefact {kind} copy source '{source}' does not exist.");
        }

        if (sourceIsDirectory && copyContents)
        {
            ValidateDirectoryTreeContainsNoReparsePoints(
                source,
                $"Artefact directory copy source '{Path.GetFullPath(source)}'",
                allowCloudPlaceholders: true);
        }
        else if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 &&
                 !WindowsCloudFiles.IsPlaceholder(source))
        {
            throw new InvalidOperationException(
                $"Artefact file copy source '{Path.GetFullPath(source)}' must not be a symbolic link or reparse point.");
        }

        if (!clearsOutput || !WouldOutputClearRemoveSource(source, sourceIsDirectory, outputRoot, artefactType))
            return;

        throw new InvalidOperationException(
            $"Artefact copy source '{source}' would be removed while clearing output root '{Path.GetFullPath(outputRoot)}'. " +
            "Keep copy sources outside the artefact output root or enable DoNotClear.");
    }

    private static bool WouldOutputClearRemoveSource(
        string source,
        bool sourceIsDirectory,
        string outputRoot,
        ArtefactType artefactType)
    {
        if (!IsSameOrBelowPath(source, outputRoot))
            return false;

        if (artefactType is ArtefactType.Script or ArtefactType.Unpacked)
            return true;

        if (artefactType is not (ArtefactType.ScriptPacked or ArtefactType.Packed))
            return false;

        string fullSource = Path.GetFullPath(source);
        string fullOutputRoot = Path.GetFullPath(outputRoot);
        if (string.Equals(fullSource, fullOutputRoot, GetPathComparison(fullSource, fullOutputRoot)))
            return true;
        if (sourceIsDirectory)
            return false;

        string? parent = Path.GetDirectoryName(fullSource);
        return parent is not null &&
               string.Equals(parent, fullOutputRoot, GetPathComparison(parent, fullOutputRoot)) &&
               !WildcardMatch(Path.GetFileName(fullSource), "*.zip");
    }

    private static void ValidateCopyMappingSourcesSurviveDestinations(
        IReadOnlyList<(string Path, bool IsDirectory)> sources,
        IReadOnlyList<string> directoryDestinations,
        IReadOnlyList<string> fileDestinations)
    {
        foreach (string destination in directoryDestinations)
        {
            foreach ((string source, bool sourceIsDirectory) in sources)
            {
                bool overlaps = IsSameOrBelowPath(source, destination) ||
                                (sourceIsDirectory && IsSameOrBelowPath(destination, source));
                if (!overlaps)
                    continue;

                throw new InvalidOperationException(
                    $"Artefact directory copy destination '{Path.GetFullPath(destination)}' overlaps configured copy source '{Path.GetFullPath(source)}'. " +
                    "Use separate source and destination trees.");
            }
        }

        foreach (string destination in fileDestinations)
        {
            foreach ((string source, bool sourceIsDirectory) in sources)
            {
                bool overlaps = sourceIsDirectory
                    ? CopyPathsOverlap(source, destination)
                    : string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), GetPathComparison(source, destination));
                if (!overlaps)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Artefact file copy destination '{Path.GetFullPath(destination)}' overlaps configured copy source '{Path.GetFullPath(source)}'. " +
                    "Use separate source and destination paths.");
            }
        }
    }

    private static void ValidateDirectoryCopyDestinationSafety(
        string destination,
        string trustedDestinationRoot,
        string outputRoot,
        string projectRoot,
        string stagingPath)
    {
        string fullDestination = Path.GetFullPath(destination);
        string fullProjectRoot = Path.GetFullPath(projectRoot);
        ValidateScriptDestinationDoesNotTraverseReparsePoint(
            fullDestination,
            IsSameOrBelowPath(fullDestination, trustedDestinationRoot)
                ? trustedDestinationRoot
                : ResolveTrustedSystemTemporaryBoundary(fullDestination),
            "directory copy destination");
        if (IsSameOrBelowPath(fullProjectRoot, fullDestination))
        {
            throw new InvalidOperationException(
                $"Artefact directory copy destination '{fullDestination}' contains project root '{fullProjectRoot}' and would erase project sources.");
        }
        if (IsSameOrBelowPath(fullDestination, fullProjectRoot) &&
            !IsSameOrBelowPath(fullDestination, outputRoot))
        {
            throw new InvalidOperationException(
                $"Artefact directory copy destination '{fullDestination}' is inside project root '{fullProjectRoot}' and would erase or modify project sources.");
        }

        string fullStagingPath = Path.GetFullPath(stagingPath);
        if (!CopyPathsOverlap(fullDestination, fullStagingPath))
            return;

        throw new InvalidOperationException(
            $"Artefact directory copy destination '{fullDestination}' overlaps staging source '{fullStagingPath}' and would erase or modify build inputs.");
    }

    private static void ValidateFileCopyDestinationSafety(
        string destination,
        string trustedDestinationRoot,
        string outputRoot,
        string projectRoot,
        string stagingPath)
    {
        string fullDestination = Path.GetFullPath(destination);
        string fullProjectRoot = Path.GetFullPath(projectRoot);
        ValidateScriptDestinationDoesNotTraverseReparsePoint(
            fullDestination,
            IsSameOrBelowPath(fullDestination, trustedDestinationRoot)
                ? trustedDestinationRoot
                : ResolveTrustedSystemTemporaryBoundary(fullDestination),
            "file copy destination");
        if (IsSameOrBelowPath(fullDestination, fullProjectRoot) &&
            !IsSameOrBelowPath(fullDestination, outputRoot))
        {
            throw new InvalidOperationException(
                $"Artefact file copy destination '{fullDestination}' is inside project root '{fullProjectRoot}' and would overwrite project content.");
        }

        string fullStagingPath = Path.GetFullPath(stagingPath);
        if (!CopyPathsOverlap(fullDestination, fullStagingPath))
            return;

        throw new InvalidOperationException(
            $"Artefact file copy destination '{fullDestination}' overlaps staging source '{fullStagingPath}'. " +
            "Keep staging and artefact destination paths separate.");
    }

}
