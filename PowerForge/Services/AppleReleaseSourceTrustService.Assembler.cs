using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private void ValidateAssemblerInputs(string repositoryRoot, string sourcePath, string source)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!_validatedAssemblerInputFiles.Add(fullSourcePath))
            return;

        RejectUnboundAssemblerPreprocessorMacros(fullSourcePath, source);
        ValidateAssemblerDirectives(repositoryRoot, fullSourcePath, source);
    }

    private static void RejectUnboundAssemblerPreprocessorMacros(string sourcePath, string source)
    {
        var syntax = MaskCStringAndCharacterLiterals(source);
        foreach (Match definition in Regex.Matches(
                     syntax,
                     "(?m)^[ \\t]*(?:#|%:)[ \\t]*define[ \\t]+[A-Za-z_][A-Za-z0-9_]*(?<parameters>[ \\t]*\\([^\\r\\n)]*\\))?[ \\t]*(?<body>[^\\r\\n]*)",
                     RegexOptions.CultureInvariant))
        {
            if (!CanConstructAssemblerFileDirective(
                    definition.Groups["body"].Value,
                    definition.Groups["parameters"].Success
                        ? definition.Groups["parameters"].Value
                        : null))
                continue;

            throw new InvalidOperationException(
                $"Preprocessed assembler source input '{sourcePath}' defines a macro that can construct a file-consuming .include or .incbin directive, whose expanded input cannot be bound safely to the exact source commit.");
        }

        var indirectDirective = Regex.Match(
            syntax,
            "(?<![A-Za-z0-9_])\\.(?:include|incbin)(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (indirectDirective.Success &&
            !Regex.IsMatch(
                NormalizeAssemblerStatementBoundaries(syntax),
                "(?im)^[ \\t]*(?:(?:[A-Za-z_.$][A-Za-z0-9_.$]*|[0-9]+):[ \\t]*)*\\.(?:include|incbin)(?![A-Za-z0-9_])",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                $"Preprocessed assembler source input '{sourcePath}' passes '{indirectDirective.Value}' through a macro or computed context, whose expanded file input cannot be bound safely to the exact source commit.");
        }
    }

    private static bool CanConstructAssemblerFileDirective(string body, string? parameters)
    {
        body = body.Trim();
        var collapsed = Regex.Replace(body, "[ \\t]*##[ \\t]*", string.Empty, RegexOptions.CultureInvariant);
        if (body.Contains("##", StringComparison.Ordinal) ||
            body.Equals(".", StringComparison.Ordinal) ||
            Regex.IsMatch(
                collapsed,
                "(?<![A-Za-z0-9_])(?:include|incbin)(?![A-Za-z0-9_])",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(parameters))
            return false;

        return parameters!.Trim().Trim('(', ')')
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static parameter => parameter.Trim())
            .Where(static parameter => Regex.IsMatch(parameter, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            .Any(parameter => Regex.IsMatch(
                body,
                $"(?<![A-Za-z0-9_])\\.[ \\t]*{Regex.Escape(parameter)}(?![A-Za-z0-9_])",
                RegexOptions.CultureInvariant));
    }

    private void ValidateAssemblerDirectives(string repositoryRoot, string sourcePath, string source)
    {
        source = NormalizeAssemblerStatementBoundaries(source);
        foreach (Match directive in Regex.Matches(
                     source,
                     "(?im)^[ \\t]*(?:(?:[A-Za-z_.$][A-Za-z0-9_.$]*|[0-9]+):[ \\t]*)*\\.(?<kind>include|incbin)(?![A-Za-z0-9_])[ \\t]+(?<operand>[^\\r\\n]+)",
                     RegexOptions.CultureInvariant))
        {
            var operand = directive.Groups["operand"].Value.Trim();
            var literal = Regex.Match(operand, "^\\\"(?<path>[^\\\"\\\\]*)\\\"", RegexOptions.CultureInvariant);
            if (!literal.Success)
            {
                throw new InvalidOperationException(
                    $"Assembler source input '{sourcePath}' uses computed .{directive.Groups["kind"].Value} input '{operand}', which cannot be bound to the exact source commit.");
            }

            var input = literal.Groups["path"].Value;
            if (Path.IsPathRooted(input))
            {
                throw new InvalidOperationException(
                    $"Assembler source input '{sourcePath}' references absolute .{directive.Groups["kind"].Value} input '{input}', which is outside the exact-source graph.");
            }

            var candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, input));
            EnsurePathWithinRepository(repositoryRoot, candidate, $"assembler .{directive.Groups["kind"].Value} input from {sourcePath}");
            if (!File.Exists(candidate))
                throw new FileNotFoundException($"Assembler input was not found inside the exact checked-out source: {candidate}", candidate);
            EnsureTrackedFile(repositoryRoot, candidate, $"assembler .{directive.Groups["kind"].Value} input from {sourcePath}");
            if (directive.Groups["kind"].Value.Equals("include", StringComparison.OrdinalIgnoreCase))
            {
                var nestedPhysicalSource = File.ReadAllText(candidate);
                RejectCTrigraphs(nestedPhysicalSource, candidate);
                var nestedSource = RemoveCComments(SpliceCPreprocessingLines(nestedPhysicalSource));
                ValidateAssemblerInputs(repositoryRoot, candidate, nestedSource);
            }
        }
    }

    private static string NormalizeAssemblerStatementBoundaries(string source)
    {
        var normalized = source.ToCharArray();
        var insideString = false;
        var escaped = false;
        for (var index = 0; index < normalized.Length; index++)
        {
            var value = normalized[index];
            if (insideString && value == '\\' && !escaped)
            {
                escaped = true;
                continue;
            }
            if (value == '"' && !escaped)
                insideString = !insideString;
            if (value == ';' && !insideString)
                normalized[index] = '\n';
            escaped = false;
        }
        return new string(normalized);
    }
}
