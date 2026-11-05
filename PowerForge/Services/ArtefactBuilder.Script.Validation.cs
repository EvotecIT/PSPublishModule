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
        ScriptPackageDestinationNamespace packageNamespace,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        bool enforceRelativeDestination)
    {
        string[] requiredModuleDestinations = cfg.RequiredModules.Enabled == true
            ? requiredModules
                .Where(static module => module is not null && !string.IsNullOrWhiteSpace(module.ModuleName))
                .Select(module => Path.GetFullPath(Path.Combine(requiredRoot, module.ModuleName.Trim())))
                .ToArray()
            : Array.Empty<string>();

        foreach (var mapping in cfg.DirectoryOutput ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null)
                continue;
            var destination = ResolveOutputPath(
                mapping.Destination,
                destinationRoot,
                cfg.DestinationDirectoriesRelative == true,
                enforceRelativeDestination,
                moduleName,
                moduleVersion,
                preRelease);
            ValidateDirectoryCopyDestinationSafety(
                destination,
                destinationRoot,
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
            ValidateScriptCopyDestinationDoesNotReplacePackage(
                destination,
                packageNamespace,
                "directory");
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
            var destination = ResolveOutputPath(
                mapping.Destination,
                destinationRoot,
                cfg.DestinationFilesRelative == true,
                enforceRelativeDestination,
                moduleName,
                moduleVersion,
                preRelease);
            ValidateFileCopyDestinationSafety(
                destination,
                destinationRoot,
                outputRootToClear,
                projectRoot,
                stagingPath);
            if (Directory.Exists(source))
            {
                string? conflictingFile = packageNamespace.Files.Append(scriptPath)
                    .FirstOrDefault(path => IsSameOrBelowPath(destination, path));
                if (conflictingFile is not null)
                    throw new InvalidOperationException(
                        $"Script artefact directory copy destination '{destination}' conflicts with packaged file '{conflictingFile}'.");
                continue;
            }
            if (CopyPathsOverlap(destination, scriptPath))
            {
                throw new InvalidOperationException(
                    $"Script artefact file copy destination '{destination}' overlaps the generated entry point '{Path.GetFullPath(scriptPath)}'.");
            }
            string? requiredModuleDestination = requiredModuleDestinations.FirstOrDefault(requiredDestination =>
                CopyPathsOverlap(destination, requiredDestination));
            if (requiredModuleDestination is not null)
            {
                throw new InvalidOperationException(
                    $"Script artefact file copy destination '{destination}' overlaps required module destination '{requiredModuleDestination}' and would overwrite or conflict with bundled dependency content.");
            }
            ValidateScriptCopyDestinationDoesNotReplacePackage(
                destination,
                packageNamespace,
                "file");
        }

    }

    private static void ValidateScriptCopyDestinationDoesNotReplacePackage(
        string destination,
        ScriptPackageDestinationNamespace packageNamespace,
        string copyKind)
    {
        string? conflictingFile = packageNamespace.Files.FirstOrDefault(packagePath =>
            CopyPathsOverlap(packagePath, destination));
        string? conflictingDirectory = packageNamespace.Directories.FirstOrDefault(packagePath =>
            IsSameOrBelowPath(packagePath, destination));
        string? conflictingPayload = conflictingFile ?? conflictingDirectory;
        if (conflictingPayload is null)
            return;

        throw new InvalidOperationException(
            $"Script artefact {copyKind} copy destination '{Path.GetFullPath(destination)}' conflicts with packaged payload destination '{conflictingPayload}' and would replace included content.");
    }

    private static void ValidateRequiredModuleDestinations(
        ArtefactConfiguration cfg,
        string requiredRoot,
        string scriptRoot,
        string artefactRoot,
        string projectRoot,
        string stagingPath,
        IReadOnlyList<RequiredModuleReference> requiredModules,
        ScriptPackageDestinationNamespace packageNamespace)
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
            if (CopyPathsOverlap(destination, projectRoot) &&
                !IsSameOrBelowPath(destination, artefactRoot))
            {
                throw new InvalidOperationException(
                    $"Required module destination '{destination}' overlaps project root '{Path.GetFullPath(projectRoot)}' and would erase or modify project sources.");
            }
            ValidateScriptDestinationDoesNotTraverseReparsePoint(
                destination,
                requiredRoot,
                "required module destination");
            if (IsSameOrBelowPath(scriptRoot, destination))
            {
                throw new InvalidOperationException(
                    $"Required module destination '{destination}' contains the generated script root '{Path.GetFullPath(scriptRoot)}' and would erase it.");
            }

            string? conflictingFile = packageNamespace.Files.FirstOrDefault(packagePath =>
                CopyPathsOverlap(packagePath, destination));
            string? conflictingDirectory = packageNamespace.Directories.FirstOrDefault(packagePath =>
                IsSameOrBelowPath(packagePath, destination));
            string? conflictingPayload = conflictingFile ?? conflictingDirectory;
            if (conflictingPayload is not null)
            {
                throw new InvalidOperationException(
                    $"Required module destination '{destination}' conflicts with packaged payload destination '{conflictingPayload}' and would replace included module content.");
            }
        }
    }

    private static IEnumerable<string> EnumerateUnsupportedScriptEvidencePaths(string scriptRoot, string moduleName)
    {
        yield return Path.Combine(scriptRoot, moduleName + ".powerforge-compilation.json");
        yield return Path.Combine(scriptRoot, moduleName + ".powerforge-compilation.p7s");
        yield return Path.Combine(scriptRoot, PowerForgeModuleSourceAttestationWriter.FileName);
        yield return Path.Combine(scriptRoot, PublishedRegistryProvenanceValidator.ModuleProvenanceFileName);
    }

    private static void RemoveUnsupportedEvidenceFromTransformedScript(string scriptRoot, string moduleName)
    {
        foreach (string path in EnumerateUnsupportedScriptEvidencePaths(scriptRoot, moduleName))
            if (File.Exists(path))
                File.Delete(path);
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
