using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static string ValidateScriptSourceLayout(
        string stagingPath,
        string moduleName,
        string? preScriptMerge,
        string scriptName,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders,
        IReadOnlyList<string>? finalizedPayloadFiles)
    {
        var manifestPath = Path.Combine(stagingPath, moduleName + ".psd1");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("The staged module manifest required for a script artefact was not found.", manifestPath);

        var expectedRootModule = moduleName + ".psm1";
        var effectiveRootModule = ModuleManifestValueReader.ReadModuleEntryPoint(manifestPath, out _);
        var modulePath = Path.Combine(stagingPath, expectedRootModule);
        if (string.IsNullOrWhiteSpace(effectiveRootModule) ||
            !string.Equals(
                Path.GetFullPath(Path.Combine(
                    stagingPath,
                    (effectiveRootModule ?? string.Empty).Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))),
                Path.GetFullPath(modulePath),
                GetPathComparison(stagingPath, modulePath)))
        {
            throw new InvalidOperationException(
                $"Script artefacts require the module manifest to select the staged script root module '{expectedRootModule}', but it selects '{effectiveRootModule ?? "(empty)"}'. " +
                "Binary-only and nested root modules cannot be converted to a standalone script.");
        }

        if (!File.Exists(modulePath))
        {
            throw new InvalidOperationException(
                $"Script artefacts require the staged script root module '{expectedRootModule}', but it was not found. " +
                "Binary-only compiled modules cannot be converted to a standalone script.");
        }

        string manifestRuntimePreamble = CreateScriptManifestRuntimePreamble(manifestPath);

        var packageSourceFiles = ResolveModulePackageSourceFiles(
            stagingPath,
            information,
            delivery,
            includeScriptFolders,
            finalizedPayloadFiles);
        var selectedPayload = new HashSet<string>(
            packageSourceFiles.Select(Path.GetFullPath),
            CreateCurrentFileSystemPathComparer());
        var payloadDescription = finalizedPayloadFiles is { Count: > 0 }
            ? "finalized module payload"
            : "module package selection";
        if (!selectedPayload.Contains(Path.GetFullPath(manifestPath)))
        {
            throw new InvalidOperationException(
                $"The {payloadDescription} must include the script artefact manifest '{moduleName}.psd1'.");
        }
        if (!selectedPayload.Contains(Path.GetFullPath(modulePath)))
        {
            throw new InvalidOperationException(
                $"The {payloadDescription} must include the script root module '{expectedRootModule}'.");
        }

        var manifestLoadedKeys = new List<string>();
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "NestedModules", moduleReferences: true);
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "RequiredAssemblies");
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "ScriptsToProcess");
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "TypesToProcess");
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "FormatsToProcess");
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "RequiredModules", moduleReferences: true);
        if (manifestLoadedKeys.Count > 0)
        {
            throw new InvalidOperationException(
                "Script artefacts cannot discard manifest-loaded content. Remove or merge these manifest entries into the script before packaging: " +
                string.Join(", ", manifestLoadedKeys) + ".");
        }

        if (HasExecutablePreScriptContent(preScriptMerge) &&
            HasLeadingScriptParameterBlock(ModuleManifestValueReader.ReadPowerShellCompatibleText(modulePath)))
        {
            throw new InvalidOperationException(
                "PreScriptMerge cannot be injected before a staged module parameter block. " +
                "Merge the script-level parameters into one leading param block before building a Script or ScriptPacked artefact.");
        }

        var scriptSourcePath = Path.GetFullPath(Path.Combine(stagingPath, scriptName));
        if (packageSourceFiles.Any(sourcePath => string.Equals(
                Path.GetFullPath(sourcePath),
                scriptSourcePath,
                GetPathComparison(sourcePath, scriptSourcePath))))
        {
            throw new InvalidOperationException(
                $"ScriptName '{scriptName}' conflicts with a packaged payload file. Choose an entry point name that does not replace included module content.");
        }

        return manifestRuntimePreamble;
    }

    private static bool HasExecutablePreScriptContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        _ = ModuleMergeComposer.ExtractMergedScriptPreamble(content, out string body);
        return SkipPowerShellTrivia(body, 0) < body.Length;
    }

    private static void AddManifestLoadedKey(
        List<string> keys,
        string manifestPath,
        string key,
        bool moduleReferences = false)
    {
        var values = moduleReferences
            ? ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, key)
            : ModuleManifestValueReader.ReadTopLevelLiteralStringOrArrayOrThrow(
                manifestPath,
                key,
                "Script and ScriptPacked conversion") ?? Array.Empty<string>();
        if (values.Length > 0)
            keys.Add(key);
    }

    private static bool HasLeadingScriptParameterBlock(string content)
    {
        _ = ModuleMergeComposer.ExtractMergedScriptPreamble(content, out var body);
        var index = SkipPowerShellTrivia(body, 0);
        while (index < body.Length && body[index] == '[')
        {
            var closing = FindMatchingPowerShellBracket(body, index);
            if (closing < 0)
                return false;
            index = SkipPowerShellTrivia(body, closing + 1);
        }

        const string keyword = "param";
        if (index + keyword.Length > body.Length ||
            !string.Equals(body.Substring(index, keyword.Length), keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var keywordEnd = index + keyword.Length;
        if (keywordEnd < body.Length && (char.IsLetterOrDigit(body[keywordEnd]) || body[keywordEnd] == '_'))
            return false;
        return SkipPowerShellTrivia(body, keywordEnd) is var opening && opening < body.Length && body[opening] == '(';
    }

    private static int SkipPowerShellTrivia(string text, int start)
    {
        var index = start;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            if (text[index] == '#')
            {
                while (index < text.Length && text[index] != '\r' && text[index] != '\n')
                    index++;
                continue;
            }

            if (index + 1 < text.Length && text[index] == '<' && text[index + 1] == '#')
            {
                var depth = 1;
                index += 2;
                while (index < text.Length && depth > 0)
                {
                    if (index + 1 < text.Length && text[index] == '<' && text[index + 1] == '#')
                    {
                        depth++;
                        index += 2;
                    }
                    else if (index + 1 < text.Length && text[index] == '#' && text[index + 1] == '>')
                    {
                        depth--;
                        index += 2;
                    }
                    else
                    {
                        index++;
                    }
                }
                continue;
            }

            break;
        }
        return index;
    }

    private static int FindMatchingPowerShellBracket(string text, int opening)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = opening; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (quote != '\0')
            {
                if (quote == '"' && current == '`')
                {
                    index++;
                    continue;
                }
                if (current != quote)
                    continue;
                if (quote == '\'' && next == '\'')
                {
                    index++;
                    continue;
                }
                quote = '\0';
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }
            if (current == '#')
            {
                while (index < text.Length && text[index] != '\r' && text[index] != '\n')
                    index++;
                continue;
            }
            if (current == '<' && next == '#')
            {
                var end = text.IndexOf("#>", index + 2, StringComparison.Ordinal);
                if (end < 0)
                    return -1;
                index = end + 1;
                continue;
            }
            if (current == '[')
                depth++;
            else if (current == ']' && --depth == 0)
                return index;
        }
        return -1;
    }

    private static void ValidateScriptCopyMappings(
        ArtefactConfiguration cfg,
        string destinationRoot,
        string outputRootToClear,
        string scriptRoot,
        string scriptPath,
        string requiredRoot,
        IReadOnlyList<RequiredModuleReference> requiredModules,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        ArtefactType artefactType,
        bool enforceRelativeDestination)
    {
        string[] requiredModuleDestinations = cfg.RequiredModules.Enabled == true
            ? requiredModules
                .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.ModuleName))
                .Select(module => Path.GetFullPath(Path.Combine(requiredRoot, module.ModuleName.Trim())))
                .ToArray()
            : Array.Empty<string>();
        var mappingSources = new List<(string Path, bool IsDirectory)>();
        var directoryDestinations = new List<string>();
        var fileDestinations = new List<string>();

        foreach (var mapping in cfg.DirectoryOutput ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null)
                continue;
            var source = ResolveInputPath(
                mapping.Source,
                projectRoot,
                moduleName,
                moduleVersion,
                preRelease);
            ValidateScriptCopyMappingSource(
                source,
                sourceIsDirectory: true,
                cfg.DoNotClear != true,
                outputRootToClear,
                artefactType);
            mappingSources.Add((source, true));
            var destination = ResolveOutputPath(
                mapping.Destination,
                destinationRoot,
                cfg.DestinationDirectoriesRelative == true,
                enforceRelativeDestination,
                moduleName,
                moduleVersion,
                preRelease);
            directoryDestinations.Add(destination);
            ValidateScriptDirectoryCopyDestinationSafety(
                destination,
                outputRootToClear,
                projectRoot,
                stagingPath);
            if (IsSameOrBelowPath(scriptRoot, destination))
            {
                throw new InvalidOperationException(
                    $"Script artefact directory copy destination '{destination}' contains the generated script root '{Path.GetFullPath(scriptRoot)}' and would erase it.");
            }
            string? requiredModuleDestination = requiredModuleDestinations.FirstOrDefault(requiredDestination =>
                IsSameOrBelowPath(destination, requiredDestination) ||
                IsSameOrBelowPath(requiredDestination, destination));
            if (requiredModuleDestination is not null)
            {
                throw new InvalidOperationException(
                    $"Script artefact directory copy destination '{destination}' overlaps required module destination '{requiredModuleDestination}' and could erase or corrupt the bundled dependency.");
            }
        }

        foreach (var mapping in cfg.FilesOutput ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null)
                continue;
            var source = ResolveInputPath(
                mapping.Source,
                projectRoot,
                moduleName,
                moduleVersion,
                preRelease);
            ValidateScriptCopyMappingSource(
                source,
                sourceIsDirectory: false,
                cfg.DoNotClear != true,
                outputRootToClear,
                artefactType);
            mappingSources.Add((source, false));
            var destination = ResolveOutputPath(
                mapping.Destination,
                destinationRoot,
                cfg.DestinationFilesRelative == true,
                enforceRelativeDestination,
                moduleName,
                moduleVersion,
                preRelease);
            fileDestinations.Add(destination);
            ValidateScriptFileCopyDestinationSafety(
                destination,
                outputRootToClear,
                projectRoot,
                stagingPath);
            if (ScriptPathsOverlap(destination, scriptPath))
            {
                throw new InvalidOperationException(
                    $"Script artefact file copy destination '{destination}' overlaps the generated entry point '{Path.GetFullPath(scriptPath)}'.");
            }
            string? requiredModuleDestination = requiredModuleDestinations.FirstOrDefault(requiredDestination =>
                ScriptPathsOverlap(destination, requiredDestination));
            if (requiredModuleDestination is not null)
            {
                throw new InvalidOperationException(
                    $"Script artefact file copy destination '{destination}' overlaps required module destination '{requiredModuleDestination}' and would overwrite or conflict with bundled dependency content.");
            }
        }

        ValidateScriptCopyMappingSourcesSurviveDestinations(
            mappingSources,
            directoryDestinations,
            fileDestinations);
        ValidateScriptDirectoryCopyDestinationsDoNotOverlap(directoryDestinations);
        ValidateScriptFileCopyDestinationsDoNotConflict(directoryDestinations, fileDestinations);
    }

    private static void ValidateScriptDirectoryCopyDestinationSafety(
        string destination,
        string outputRoot,
        string projectRoot,
        string stagingPath)
    {
        string fullDestination = Path.GetFullPath(destination);
        string fullProjectRoot = Path.GetFullPath(projectRoot);
        ValidateScriptDestinationDoesNotTraverseReparsePoint(
            fullDestination,
            outputRoot,
            "directory copy destination",
            projectRoot,
            stagingPath);
        if (IsSameOrBelowPath(fullProjectRoot, fullDestination))
        {
            throw new InvalidOperationException(
                $"Script artefact directory copy destination '{fullDestination}' contains project root '{fullProjectRoot}' and would erase project sources.");
        }
        if (IsSameOrBelowPath(fullDestination, fullProjectRoot) &&
            !IsSameOrBelowPath(fullDestination, outputRoot))
        {
            throw new InvalidOperationException(
                $"Script artefact directory copy destination '{fullDestination}' is inside project root '{fullProjectRoot}' and would erase or modify project sources.");
        }

        string fullStagingPath = Path.GetFullPath(stagingPath);
        if (!ScriptPathsOverlap(fullDestination, fullStagingPath))
            return;

        throw new InvalidOperationException(
            $"Script artefact directory copy destination '{fullDestination}' overlaps staging source '{fullStagingPath}' and would erase or modify build inputs.");
    }

    private static void ValidateScriptFileCopyDestinationSafety(
        string destination,
        string outputRoot,
        string projectRoot,
        string stagingPath)
    {
        string fullDestination = Path.GetFullPath(destination);
        string fullProjectRoot = Path.GetFullPath(projectRoot);
        ValidateScriptDestinationDoesNotTraverseReparsePoint(
            fullDestination,
            outputRoot,
            "file copy destination",
            projectRoot,
            stagingPath);
        if (IsSameOrBelowPath(fullDestination, fullProjectRoot) &&
            !IsSameOrBelowPath(fullDestination, outputRoot))
        {
            throw new InvalidOperationException(
                $"Script artefact file copy destination '{fullDestination}' is inside project root '{fullProjectRoot}' and would overwrite project content.");
        }

        string fullStagingPath = Path.GetFullPath(stagingPath);
        if (!ScriptPathsOverlap(fullDestination, fullStagingPath))
            return;

        throw new InvalidOperationException(
            $"Script artefact file copy destination '{fullDestination}' overlaps staging source '{fullStagingPath}'. " +
            "Keep staging and artefact destination paths separate.");
    }

    private static void ValidateScriptDirectoryCopyDestinationsDoNotOverlap(
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
                    $"Script artefact directory copy destinations '{Path.GetFullPath(first)}' and '{Path.GetFullPath(second)}' overlap. " +
                    "Use separate destination trees so one mapping cannot erase another mapping's payload.");
            }
        }
    }

    private static void ValidateScriptFileCopyDestinationsDoNotConflict(
        IReadOnlyList<string> directoryDestinations,
        IReadOnlyList<string> fileDestinations)
    {
        foreach (string fileDestination in fileDestinations)
        {
            string? conflictingDirectory = directoryDestinations.FirstOrDefault(directoryDestination =>
                IsSameOrBelowPath(directoryDestination, fileDestination));
            if (conflictingDirectory is not null)
            {
                throw new InvalidOperationException(
                    $"Script artefact file copy destination '{Path.GetFullPath(fileDestination)}' is equal to or contains directory copy destination '{Path.GetFullPath(conflictingDirectory)}'. " +
                    "Use separate destination paths so a file mapping cannot collide with a directory mapping.");
            }
        }

        for (int firstIndex = 0; firstIndex < fileDestinations.Count; firstIndex++)
        {
            string first = fileDestinations[firstIndex];
            for (int secondIndex = firstIndex + 1; secondIndex < fileDestinations.Count; secondIndex++)
            {
                string second = fileDestinations[secondIndex];
                if (!ScriptPathsOverlap(first, second))
                    continue;

                throw new InvalidOperationException(
                    $"Script artefact file copy destinations '{Path.GetFullPath(first)}' and '{Path.GetFullPath(second)}' overlap. " +
                    "Use separate file paths so one mapping cannot block or overwrite another mapping.");
            }
        }
    }

    private static bool ScriptPathsOverlap(string first, string second) =>
        IsSameOrBelowPath(first, second) || IsSameOrBelowPath(second, first);

    private static void ValidateScriptBuildRootsDoNotOverlapStaging(
        string stagingPath,
        string outputRoot,
        string projectRoot,
        string scriptRoot,
        string? requiredModulesRoot,
        bool rejectOutputRootContainingProject)
    {
        string fullOutputRoot = Path.GetFullPath(outputRoot);
        string fullProjectRoot = Path.GetFullPath(projectRoot);
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
            fullOutputRoot,
            "output root",
            projectRoot,
            stagingPath);
        ValidateScriptDestinationDoesNotTraverseReparsePoint(
            fullScriptRoot,
            fullOutputRoot,
            "generated script root",
            projectRoot,
            stagingPath);
    }

    private static void ValidateScriptCopyMappingSource(
        string source,
        bool sourceIsDirectory,
        bool clearsOutput,
        string outputRoot,
        ArtefactType artefactType)
    {
        bool exists = sourceIsDirectory ? Directory.Exists(source) : File.Exists(source);
        if (!exists)
        {
            string kind = sourceIsDirectory ? "directory" : "file";
            throw new InvalidOperationException(
                $"Script artefact {kind} copy source '{source}' does not exist.");
        }

        if (!clearsOutput || !WouldScriptOutputClearRemoveSource(source, sourceIsDirectory, outputRoot, artefactType))
            return;

        throw new InvalidOperationException(
            $"Script artefact copy source '{source}' would be removed while clearing output root '{Path.GetFullPath(outputRoot)}'. " +
            "Keep copy sources outside the artefact output root or enable DoNotClear.");
    }

    private static bool WouldScriptOutputClearRemoveSource(
        string source,
        bool sourceIsDirectory,
        string outputRoot,
        ArtefactType artefactType)
    {
        if (!IsSameOrBelowPath(source, outputRoot))
            return false;

        if (artefactType == ArtefactType.Script)
            return true;

        if (artefactType != ArtefactType.ScriptPacked)
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

    private static void ValidateScriptCopyMappingSourcesSurviveDestinations(
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
                    $"Script artefact directory copy destination '{Path.GetFullPath(destination)}' overlaps configured copy source '{Path.GetFullPath(source)}'. " +
                    "Use separate source and destination trees.");
            }
        }

        foreach (string destination in fileDestinations)
        {
            foreach ((string source, bool sourceIsDirectory) in sources)
            {
                bool overlaps = sourceIsDirectory
                    ? ScriptPathsOverlap(source, destination)
                    : string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), GetPathComparison(source, destination));
                if (!overlaps)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Script artefact file copy destination '{Path.GetFullPath(destination)}' overlaps configured copy source '{Path.GetFullPath(source)}'. " +
                    "Use separate source and destination paths.");
            }
        }
    }

    private static void ValidateRequiredModuleDestinations(
        ArtefactConfiguration cfg,
        string requiredRoot,
        string scriptRoot,
        string artefactRoot,
        string projectRoot,
        string stagingPath,
        IReadOnlyList<RequiredModuleReference> requiredModules)
    {
        if (cfg.RequiredModules.Enabled != true)
            return;

        foreach (var module in requiredModules)
        {
            if (module is null || string.IsNullOrWhiteSpace(module.ModuleName))
                continue;

            var destination = Path.GetFullPath(Path.Combine(requiredRoot, module.ModuleName.Trim()));
            if (!IsSameOrBelowPath(destination, requiredRoot))
            {
                throw new InvalidOperationException(
                    $"Required module destination '{destination}' resolves outside required modules root '{Path.GetFullPath(requiredRoot)}'.");
            }
            if (ScriptPathsOverlap(destination, projectRoot) &&
                !IsSameOrBelowPath(destination, artefactRoot))
            {
                throw new InvalidOperationException(
                    $"Required module destination '{destination}' overlaps project root '{Path.GetFullPath(projectRoot)}' and would erase or modify project sources.");
            }
            ValidateScriptDestinationDoesNotTraverseReparsePoint(
                destination,
                requiredRoot,
                "required module destination",
                projectRoot,
                stagingPath);
            if (!IsSameOrBelowPath(scriptRoot, destination))
                continue;

            throw new InvalidOperationException(
                $"Required module destination '{destination}' contains the generated script root '{Path.GetFullPath(scriptRoot)}' and would erase it.");
        }
    }

    private static void RemoveUnsupportedEvidenceFromTransformedScript(string scriptRoot, string moduleName)
    {
        foreach (var suffix in new[] { ".powerforge-compilation.json", ".powerforge-compilation.p7s" })
        {
            var path = Path.Combine(scriptRoot, moduleName + suffix);
            if (File.Exists(path))
                File.Delete(path);
        }

        foreach (string fileName in new[]
                 {
                     PowerForgeModuleSourceAttestationWriter.FileName,
                     PublishedRegistryProvenanceValidator.ModuleProvenanceFileName
                 })
        {
            string path = Path.Combine(scriptRoot, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static string RemoveTrailingAuthenticodeSignatureBlock(string content)
    {
        const string begin = "# SIG # Begin signature block";
        const string end = "# SIG # End signature block";
        int markerIndex = content.LastIndexOf(begin, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || !TryGetExactMarkerLine(content, markerIndex, begin, out var beginLineStart, out var beginLineEnd))
            return content;

        int endMarkerIndex = content.LastIndexOf(end, StringComparison.OrdinalIgnoreCase);
        if (endMarkerIndex <= markerIndex ||
            !TryGetExactMarkerLine(content, endMarkerIndex, end, out _, out var endLineEnd) ||
            !string.IsNullOrWhiteSpace(content.Substring(endLineEnd)) ||
            !ContainsOnlySignatureCommentLines(content, beginLineEnd, endMarkerIndex))
        {
            return content;
        }

        return content.Substring(0, beginLineStart).TrimEnd() + Environment.NewLine;
    }

    private static bool TryGetExactMarkerLine(
        string content,
        int markerIndex,
        string marker,
        out int lineStart,
        out int lineEnd)
    {
        lineStart = markerIndex;
        while (lineStart > 0 && content[lineStart - 1] != '\r' && content[lineStart - 1] != '\n')
            lineStart--;

        lineEnd = markerIndex + marker.Length;
        while (lineEnd < content.Length && content[lineEnd] != '\r' && content[lineEnd] != '\n')
            lineEnd++;

        return string.Equals(
            content.Substring(lineStart, lineEnd - lineStart).Trim(),
            marker,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOnlySignatureCommentLines(string content, int start, int end)
    {
        var lineStart = start;
        while (lineStart < end)
        {
            while (lineStart < end && (content[lineStart] == '\r' || content[lineStart] == '\n'))
                lineStart++;
            if (lineStart >= end)
                break;

            var lineEnd = lineStart;
            while (lineEnd < end && content[lineEnd] != '\r' && content[lineEnd] != '\n')
                lineEnd++;
            var line = content.Substring(lineStart, lineEnd - lineStart).TrimStart();
            if (line.Length > 0 && line[0] != '#')
                return false;
            lineStart = lineEnd;
        }

        return true;
    }
}
