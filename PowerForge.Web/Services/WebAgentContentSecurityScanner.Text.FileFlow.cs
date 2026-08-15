using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex FileTransformCommandRegex = new(
        @"(?:^|&&|;|(?<!&)&(?!&)|\r?\n)\s*(?:sudo\s+)?(?<command>mv|move|cp|copy|ren|rename|Move-Item|Copy-Item|Rename-Item|mi|cpi|rni)\b(?<arguments>[^\r\n;&|]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex DownloadedArtifactReaderPipelineRegex = new(
        @"(?:^|&&|;|(?<!&)&(?!&)|\r?\n)\s*(?:sudo\s+)?(?<reader>cat|type|more|head|tail|Get-Content|gc)\b(?<arguments>[^\r\n;&|]*)(?<pipeline>(?:\|[^\r\n;&]*)+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex DownloadedArtifactReaderSubstitutionRegex = new(
        @"(?:\$\(\s*|`)(?<reader>cat|type|more|head|tail|Get-Content|gc)\b(?<arguments>[^)\r\n`]*)(?:\)|`)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StartProcessInvocationRegex = new(
        @"(?:^|&&|;|(?<!&)&(?!&)|\r?\n)\s*(?<launcher>Start-Process|saps|start|Invoke-Item|ii)\b(?<arguments>[^\r\n;&|]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static void PropagateDownloadedPathsThroughFileTransforms(
        string content,
        long positionOffset,
        RemoteExecutionFlowState flowState)
    {
        foreach (Match transform in FileTransformCommandRegex.Matches(content))
        {
            foreach (var (source, destination) in EnumerateFileTransformPaths(transform.Groups["arguments"].Value))
            {
                var transformPosition = positionOffset + transform.Index;
                var normalizedSource = ResolveComparedPathAt(source, transformPosition, flowState);
                if (!flowState.DownloadedPaths.TryGetValue(normalizedSource, out var download) ||
                    download.Position >= transformPosition)
                    continue;

                var normalizedDestination = ResolveComparedPathAt(destination, transformPosition, flowState);
                foreach (var candidate in ResolveFileTransformDestinations(normalizedSource, normalizedDestination))
                    flowState.DownloadedPaths.TryAdd(candidate, download);
            }
        }
    }

    private static void ScanDownloadedReaderPipelines(
        string content,
        string path,
        List<WebAgentContentSecurityFinding> findings,
        long positionOffset,
        RemoteExecutionFlowState flowState)
    {
        foreach (Match reader in DownloadedArtifactReaderPipelineRegex.Matches(content))
        {
            if (!InterpreterCommandRegex.IsMatch(reader.Groups["pipeline"].Value))
                continue;

            var readerPosition = positionOffset + reader.Index;
            ReportDownloadedReaderArguments(
                reader.Groups["arguments"].Value,
                readerPosition,
                path,
                findings,
                flowState);
        }

        foreach (Match interpreter in InterpreterCommandRegex.Matches(content))
        {
            var arguments = interpreter.Groups["arguments"];
            foreach (Match substitution in DownloadedArtifactReaderSubstitutionRegex.Matches(arguments.Value))
            {
                ReportDownloadedReaderArguments(
                    substitution.Groups["arguments"].Value,
                    positionOffset + arguments.Index + substitution.Index,
                    path,
                    findings,
                    flowState);
            }
        }
    }

    private static bool ReportDownloadedReaderArguments(
        string arguments,
        long readerPosition,
        string path,
        List<WebAgentContentSecurityFinding> findings,
        RemoteExecutionFlowState flowState)
    {
        foreach (var token in Tokenize(arguments))
        {
            var candidate = ResolveComparedPathAt(token, readerPosition, flowState);
            if (!flowState.DownloadedPaths.TryGetValue(candidate, out var download) ||
                download.Position >= readerPosition ||
                !flowState.ReportedPaths.Add(candidate))
                continue;

            AddFinding(findings, "error", "PFAGENT.COMMAND.REMOTE_EXECUTION", path, download.Line,
                "Downloaded content is read from a saved file and passed to an interpreter. Prefer a pinned, integrity-checked artifact and a separate execution step.");
            return true;
        }
        return false;
    }

    private static IEnumerable<(string Path, int Index)> EnumerateStartProcessPaths(string content)
    {
        foreach (Match match in StartProcessInvocationRegex.Matches(content))
        {
            var tokens = Tokenize(match.Groups["arguments"].Value);
            var path = FindNamedFileTransformValue(tokens, "-FilePath", "-Path", "-LiteralPath");
            if (string.IsNullOrWhiteSpace(path) && tokens.Length > 0 &&
                !tokens[0].StartsWith("-", StringComparison.Ordinal))
                path = tokens[0];
            if (!string.IsNullOrWhiteSpace(path))
                yield return (path, match.Index);
        }
    }

    private static IEnumerable<(string Source, string Destination)> EnumerateFileTransformPaths(string arguments)
    {
        var tokens = Tokenize(arguments);
        var source = FindNamedFileTransformValue(tokens, "-Path", "-LiteralPath") ?? string.Empty;
        var destination = FindNamedFileTransformValue(tokens, "-Destination", "-NewName") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(destination))
        {
            yield return (source, destination);
            yield break;
        }

        var targetDirectory = FindNamedFileTransformValue(tokens, "-t", "--target-directory");

        var operands = new List<string>();
        for (var index = 0; index < tokens.Length; index++)
        {
            if (tokens[index].StartsWith("-", StringComparison.Ordinal))
            {
                if (tokens[index].Equals("-Path", StringComparison.OrdinalIgnoreCase) ||
                    tokens[index].Equals("-LiteralPath", StringComparison.OrdinalIgnoreCase) ||
                    tokens[index].Equals("-Destination", StringComparison.OrdinalIgnoreCase) ||
                    tokens[index].Equals("-NewName", StringComparison.OrdinalIgnoreCase) ||
                    tokens[index].Equals("-t", StringComparison.OrdinalIgnoreCase) ||
                    tokens[index].Equals("--target-directory", StringComparison.OrdinalIgnoreCase))
                    index++;
                continue;
            }
            operands.Add(tokens[index]);
        }

        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            foreach (var operand in operands)
                yield return (operand, targetDirectory + "/");
            yield break;
        }

        if (operands.Count < 2)
            yield break;
        destination = operands[^1];
        foreach (var operand in operands.Take(operands.Count - 1))
            yield return (operand, destination);
    }

    private static string? FindNamedFileTransformValue(string[] tokens, params string[] names)
    {
        for (var index = 0; index < tokens.Length; index++)
        {
            foreach (var name in names)
            {
                if (tokens[index].StartsWith(name + ":", StringComparison.OrdinalIgnoreCase) ||
                    tokens[index].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return tokens[index][(name.Length + 1)..];
                if (tokens[index].Equals(name, StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Length)
                    return tokens[index + 1];
            }
        }
        return null;
    }

    private static IEnumerable<string> ResolveFileTransformDestinations(string source, string destination)
    {
        var normalizedDestination = NormalizeComparedPath(destination);
        yield return normalizedDestination;

        var sourceFileName = source[(source.LastIndexOf('/') + 1)..];
        if (!string.IsNullOrWhiteSpace(sourceFileName))
            yield return normalizedDestination.TrimEnd('/') + "/" + sourceFileName;

        var sourceDirectoryEnd = source.LastIndexOf('/');
        if (sourceDirectoryEnd >= 0 && !normalizedDestination.Contains('/'))
            yield return source[..(sourceDirectoryEnd + 1)] + normalizedDestination;
    }
}
