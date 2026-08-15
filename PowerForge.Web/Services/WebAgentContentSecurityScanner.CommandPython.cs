namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private static void ParsePython(
        string[] tokens,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (!ValidatePackageSourceOptions("pypi", tokens, path, line, findings))
            return;
        var command = tokens[0].ToLowerInvariant();
        if (command is "python" or "python3" or "py")
        {
            if (ContainsPythonSetupScript(tokens))
            {
                AddUnverifiableOperand(tokens[0], path, line, findings, "project-controlled setup script");
                return;
            }

            var moduleIndex = FindPythonModuleIndex(tokens, path, line, findings);
            if (moduleIndex < 0 || moduleIndex + 1 >= tokens.Length)
                return;
            var module = tokens[moduleIndex + 1];
            if ((IsPythonPipModule(module) || IsPythonPipxModule(module)) &&
                !HasSafePythonModuleLookup(tokens, moduleIndex))
            {
                AddUnverifiableOperand($"{tokens[0]} -m {module}", path, line, findings,
                    "unsafe local Python module lookup");
                return;
            }
            if (IsPythonPipxModule(module))
            {
                ParsePipx(tokens, moduleIndex + 2, $"{tokens[0]} -m pipx", path, line, references, findings);
                return;
            }
            if (IsPythonPackagingModule(module))
            {
                AddUnverifiableOperand($"{tokens[0]} -m {module}", path, line, findings,
                    "project build, installer, or packaging dependency set");
                return;
            }
            if (!IsPythonPipModule(module))
                return;
            if (IsPipInformationalInvocation(tokens, moduleIndex + 2))
                return;
            var installIndex = FindVerbIndex(tokens, moduleIndex + 2, "install", $"{tokens[0]} -m pip", path, line, findings);
            if (installIndex >= 0)
                AddMultipleOperands("pypi", $"{tokens[0]} -m pip install", tokens, installIndex + 1, path, line, references, findings);
            return;
        }
        if (command is "pip" or "pip3")
        {
            if (IsPipInformationalInvocation(tokens, 1))
                return;
            var installIndex = FindVerbIndex(tokens, 1, "install", tokens[0], path, line, findings);
            if (installIndex >= 0)
                AddMultipleOperands("pypi", tokens[0] + " install", tokens, installIndex + 1, path, line, references, findings);
            return;
        }
        if (command == "uv")
        {
            var verbIndex = FindKnownVerbIndex(tokens, 1, new[] { "pip", "add", "tool", "run", "sync", "build" }, "uv", path, line, findings);
            var start = -1;
            if (verbIndex >= 0 && tokens[verbIndex].Equals("pip", StringComparison.OrdinalIgnoreCase))
            {
                var installIndex = FindVerbIndex(tokens, verbIndex + 1, "install", "uv pip", path, line, findings);
                if (installIndex >= 0)
                    start = installIndex + 1;
            }
            else if (verbIndex >= 0 && tokens[verbIndex].Equals("tool", StringComparison.OrdinalIgnoreCase))
            {
                var installIndex = FindVerbIndex(tokens, verbIndex + 1, "install", "uv tool", path, line, findings);
                if (installIndex >= 0)
                {
                    var auxiliaryInput = FindOptionValue(tokens, installIndex + 1,
                        "--with", "--with-requirements", "--with-editable");
                    if (auxiliaryInput is not null)
                    {
                        AddUnverifiableOperand("uv tool install", path, line, findings, "auxiliary dependency input");
                        return;
                    }
                    AddRunnerOperand("pypi", "uv tool install", tokens, installIndex + 1, path, line, references, findings);
                }
                return;
            }
            else if (verbIndex >= 0)
            {
                if (tokens[verbIndex].Equals("sync", StringComparison.OrdinalIgnoreCase) ||
                    tokens[verbIndex].Equals("build", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnverifiableOperand("uv " + tokens[verbIndex], path, line, findings,
                        "project build, lockfile, or dependency set");
                    return;
                }
                if (tokens[verbIndex].Equals("run", StringComparison.OrdinalIgnoreCase))
                {
                    if (RejectUvRunPackageManagerPayload(tokens, verbIndex + 1, path, line, findings))
                        return;
                    var requirementInput = FindOptionValue(tokens, verbIndex + 1, "--with-requirements", "--with-editable");
                    if (requirementInput is not null)
                    {
                        AddUnverifiableOperand("uv run", path, line, findings, "external or editable dependency input");
                        return;
                    }
                    var dependencies = FindOptionValues(tokens, verbIndex + 1, "--with");
                    if (dependencies.Count == 0)
                        AddUnverifiableOperand("uv run", path, line, findings, "project dependency set");
                    else
                        foreach (var dependency in dependencies)
                            AddToken("pypi", "uv run --with", dependency, null, path, line, references, findings);
                    return;
                }
                start = verbIndex + 1;
            }
            if (start >= 0)
                AddMultipleOperands("pypi", "uv", tokens, start, path, line, references, findings);
            return;
        }
        if (command == "pipx")
            ParsePipx(tokens, 1, "pipx", path, line, references, findings);
    }

    private static bool ContainsPythonSetupScript(IEnumerable<string> tokens)
        => tokens.Skip(1).Any(static token =>
            Path.GetFileName(token.Replace('\\', '/')).Equals("setup.py", StringComparison.OrdinalIgnoreCase));

    private static bool IsPythonPipModule(string module)
        => module.Equals("pip", StringComparison.OrdinalIgnoreCase) ||
           module.Equals("pip.__main__", StringComparison.OrdinalIgnoreCase);

    private static bool IsPythonPipxModule(string module)
        => module.Equals("pipx", StringComparison.OrdinalIgnoreCase) ||
           module.Equals("pipx.__main__", StringComparison.OrdinalIgnoreCase);

    private static bool HasSafePythonModuleLookup(string[] tokens, int moduleIndex)
        => tokens.Take(moduleIndex).Any(static token =>
            token.Equals("-P", StringComparison.Ordinal) ||
            token.Equals("-I", StringComparison.Ordinal));

    private static void ParsePipx(
        string[] tokens,
        int start,
        string command,
        string path,
        int line,
        ICollection<WebAgentPackageReference> references,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        if (FindOptionValue(tokens, start, "--pip-args", "--preinstall") is not null)
        {
            AddUnverifiableOperand(command, path, line, findings, "auxiliary dependency or pip argument input");
            return;
        }
        var verbIndex = FindKnownVerbIndex(tokens, start,
            new[] { "install", "run", "runpip", "inject", "upgrade", "upgrade-all", "reinstall", "reinstall-all" },
            command, path, line, findings);
        if (verbIndex < 0)
            return;
        if (tokens[verbIndex].Equals("upgrade-all", StringComparison.OrdinalIgnoreCase) ||
            tokens[verbIndex].Equals("reinstall-all", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand(command + " " + tokens[verbIndex], path, line, findings, "installed application package set");
            return;
        }
        if (tokens[verbIndex].Equals("runpip", StringComparison.OrdinalIgnoreCase))
        {
            AddUnverifiableOperand(command + " runpip", path, line, findings, "forwarded pip command");
            return;
        }
        if (!tokens[verbIndex].Equals("inject", StringComparison.OrdinalIgnoreCase))
        {
            AddRunnerOperand("pypi", command + " " + tokens[verbIndex], tokens, verbIndex + 1, path, line, references, findings);
            return;
        }

        var environmentIndex = FindNextOperand(tokens, verbIndex + 1);
        AddMultipleOperands("pypi", command + " inject", tokens, environmentIndex < 0 ? tokens.Length : environmentIndex + 1,
            path, line, references, findings);
    }

    private static bool IsPythonPackagingModule(string module)
        => module.ToLowerInvariant() is "build" or "installer" or "setuptools" or "wheel" or
            "hatch" or "hatchling" or "flit" or "flit_core" or "poetry" or "ensurepip" or "easy_install";

    private static bool RejectUvRunPackageManagerPayload(
        string[] tokens,
        int start,
        string path,
        int line,
        ICollection<WebAgentContentSecurityFinding> findings)
    {
        for (var index = start; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token == "--")
            {
                index++;
                return RejectPackageManagerInvocationAt("uv run", tokens, index, path, line, findings);
            }
            if (token.Equals("--with", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--with-requirements", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--with-editable", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--python", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--directory", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--project", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }
            if (token.StartsWith("--with=", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("--with-requirements=", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("--with-editable=", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("--python=", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("--directory=", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("--project=", StringComparison.OrdinalIgnoreCase) ||
                token is "--isolated" or "--no-project")
                continue;
            if (token.StartsWith("-", StringComparison.Ordinal))
                continue;
            return RejectPackageManagerInvocationAt("uv run", tokens, index, path, line, findings);
        }
        return false;
    }
}
