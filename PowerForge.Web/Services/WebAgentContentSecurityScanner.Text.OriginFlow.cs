using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex LaunchWrapperInvocationRegex = new(
        @"\b(?<launcher>exec|nohup|setsid|timeout|nice|ionice|chrt|stdbuf|taskset|time)\b(?<arguments>[^\r\n;&|]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ArchiveExtractionCommandRegex = new(
        @"(?:^|&&|;|(?<!&)&(?!&)|\r?\n)\s*(?:sudo\s+)?(?:(?:busybox|toybox)(?:\.exe)?\s+)?(?<command>tar|bsdtar|unzip|Expand-Archive|7z|7za|7zr|jar|unrar|unar)(?:\.exe)?\b(?<arguments>[^\r\n;&|]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex RemoteCloneCommandRegex = new(
        @"(?:^|&&|;|(?<!&)&(?!&)|\r?\n)\s*(?:(?<command>git)(?:\.exe)?(?:\s+(?:-c|-C|--git-dir|--work-tree|--namespace)(?:=|\s+)(?:""[^""]+""|'[^']+'|[^\s;&|]+))*\s+clone|(?<command>gh|glab|hub)(?:\.exe)?\s+(?:repo\s+)?clone|(?<command>hg)(?:\.exe)?\s+clone|(?<command>svn)(?:\.exe)?\s+(?:checkout|co|export))\b(?<arguments>[^\r\n;&|]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static IEnumerable<(string Path, int Index)> EnumerateLaunchWrapperPaths(string content)
    {
        foreach (Match match in LaunchWrapperInvocationRegex.Matches(content))
        {
            var tokens = Tokenize(match.Groups["arguments"].Value);
            var launcher = match.Groups["launcher"].Value;
            var tasksetHasCpuList = tokens.Any(token => token.Equals("-c", StringComparison.OrdinalIgnoreCase) ||
                                                        token.Equals("--cpu-list", StringComparison.OrdinalIgnoreCase));
            var skipOperands = launcher.Equals("timeout", StringComparison.OrdinalIgnoreCase) ||
                               launcher.Equals("chrt", StringComparison.OrdinalIgnoreCase) ||
                               launcher.Equals("taskset", StringComparison.OrdinalIgnoreCase) && !tasksetHasCpuList
                ? 1
                : 0;
            for (var index = 0; index < tokens.Length; index++)
            {
                var token = tokens[index];
                if (token == "--")
                    continue;
                if (launcher.Equals("exec", StringComparison.OrdinalIgnoreCase) &&
                    token.Equals("-a", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    continue;
                }
                if ((launcher.Equals("nice", StringComparison.OrdinalIgnoreCase) && token.Equals("-n", StringComparison.OrdinalIgnoreCase)) ||
                    (launcher.Equals("ionice", StringComparison.OrdinalIgnoreCase) && token is "-c" or "--class" or "-n" or "--classdata") ||
                    (launcher.Equals("time", StringComparison.OrdinalIgnoreCase) && token is "-f" or "--format" or "-o" or "--output") ||
                    (launcher.Equals("taskset", StringComparison.OrdinalIgnoreCase) && token is "-c" or "--cpu-list"))
                {
                    index++;
                    continue;
                }
                if (token.StartsWith("-", StringComparison.Ordinal))
                    continue;
                if (skipOperands-- > 0)
                    continue;
                yield return (token, match.Index);
                break;
            }
        }
    }

    private static void TrackDownloadedArchiveExtractions(
        string content,
        long positionOffset,
        RemoteExecutionFlowState flowState)
    {
        foreach (Match match in ArchiveExtractionCommandRegex.Matches(content))
        {
            var command = NormalizeExecutable(match.Groups["command"].Value);
            var tokens = Tokenize(match.Groups["arguments"].Value);
            if (!IsArchiveExtraction(command, tokens))
                continue;

            var extractionPosition = positionOffset + match.Index;
            var archive = tokens
                .Select(NormalizeComparedPath)
                .Select(path => flowState.DownloadedPaths.TryGetValue(path, out var origin)
                    ? (Found: true, Origin: origin)
                    : (Found: false, Origin: default(SavedDownload)))
                .FirstOrDefault(static candidate => candidate.Found);
            if (!archive.Found || archive.Origin.Position >= extractionPosition)
                continue;

            var destination = FindArchiveDestination(command, tokens);
            var normalizedDestination = NormalizeComparedPath(destination ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalizedDestination) || normalizedDestination == ".")
            {
                if (!flowState.UnknownExtractedContent.Any(origin => origin.Position == extractionPosition))
                    flowState.UnknownExtractedContent.Add(new SavedDownload(extractionPosition, archive.Origin.Line));
                continue;
            }

            flowState.UntrustedDirectoryPrefixes.TryAdd(normalizedDestination + "/", archive.Origin);
        }
    }

    private static bool IsArchiveExtraction(string command, string[] tokens)
    {
        if (command == "unzip")
            return !tokens.Any(token => token is "-l" or "--list" or "-t" or "-v" or "-z");
        if (command == "expand-archive")
            return true;
        if (command is "7z" or "7za" or "7zr")
            return tokens.Any(token => token.Equals("x", StringComparison.OrdinalIgnoreCase) ||
                                       token.Equals("e", StringComparison.OrdinalIgnoreCase));
        if (command == "jar")
            return tokens.Any(token => token.TrimStart('-').StartsWith("x", StringComparison.OrdinalIgnoreCase));
        if (command == "unrar")
            return tokens.Any(token => token.Equals("x", StringComparison.OrdinalIgnoreCase) ||
                                       token.Equals("e", StringComparison.OrdinalIgnoreCase));
        if (command == "unar")
            return true;
        return tokens.Any(token =>
            token.Equals("--extract", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("--get", StringComparison.OrdinalIgnoreCase) ||
            token.TrimStart('-').Contains('x') || token.TrimStart('-').Contains('X'));
    }

    private static string? FindArchiveDestination(string command, string[] tokens)
    {
        if (command is "tar" or "bsdtar")
            return FindNamedFileTransformValue(tokens, "-C", "--directory");
        if (command == "unzip")
            return FindNamedFileTransformValue(tokens, "-d");
        if (command == "expand-archive")
            return FindNamedFileTransformValue(tokens, "-DestinationPath");
        if (command == "unrar")
            return tokens.LastOrDefault(token => token.EndsWith("/", StringComparison.Ordinal) ||
                                                 token.EndsWith("\\", StringComparison.Ordinal));
        if (command == "unar")
            return FindNamedFileTransformValue(tokens, "-o", "--output-directory");
        foreach (var token in tokens)
        {
            if (token.StartsWith("-o", StringComparison.OrdinalIgnoreCase) && token.Length > 2)
                return token[2..];
        }
        return FindNamedFileTransformValue(tokens, "-o");
    }

    private static void TrackRemoteRepositoryClones(
        string content,
        string original,
        int lineOffset,
        bool countLogicalLines,
        long positionOffset,
        RemoteExecutionFlowState flowState)
    {
        foreach (Match match in RemoteCloneCommandRegex.Matches(content))
        {
            var command = NormalizeExecutable(match.Groups["command"].Value);
            var operands = EnumerateCloneOperands(Tokenize(match.Groups["arguments"].Value)).ToArray();
            if (operands.Length == 0 || !IsRemoteRepository(command, operands[0]))
                continue;

            var destination = operands.Length > 1 ? operands[1] : DeriveRepositoryDirectory(operands[0]);
            var normalizedDestination = NormalizeComparedPath(destination).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalizedDestination) || normalizedDestination == ".")
            {
                flowState.UnknownExtractedContent.Add(
                    new SavedDownload(
                        positionOffset + match.Index,
                        GetReportedLine(original, match.Index, lineOffset, countLogicalLines)));
                continue;
            }

            flowState.UntrustedDirectoryPrefixes.TryAdd(
                normalizedDestination + "/",
                new SavedDownload(
                    positionOffset + match.Index,
                    GetReportedLine(original, match.Index, lineOffset, countLogicalLines)));
        }
    }

    private static IEnumerable<string> EnumerateCloneOperands(string[] tokens)
    {
        var valueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-b", "--branch", "--depth", "--origin", "-o", "--config", "-c", "--filter",
            "--reference", "--reference-if-able", "--separate-git-dir", "--template", "--upload-pack",
            "-u", "--ssh-command", "-j", "--jobs", "--server-option", "--shallow-since",
            "--shallow-exclude", "--upstream-remote-name", "-r", "--rev", "--revision", "--ssh",
            "--remotecmd", "--depth", "--username", "--password", "--config-dir", "--config-option"
        };
        var afterOptions = false;
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (!afterOptions && token == "--")
            {
                afterOptions = true;
                continue;
            }
            if (!afterOptions && token.StartsWith("-", StringComparison.Ordinal))
            {
                var optionName = token.Split('=', 2)[0];
                if (!token.Contains('=') && valueOptions.Contains(optionName))
                    index++;
                continue;
            }
            yield return token;
        }
    }

    private static bool IsRemoteRepository(string command, string source)
    {
        var normalized = NormalizeToken(source);
        if (normalized.Contains("://", StringComparison.Ordinal) ||
            normalized.StartsWith("git@", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(normalized, @"^[^@\s]+@[^:\s]+:.+$", RegexOptions.CultureInvariant))
            return true;
        if (command is "gh" or "glab" or "hub")
            return normalized.Count(character => character == '/') == 1;
        return Regex.IsMatch(normalized, @"^(?![A-Za-z]:[\\/])[^/\s:]+:.+$", RegexOptions.CultureInvariant);
    }

    private static string DeriveRepositoryDirectory(string source)
    {
        var normalized = NormalizeToken(source).Replace('\\', '/').TrimEnd('/');
        var separator = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf(':'));
        var directory = separator >= 0 ? normalized[(separator + 1)..] : normalized;
        return directory.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? directory[..^4] : directory;
    }

    private static bool TryResolveUntrustedExecutionOrigin(
        string rawCandidate,
        long executionPosition,
        RemoteExecutionFlowState flowState,
        out SavedDownload origin,
        out string reportKey)
    {
        var candidate = NormalizeComparedPath(rawCandidate);
        if (flowState.DownloadedPaths.TryGetValue(candidate, out origin) && origin.Position < executionPosition)
        {
            reportKey = candidate;
            return true;
        }

        foreach (var (prefix, prefixOrigin) in flowState.UntrustedDirectoryPrefixes)
        {
            if (prefixOrigin.Position >= executionPosition ||
                !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            origin = prefixOrigin;
            reportKey = "directory:" + prefix + candidate;
            return true;
        }

        if (IsLikelyLocalExecutionPath(candidate))
        {
            foreach (var extraction in flowState.UnknownExtractedContent)
            {
                if (extraction.Position >= executionPosition)
                    continue;
                origin = extraction;
                reportKey = "archive:" + extraction.Position + ":" + candidate;
                return true;
            }
        }

        origin = default;
        reportKey = string.Empty;
        return false;
    }

    private static bool IsLikelyLocalExecutionPath(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith("-", StringComparison.Ordinal) ||
            candidate.Contains("://", StringComparison.Ordinal))
            return false;
        return candidate.Contains('/') || Path.HasExtension(candidate);
    }
}
