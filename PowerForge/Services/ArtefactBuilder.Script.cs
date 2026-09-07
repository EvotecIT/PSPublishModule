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
        if (cfg.DoNotClear != true)
            ClearDirectorySafe(outputRoot);
        else
            Directory.CreateDirectory(outputRoot);

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
            evidencePaths);
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
        Directory.CreateDirectory(outputRoot);
        if (cfg.DoNotClear != true)
            ClearDirectoryContentsSafe(outputRoot, excludePatterns: new[] { "*.zip" }, includeDirectories: false);

        var artefactName = ResolveArtefactFileName(cfg, moduleName, moduleVersion, preRelease);
        var zipPath = Path.Combine(outputRoot, artefactName);
        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "artefacts", $"{moduleName}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var copied = new List<ArtefactCopyEntry>();
        var modules = new List<ArtefactModuleEntry>();
        var evidencePaths = Array.Empty<string>();
        try
        {
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
                clearDestination: true);
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

            CreateZipFromDirectoryContents(tempRoot, zipPath);
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
            evidencePaths);
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
        bool clearDestination)
    {
        var include = ResolvePackagingInformation(information, delivery, includeScriptFolders);
        CopyModulePackage(stagingPath, scriptRoot, include, finalizedPayloadFiles, clearDestination);

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
        RewriteScriptContent(scriptPath, cfg.PreScriptMerge, cfg.PostScriptMerge);
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
    {
        var replaced = ModulePathTokenFormatter.ReplacePathTokens(configuredName, moduleName, moduleVersion, preRelease).Trim();
        var scriptName = string.IsNullOrWhiteSpace(replaced) ? moduleName + ".ps1" : replaced;
        if (!scriptName.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            scriptName += ".ps1";

        if (Path.IsPathRooted(scriptName) || scriptName.IndexOf('/') >= 0 || scriptName.IndexOf('\\') >= 0)
            throw new InvalidOperationException($"ScriptName must be a file name, but got '{scriptName}'.");

        return scriptName;
    }

    private static void RewriteScriptContent(string scriptPath, string? preScriptMerge, string? postScriptMerge)
    {
        var content = File.ReadAllText(scriptPath);
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

        var exportBoundary = lines.FindLastIndex(static line =>
            string.Equals(line.Trim(), "# Export functions and aliases as required", StringComparison.Ordinal));
        if (exportBoundary >= 0)
        {
            lines.RemoveRange(exportBoundary, lines.Count - exportBoundary);
        }
        else
        {
            RemoveHandWrittenExportInvocations(lines);
        }

        var moduleContent = string.Join(newline, lines).TrimEnd('\r', '\n');
        var preamble = ModuleMergeComposer.ExtractMergedScriptPreamble(moduleContent, out var body);

        var sections = new List<string>(4);
        if (!string.IsNullOrWhiteSpace(preamble))
            sections.Add(NormalizeNewlines(preamble.Trim(), newline));

        if (!string.IsNullOrWhiteSpace(preScriptMerge))
            sections.Add(NormalizeNewlines(preScriptMerge!.Trim(), newline));

        if (!string.IsNullOrEmpty(body))
            sections.Add(NormalizeNewlines(body.TrimEnd('\r', '\n'), newline));

        if (!string.IsNullOrWhiteSpace(postScriptMerge))
            sections.Add(NormalizeNewlines(postScriptMerge!.Trim(), newline));

        var rewritten = string.Join(newline, sections) + newline;
        File.WriteAllText(scriptPath, rewritten, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string NormalizeNewlines(string value, string newline)
        => value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", newline);

    private static void RemoveHandWrittenExportInvocations(List<string> lines)
    {
        var state = ScriptLexicalState.Normal;
        var blockCommentDepth = 0;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex] ?? string.Empty;
            if (state == ScriptLexicalState.Normal &&
                blockCommentDepth == 0 &&
                TryGetExportModuleMemberInvocationStart(line, out var commandStart))
            {
                var endLine = FindPowerShellCommandEnd(lines, lineIndex, commandStart, out var suffixStart);
                var removeCount = endLine - lineIndex + 1;
                var suffix = suffixStart >= 0
                    ? lines[endLine].Substring(suffixStart).TrimStart()
                    : string.Empty;
                lines.RemoveRange(lineIndex, removeCount);
                if (!string.IsNullOrWhiteSpace(suffix))
                    lines.Insert(lineIndex, suffix);
                lineIndex--;
                continue;
            }

            UpdateScriptLexicalState(line, ref state, ref blockCommentDepth);
        }
    }

    private static int FindPowerShellCommandEnd(
        IReadOnlyList<string> lines,
        int startLine,
        int commandStart,
        out int suffixStart)
    {
        var state = ScriptLexicalState.Normal;
        var blockCommentDepth = 0;
        var parenthesisDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        suffixStart = -1;

        for (var lineIndex = startLine; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex] ?? string.Empty;
            var characterIndex = lineIndex == startLine ? commandStart : 0;
            var lineContinues = false;
            var lastSignificant = '\0';

            if (state is ScriptLexicalState.SingleQuotedHereString or ScriptLexicalState.DoubleQuotedHereString)
            {
                var terminator = state == ScriptLexicalState.SingleQuotedHereString ? "'@" : "\"@";
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith(terminator, StringComparison.Ordinal))
                    continue;

                characterIndex = line.Length - trimmed.Length + terminator.Length;
                state = ScriptLexicalState.Normal;
            }

            for (; characterIndex < line.Length; characterIndex++)
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
                    lastSignificant = current;
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
                    {
                        state = ScriptLexicalState.Normal;
                        lastSignificant = current;
                    }
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
                    lastSignificant = current;
                    continue;
                }
                if (current == '"')
                {
                    state = ScriptLexicalState.DoubleQuotedString;
                    lastSignificant = current;
                    continue;
                }
                if (current == '`')
                {
                    if (string.IsNullOrWhiteSpace(line.Substring(characterIndex + 1)))
                    {
                        lineContinues = true;
                        break;
                    }
                    characterIndex++;
                    continue;
                }

                if (char.IsWhiteSpace(current))
                    continue;

                if (current == ';' && parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                {
                    suffixStart = characterIndex + 1;
                    return lineIndex;
                }

                switch (current)
                {
                    case '(':
                        parenthesisDepth++;
                        break;
                    case ')':
                        if (parenthesisDepth > 0) parenthesisDepth--;
                        break;
                    case '[':
                        bracketDepth++;
                        break;
                    case ']':
                        if (bracketDepth > 0) bracketDepth--;
                        break;
                    case '{':
                        braceDepth++;
                        break;
                    case '}':
                        if (braceDepth > 0) braceDepth--;
                        break;
                }

                lastSignificant = current;
            }

            if (state == ScriptLexicalState.Normal &&
                blockCommentDepth == 0 &&
                parenthesisDepth == 0 &&
                bracketDepth == 0 &&
                braceDepth == 0 &&
                !lineContinues &&
                lastSignificant is not ('|' or ','))
            {
                return lineIndex;
            }
        }

        return lines.Count - 1;
    }

    private static void UpdateScriptLexicalState(
        string line,
        ref ScriptLexicalState state,
        ref int blockCommentDepth)
    {
        if (state is ScriptLexicalState.SingleQuotedHereString or ScriptLexicalState.DoubleQuotedHereString)
        {
            var terminator = state == ScriptLexicalState.SingleQuotedHereString ? "'@" : "\"@";
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(terminator, StringComparison.Ordinal))
            {
                var suffix = trimmed.Substring(terminator.Length);
                if (string.IsNullOrWhiteSpace(suffix) || suffix.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    state = ScriptLexicalState.Normal;
            }
            return;
        }

        for (var characterIndex = 0; characterIndex < line.Length; characterIndex++)
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

    private static bool TryGetExportModuleMemberInvocationStart(string line, out int commandStart)
    {
        var source = line ?? string.Empty;
        var trimmed = source.TrimStart();
        commandStart = source.Length - trimmed.Length;
        return StartsWithCommandName(trimmed, "Export-ModuleMember") ||
               StartsWithCommandName(trimmed, "Microsoft.PowerShell.Core\\Export-ModuleMember");
    }

    private static bool StartsWithCommandName(string line, string commandName)
    {
        if (!line.StartsWith(commandName, StringComparison.OrdinalIgnoreCase))
            return false;

        return line.Length == commandName.Length ||
               char.IsWhiteSpace(line[commandName.Length]) ||
               line[commandName.Length] is '`' or ';' or '|';
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
