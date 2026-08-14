using System.Text.RegularExpressions;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static void ParseComposer(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (!ValidatePackageSourceOptions("packagist", tokens, path, line, findings))
            return;
        var verbIndex = FindNextOperand(tokens, 1, "composer");
        if (verbIndex < 0)
            return;
        var verb = NormalizeComposerVerb(tokens[verbIndex]);
        if (verb is null)
        {
            AddUnverifiableOperand("composer", path, line, findings, tokens[verbIndex]);
            return;
        }
        if (verb is "config" or "repository")
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"Package-manager configuration command for '{tokens[0]}' can change the source used by later installation commands; argument values are redacted.");
            return;
        }
        if (verb == "create-project")
        {
            AddUnverifiableOperand("composer create-project", path, line, findings, "project dependency set");
            return;
        }
        if (verb is "update" or "upgrade" or "reinstall" or "remove" or "uninstall")
        {
            AddUnverifiableOperand("composer " + verb, path, line, findings, "project dependency set");
            return;
        }
        if (verb == "install")
        {
            AddUnverifiableOperand("composer install", path, line, findings, "lockfile dependency set");
            return;
        }
        AddMultipleOperands("packagist", "composer require", tokens, verbIndex + 1, path, line, references, findings);
    }

    private static void ParseBundle(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (!ValidatePackageSourceOptions("rubygems", tokens, path, line, findings))
            return;
        var verbIndex = FindNextOperand(tokens, 1, "bundle");
        if (verbIndex < 0)
            return;
        var verb = NormalizeBundleVerb(tokens[verbIndex]);
        if (verb is null)
        {
            AddUnverifiableOperand("bundle", path, line, findings, tokens[verbIndex]);
            return;
        }
        if (verb == "config")
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"Package-manager configuration command for '{tokens[0]}' can change the source used by later installation commands; argument values are redacted.");
            return;
        }
        if (verb == "install")
        {
            AddUnverifiableOperand("bundle install", path, line, findings, "lockfile dependency set");
            return;
        }
        if (verb == "update")
        {
            AddUnverifiableOperand("bundle update", path, line, findings, "project dependency set");
            return;
        }
        AddMultipleOperands("rubygems", "bundle add", tokens, verbIndex + 1, path, line, references, findings);
    }

    private static string? NormalizeComposerVerb(string value)
    {
        value = value.ToLowerInvariant();
        if (value == "r" || value.StartsWith("req", StringComparison.Ordinal) && "require".StartsWith(value, StringComparison.Ordinal))
            return "require";
        if (value is "i" or "install")
            return "install";
        if (value == "create-project")
            return value;
        if (value == "config")
            return value;
        if (value is "repository" or "repo")
            return "repository";
        if (value is "u" or "update")
            return "update";
        if (value is "upgrade" or "reinstall" or "uninstall")
            return value;
        if (value is "rm" or "remove")
            return "remove";
        return null;
    }

    private static string? NormalizeBundleVerb(string value)
    {
        value = value.ToLowerInvariant();
        if (value is "a" or "ad" or "add")
            return "add";
        if (value is "i" or "in" or "ins" or "inst" or "insta" or "instal" or "install")
            return "install";
        if (value is "u" or "up" or "upd" or "upda" or "updat" or "update")
            return "update";
        if (value is "config" or "conf" or "confi")
            return "config";
        return null;
    }

    private static void ParseRubyGems(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 2 || !ValidatePackageSourceOptions("rubygems", tokens, path, line, findings))
            return;
        var verbIndex = FindKnownVerbIndex(tokens, 1,
            new[] { "install", "i", "in", "ins", "inst", "insta", "instal", "update", "upd", "upda", "updat", "exec", "ex", "exe" },
            "gem", path, line, findings);
        if (verbIndex < 0)
            return;
        if (tokens[verbIndex].Equals("exec", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("ex", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("exe", StringComparison.OrdinalIgnoreCase))
        {
            var selectedGems = FindOptionValues(tokens, verbIndex + 1, "--gem", "-g");
            if (selectedGems.Count > 1 || selectedGems.Any(string.IsNullOrWhiteSpace))
            {
                AddUnverifiableOperand("gem exec", path, line, findings, "ambiguous --gem package selection");
                return;
            }
            if (selectedGems.Count == 1)
            {
                AddToken("rubygems", "gem exec", selectedGems[0], FindVersionOption(tokens, 0), path, line, references, findings);
                return;
            }
            AddRunnerOperand("rubygems", "gem exec", tokens, verbIndex + 1, path, line, references, findings);
            return;
        }
        if (tokens.Any(static token =>
                token.Equals("-g", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--file", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("--file=", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnverifiableOperand("gem", path, line, findings, "RubyGems dependency file");
            return;
        }
        AddMultipleOperands("rubygems", "gem " + tokens[verbIndex], tokens, verbIndex + 1, path, line, references, findings);
    }

    private static void AddNodeInitializer(
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var index = FindNextOperand(tokens, start);
        if (index < 0)
        {
            AddUnverifiableOperand("npm init", path, line, findings, "initializer package");
            return;
        }
        var operand = NormalizeToken(tokens[index]);
        var package = operand.StartsWith('@')
            ? operand.Contains('/')
                ? operand[..operand.IndexOf('/')] + "/create-" + operand[(operand.IndexOf('/') + 1)..]
                : operand + "/create"
            : "create-" + operand;
        AddToken("npm", "npm init", package, null, path, line, references, findings);
    }

    private static void AddPowerShellNames(
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
        var version = FindVersionOption(tokens, 0);
        for (var index = start; index < tokens.Length; index++)
        {
            if (tokens[index].StartsWith("-", StringComparison.Ordinal))
                break;
            foreach (var name in tokens[index].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddToken(ecosystem, command, name, version, path, line, references, findings);
                added = true;
            }
        }
        if (!added)
            AddUnverifiableOperand(command, path, line, findings);
    }

    private static bool RejectPersistentPackageConfiguration(
        string executable,
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        var verb = FindLeadingVerb(tokens, executable);
        var changesConfiguration = executable is "register-psrepository" or "set-psrepository" or
                "register-psresourcerepository" or "set-psresourcerepository" ||
            executable == "dotnet" && tokens.Length > 3 && tokens[1].Equals("nuget", StringComparison.OrdinalIgnoreCase) &&
                tokens[3].Equals("source", StringComparison.OrdinalIgnoreCase) ||
            executable == "gem" && verb == "sources" ||
            executable == "composer" && verb is "config" or "repository" or "repo" ||
            executable == "bundle" && verb == "config" ||
            executable is "npm" or "pnpm" or "yarn" or "bun" && NormalizeNodeVerb(verb ?? string.Empty) is "config" or "set" ||
            executable is "pip" or "pip3" && verb == "config" ||
            executable is "python" or "python3" or "py" && tokens.Length > 3 &&
                tokens[1].Equals("-m", StringComparison.OrdinalIgnoreCase) && tokens[2].Equals("pip", StringComparison.OrdinalIgnoreCase) &&
                tokens[3].Equals("config", StringComparison.OrdinalIgnoreCase) ||
            executable == "cargo" && verb == "config";
        if (!changesConfiguration)
            return false;

        AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
            $"Package-manager configuration command for '{tokens[0]}' can change the source used by later installation commands; argument values are redacted.");
        return true;
    }

    private static string? FindLeadingVerb(string[] tokens, string optionContext)
    {
        for (var index = 1; index < tokens.Length; index++)
        {
            if (!tokens[index].StartsWith("-", StringComparison.Ordinal))
                return tokens[index].ToLowerInvariant();
            if (!TrySkipOption(tokens, ref index, optionContext))
                return null;
        }
        return null;
    }

    private static bool TrySkipOption(string[] tokens, ref int index, string? optionContext = null)
    {
        var option = tokens[index];
        var equals = option.IndexOf('=');
        if (equals > 0)
        {
            var name = option[..equals];
            return OptionConsumesValue(name, optionContext) || OptionIsFlag(name, optionContext);
        }
        if (OptionIsFlag(option, optionContext))
            return true;
        if (!OptionConsumesValue(option, optionContext) || index + 1 >= tokens.Length)
            return false;
        index++;
        return true;
    }

    private static string NormalizeToken(string token)
        => token.Trim().Trim('\'', '"', (char)0x60, ',', ';');

    private static string? NormalizeVersion(string token)
    {
        var normalized = NormalizeToken(token).TrimStart('v');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool IsCandidatePackageId(string? id)
        => !string.IsNullOrWhiteSpace(id) &&
           id.Length <= 256 &&
           id[0] != '-' &&
           id.IndexOfAny(new[] { '$', '{', '}', '<', '>', '*', '?', '%', '(', ')' }) < 0 &&
           !Uri.TryCreate(id, UriKind.Absolute, out _);

    private static readonly string[] NodeVerbs =
    {
        "exec", "x", "dlx", "install", "i", "in", "ins", "inst", "insta", "instal",
        "isnt", "isnta", "isntal", "isntall", "add", "ci", "clean-install", "ic", "install-clean", "isntall-clean",
        "install-test", "it", "ci-test", "cit", "install-ci-test", "install-clean-test", "clean-install-test", "sit",
        "update", "up", "upgrade", "udpate",
        "audit", "link", "ln", "dedupe", "ddp", "rebuild",
        "config", "c", "conf", "set", "init", "create", "innit"
    };

    private static string[] Tokenize(string command)
        => ShellTokenRegex.Matches(StripShellComment(ShellContinuationRegex.Replace(command, " ")))
            .Select(static token => token.Groups["quoted"].Success ? token.Groups["quoted"].Value : token.Groups["plain"].Value)
            .Select(NormalizeToken)
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

    private static string NormalizeNodeVerb(string verb)
    {
        verb = verb.ToLowerInvariant();
        return verb switch
        {
            "i" or "in" or "ins" or "inst" or "insta" or "instal" or
                "isnt" or "isnta" or "isntal" or "isntall" => "install",
            "c" or "conf" => "config",
            "clean-install" or "ic" or "install-clean" or "isntall-clean" => "ci",
            "ci-test" or "cit" or "install-ci-test" or "install-clean-test" or "clean-install-test" or "sit" => "ci",
            "install-test" or "it" => "install",
            "up" or "upgrade" or "udpate" => "update",
            "ln" => "link",
            "ddp" => "dedupe",
            "create" or "innit" => "init",
            _ => verb
        };
    }

    private static string StripShellComment(string command)
    {
        char quote = '\0';
        var escaped = false;
        for (var index = 0; index < command.Length; index++)
        {
            var current = command[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (current == '\\' && quote != '\'')
            {
                escaped = true;
                continue;
            }
            if (quote != '\0')
            {
                if (current == quote)
                    quote = '\0';
                continue;
            }
            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }
            if (current == '#' && (index == 0 || char.IsWhiteSpace(command[index - 1])))
                return command[..index].TrimEnd();
        }
        return command;
    }

    private static string NormalizeExecutable(string executable)
    {
        var normalized = executable.ToLowerInvariant();
        if (normalized.EndsWith(".exe", StringComparison.Ordinal) ||
            normalized.EndsWith(".cmd", StringComparison.Ordinal))
            normalized = Path.GetFileNameWithoutExtension(normalized);
        var launcherName = Path.GetFileName(normalized.Replace('\\', '/'));
        normalized = launcherName switch
        {
            "npm-cli.js" => "npm",
            "npx-cli.js" => "npx",
            "pnpm.cjs" => "pnpm",
            "pnpx.cjs" => "pnpx",
            "yarn.js" => "yarn",
            "yarnpkg.js" => "yarn",
            "corepack.js" => "corepack",
            "corepack.cjs" => "corepack",
            "composer.phar" => "composer",
            _ => normalized
        };
        if (normalized == "yarnpkg")
            return "yarn";
        if (Regex.IsMatch(normalized, @"^python\d+(?:\.\d+)*$", RegexOptions.CultureInvariant))
            return "python";
        if (Regex.IsMatch(normalized, @"^pip\d+(?:\.\d+)*$", RegexOptions.CultureInvariant))
            return "pip";
        return normalized;
    }

    private static bool IsNpmRegistrySelector(string selector)
    {
        selector = selector.Trim();
        if (string.IsNullOrWhiteSpace(selector))
            return false;
        return !selector.Contains(':', StringComparison.Ordinal) &&
               !selector.StartsWith(".", StringComparison.Ordinal) &&
               !selector.StartsWith("/", StringComparison.Ordinal) &&
               !selector.StartsWith("\\", StringComparison.Ordinal) &&
               !selector.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNpmNonRegistryOperand(string token)
    {
        var value = token.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return true;
        var selectorSeparator = value.StartsWith('@') ? value.IndexOf('@', 1) : value.IndexOf('@');
        var packagePart = selectorSeparator > 0 ? value[..selectorSeparator] : value;
        if (packagePart.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
            packagePart.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            packagePart.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value.StartsWith(".", StringComparison.Ordinal) ||
            value.StartsWith("/", StringComparison.Ordinal) ||
            value.StartsWith("\\", StringComparison.Ordinal) ||
            value.StartsWith("~", StringComparison.Ordinal) ||
            value.Contains('\\') ||
            Regex.IsMatch(value, @"^[A-Za-z]:", RegexOptions.CultureInvariant))
            return true;
        if (!value.StartsWith('@') && value.Contains('/'))
            return true;
        return value.Contains(':', StringComparison.Ordinal) ||
               value.Contains('#', StringComparison.Ordinal);
    }

    private static bool TryGetNpmSelector(string token, out string selector)
    {
        var separator = token.StartsWith('@') ? token.IndexOf('@', 1) : token.IndexOf('@');
        if (separator <= 0)
        {
            selector = string.Empty;
            return false;
        }
        selector = token[(separator + 1)..];
        return true;
    }
}
