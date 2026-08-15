using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex UrlRegex = new(
        "https?://[^\\s<>\\\"'(){}]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SchemeRelativeUrlRegex = new(
        @"(?<![:/])//(?:\[[^\]\s<>\""'(){}]+\]|(?:localhost|(?:[A-Za-z0-9\u0080-\uFFFF-]+\.)+[A-Za-z0-9\u0080-\uFFFF-]+))(?::\d{1,5})?(?:/[^\s<>\""'(){}]*)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly (Regex Pattern, string Label)[] PromptInjectionPatterns =
    {
        (new Regex(@"\bignore\s+(?:all\s+)?(?:previous|prior|earlier)\s+(?:instructions?|prompts?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "ignore-prior-instructions"),
        (new Regex(@"\b(?:reveal|print|exfiltrate|send)\s+(?:the\s+)?(?:system\s+prompt|secrets?|credentials?|tokens?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "secret-exfiltration-directive")
    };
    private static readonly Regex RemoteExecutionPipelineRegex = new(
        @"\b(?:curl(?:\.exe)?|wget|Invoke-WebRequest|iwr|Invoke-RestMethod|irm)\b[^\r\n|;&]*(?:\|[^\r\n|;&]*)*\|\s*(?:sudo\s+)?(?:(?:(?:[A-Za-z]:)?[\\/][^\s|;&]*[\\/])?(?:env|command)(?:\s+(?:-[^\s]+|[A-Za-z_][A-Za-z0-9_]*=[^\s]+))*\s+)?(?:(?:busybox|toybox)(?:\.exe)?\s+)?(?:(?:[A-Za-z]:)?[\\/][^\s|;&]*[\\/])?(?:sh|bash|zsh|dash|ash|ksh|fish|csh|tcsh|pwsh|powershell|iex|Invoke-Expression|cmd|python(?:\d+(?:\.\d+)*)?|py|ruby|perl|node|php)(?:\.exe)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RemoteExecutionPrivilegedPipelineRegex = new(
        @"\b(?:curl(?:\.exe)?|wget|Invoke-WebRequest|iwr|Invoke-RestMethod|irm)\b[^\r\n;&]*(?:\|[^\r\n;&]*)*\|\s*sudo\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SavedDownloadCommandRegex = new(
        @"\b(?<downloader>curl(?:\.exe)?|wget|Invoke-WebRequest|iwr|Invoke-RestMethod|irm)\b(?<arguments>(?:(?!&&|;|\r?\n).)*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CurlOutputPathRegex = new(
        @"(?:^|\s)(?:-o(?:=|\s+)?|--output(?:=|\s+))(?<path>""[^""]+""|'[^']+'|[^\s;&|]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WgetOutputPathRegex = new(
        @"(?:^|\s)(?:-O(?:=|\s+)?|--output-document(?:=|\s+))(?<path>""[^""]+""|'[^']+'|[^\s;&|]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PowerShellOutputPathRegex = new(
        @"(?:^|\s)-OutFile(?::|=|\s+)(?<path>""[^""]+""|'[^']+'|[^\s;&|]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ShellOutputPathRegex = new(
        @"(?:^|\s)(?:&>>?|\*>>?|>\||>&|(?:1)?>>?)\s*(?<path>""[^""]+""|'[^']+'|[^\s;&|]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TeeOutputPathRegex = new(
        @"\|\s*tee\b(?:\s+-[^\s;&|]+)*\s+(?<path>""[^""]+""|'[^']+'|[^\s;&|]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PowerShellPipelineOutputPathRegex = new(
        @"\|\s*(?:Set-Content|Out-File)\b(?:\s+-(?:LiteralPath|Path|FilePath))?\s+(?<path>""[^""]+""|'[^']+'|[^\s;&|]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex InterpreterCommandRegex = new(
        @"(?:^|[|;&]\s*|\s)(?:sudo\s+)?(?:(?:(?:[A-Za-z]:)?[\\/][^\s|;&]*[\\/])?(?:env|command)(?:\s+(?:-[^\s]+|[A-Za-z_][A-Za-z0-9_]*=[^\s]+))*\s+)?(?:(?:busybox|toybox)(?:\.exe)?\s+)?(?:(?:[A-Za-z]:)?[\\/][^\s|;&]*[\\/])?(?:sh|bash|zsh|dash|ash|ksh|fish|csh|tcsh|pwsh|powershell|iex|Invoke-Expression|cmd|python(?:\d+(?:\.\d+)*)?|py|ruby|perl|node|php|source)(?:\.exe)?\b(?<arguments>[^\r\n;&|]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SavedArtifactInvocationRegex = new(
        @"(?:^|&&|;|(?<!&)&(?!&)|\r?\n)\s*(?:sudo\s+)?(?:(?:env|command)(?:\s+(?:-[^\s]+|[A-Za-z_][A-Za-z0-9_]*=[^\s]+))*\s+)?(?<command>""[^""]+""|'[^']+'|[^\s;&|]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SavedArtifactDotSourceRegex = new(
        @"(?:^|&&|;|\r?\n)\s*\.\s+(?<path>""[^""]+""|'[^']+'|[^\s;&|]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RemoteExecutionPowerShellExpressionRegex = new(
        @"\b(?:iex|Invoke-Expression)\b\s*(?:(?:\$\s*)?\(+|[""']\s*\$\()\s*(?:(?:curl(?:\.exe)?|wget|Invoke-WebRequest|iwr|Invoke-RestMethod|irm)\b|[^\r\n]{0,200}\bDownloadString\s*\()",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RemoteExecutionShellExpressionRegex = new(
        @"\b(?:eval\b\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|(?:sh|bash|zsh|dash|ash|ksh|fish|csh|tcsh|python(?:\d+(?:\.\d+)*)?|py)\b[^\r\n]{0,80}?-c\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|(?:pwsh|powershell)\b[^\r\n]{0,80}?(?:-c|-Command)\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|(?:node)\b[^\r\n]{0,80}?(?:-e|--eval|-p|--print)\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|(?:ruby|perl)\b[^\r\n]{0,80}?(?:-e|--eval)\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|php\b[^\r\n]{0,80}?-r\s*[""']?\s*\$\(\s*(?:curl(?:\.exe)?|wget)\b|(?:sh|bash|zsh|dash|ash|ksh|fish|csh|tcsh)\b\s*<\(\s*(?:curl(?:\.exe)?|wget)\b|(?:source\b|\.)\s*<\(\s*(?:curl(?:\.exe)?|wget)\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RemoteExecutionScriptBlockRegex = new(
        @"(?:&|Invoke-Command\b[^\r\n]{0,80})\s*\(\s*\[scriptblock\]::Create\s*\(\s*\(*\s*(?:curl(?:\.exe)?|wget|Invoke-WebRequest|iwr|Invoke-RestMethod|irm)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RemoteExecutionDotSourceProcessSubstitutionRegex = new(
        @"(?:^|[;&]\s*|\s)\.\s*<\(\s*(?:curl(?:\.exe)?|wget)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly ConditionalWeakTable<string, int[]> LineStartsByContent = new();

    private static void ScanInvisibleUnicode(
        string content,
        string path,
        List<WebAgentContentSecurityFinding> findings,
        int lineOffset = 0,
        bool countLogicalLines = true)
    {
        var line = 1;
        for (var index = 0; index < content.Length;)
        {
            if (countLogicalLines && content[index] == '\n')
                line++;

            if (!Rune.TryGetRuneAt(content, index, out var rune))
            {
                index++;
                continue;
            }

            var value = rune.Value;
            var isBidi = value is 0x061C or 0x200E or 0x200F or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069;
            var isTag = value is >= 0xE0000 and <= 0xE007F;
            var isZeroWidth = value is 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF;
            if (isBidi || isTag || isZeroWidth)
            {
                var kind = isBidi ? "bidirectional control" : isTag ? "Unicode tag character" : "zero-width control";
                AddFinding(findings, "error", "PFAGENT.TEXT.INVISIBLE_UNICODE", path, lineOffset + line,
                    $"Artifact contains a {kind} U+{value:X4}; machine-facing instructions must not contain invisible control characters.");
            }

            index += rune.Utf16SequenceLength;
        }
    }

    private static void ScanPromptInjection(
        string content,
        string path,
        List<WebAgentContentSecurityFinding> findings,
        int lineOffset = 0,
        bool countLogicalLines = true)
    {
        foreach (var (pattern, label) in PromptInjectionPatterns)
        {
            foreach (Match match in pattern.Matches(content))
            {
                AddFinding(findings, "warning", "PFAGENT.TEXT.PROMPT_DIRECTIVE", path, GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    $"Potential agent-directed prompt injection phrase detected ({label}). Review the surrounding content.");
            }
        }

    }

    private static void ScanRemoteExecution(
        string content,
        string path,
        List<WebAgentContentSecurityFinding> findings,
        int lineOffset = 0,
        bool countLogicalLines = true,
        RemoteExecutionFlowState? flowState = null)
    {
        var normalized = ShellContinuationRegex.Replace(content, static match => new string(' ', match.Length));
        foreach (Match download in SavedDownloadCommandRegex.Matches(normalized))
        {
            if (!UsesServerSelectedDownloadName(
                    download.Groups["downloader"].Value,
                    download.Groups["arguments"].Value))
                continue;
            AddFinding(findings, "error", "PFAGENT.COMMAND.REMOTE_EXECUTION", path,
                GetReportedLine(content, download.Index, lineOffset, countLogicalLines),
                "Server-selected download filenames cannot be correlated safely with later execution; use an explicit local output path.");
        }
        foreach (var pattern in new[]
                 {
                      RemoteExecutionPipelineRegex,
                      RemoteExecutionPrivilegedPipelineRegex,
                     RemoteExecutionPowerShellExpressionRegex,
                     RemoteExecutionShellExpressionRegex,
                     RemoteExecutionScriptBlockRegex,
                     RemoteExecutionDotSourceProcessSubstitutionRegex
                 })
        {
            foreach (Match match in pattern.Matches(normalized))
            {
                AddFinding(findings, "error", "PFAGENT.COMMAND.REMOTE_EXECUTION", path, GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    "Downloaded content is passed directly to an interpreter. Prefer a pinned, integrity-checked artifact and a separate execution step.");
            }
        }

        ScanSavedDownloadExecution(normalized, content, path, findings, lineOffset, countLogicalLines,
            flowState ?? new RemoteExecutionFlowState());
    }

    private static void ScanSavedDownloadExecution(
        string normalized,
        string original,
        string path,
        List<WebAgentContentSecurityFinding> findings,
        int lineOffset,
        bool countLogicalLines,
        RemoteExecutionFlowState flowState)
    {
        var positionOffset = flowState.NextPosition;
        flowState.NextPosition = checked(positionOffset + normalized.Length + 1L);
        foreach (Match download in SavedDownloadCommandRegex.Matches(normalized))
        {
            foreach (var outputPath in FindDownloadedOutputPaths(
                         download.Groups["downloader"].Value,
                         download.Groups["arguments"].Value))
                flowState.DownloadedPaths.TryAdd(
                    NormalizeComparedPath(outputPath),
                    new SavedDownload(
                        positionOffset + download.Index,
                        GetReportedLine(original, download.Index, lineOffset, countLogicalLines)));
        }

        PropagateDownloadedPathsThroughFileTransforms(normalized, positionOffset, flowState);
        ScanDownloadedReaderPipelines(normalized, path, findings, positionOffset, flowState);

        if (flowState.DownloadedPaths.Count == 0)
            return;

        foreach (Match interpreter in InterpreterCommandRegex.Matches(normalized))
        {
            foreach (var token in Tokenize(interpreter.Groups["arguments"].Value))
            {
                var candidate = NormalizeComparedPath(token);
                if (!flowState.DownloadedPaths.TryGetValue(candidate, out var download) ||
                    download.Position >= positionOffset + interpreter.Index ||
                    !flowState.ReportedPaths.Add(candidate))
                    continue;

                AddFinding(findings, "error", "PFAGENT.COMMAND.REMOTE_EXECUTION", path,
                    download.Line,
                    "Downloaded content is saved and then passed to an interpreter. Prefer a pinned, integrity-checked artifact and a separate execution step.");
                break;
            }
        }

        foreach (var candidate in EnumerateDirectlyExecutedPaths(normalized))
        {
            var normalizedPath = NormalizeComparedPath(candidate.Path);
            if (!flowState.DownloadedPaths.TryGetValue(normalizedPath, out var download) ||
                download.Position >= positionOffset + candidate.Index ||
                !flowState.ReportedPaths.Add(normalizedPath))
                continue;

            AddFinding(findings, "error", "PFAGENT.COMMAND.REMOTE_EXECUTION", path,
                download.Line,
                "A downloaded artifact is executed directly. Prefer a pinned, integrity-checked artifact and a separate execution step.");
        }
    }

    private sealed class RemoteExecutionFlowState
    {
        public long NextPosition { get; set; }
        public Dictionary<string, SavedDownload> DownloadedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ReportedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct SavedDownload(long Position, int Line);

    private static IEnumerable<(string Path, int Index)> EnumerateDirectlyExecutedPaths(string content)
    {
        foreach (Match match in SavedArtifactInvocationRegex.Matches(content))
            yield return (match.Groups["command"].Value, match.Index);
        foreach (Match match in SavedArtifactDotSourceRegex.Matches(content))
            yield return (match.Groups["path"].Value, match.Index);
    }

    private static string NormalizeComparedPath(string value)
    {
        var normalized = NormalizeToken(value).Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized;
    }

    private static IEnumerable<string> FindDownloadedOutputPaths(string downloader, string arguments)
    {
        var executable = NormalizeExecutable(downloader);
        var pattern = executable switch
        {
            "curl" => CurlOutputPathRegex,
            "wget" => WgetOutputPathRegex,
            _ => PowerShellOutputPathRegex
        };
        var match = pattern.Match(arguments);
        var explicitStandardOutput = false;
        if (match.Success)
        {
            var outputPath = match.Groups["path"].Value.Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(outputPath) && outputPath != "-")
            {
                yield return outputPath;
                yield break;
            }
            explicitStandardOutput = outputPath == "-";
        }

        foreach (var outputPattern in new[] { TeeOutputPathRegex, PowerShellPipelineOutputPathRegex, ShellOutputPathRegex })
        {
            match = outputPattern.Match(arguments);
            if (match.Success)
            {
                yield return match.Groups["path"].Value.Trim('"', '\'');
                yield break;
            }
        }
        if (explicitStandardOutput)
            yield break;

        var tokens = Tokenize(arguments);
        var usesRemoteName = executable == "wget" || executable == "curl" && tokens.Any(static token =>
            token.Equals("--remote-name", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("--remote-name-all", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("-", StringComparison.Ordinal) && !token.StartsWith("--", StringComparison.Ordinal) && token[1..].Contains('O'));
        if (!usesRemoteName)
            yield break;

        var outputDirectory = executable switch
        {
            "curl" => FindOptionValue(tokens, 0, "--output-dir"),
            "wget" => FindOptionValue(tokens, 0, "-P", "--directory-prefix"),
            _ => null
        };

        foreach (Match urlMatch in UrlRegex.Matches(arguments))
        {
            var candidate = urlMatch.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                continue;
            var rawPathEnd = candidate.IndexOfAny(['?', '#']);
            var rawPath = rawPathEnd >= 0 ? candidate[..rawPathEnd] : candidate;
            var rawFileName = rawPath[(rawPath.LastIndexOf('/') + 1)..];
            var escapedPath = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
            var normalizedFileName = escapedPath[(escapedPath.LastIndexOf('/') + 1)..];
            foreach (var fileName in new[] { rawFileName, normalizedFileName }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(fileName))
                    yield return string.IsNullOrWhiteSpace(outputDirectory)
                        ? fileName
                        : NormalizeToken(outputDirectory).TrimEnd('/', '\\') + "/" + fileName;
            }
        }
    }

    private static bool UsesServerSelectedDownloadName(string downloader, string arguments)
    {
        var executable = NormalizeExecutable(downloader);
        return Tokenize(arguments).Any(token => executable switch
        {
            "wget" => token.Equals("--content-disposition", StringComparison.OrdinalIgnoreCase) ||
                      token.StartsWith("--content-disposition=", StringComparison.OrdinalIgnoreCase) ||
                      token.Equals("--trust-server-names", StringComparison.OrdinalIgnoreCase) ||
                      token.StartsWith("--trust-server-names=", StringComparison.OrdinalIgnoreCase),
            "curl" => token.Equals("--remote-header-name", StringComparison.OrdinalIgnoreCase) ||
                      token.StartsWith("--remote-header-name=", StringComparison.OrdinalIgnoreCase) ||
                      token.StartsWith("-", StringComparison.Ordinal) &&
                      !token.StartsWith("--", StringComparison.Ordinal) && token[1..].Contains('J'),
            _ => false
        });
    }

    private static void ExtractUrls(string content, ISet<Uri> urls)
    {
        foreach (Match match in UrlRegex.Matches(content))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', ':', '!', '?');
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                urls.Add(uri);
        }

        foreach (Match match in SchemeRelativeUrlRegex.Matches(content))
        {
            var candidate = ("https:" + match.Value).TrimEnd('.', ',', ';', ':', '!', '?');
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                urls.Add(uri);
        }
    }

    private static int GetLineNumber(string content, int index)
    {
        var lineStarts = LineStartsByContent.GetValue(content, static value =>
        {
            var starts = new List<int> { 0 };
            for (var position = 0; position < value.Length; position++)
            {
                if (value[position] == '\n' && position + 1 < value.Length)
                    starts.Add(position + 1);
            }
            return starts.ToArray();
        });
        var boundedIndex = Math.Clamp(index, 0, content.Length);
        var result = Array.BinarySearch(lineStarts, boundedIndex);
        return result >= 0 ? result + 1 : ~result;
    }

    private static int GetReportedLine(string content, int index, int lineOffset, bool countLogicalLines)
        => lineOffset + (countLogicalLines ? GetLineNumber(content, index) : 1);
}
