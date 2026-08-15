namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static void ParseCorepack(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 2)
            return;
        var verb = tokens[1].ToLowerInvariant();
        if (verb is "--help" or "-h" or "--version" or "-v" or "help" or "enable" or "disable" ||
            verb == "cache" && tokens.Skip(2).FirstOrDefault()?.Equals("clean", StringComparison.OrdinalIgnoreCase) == true)
            return;
        AddUnverifiableOperand("corepack " + tokens[1], path, line, findings,
            "package-manager release or project dependency set");
    }

    private static void ParsePoetry(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 2)
            return;
        var verb = tokens[1].ToLowerInvariant();
        if (verb is "source" or "config")
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                "Poetry configuration commands can change package sources used by later installation commands; argument values are redacted.");
            return;
        }
        AddUnverifiableOperand("poetry " + tokens[1], path, line, findings,
            verb is "self" or "plugin" or "python"
                ? "Poetry-managed executable or plugin dependency set"
                : "Poetry project dependency set");
    }

    private static void ParseDotNet(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 2)
            return;
        if (!ValidatePackageSourceOptions("nuget", tokens, path, line, findings))
            return;
        var primaryVerb = tokens[1].ToLowerInvariant();
        if (primaryVerb == "restore")
        {
            AddUnverifiableOperand("dotnet restore", path, line, findings, "project dependency set");
            return;
        }
        if (primaryVerb is "build" or "publish" or "run" or "test" or "pack" or
            "msbuild" or "vstest" or "watch" or "format" or "workload")
        {
            AddUnverifiableOperand("dotnet " + tokens[1], path, line, findings,
                "project dependency graph or executable project targets");
            return;
        }
        if (tokens.Length < 3)
        {
            if (primaryVerb is not ("--info" or "--version" or "--list-sdks" or "--list-runtimes" or "--help" or "-h" or "help"))
                AddUnverifiableOperand("dotnet " + tokens[1], path, line, findings,
                    "unsupported SDK command or project dependency set");
            return;
        }
        if (tokens[1].Equals("tool", StringComparison.OrdinalIgnoreCase) &&
            tokens[2].Equals("restore", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand("dotnet tool restore", path, line, findings, "tool manifest dependency set");
            return;
        }
        if (tokens[1].Equals("tool", StringComparison.OrdinalIgnoreCase) &&
            (tokens[2].Equals("install", StringComparison.OrdinalIgnoreCase) ||
             tokens[2].Equals("update", StringComparison.OrdinalIgnoreCase)))
        {
            AddSingleOperand("nuget", "dotnet tool " + tokens[2], tokens, 3, path, line, references, findings);
            return;
        }
        if (tokens[1].Equals("package", StringComparison.OrdinalIgnoreCase) &&
            (tokens[2].Equals("add", StringComparison.OrdinalIgnoreCase) ||
             tokens[2].Equals("update", StringComparison.OrdinalIgnoreCase)))
        {
            AddSingleOperand("nuget", "dotnet package " + tokens[2], tokens, 3, path, line, references, findings);
            return;
        }
        if (tokens[1].Equals("new", StringComparison.OrdinalIgnoreCase) &&
            tokens[2].Equals("install", StringComparison.OrdinalIgnoreCase))
        {
            AddSingleOperand("nuget", "dotnet new install", tokens, 3, path, line, references, findings);
            return;
        }
        if (tokens[1].Equals("new", StringComparison.OrdinalIgnoreCase) &&
            tokens[2].Equals("update", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand("dotnet new update", path, line, findings, "installed template package set");
            return;
        }
        if (primaryVerb != "add")
        {
            if (primaryVerb is not ("--info" or "--version" or "--list-sdks" or "--list-runtimes" or "--help" or "-h" or "help"))
                AddUnverifiableOperand("dotnet " + tokens[1], path, line, findings,
                    "unsupported SDK command or project dependency set");
            return;
        }
        var packageIndex = Array.FindIndex(tokens, 2, token => token.Equals("package", StringComparison.OrdinalIgnoreCase));
        if (packageIndex >= 0)
            AddSingleOperand("nuget", "dotnet add package", tokens, packageIndex + 1, path, line, references, findings);
    }

    private static void ParseNuGetCli(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 2 || !ValidatePackageSourceOptions("nuget", tokens, path, line, findings))
            return;
        var verb = tokens[1].ToLowerInvariant();
        if (verb == "install")
        {
            AddSingleOperand("nuget", "nuget install", tokens, 2, path, line, references, findings);
            return;
        }
        AddUnverifiableOperand("nuget " + tokens[1], path, line, findings,
            "project, packages.config, or installed dependency set");
    }

    private static void ParsePowerShell(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (!ValidatePackageSourceOptions("powershellgallery", tokens, path, line, findings))
            return;
        var nameIndex = Array.FindIndex(tokens, 1, token => token.Equals("-Name", StringComparison.OrdinalIgnoreCase));
        AddPowerShellNames("powershellgallery", tokens[0], tokens, nameIndex >= 0 ? nameIndex + 1 : 1, path, line, references, findings);
    }

    private static void ParsePowerShellNuGet(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens[0].Equals("save-package", StringComparison.OrdinalIgnoreCase) &&
            tokens.Any(static token =>
                token.Equals("-InputObject", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("-InputObject:", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("-IncludeDependencies", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("-IncludeDependencies:", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("-ForceBootstrap", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("-ForceBootstrap:", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnverifiableOperand("Save-Package", path, line, findings,
                "pipeline-provided, transitive, or provider-bootstrap dependency set");
            return;
        }
        if (!ValidatePackageSourceOptions("nuget", tokens, path, line, findings))
            return;
        var provider = FindOptionValue(tokens, 1, "-ProviderName");
        if (!string.Equals(provider, "NuGet", StringComparison.OrdinalIgnoreCase))
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                "PackageManagement commands must select '-ProviderName NuGet' explicitly; alternate or implicit providers cannot be verified against nuget.org.");
            return;
        }
        if (tokens[0].Equals("save-package", StringComparison.OrdinalIgnoreCase))
        {
            var source = FindOptionValue(tokens, 1, "-Source", "--source", "-s");
            if (source is null)
            {
                AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                    "Save-Package must select nuget.org explicitly; registered machine sources cannot be proven to be canonical.");
                return;
            }

            var outputPath = FindOptionValue(tokens, 1, "-Path", "-LiteralPath");
            if (string.IsNullOrWhiteSpace(outputPath) ||
                NormalizeToken(outputPath).Contains("://", StringComparison.Ordinal))
            {
                AddUnverifiableOperand("Save-Package", path, line, findings,
                    "literal local output path");
                return;
            }
        }
        var nameIndex = Array.FindIndex(tokens, 1, token =>
            token.Equals("-Name", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("-Id", StringComparison.OrdinalIgnoreCase));
        AddPowerShellNames("nuget", tokens[0], tokens, nameIndex >= 0 ? nameIndex + 1 : 1, path, line, references, findings);
    }

    private static void ParseNode(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 2)
            return;
        if (!ValidatePackageSourceOptions("npm", tokens, path, line, findings))
            return;
        var verbIndex = FindKnownVerbIndex(tokens, 1, NodeVerbs, tokens[0], path, line, findings);
        if (verbIndex < 0)
        {
            if (!IsNodeInformationalInvocation(tokens))
                AddUnverifiableOperand(tokens[0], path, line, findings,
                    tokens.Skip(1).FirstOrDefault() ?? "missing package-manager verb");
            return;
        }
        var verb = NormalizeNodeVerb(tokens[verbIndex]);
        if (verb == "run")
        {
            AddUnverifiableOperand(tokens[0] + " " + tokens[verbIndex], path, line, findings,
                "project package script");
            return;
        }
        if (verb is "config" or "set")
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"Package-manager configuration command for '{tokens[0]}' can change the registry used by later installation commands; argument values are redacted.");
            return;
        }
        if (verb == "ci")
        {
            AddUnverifiableOperand("npm ci", path, line, findings, "lockfile dependency set");
            return;
        }
        if (verb == "update")
        {
            var findingCount = findings.Count;
            AddMultipleOperands("npm", $"{tokens[0]} {tokens[verbIndex]}", tokens, verbIndex + 1,
                path, line, references, findings);
            if (findings.Count > findingCount)
                return;
            if (!IsNodeInstallIsolatedFromProject(tokens))
            {
                references.Clear();
                AddUnverifiableOperand(tokens[0] + " " + tokens[verbIndex], path, line, findings,
                    "project dependency graph and lifecycle scripts");
                return;
            }
            return;
        }
        if (verb == "audit")
        {
            if (tokens.Skip(verbIndex + 1).Any(static token =>
                    token.Equals("fix", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("--fix", StringComparison.OrdinalIgnoreCase)))
            {
                AddUnverifiableOperand(tokens[0] + " audit fix", path, line, findings, "project dependency set");
            }
            return;
        }
        if (verb is "dedupe" or "rebuild")
        {
            AddUnverifiableOperand(tokens[0] + " " + verb, path, line, findings, "installed project dependency set");
            return;
        }
        if (verb == "link")
        {
            AddUnverifiableOperand(tokens[0] + " " + tokens[verbIndex], path, line, findings,
                "local or globally linked dependency set");
            return;
        }
        if (tokens[0].Equals("yarn", StringComparison.OrdinalIgnoreCase) && verb is "exec" or "x")
        {
            AddUnverifiableOperand("yarn " + tokens[verbIndex], path, line, findings,
                "shell command or unsupported executable payload");
            return;
        }
        if ((tokens[0].Equals("npm", StringComparison.OrdinalIgnoreCase) ||
             tokens[0].Equals("pnpm", StringComparison.OrdinalIgnoreCase) ||
             tokens[0].Equals("bun", StringComparison.OrdinalIgnoreCase)) &&
            verb is "exec" or "x")
        {
            if (RejectNestedPackageManagerPayload(tokens[0] + " " + tokens[verbIndex], tokens, verbIndex + 1, path, line, findings))
                return;
            var packageOptions = FindOptionValues(tokens, verbIndex + 1, "--package", "-p");
            if (packageOptions.Count > 0)
            {
                foreach (var packageOption in packageOptions)
                {
                    if (string.IsNullOrWhiteSpace(packageOption))
                        AddUnverifiableOperand("npm exec", path, line, findings, "--package");
                    else
                        AddToken("npm", "npm exec", packageOption, null, path, line, references, findings);
                }
            }
            else
                AddRunnerOperand("npm", "npm exec", tokens, verbIndex + 1, path, line, references, findings);
            return;
        }
        if ((tokens[0].Equals("pnpm", StringComparison.OrdinalIgnoreCase) ||
             tokens[0].Equals("yarn", StringComparison.OrdinalIgnoreCase)) && verb == "dlx")
        {
            AddRunnerOperand("npm", tokens[0] + " dlx", tokens, verbIndex + 1, path, line, references, findings);
            return;
        }
        if (verb == "init")
        {
            AddNodeInitializer(tokens, verbIndex + 1, path, line, references, findings);
            return;
        }
        if (verb is not ("install" or "add"))
            return;
        var installFindingCount = findings.Count;
        AddMultipleOperands("npm", $"{tokens[0]} {tokens[verbIndex]}", tokens, verbIndex + 1, path, line, references, findings);
        if (findings.Count > installFindingCount)
            return;
        if (!IsNodeInstallIsolatedFromProject(tokens))
        {
            references.Clear();
            AddUnverifiableOperand(tokens[0] + " " + tokens[verbIndex], path, line, findings,
                "project dependency graph and lifecycle scripts");
            return;
        }
    }

    private static bool IsNodeInstallIsolatedFromProject(string[] tokens)
    {
        var executable = tokens[0].ToLowerInvariant();
        if (tokens.Any(static token => token.Equals("--workspace", StringComparison.OrdinalIgnoreCase) ||
                                       token.StartsWith("--workspace=", StringComparison.OrdinalIgnoreCase) ||
                                       token.Equals("--workspaces", StringComparison.OrdinalIgnoreCase) ||
                                       token.Equals("--include-workspace-root", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (executable is "npm" or "pnpm" or "bun" &&
            tokens.Any(static token => token.Equals("--global", StringComparison.OrdinalIgnoreCase) ||
                                       token.Equals("-g", StringComparison.OrdinalIgnoreCase)))
            return true;

        return executable == "npm" &&
               tokens.Any(static token => token.Equals("--ignore-scripts", StringComparison.OrdinalIgnoreCase)) &&
               tokens.Any(static token => token.Equals("--package-lock-only", StringComparison.OrdinalIgnoreCase));
    }

    private static void ParseCargo(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (tokens.Length < 2 || !ValidatePackageSourceOptions("crates", tokens, path, line, findings))
            return;

        var verbIndex = FindCargoVerbIndex(tokens, path, line, findings);
        if (verbIndex < 0)
            return;
        var verb = tokens[verbIndex].ToLowerInvariant();
        if (verb is "add" or "install")
        {
            if (tokens.Any(static token =>
                    token.Equals("--offline", StringComparison.OrdinalIgnoreCase) ||
                    token.StartsWith("--offline=", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("--frozen", StringComparison.OrdinalIgnoreCase) ||
                    token.StartsWith("--frozen=", StringComparison.OrdinalIgnoreCase)))
            {
                AddUnverifiableOperand("cargo " + verb, path, line, findings,
                    "offline or frozen local-cache dependency mode");
                return;
            }
            AddMultipleOperands("crates", "cargo " + verb, tokens, verbIndex + 1, path, line, references, findings);
            return;
        }
        if (verb is "help" or "new" or "init" or "clean" or "search")
            return;

        AddUnverifiableOperand("cargo " + verb, path, line, findings,
            "project, lockfile, or external subcommand dependency set");
    }

    private static int FindCargoVerbIndex(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        for (var index = 1; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token is "--version" or "-V" or "--help" or "-h")
                continue;
            if (token is "--verbose" or "-v" or "--quiet" or "-q" or "--locked" or "--offline" or "--frozen")
                continue;
            if (token.StartsWith("--color=", StringComparison.OrdinalIgnoreCase))
                continue;
            if (token is "--color" or "--target-dir")
            {
                if (++index < tokens.Length)
                    continue;
                AddUnverifiableOperand("cargo", path, line, findings, token);
                return -1;
            }
            if (token.StartsWith("-", StringComparison.Ordinal))
            {
                AddUnverifiableOperand("cargo", path, line, findings, token);
                return -1;
            }
            return index;
        }
        return -1;
    }

    private static bool IsNodeInformationalInvocation(string[] tokens)
    {
        if (tokens.Length < 2)
            return false;

        var informational = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--version", "-v", "--help", "-h", "help", "view", "info", "show", "search",
            "list", "ls", "why", "outdated", "doctor", "ping", "whoami", "fund", "explain",
            "root", "prefix", "bin", "query", "pm"
        };
        return informational.Contains(tokens[1]) &&
               tokens.Skip(1).All(static token =>
                   token.IndexOfAny(['$', '%', '{', '}', '(', ')', '`']) < 0);
    }
}
