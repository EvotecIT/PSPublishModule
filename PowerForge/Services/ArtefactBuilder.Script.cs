using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private ArtefactBuildResult BuildScript(
        ArtefactConfiguration cfg,
        string outputRoot,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<RequiredModuleReference> requiredModules,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders,
        Func<PackedArtefactFinalizationContext, IReadOnlyList<string>?>? finalizeArtefact,
        IReadOnlyList<string>? finalizedPayloadFiles)
    {
        var scriptName = ResolveScriptName(cfg.ScriptName, moduleName, moduleVersion, preRelease);
        string manifestRuntimePreamble = ValidateScriptSourceLayout(
            stagingPath,
            moduleName,
            cfg.PreScriptMerge,
            information,
            delivery,
            includeScriptFolders,
            finalizedPayloadFiles);

        var requiredRoot = ResolveRequiredModulesRootForUnpacked(
            cfg,
            outputRoot,
            projectRoot,
            moduleName,
            moduleVersion,
            preRelease);

        var scriptRoot = ResolveModulesRootForUnpacked(
            cfg,
            outputRoot,
            requiredRoot,
            projectRoot,
            moduleName,
            moduleVersion,
            preRelease);
        ScriptPackageDestinationNamespace packageNamespace = ResolveScriptPackageDestinationNamespace(
            stagingPath,
            scriptRoot,
            information,
            delivery,
            includeScriptFolders,
            finalizedPayloadFiles);
        ValidateScriptEntryPointDoesNotConflictWithPackage(scriptRoot, scriptName, packageNamespace);
        ValidateScriptBuildRootsDoNotOverlapStaging(
            stagingPath,
            outputRoot,
            projectRoot,
            scriptRoot,
            cfg.RequiredModules.Enabled == true ? requiredRoot : null,
            temporaryBuildRoot: null,
            rejectOutputRootContainingProject: true);
        ValidateScriptPackageDestinationsDoNotTraverseReparsePoints(
            scriptRoot,
            stagingPath,
            information,
            delivery,
            includeScriptFolders,
            finalizedPayloadFiles);
        var filteredRequiredModules = FilterRequiredModulesForArtefact(
            requiredModules,
            cfg.RequiredModules.ExcludeModuleName);
        ValidateRequiredModuleDestinations(
            cfg,
            requiredRoot,
            scriptRoot,
            outputRoot,
            projectRoot,
            stagingPath,
            filteredRequiredModules,
            packageNamespace);
        ValidateScriptCopyMappings(
            cfg,
            outputRoot,
            outputRoot,
            scriptRoot,
            Path.Combine(scriptRoot, scriptName),
            requiredRoot,
            filteredRequiredModules,
            packageNamespace,
            projectRoot,
            stagingPath,
            moduleName,
            moduleVersion,
            preRelease,
            ArtefactType.Script,
            enforceRelativeDestination: false);

        if (cfg.DoNotClear != true)
            ClearDirectorySafe(outputRoot);
        else
            Directory.CreateDirectory(outputRoot);

        var copied = new List<ArtefactCopyEntry>();
        var modules = new List<ArtefactModuleEntry>();
        var evidencePaths = Array.Empty<string>();
        _logger.Info($"Creating script artefact at '{scriptRoot}'");
        string scriptPath = BuildScriptLayout(
            cfg,
            scriptRoot,
            stagingPath,
            moduleName,
            moduleVersion,
            preRelease,
            information,
            delivery,
            includeScriptFolders,
            finalizedPayloadFiles,
            manifestRuntimePreamble,
            clearDestination: cfg.DoNotClear != true);
        modules.Add(new ArtefactModuleEntry(moduleName, isMainModule: true, version: moduleVersion, path: scriptRoot));

        AddRequiredModules(cfg, requiredRoot, requiredModules, modules);
        CopyExtraMappings(cfg, projectRoot, outputRoot, moduleName, moduleVersion, preRelease, copied);

        if (finalizeArtefact is not null)
        {
            string version = ModulePathTokenFormatter.FormatVersionWithPreRelease(moduleVersion, preRelease);
            var context = new PackedArtefactFinalizationContext(
                ArtefactType.Script,
                outputRoot,
                scriptRoot,
                string.Empty,
                scriptPath,
                outputRoot,
                moduleName,
                version);
            evidencePaths = FinalizeArtefactLayout(finalizeArtefact, context);
        }

        return new ArtefactBuildResult(
            ArtefactType.Script,
            cfg.ID,
            outputRoot,
            modules.ToArray(),
            copied.ToArray(),
            evidencePaths,
            FrameworkCompatibility.GetRelativePath(outputRoot, scriptPath));
    }

    private ArtefactBuildResult BuildScriptPacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<RequiredModuleReference> requiredModules,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders,
        Func<PackedArtefactFinalizationContext, IReadOnlyList<string>?>? finalizeArtefact,
        IReadOnlyList<string>? finalizedPayloadFiles)
    {
        var scriptName = ResolveScriptName(cfg.ScriptName, moduleName, moduleVersion, preRelease);
        string manifestRuntimePreamble = ValidateScriptSourceLayout(
            stagingPath,
            moduleName,
            cfg.PreScriptMerge,
            information,
            delivery,
            includeScriptFolders,
            finalizedPayloadFiles);
        var artefactName = ResolveArtefactFileName(cfg, moduleName, moduleVersion, preRelease);
        var zipPath = Path.Combine(outputRoot, artefactName);
        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "artefacts", $"{moduleName}_{Guid.NewGuid():N}");
        var requiredRoot = ResolveRequiredModulesRootForPacked(
            cfg,
            outputRoot,
            tempRoot,
            moduleName,
            moduleVersion,
            preRelease);
        var scriptRoot = ResolveModulesRootForPacked(
            cfg,
            outputRoot,
            tempRoot,
            requiredRoot,
            moduleName,
            moduleVersion,
            preRelease);
        ScriptPackageDestinationNamespace packageNamespace = ResolveScriptPackageDestinationNamespace(
            stagingPath,
            scriptRoot,
            information,
            delivery,
            includeScriptFolders,
            finalizedPayloadFiles);
        ValidateScriptEntryPointDoesNotConflictWithPackage(scriptRoot, scriptName, packageNamespace);
        ValidateScriptBuildRootsDoNotOverlapStaging(
            stagingPath,
            outputRoot,
            projectRoot,
            scriptRoot,
            cfg.RequiredModules.Enabled == true ? requiredRoot : null,
            tempRoot,
            rejectOutputRootContainingProject: cfg.DoNotClear != true);
        ValidateScriptPackageDestinationsDoNotTraverseReparsePoints(
            scriptRoot,
            stagingPath,
            information,
            delivery,
            includeScriptFolders,
            finalizedPayloadFiles);
        var filteredRequiredModules = FilterRequiredModulesForArtefact(
            requiredModules,
            cfg.RequiredModules.ExcludeModuleName);
        ValidateRequiredModuleDestinations(
            cfg,
            requiredRoot,
            scriptRoot,
            tempRoot,
            projectRoot,
            stagingPath,
            filteredRequiredModules,
            packageNamespace);
        ValidateScriptCopyMappings(
            cfg,
            tempRoot,
            outputRoot,
            scriptRoot,
            Path.Combine(scriptRoot, scriptName),
            requiredRoot,
            filteredRequiredModules,
            packageNamespace,
            projectRoot,
            stagingPath,
            moduleName,
            moduleVersion,
            preRelease,
            ArtefactType.ScriptPacked,
            enforceRelativeDestination: true);

        Directory.CreateDirectory(outputRoot);
        if (cfg.DoNotClear != true)
            ClearDirectoryContentsSafe(outputRoot, excludePatterns: new[] { "*.zip" }, includeDirectories: false);
        Directory.CreateDirectory(tempRoot);

        var copied = new List<ArtefactCopyEntry>();
        var modules = new List<ArtefactModuleEntry>();
        var evidencePaths = Array.Empty<string>();
        string? entryPointRelativePath = null;
        try
        {
            _logger.Info($"Staging packed script artefact '{zipPath}'");
            string scriptPath = BuildScriptLayout(
                cfg,
                scriptRoot,
                stagingPath,
                moduleName,
                moduleVersion,
                preRelease,
                information,
                delivery,
                includeScriptFolders,
                finalizedPayloadFiles,
                manifestRuntimePreamble,
                clearDestination: true);
            entryPointRelativePath = FrameworkCompatibility.GetRelativePath(tempRoot, scriptPath);
            modules.Add(new ArtefactModuleEntry(moduleName, isMainModule: true, version: moduleVersion, path: scriptRoot));

            AddRequiredModules(cfg, requiredRoot, requiredModules, modules);
            CopyExtraMappings(
                cfg,
                projectRoot,
                tempRoot,
                moduleName,
                moduleVersion,
                preRelease,
                copied,
                enforceRelativeDestination: true);

            if (finalizeArtefact is not null)
            {
                string version = ModulePathTokenFormatter.FormatVersionWithPreRelease(moduleVersion, preRelease);
                var context = new PackedArtefactFinalizationContext(
                    ArtefactType.ScriptPacked,
                    tempRoot,
                    scriptRoot,
                    string.Empty,
                    scriptPath,
                    zipPath,
                    moduleName,
                    version);
                evidencePaths = FinalizeArtefactLayout(finalizeArtefact, context);
            }

            CreateZipFromDirectoryContents(
                tempRoot,
                zipPath,
                ScriptStartsWithShebang(scriptPath)
                    ? new[] { entryPointRelativePath }
                    : Array.Empty<string>());
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }

        return new ArtefactBuildResult(
            ArtefactType.ScriptPacked,
            cfg.ID,
            zipPath,
            modules.ToArray(),
            copied.ToArray(),
            evidencePaths,
            entryPointRelativePath);
    }

    private string BuildScriptLayout(
        ArtefactConfiguration cfg,
        string scriptRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders,
        IReadOnlyList<string>? finalizedPayloadFiles,
        string manifestRuntimePreamble,
        bool clearDestination)
    {
        var include = ResolvePackagingInformation(information, delivery, includeScriptFolders);
        CopyModulePackage(stagingPath, scriptRoot, include, finalizedPayloadFiles, clearDestination);

        RemoveUnsupportedEvidenceFromTransformedScript(scriptRoot, moduleName);

        var manifestPath = Path.Combine(scriptRoot, moduleName + ".psd1");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("The staged module manifest required for a script artefact was not found.", manifestPath);
        File.Delete(manifestPath);

        var modulePath = Path.Combine(scriptRoot, moduleName + ".psm1");
        if (!File.Exists(modulePath))
            throw new FileNotFoundException("The staged root module required for a script artefact was not found.", modulePath);

        var scriptName = ResolveScriptName(cfg.ScriptName, moduleName, moduleVersion, preRelease);
        var scriptPath = Path.Combine(scriptRoot, scriptName);
        if (File.Exists(scriptPath))
            File.Delete(scriptPath);
        File.Move(modulePath, scriptPath);
        RewriteScriptContent(
            scriptPath,
            cfg.PreScriptMerge,
            cfg.PostScriptMerge,
            manifestRuntimePreamble);
        return scriptPath;
    }

    private void AddRequiredModules(
        ArtefactConfiguration cfg,
        string requiredRoot,
        IReadOnlyList<RequiredModuleReference> requiredModules,
        List<ArtefactModuleEntry> modules)
    {
        if (cfg.RequiredModules.Enabled != true)
            return;

        var tool = cfg.RequiredModules.Tool ?? ModuleSaveTool.Auto;
        var source = cfg.RequiredModules.Source ?? RequiredModulesSource.Installed;
        foreach (var requiredModule in FilterRequiredModulesForArtefact(requiredModules, cfg.RequiredModules.ExcludeModuleName))
        {
            modules.Add(SaveRequiredModuleToFolder(
                requiredModule,
                requiredRoot,
                cfg.RequiredModules.Repository,
                cfg.RequiredModules.Credential,
                tool,
                source));
        }
    }

    private static string ResolveScriptName(string? configuredName, string moduleName, string moduleVersion, string? preRelease)
        => ArtefactLayoutPathResolver.ResolveScriptName(configuredName, moduleName, moduleVersion, preRelease);

    private static void RewriteScriptContent(
        string scriptPath,
        string? preScriptMerge,
        string? postScriptMerge,
        string manifestRuntimePreamble)
    {
        var content = RemoveTrailingAuthenticodeSignatureBlock(ModuleManifestValueReader.ReadPowerShellCompatibleText(scriptPath));
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        var exportBoundary = FindGeneratedExportBoundary(lines);
        if (exportBoundary >= 0)
        {
            lines.RemoveRange(exportBoundary, lines.Count - exportBoundary);
        }
        else
        {
            RemoveHandWrittenExportInvocations(lines);
        }

        var moduleContent = string.Join(newline, lines).TrimEnd('\r', '\n');
        var moduleShebang = ExtractLeadingShebang(moduleContent, newline, out var moduleWithoutShebang);
        var normalizedPreScript = string.IsNullOrWhiteSpace(preScriptMerge)
            ? string.Empty
            : NormalizeNewlines(preScriptMerge!.Trim(), newline);
        var preScriptShebang = ExtractLeadingShebang(normalizedPreScript, newline, out var preScriptWithoutShebang);
        var shebang = string.IsNullOrWhiteSpace(preScriptShebang) ? moduleShebang : preScriptShebang;
        var normalizedPostScript = string.IsNullOrWhiteSpace(postScriptMerge)
            ? string.Empty
            : NormalizeNewlines(postScriptMerge!.Trim(), newline);
        var runtimePreamble = ConsolidateScriptRuntimeRequirements(
            manifestRuntimePreamble,
            ref preScriptWithoutShebang,
            ref moduleWithoutShebang,
            ref normalizedPostScript,
            newline);
        var modulePreamble = ModuleMergeComposer.ExtractMergedScriptPreamble(moduleWithoutShebang, out var body);
        var preScriptPreamble = ModuleMergeComposer.ExtractMergedScriptPreamble(
            preScriptWithoutShebang,
            out var preScriptBody);

        var sections = new List<string>(7);
        if (!string.IsNullOrWhiteSpace(shebang))
            sections.Add(shebang);

        if (!string.IsNullOrWhiteSpace(runtimePreamble))
            sections.Add(runtimePreamble);

        if (!string.IsNullOrWhiteSpace(preScriptPreamble))
            sections.Add(NormalizeNewlines(preScriptPreamble.Trim(), newline));

        if (!string.IsNullOrWhiteSpace(modulePreamble))
            sections.Add(NormalizeNewlines(modulePreamble.Trim(), newline));

        if (!string.IsNullOrWhiteSpace(preScriptBody))
            sections.Add(NormalizeNewlines(preScriptBody.Trim(), newline));

        if (!string.IsNullOrEmpty(body))
            sections.Add(NormalizeNewlines(body.TrimEnd('\r', '\n'), newline));

        if (!string.IsNullOrWhiteSpace(normalizedPostScript))
            sections.Add(normalizedPostScript);

        var rewritten = string.Join(newline, sections) + newline;
        bool startsWithShebang = rewritten.StartsWith("#!", StringComparison.Ordinal);
        File.WriteAllText(
            scriptPath,
            rewritten,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: !startsWithShebang));
#if NET8_0_OR_GREATER
        if (startsWithShebang && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
#endif
    }

    internal static bool ScriptStartsWithShebang(string scriptPath)
    {
        using var stream = File.OpenRead(scriptPath);
        return stream.ReadByte() == '#' && stream.ReadByte() == '!';
    }

    private static string NormalizeNewlines(string value, string newline)
        => value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", newline);

    private static string ExtractLeadingShebang(string content, string newline, out string remainder)
    {
        remainder = content;
        if (!content.StartsWith("#!", StringComparison.Ordinal))
            return string.Empty;

        int lineEnd = content.IndexOf(newline, StringComparison.Ordinal);
        if (lineEnd < 0)
        {
            remainder = string.Empty;
            return content;
        }

        remainder = content.Substring(lineEnd + newline.Length).TrimStart('\r', '\n');
        return content.Substring(0, lineEnd);
    }

    private static int FindGeneratedExportBoundary(IReadOnlyList<string> lines)
    {
        var state = ScriptLexicalState.Normal;
        var blockCommentDepth = 0;
        var boundary = -1;
        var generatedMarkerLine = -1;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex] ?? string.Empty;
            if (state == ScriptLexicalState.Normal && blockCommentDepth == 0)
            {
                string trimmed = line.Trim();
                if (string.Equals(trimmed, "# Auto-generated by PowerForge. Do not edit.", StringComparison.Ordinal))
                {
                    generatedMarkerLine = lineIndex;
                }
                else if (string.Equals(trimmed, "# Export functions and aliases as required", StringComparison.Ordinal))
                {
                    boundary = lineIndex;
                }
            }

            UpdateScriptLexicalState(line, ref state, ref blockCommentDepth);
        }

        return generatedMarkerLine >= 0 && boundary > generatedMarkerLine ? boundary : -1;
    }

    private static void UpdateScriptLexicalState(
        string line,
        ref ScriptLexicalState state,
        ref int blockCommentDepth)
    {
        var scanStart = 0;
        if (state is ScriptLexicalState.SingleQuotedHereString or ScriptLexicalState.DoubleQuotedHereString)
        {
            var terminator = state == ScriptLexicalState.SingleQuotedHereString ? "'@" : "\"@";
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith(terminator, StringComparison.Ordinal))
                return;

            state = ScriptLexicalState.Normal;
            scanStart = line.Length - trimmed.Length + terminator.Length;
        }

        for (var characterIndex = scanStart; characterIndex < line.Length; characterIndex++)
        {
            var current = line[characterIndex];
            var next = characterIndex + 1 < line.Length ? line[characterIndex + 1] : '\0';
            if (blockCommentDepth > 0)
            {
                if (current == '<' && next == '#')
                {
                    blockCommentDepth++;
                    characterIndex++;
                }
                else if (current == '#' && next == '>')
                {
                    blockCommentDepth--;
                    characterIndex++;
                }
                continue;
            }
            if (state == ScriptLexicalState.SingleQuotedString)
            {
                if (current != '\'')
                    continue;
                if (next == '\'')
                {
                    characterIndex++;
                    continue;
                }
                state = ScriptLexicalState.Normal;
                continue;
            }
            if (state == ScriptLexicalState.DoubleQuotedString)
            {
                if (current == '`')
                {
                    characterIndex++;
                    continue;
                }
                if (current == '"')
                    state = ScriptLexicalState.Normal;
                continue;
            }
            if (current == '<' && next == '#')
            {
                blockCommentDepth++;
                characterIndex++;
                continue;
            }
            if (current == '#')
                break;
            if (current == '@' && next is '\'' or '"')
            {
                state = next == '\''
                    ? ScriptLexicalState.SingleQuotedHereString
                    : ScriptLexicalState.DoubleQuotedHereString;
                break;
            }
            if (current == '\'')
            {
                state = ScriptLexicalState.SingleQuotedString;
                continue;
            }
            if (current == '"')
            {
                state = ScriptLexicalState.DoubleQuotedString;
                continue;
            }
            if (current == '`')
                characterIndex++;
        }
    }

    private enum ScriptLexicalState
    {
        Normal,
        SingleQuotedString,
        DoubleQuotedString,
        SingleQuotedHereString,
        DoubleQuotedHereString
    }
}
