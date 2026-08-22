using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace PowerForge;

internal static partial class ModuleMergeComposer
{
    private static string RebaseUsingDirective(string directive, string sourcePath, string moduleRoot)
    {
        if (string.IsNullOrWhiteSpace(directive) || string.IsNullOrWhiteSpace(moduleRoot))
            return directive;

        if (!TryParseUsingDirectiveHeader(directive, out var kind, out var index))
            return directive;
        if (!string.Equals(kind, "assembly", System.StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(kind, "module", System.StringComparison.OrdinalIgnoreCase))
        {
            return directive;
        }

        if (index >= directive.Length)
            return directive;
        if (directive[index] == '@')
            return string.Equals(kind, "module", System.StringComparison.OrdinalIgnoreCase)
                ? RebaseUsingModuleSpecification(directive, sourcePath, moduleRoot)
                : directive;

        var quote = directive[index] is '\'' or '"' ? directive[index++] : '\0';
        var pathStart = index;
        var pathEnd = quote == '\0'
            ? FindUnquotedPathEnd(directive, pathStart)
            : directive.IndexOf(quote, pathStart);
        if (pathEnd < pathStart)
            return directive;

        var usingPath = directive.Substring(pathStart, pathEnd - pathStart);
        var rebased = RebaseUsingPath(
            usingPath,
            sourcePath,
            moduleRoot,
            treatBareNameAsPath: string.Equals(kind, "assembly", System.StringComparison.OrdinalIgnoreCase));
        if (string.Equals(usingPath, rebased, System.StringComparison.Ordinal))
            return directive;

        return directive.Substring(0, pathStart) + rebased + directive.Substring(pathEnd);
    }

    private static int FindUsingDirectiveEnd(
        IReadOnlyList<string> lines,
        int startLine,
        int directiveStart)
    {
        var firstLine = lines[startLine].Substring(directiveStart);
        if (!TryParseUsingDirectiveHeader(firstLine, out var kind, out var argumentStart) ||
            !string.Equals(kind, "module", System.StringComparison.OrdinalIgnoreCase) ||
            argumentStart + 1 >= firstLine.Length ||
            firstLine[argumentStart] != '@' ||
            firstLine[argumentStart + 1] != '{')
        {
            return startLine;
        }

        var braceDepth = 0;
        var blockCommentDepth = 0;
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        var started = false;
        for (var lineIndex = startLine; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var characterIndex = lineIndex == startLine ? directiveStart + argumentStart : 0;
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

                if (inSingleQuotedString)
                {
                    if (current != '\'')
                        continue;
                    if (next == '\'')
                    {
                        characterIndex++;
                        continue;
                    }
                    inSingleQuotedString = false;
                    continue;
                }

                if (inDoubleQuotedString)
                {
                    if (current == '`')
                    {
                        characterIndex++;
                        continue;
                    }
                    if (current == '"')
                        inDoubleQuotedString = false;
                    continue;
                }

                if (current == '#')
                    break;
                if (current == '<' && next == '#')
                {
                    blockCommentDepth++;
                    characterIndex++;
                    continue;
                }
                if (current == '\'')
                {
                    inSingleQuotedString = true;
                    continue;
                }
                if (current == '"')
                {
                    inDoubleQuotedString = true;
                    continue;
                }
                if (current == '{')
                {
                    braceDepth++;
                    started = true;
                    continue;
                }
                if (current != '}' || !started)
                    continue;

                braceDepth--;
                if (braceDepth == 0)
                    return lineIndex;
            }
        }

        return startLine;
    }

    private static bool TryParseUsingDirectiveHeader(
        string directive,
        out string kind,
        out int argumentStart)
    {
        kind = string.Empty;
        argumentStart = 0;
        if (string.IsNullOrWhiteSpace(directive) ||
            !StartsWithDirective(directive, 0, "using"))
        {
            return false;
        }

        var index = "using".Length;
        while (index < directive.Length && char.IsWhiteSpace(directive[index]))
            index++;
        var kindStart = index;
        while (index < directive.Length && !char.IsWhiteSpace(directive[index]))
            index++;
        if (index == kindStart)
            return false;

        kind = directive.Substring(kindStart, index - kindStart);
        while (index < directive.Length && char.IsWhiteSpace(directive[index]))
            index++;
        argumentStart = index;
        return true;
    }

    private static string RebaseUsingModuleSpecification(
        string directive,
        string sourcePath,
        string moduleRoot)
        => Regex.Replace(
            directive,
            @"(?im)(\bModuleName\s*=\s*)(?<quote>['""])(?<path>[^'""\r\n]+)\k<quote>",
            match =>
            {
                var path = match.Groups["path"].Value;
                var rebased = RebaseUsingPath(path, sourcePath, moduleRoot, treatBareNameAsPath: false);
                if (string.Equals(path, rebased, System.StringComparison.Ordinal))
                    return match.Value;

                var quote = match.Groups["quote"].Value;
                return match.Groups[1].Value + quote + rebased + quote;
            });

    private static string RebaseUsingPath(
        string usingPath,
        string sourcePath,
        string moduleRoot,
        bool treatBareNameAsPath)
    {
        if (!IsRelativeUsingPath(usingPath, treatBareNameAsPath))
            return usingPath;

        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
        if (string.IsNullOrWhiteSpace(sourceDirectory))
            return usingPath;

        var normalizedPath = usingPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.GetFullPath(Path.Combine(sourceDirectory, normalizedPath));
        var rebased = FrameworkCompatibility.GetRelativePath(moduleRoot, absolutePath).Replace('\\', '/');
        return rebased.StartsWith(".", System.StringComparison.Ordinal) ? rebased : "./" + rebased;
    }

    private static int FindUnquotedPathEnd(string directive, int start)
    {
        var index = start;
        while (index < directive.Length && !char.IsWhiteSpace(directive[index]) && directive[index] != ';')
            index++;
        return index;
    }

    private static bool IsRelativeUsingPath(string path, bool treatBareNameAsPath)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.StartsWith("\\\\", System.StringComparison.Ordinal) ||
            path.StartsWith("//", System.StringComparison.Ordinal) ||
            (path.Length > 2 && path[1] == ':' && (path[2] == '\\' || path[2] == '/')))
        {
            return false;
        }

        return treatBareNameAsPath ||
               path.StartsWith(".", System.StringComparison.Ordinal) ||
               path.IndexOf('\\') >= 0 ||
               path.IndexOf('/') >= 0;
    }

}
