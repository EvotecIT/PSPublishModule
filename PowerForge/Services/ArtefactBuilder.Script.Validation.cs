using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static void ValidateScriptSourceLayout(
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
        var configuredRootModule = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "RootModule");
        var legacyRootModule = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleToProcess");
        var effectiveRootModule = string.IsNullOrWhiteSpace(configuredRootModule)
            ? legacyRootModule
            : configuredRootModule;
        var modulePath = Path.Combine(stagingPath, expectedRootModule);
        if (!string.IsNullOrWhiteSpace(effectiveRootModule) &&
            !string.Equals(
                Path.GetFullPath(Path.Combine(
                    stagingPath,
                    effectiveRootModule!.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))),
                Path.GetFullPath(modulePath),
                GetPathComparison(stagingPath, modulePath)))
        {
            throw new InvalidOperationException(
                $"Script artefacts require the staged script root module '{expectedRootModule}', but the module manifest selects '{effectiveRootModule}'. " +
                "Binary-only and nested root modules cannot be converted to a standalone script.");
        }

        if (!File.Exists(modulePath))
        {
            throw new InvalidOperationException(
                $"Script artefacts require the staged script root module '{expectedRootModule}', but it was not found. " +
                "Binary-only compiled modules cannot be converted to a standalone script.");
        }

        var manifestLoadedKeys = new List<string>();
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "NestedModules", moduleReferences: true);
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "RequiredAssemblies");
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "ScriptsToProcess");
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "TypesToProcess");
        AddManifestLoadedKey(manifestLoadedKeys, manifestPath, "FormatsToProcess");
        if (ModuleManifestValueReader.ReadRequiredModules(manifestPath).Any())
            manifestLoadedKeys.Add("RequiredModules");
        if (manifestLoadedKeys.Count > 0)
        {
            throw new InvalidOperationException(
                "Script artefacts cannot discard manifest-loaded content. Remove or merge these manifest entries into the script before packaging: " +
                string.Join(", ", manifestLoadedKeys) + ".");
        }

        if (!string.IsNullOrWhiteSpace(preScriptMerge) &&
            HasLeadingScriptParameterBlock(ModuleManifestValueReader.ReadPowerShellCompatibleText(modulePath)))
        {
            throw new InvalidOperationException(
                "PreScriptMerge cannot be injected before a staged module parameter block. " +
                "Merge the script-level parameters into one leading param block before building a Script or ScriptPacked artefact.");
        }

        var scriptSourcePath = Path.GetFullPath(Path.Combine(stagingPath, scriptName));
        if (ResolveModulePackageSourceFiles(
                stagingPath,
                information,
                delivery,
                includeScriptFolders,
                finalizedPayloadFiles)
            .Any(sourcePath => string.Equals(
                Path.GetFullPath(sourcePath),
                scriptSourcePath,
                GetPathComparison(sourcePath, scriptSourcePath))))
        {
            throw new InvalidOperationException(
                $"ScriptName '{scriptName}' conflicts with a packaged payload file. Choose an entry point name that does not replace included module content.");
        }
    }

    private static void AddManifestLoadedKey(
        List<string> keys,
        string manifestPath,
        string key,
        bool moduleReferences = false)
    {
        var values = moduleReferences
            ? ModuleManifestValueReader.ReadTopLevelModuleReferencePaths(manifestPath, key)
            : ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, key);
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
        string scriptRoot,
        string scriptPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        bool enforceRelativeDestination)
    {
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
            if (IsSameOrBelowPath(scriptRoot, destination))
            {
                throw new InvalidOperationException(
                    $"Script artefact directory copy destination '{destination}' contains the generated script root '{Path.GetFullPath(scriptRoot)}' and would erase it.");
            }
        }

        foreach (var mapping in cfg.FilesOutput ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null)
                continue;
            var destination = ResolveOutputPath(
                mapping.Destination,
                destinationRoot,
                cfg.DestinationFilesRelative == true,
                enforceRelativeDestination,
                moduleName,
                moduleVersion,
                preRelease);
            if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(scriptPath), GetPathComparison(destination, scriptPath)))
            {
                throw new InvalidOperationException(
                    $"Script artefact file copy destination '{destination}' would overwrite the generated entry point '{Path.GetFullPath(scriptPath)}'.");
            }
        }
    }

    private static void RemoveCompilationEvidenceFromTransformedScript(string scriptRoot, string moduleName)
    {
        foreach (var suffix in new[] { ".powerforge-compilation.json", ".powerforge-compilation.p7s" })
        {
            var path = Path.Combine(scriptRoot, moduleName + suffix);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
