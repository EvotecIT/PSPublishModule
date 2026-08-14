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
        if (tokens[1].Equals("restore", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand("dotnet restore", path, line, findings, "project dependency set");
            return;
        }
        if (tokens.Length < 3)
            return;
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
        if (!tokens[1].Equals("add", StringComparison.OrdinalIgnoreCase))
            return;
        var packageIndex = Array.FindIndex(tokens, 2, token => token.Equals("package", StringComparison.OrdinalIgnoreCase));
        if (packageIndex >= 0)
            AddSingleOperand("nuget", "dotnet add package", tokens, packageIndex + 1, path, line, references, findings);
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
        if (!ValidatePackageSourceOptions("nuget", tokens, path, line, findings))
            return;
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
            if (tokens[0].Equals("yarn", StringComparison.OrdinalIgnoreCase) &&
                !IsYarnInformationalInvocation(tokens))
            {
                AddUnverifiableOperand("yarn", path, line, findings, "lockfile dependency set");
            }
            return;
        }
        var verb = NormalizeNodeVerb(tokens[verbIndex]);
        if (verb is "config" or "set")
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.UNTRUSTED_SOURCE", path, line,
                $"Package-manager configuration command '{string.Join(' ', tokens)}' can change the registry used by later installation commands.");
            return;
        }
        if (verb == "ci")
        {
            AddUnverifiableOperand("npm ci", path, line, findings, "lockfile dependency set");
            return;
        }
        if (verb == "update")
        {
            AddMultipleOperands("npm", $"{tokens[0]} {tokens[verbIndex]}", tokens, verbIndex + 1,
                path, line, references, findings);
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
            AddMultipleOperands("npm", tokens[0] + " " + tokens[verbIndex], tokens, verbIndex + 1,
                path, line, references, findings);
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
        AddMultipleOperands("npm", $"{tokens[0]} {tokens[verbIndex]}", tokens, verbIndex + 1, path, line, references, findings);
    }

    private static bool IsYarnInformationalInvocation(string[] tokens)
        => tokens.Length > 1 && tokens.Skip(1).All(static token =>
            token.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("-h", StringComparison.OrdinalIgnoreCase));
}
