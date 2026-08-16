using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static readonly Regex CommandSegmentRegex = new(
        @"(?<![A-Za-z0-9_.-])(?<command>(?:dotnet|dnx|nuget(?:\.exe|\.cmd)?(?=\s+(?:install|restore|update|sources?)\b)|Install-Package|Update-Package|Save-Package|Install-Module|Save-Module|Install-Script|Update-Script|Save-Script|Install-PSResource|Save-PSResource|Update-Module|Update-PSResource|Install-PackageProvider|Register-PSRepository|Set-PSRepository|Register-PSResourceRepository|Set-PSResourceRepository|corepack|npm|npx|pnpx|pnpm|yarnpkg|yarn|bun|bunx|python(?:\d+(?:\.\d+)*)?|py|pip(?:\d+(?:\.\d+)*)?|uv|uvx|pipx|poetry(?=\s+(?:add|install|sync|update|remove|lock|run|build|self|plugin|source|config|python)\b)|cargo|gem|composer|bundler|bundle)\b(?:[^\x5C`\^\r\n;&|]|[\x5C`\^]\r?\n|[\x5C`\^][^\r\n])*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ShellTokenConstructionRegex = new(
        @"[\x5C`](?!\r?\n)[^\r\n]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ShellContinuationTokenConstructionRegex = new(
        @"(?<=\S)[\x5C`\^]\r?\n(?=\S)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ShellExpansionRegex = new(
        @"(?:\$\(|[<>]\(|\$\{|\$env:|%[A-Za-z_][A-Za-z0-9_]*%|\$[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ObfuscatedExecutableRegex = new(
        @"(?<![A-Za-z0-9_.-])(?<command>(?:[A-Za-z0-9_.-]+[\x5C`\^][A-Za-z0-9_.\x5C`\^-]+|[A-Za-z0-9_.-]+(?:['""][A-Za-z0-9_.-]*['""][A-Za-z0-9_.-]*)+))(?=\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex ShellContinuationRegex = new(
        @"[\x5C\`\^]\r?\n",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ShellTokenRegex = new(
        @"['""](?<quoted>[^'""]+)['""]|(?<plain>[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static IEnumerable<WebAgentPackageReference> ExtractPackageReferences(
        string content,
        string path,
        int lineOffset,
        bool countLogicalLines,
        List<WebAgentContentSecurityFinding> findings,
        PackageExecutionFlowState? flowState = null)
    {
        var references = new List<WebAgentPackageReference>();
        var workingDirectoryChange = PersistentWorkingDirectoryChangeRegex.Match(content);
        var remoteExecutionContextChange = PersistentRemoteExecutionContextRegex.Match(content);
        ScanObfuscatedPackageExecutables(content, path, lineOffset, countLogicalLines, findings);
        ScanDynamicExecutableInvocations(content, path, lineOffset, countLogicalLines, findings);
        foreach (Match match in CommandSegmentRegex.Matches(content))
        {
            if (!IsPackageCommandInvocationContext(content, match.Index) &&
                !IsUntrustedExecutableReference(content, match.Index) &&
                !HasCommandScopedEnvironmentPrefix(content, match.Index) &&
                !HasExecutionContextWrapperPrefix(content, match.Index) &&
                !HasDataAppendingWrapperPrefix(content, match.Index) &&
                !HasRemoteExecutionWrapperPrefix(content, match.Index) &&
                flowState?.WorkingDirectoryChanged != true &&
                !(workingDirectoryChange.Success && workingDirectoryChange.Index < match.Index) &&
                flowState?.RemoteExecutionContextChanged != true &&
                !(remoteExecutionContextChange.Success && remoteExecutionContextChange.Index < match.Index) &&
                !HasShellCommandSeparatorPrefix(content, match.Index))
                continue;

            var matchedCommand = TrimMarkdownInlineCodeCommand(
                content,
                match.Index,
                match.Groups["command"].Value);
            var commandForValidation = StripShellComment(matchedCommand);
            if (IsUntrustedExecutableReference(content, match.Index))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND", path,
                    GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    "Package command operands must not use shell escape construction; spell the executable and package identifiers literally.");
                continue;
            }
            if (HasCommandScopedEnvironmentPrefix(content, match.Index) ||
                HasExecutionContextWrapperPrefix(content, match.Index) ||
                flowState?.WorkingDirectoryChanged == true ||
                workingDirectoryChange.Success && workingDirectoryChange.Index < match.Index ||
                flowState?.RemoteExecutionContextChanged == true ||
                remoteExecutionContextChange.Success && remoteExecutionContextChange.Index < match.Index ||
                HasRemoteExecutionWrapperPrefix(content, match.Index))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_ENVIRONMENT", path,
                    GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    "Package commands with identity, environment, working-directory, or remote-execution wrappers cannot be proven to use the canonical public registry.");
                continue;
            }
            if (HasShellQuoteConcatenation(commandForValidation) ||
                ShellTokenConstructionRegex.IsMatch(commandForValidation) ||
                ShellContinuationTokenConstructionRegex.IsMatch(commandForValidation))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND", path,
                    GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    "Package command operands must not use shell escape construction; spell the executable and package identifiers literally.");
                continue;
            }
            if (HasDataAppendingWrapperPrefix(content, match.Index))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND", path,
                    GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    "Package commands invoked through data-appending wrappers can receive unchecked package operands from standard input.");
                continue;
            }
            var tokens = Tokenize(commandForValidation);
            if (tokens.Length == 0)
                continue;

            tokens[0] = NormalizeExecutable(tokens[0]);
            if (!IsSupportedPackageExecutable(tokens[0]))
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND", path,
                    GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                    "Package-manager executable tokens must be literal known launcher names without shell expansion or surrounding syntax.");
                continue;
            }
            var executable = tokens[0];
            var line = GetReportedLine(content, match.Index, lineOffset, countLogicalLines);
            if (tokens.Length < 2)
            {
                if (executable is "yarn" or "bundle")
                    AddUnverifiableOperand(executable, path, line, findings, "lockfile dependency set");
                continue;
            }
            if (RejectPersistentPackageConfiguration(executable, tokens, path, line, findings))
                continue;
            var commandReferences = new List<WebAgentPackageReference>();
            var findingCountBefore = findings.Count;
            switch (executable)
            {
                case "dotnet":
                    ParseDotNet(tokens, path, line, commandReferences, findings);
                    break;
                case "dnx":
                    if (ValidatePackageSourceOptions("nuget", tokens, path, line, findings))
                        AddRunnerOperand("nuget", "dnx", tokens, 1, path, line, commandReferences, findings);
                    break;
                case "nuget":
                    ParseNuGetCli(tokens, path, line, commandReferences, findings);
                    break;
                case "install-module":
                case "save-module":
                case "install-script":
                case "update-script":
                case "save-script":
                case "install-psresource":
                case "save-psresource":
                case "update-module":
                case "update-psresource":
                    ParsePowerShell(tokens, path, line, commandReferences, findings);
                    break;
                case "install-packageprovider":
                    AddUnverifiableOperand("Install-PackageProvider", path, line, findings,
                        "package-provider executable dependency");
                    break;
                case "install-package":
                case "update-package":
                case "save-package":
                    ParsePowerShellNuGet(tokens, path, line, commandReferences, findings);
                    break;
                case "corepack":
                    ParseCorepack(tokens, path, line, findings);
                    break;
                case "npm":
                case "pnpm":
                case "yarn":
                case "bun":
                    ParseNode(tokens, path, line, commandReferences, findings);
                    break;
                case "npx":
                case "pnpx":
                case "bunx":
                    if (ValidatePackageSourceOptions("npm", tokens, path, line, findings))
                        AddNodeRunnerOperands(tokens[0], tokens, 1, path, line, commandReferences, findings);
                    break;
                case "python":
                case "python3":
                case "py":
                case "pip":
                case "pip3":
                case "uv":
                case "pipx":
                    ParsePython(tokens, path, line, commandReferences, findings);
                    break;
                case "uvx":
                    if (ValidatePackageSourceOptions("pypi", tokens, path, line, findings))
                        AddRunnerOperand("pypi", "uvx", tokens, 1, path, line, commandReferences, findings);
                    break;
                case "poetry":
                    ParsePoetry(tokens, path, line, findings);
                    break;
                case "cargo":
                    ParseCargo(tokens, path, line, commandReferences, findings);
                    break;
                case "gem":
                    ParseRubyGems(tokens, path, line, commandReferences, findings);
                    break;
                case "composer":
                    ParseComposer(tokens, path, line, commandReferences, findings);
                    break;
                case "bundle":
                    ParseBundle(tokens, path, line, commandReferences, findings);
                    break;
            }
            if (findings.Count == findingCountBefore && HasShellExpansionInOptionValue(tokens))
                AddFinding(findings, "error", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND", path, line,
                    "Package command option values must be literal and must not use shell expansion.");
            if (findings.Count == findingCountBefore && findings.Count < MaximumFindingCount)
                references.AddRange(commandReferences);
        }

        if (flowState is not null && workingDirectoryChange.Success)
            flowState.WorkingDirectoryChanged = true;
        if (flowState is not null && remoteExecutionContextChange.Success)
            flowState.RemoteExecutionContextChanged = true;

        return references;
    }

    private static bool HasShellExpansionInOptionValue(string[] tokens)
    {
        for (var index = 1; index < tokens.Length; index++)
        {
            var option = tokens[index];
            if (!option.StartsWith("-", StringComparison.Ordinal))
                continue;

            var equals = option.IndexOf('=');
            if (equals > 0 && ShellExpansionRegex.IsMatch(option[(equals + 1)..]))
                return true;

            var optionIndex = index;
            if (TrySkipOption(tokens, ref optionIndex, tokens[0]) && optionIndex > index &&
                ShellExpansionRegex.IsMatch(tokens[optionIndex]))
                return true;
            index = optionIndex;
        }
        return false;
    }

    private static void ScanObfuscatedPackageExecutables(
        string content,
        string path,
        int lineOffset,
        bool countLogicalLines,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        foreach (Match match in ObfuscatedExecutableRegex.Matches(content))
        {
            if (!IsPackageCommandInvocationContext(content, match.Index))
                continue;

            var escaped = match.Groups["command"].Value;
            var normalized = NormalizeExecutable(escaped.Replace("\\", string.Empty, StringComparison.Ordinal)
                .Replace("`", string.Empty, StringComparison.Ordinal)
                .Replace("^", string.Empty, StringComparison.Ordinal)
                .Replace("'", string.Empty, StringComparison.Ordinal)
                .Replace("\"", string.Empty, StringComparison.Ordinal));
            if (!IsSupportedPackageExecutable(normalized))
                continue;

            AddFinding(findings, "error", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND", path,
                GetReportedLine(content, match.Index, lineOffset, countLogicalLines),
                $"Package-manager executable '{escaped}' uses shell token construction that obscures the command from static verification.");
        }
    }

    private static bool IsSupportedPackageExecutable(string executable)
        => executable is "dotnet" or "dnx" or "install-package" or "update-package" or "save-package" or "install-module" or "install-psresource" or
            "update-module" or "update-psresource" or "save-module" or "install-script" or "update-script" or "save-script" or
            "save-psresource" or "install-packageprovider" or "nuget" or
            "register-psrepository" or "set-psrepository" or "register-psresourcerepository" or
            "set-psresourcerepository" or "corepack" or "npm" or "npx" or "pnpx" or "pnpm" or "yarn" or
            "bun" or "bunx" or "python" or "py" or "pip" or "uv" or "uvx" or "pipx" or "poetry" or
            "cargo" or "gem" or "composer" or "bundle";

    private static void ParsePositionalInstall(
        string ecosystem,
        string command,
        string[] tokens,
        string[] verbs,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 2)
            return;
        if (!ValidatePackageSourceOptions(ecosystem, tokens, path, line, findings))
            return;
        if (ecosystem == "rubygems" && tokens.Any(static token =>
                token.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--file", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("--file=", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnverifiableOperand(command, path, line, findings, "RubyGems dependency file");
            return;
        }
        var verbIndex = FindKnownVerbIndex(tokens, 1, verbs, command, path, line, findings);
        if (verbIndex >= 0)
            AddMultipleOperands(ecosystem, command + " " + tokens[verbIndex], tokens, verbIndex + 1, path, line, references, findings);
    }

    private static void AddRunnerOperand(
        string ecosystem,
        string command,
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (RejectNestedPackageManagerPayload(command, tokens, start, path, line, findings))
            return;
        AddSingleOperand(ecosystem, command, tokens, start, path, line, references, findings);
    }

    private static void AddNodeRunnerOperands(
        string command,
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var packageOptions = FindOptionValues(tokens, start, "--package", "-p");
        if (packageOptions.Count == 0)
        {
            AddRunnerOperand("npm", command, tokens, start, path, line, references, findings);
            return;
        }

        foreach (var packageOption in packageOptions)
        {
            if (string.IsNullOrWhiteSpace(packageOption))
                AddUnverifiableOperand(command, path, line, findings, "--package");
            else
                AddToken("npm", command, packageOption, null, path, line, references, findings);
        }

        RejectNestedPackageManagerPayload(command, tokens, start, path, line, findings);
    }

    private static bool RejectNestedPackageManagerPayload(
        string command,
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (SupportsNodeCallPayload(command))
        {
            var callPayloads = FindOptionValues(tokens, start, "-c", "--call");
            foreach (var callPayload in callPayloads)
            {
                if (string.IsNullOrWhiteSpace(callPayload) ||
                    CommandSegmentRegex.IsMatch(callPayload) ||
                    ObfuscatedExecutableRegex.IsMatch(callPayload))
                {
                    AddUnverifiableOperand(command, path, line, findings, "nested package-manager call payload");
                    return true;
                }
            }
        }

        var delimiterIndex = Array.FindIndex(tokens, start, static token => token == "--");
        var payloadStart = delimiterIndex >= 0
            ? delimiterIndex + 1
            : FindNextOperand(tokens, start, command);
        return RejectPackageManagerInvocationAt(command, tokens, payloadStart, path, line, findings);
    }

    private static bool SupportsNodeCallPayload(string command)
        => command.Equals("npx", StringComparison.OrdinalIgnoreCase) ||
           command.Equals("pnpx", StringComparison.OrdinalIgnoreCase) ||
           command.Equals("bunx", StringComparison.OrdinalIgnoreCase) ||
           command.Equals("npm exec", StringComparison.OrdinalIgnoreCase) ||
           command.Equals("npm x", StringComparison.OrdinalIgnoreCase);

    private static bool RejectPackageManagerInvocationAt(
        string command,
        string[] tokens,
        int payloadStart,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (payloadStart < 0 || payloadStart >= tokens.Length)
            return false;
        var payload = string.Join(' ', tokens[payloadStart..]);
        if (!IsNestedPackageManagerInvocation(tokens, payloadStart) && !ObfuscatedExecutableRegex.IsMatch(payload))
            return false;

        AddUnverifiableOperand(command, path, line, findings, "nested package-manager runner payload");
        return true;
    }

    private static bool IsNestedPackageManagerInvocation(string[] tokens, int start)
    {
        if (start < 0 || start >= tokens.Length)
            return false;
        var executable = NormalizeExecutable(tokens[start]);
        if (executable is "python" or "py")
        {
            var module = Array.FindIndex(tokens, start + 1,
                static token => token.Equals("-m", StringComparison.OrdinalIgnoreCase));
            return module >= 0 && module + 1 < tokens.Length && IsPythonPipModule(tokens[module + 1]);
        }
        if (executable == "dotnet")
        {
            var arguments = tokens[(start + 1)..];
            return arguments.Length > 0 &&
                   (arguments[0].Equals("restore", StringComparison.OrdinalIgnoreCase) ||
                    arguments[0].Equals("add", StringComparison.OrdinalIgnoreCase) ||
                     arguments[0].Equals("package", StringComparison.OrdinalIgnoreCase) ||
                     arguments[0].Equals("tool", StringComparison.OrdinalIgnoreCase) ||
                     arguments[0].Equals("new", StringComparison.OrdinalIgnoreCase) ||
                     arguments[0] is "build" or "publish" or "run" or "test" or "pack" or "msbuild" or
                         "vstest" or "watch" or "format" or "workload");
        }
        return executable is "dnx" or "install-package" or "update-package" or "corepack" or
            "install-module" or "install-psresource" or "update-module" or "update-psresource" or
            "npm" or "npx" or "pnpx" or "pnpm" or "yarn" or "bun" or "bunx" or
            "pip" or "uv" or "uvx" or "pipx" or "cargo" or "gem" or "composer" or "bundle";
    }

    private static void AddSingleOperand(
        string ecosystem,
        string command,
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings,
        bool takeImmediateValue = false)
    {
        var index = takeImmediateValue ? start : FindNextOperand(tokens, start);
        if (index < 0 || index >= tokens.Length)
        {
            AddUnverifiableOperand(command, path, line, findings);
            return;
        }
        var version = FindVersionOption(tokens, 0);
        AddToken(ecosystem, command, tokens[index], version, path, line, references, findings);
    }

    private static void AddMultipleOperands(
        string ecosystem,
        string command,
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var added = false;
        for (var index = start; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token == "--")
                continue;
            if (token.StartsWith("-", StringComparison.Ordinal))
            {
                if (ecosystem == "rubygems" &&
                    (token.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
                     token.Equals("--file", StringComparison.OrdinalIgnoreCase) ||
                     token.StartsWith("--file=", StringComparison.OrdinalIgnoreCase)))
                {
                    AddUnverifiableOperand(command, path, line, findings, "RubyGems dependency file");
                    return;
                }
                if (ecosystem == "rubygems" && token.Equals("-v", StringComparison.OrdinalIgnoreCase) && index + 1 < tokens.Length)
                {
                    index++;
                    continue;
                }
                if (!TrySkipOption(tokens, ref index, ecosystem))
                {
                    AddUnverifiableOperand(command, path, line, findings, token);
                    return;
                }
                continue;
            }
            var optionVersion = ecosystem switch
            {
                "crates" => FindOptionValue(tokens, 0, "--version"),
                "rubygems" => FindVersionOption(tokens, 0),
                _ => null
            };
            AddToken(ecosystem, command, token, optionVersion, path, line, references, findings);
            added = true;
        }
        if (!added)
            AddUnverifiableOperand(command, path, line, findings);
    }

    private static void AddToken(
        string ecosystem,
        string command,
        string token,
        string? optionVersion,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        token = NormalizeToken(token);
        if (ecosystem != "packagist" && token.Contains('^'))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.OBFUSCATED_COMMAND", path, line,
                "Package operands outside Composer must not use caret shell construction.");
            return;
        }
        if (ecosystem == "npm" && IsNpmNonRegistryOperand(token))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"npm package operand '{RedactDiagnosticValue(token)}' uses a local archive, path, URL, or other non-registry source.");
            return;
        }
        if (ecosystem == "npm" && TryGetNpmSelector(token, out var npmSelector) && !IsNpmRegistrySelector(npmSelector))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"npm package operand '{RedactDiagnosticValue(token)}' uses a non-registry or unsupported package selector.");
            return;
        }
        var (id, embeddedVersion) = SplitPackageVersion(ecosystem, token);
        if (ecosystem == "packagist" && embeddedVersion?.Contains('#', StringComparison.Ordinal) == true)
        {
            AddUnverifiableOperand(command, path, line, findings, "Composer commit-reference constraint");
            return;
        }
        if (ecosystem == "packagist" && IsComposerPlatformRequirement(id))
            return;
        if (!IsCandidatePackageId(id))
        {
            AddUnverifiableOperand(command, path, line, findings, token);
            return;
        }
        if (ecosystem == "npm" && embeddedVersion is not null && !IsNpmRegistrySelector(embeddedVersion))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"npm package operand '{RedactDiagnosticValue(token)}' uses a non-registry or unsupported package selector.");
            return;
        }
        references.Add(new WebAgentPackageReference
        {
            Ecosystem = ecosystem,
            Id = id,
            Version = optionVersion ?? embeddedVersion,
            Path = path,
            Line = line,
            Command = command
        });
    }

    private static int FindNextOperand(string[] tokens, int start, string? optionContext = null)
    {
        for (var index = start; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token == "--")
                continue;
            if (!token.StartsWith("-", StringComparison.Ordinal))
                return index;
            if (!TrySkipOption(tokens, ref index, optionContext))
                return -1;
        }
        return -1;
    }

    private static string? FindOptionValue(string[] tokens, int start, params string[] names)
    {
        for (var index = start; index < tokens.Length; index++)
        {
            foreach (var name in names)
            {
                if (tokens[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return index + 1 < tokens.Length ? tokens[index + 1] : string.Empty;
                if (tokens[index].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return tokens[index][(name.Length + 1)..];
                if (tokens[index].StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                    return tokens[index][(name.Length + 1)..];
            }
        }
        return null;
    }

    private static List<string> FindOptionValues(string[] tokens, int start, params string[] names)
    {
        var values = new List<string>();
        for (var index = start; index < tokens.Length; index++)
        {
            if (tokens[index] == "--")
                break;
            foreach (var name in names)
            {
                if (tokens[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    values.Add(index + 1 < tokens.Length ? tokens[++index] : string.Empty);
                    break;
                }
                if (tokens[index].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    values.Add(tokens[index][(name.Length + 1)..]);
                    break;
                }
            }
        }
        return values;
    }

    private static int FindVerbIndex(
        string[] tokens,
        int start,
        string verb,
        string command,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        for (var index = start; index < tokens.Length; index++)
        {
            if (tokens[index].Equals(verb, StringComparison.OrdinalIgnoreCase))
                return index;
            if (tokens[index].StartsWith("-", StringComparison.Ordinal) && TrySkipOption(tokens, ref index, command))
                continue;
            AddUnverifiableOperand(command, path, line, findings, tokens[index]);
            return -1;
        }
        AddUnverifiableOperand(command, path, line, findings);
        return -1;
    }

    private static int FindKnownVerbIndex(
        string[] tokens,
        int start,
        IReadOnlyCollection<string> verbs,
        string command,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var candidateVerbIndex = Array.FindIndex(
            tokens,
            start,
            token => verbs.Contains(token, StringComparer.OrdinalIgnoreCase));
        if (candidateVerbIndex < 0)
            return -1;
        for (var index = start; index < tokens.Length; index++)
        {
            if (verbs.Contains(tokens[index], StringComparer.OrdinalIgnoreCase))
                return index;
            if (tokens[index].StartsWith("-", StringComparison.Ordinal) && TrySkipOption(tokens, ref index, command))
                continue;
            AddUnverifiableOperand(command, path, line, findings, tokens[index]);
            return -1;
        }
        return -1;
    }

    private static int FindPythonModuleIndex(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var moduleIndex = Array.FindIndex(tokens, 1, static token => token.Equals("-m", StringComparison.OrdinalIgnoreCase));
        if (moduleIndex < 0)
            return -1;
        for (var index = 1; index < moduleIndex; index++)
        {
            if (tokens[index].Equals("-P", StringComparison.Ordinal) ||
                tokens[index].Equals("-I", StringComparison.Ordinal))
                continue;
            if (tokens[index].Equals("-c", StringComparison.Ordinal) ||
                tokens[index].StartsWith("-c", StringComparison.Ordinal) && tokens[index].Length > 2)
            {
                AddUnverifiableOperand(tokens[0], path, line, findings, "inline Python execution mode");
                return -1;
            }
            if (Regex.IsMatch(tokens[index], @"^-\d+(?:\.\d+)?$", RegexOptions.CultureInvariant))
                continue;
            if (tokens[index].StartsWith("-", StringComparison.Ordinal) && TrySkipOption(tokens, ref index))
                continue;
            AddUnverifiableOperand(tokens[0] + " -m pip", path, line, findings, tokens[index]);
            return -1;
        }
        return moduleIndex;
    }

    private static string? FindVersionOption(string[] tokens, int start)
        => FindOptionValue(tokens, start, "--version", "-v", "-Version", "-RequiredVersion");

    private static bool IsPipInformationalInvocation(string[] tokens, int start)
        => start < tokens.Length && tokens[start].ToLowerInvariant() is
            "--version" or "-v" or "--help" or "-h" or "help";

    private static void AddUnverifiableOperand(
        string command,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings,
        string? operand = null)
        => AddFinding(findings, "error", "PFAGENT.PACKAGE.UNVERIFIABLE_OPERAND", path, line,
            string.IsNullOrWhiteSpace(operand)
                ? $"Package command '{command}' does not contain a statically verifiable package identifier."
                : $"Package command '{command}' uses dynamic or unsupported operand '{RedactDiagnosticValue(operand)}'.");

    private static string RedactDiagnosticValue(string value)
    {
        value = NormalizeToken(value);
        var separator = value.IndexOf('=');
        if (separator >= 0)
            return value[..separator] + "=<redacted>";
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            var authority = uri.IsDefaultPort
                ? $"{uri.Scheme}://{uri.IdnHost}"
                : $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}";
            return authority + "/<redacted>";
        }
        return value.Length <= 128 ? value : value[..128] + "...";
    }

    private static (string Id, string? Version) SplitPackageVersion(string ecosystem, string token)
    {
        token = NormalizeToken(token);
        if (ecosystem == "npm" || ecosystem == "nuget" && token.Contains('@'))
        {
            var separator = token.StartsWith('@') ? token.IndexOf('@', 1) : token.LastIndexOf('@');
            return separator > 0
                ? (token[..separator], NormalizeVersion(token[(separator + 1)..]))
                : (token, null);
        }
        if (ecosystem == "pypi")
        {
            var separator = token.IndexOfAny(new[] { '=', '<', '>', '!', '~' });
            if (separator > 0)
            {
                var id = TrimPythonExtras(token[..separator]);
                var suffix = token[separator..];
                return suffix.StartsWith("===", StringComparison.Ordinal)
                    ? (id, NormalizeVersion(suffix[3..]))
                    : suffix.StartsWith("==", StringComparison.Ordinal)
                    ? (id, NormalizeVersion(suffix[2..]))
                    : (id, NormalizeVersion(suffix));
            }
            return (TrimPythonExtras(token), null);
        }
        if (ecosystem == "crates")
        {
            var separator = token.LastIndexOf('@');
            return separator > 0
                ? (token[..separator], NormalizeVersion(token[(separator + 1)..]))
                : (token, null);
        }
        if (ecosystem == "packagist")
        {
            var whitespace = token.IndexOfAny(new[] { ' ', '\t' });
            if (whitespace > 0)
                return (token[..whitespace], NormalizeVersion(token[(whitespace + 1)..].Trim()));
            var colon = token.IndexOf(':');
            var equals = token.IndexOf('=');
            var separator = colon < 0 ? equals : equals < 0 ? colon : Math.Min(colon, equals);
            return separator > 0
                ? (token[..separator], NormalizeVersion(token[(separator + 1)..]))
                : (token, null);
        }
        return (token, null);
    }

    private static string TrimPythonExtras(string token)
    {
        var bracket = token.IndexOf('[');
        return bracket > 0 ? token[..bracket] : token;
    }

}
