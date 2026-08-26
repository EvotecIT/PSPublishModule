using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly ISet<string> CompilerResponseFileInputSwitches =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "additionalfile",
            "additionalfiles",
            "addmodule",
            "analyzerconfig",
            "appconfig",
            "embed",
            "evidence",
            "keyfile",
            "lib",
            "libpath",
            "linkres",
            "linkresource",
            "link",
            "r",
            "reference",
            "resource",
            "res",
            "recurse",
            "ruleset",
            "sourcelink",
            "template",
            "testcoveragemodulepaths",
            "use",
            "win32appconfig",
            "win32icon",
            "win32manifest",
            "win32res"
        };

    private static readonly ISet<string> CompilerResponseFileOutputSwitches =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "doc",
            "errorlog",
            "generatedfilesout",
            "o",
            "out",
            "pdb",
            "refout",
            "sig",
            "touchedfiles",
            "xml"
        };

    private static readonly ISet<string> CompilerResponseFileDirectoryInputSwitches =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lib",
            "libpath"
        };

    private static bool HasOnlyControlledCompilerResponseFileInputs(
        IReadOnlyCollection<string> lines,
        string responseFilePath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        Func<string, bool>? isControlledInput)
    {
        foreach (string line in lines)
        {
            if (ContainsExecutableResponseFileSwitch(line) ||
                ContainsUncontrolledEnvironmentReference(line) ||
                ContainsUncontrolledFileSystemPropertyFunction(line) ||
                ContainsUnresolvedBuildExpression(line))
            {
                return false;
            }

            foreach (string token in TokenizeCompilerResponseFileLine(line))
            {
                if (token.StartsWith("#", StringComparison.Ordinal))
                    break;
                if (token.StartsWith("@", StringComparison.Ordinal))
                    return false;

                if (TryGetCompilerResponseFileOutputOperands(token, out string[] outputOperands))
                {
                    if (outputOperands.Length == 0)
                        return false;
                    foreach (string operand in outputOperands)
                    {
                        if (!IsControlledCompilerResponseFileOutputOperand(
                                operand,
                                responseFilePath,
                                taskInputBaseDirectory,
                                declaringAllowedRoot,
                                taskInputAllowedRoot))
                        {
                            return false;
                        }
                    }
                    continue;
                }

                if (TryGetCompilerResponseFileInputOperands(
                        token,
                        out string[] operands,
                        out bool rejectSwitch,
                        out bool directoryInput))
                {
                    if (rejectSwitch || operands.Length == 0)
                        return false;
                    foreach (string operand in operands)
                    {
                        if (!IsControlledCompilerResponseFileOperand(
                                operand,
                                responseFilePath,
                                taskInputBaseDirectory,
                                declaringAllowedRoot,
                                taskInputAllowedRoot,
                                isControlledInput,
                                directoryInput))
                        {
                            return false;
                        }
                    }
                    continue;
                }

                if (token.StartsWith("-", StringComparison.Ordinal) ||
                    token.StartsWith("/", StringComparison.Ordinal))
                {
                    if (LooksLikeRootedCompilerResponseFileOperand(token))
                        return false;
                    continue;
                }

                if (!IsControlledCompilerResponseFileOperand(
                        token,
                        responseFilePath,
                        taskInputBaseDirectory,
                        declaringAllowedRoot,
                        taskInputAllowedRoot,
                        isControlledInput))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool TryGetCompilerResponseFileOutputOperands(
        string token,
        out string[] operands)
    {
        operands = Array.Empty<string>();
        string candidate = token.Trim();
        if (!candidate.StartsWith("-", StringComparison.Ordinal) &&
            !candidate.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        int prefixLength = candidate.StartsWith("--", StringComparison.Ordinal) ? 2 : 1;
        candidate = candidate.Substring(prefixLength);
        int separator = candidate.IndexOfAny(new[] { ':', '=' });
        string name = separator < 0 ? candidate : candidate.Substring(0, separator);
        if (!CompilerResponseFileOutputSwitches.Contains(name))
            return false;
        if (separator < 0 || separator == candidate.Length - 1)
            return true;

        string value = candidate.Substring(separator + 1).Trim().Trim('"', '\'');
        if (name.Equals("errorlog", StringComparison.OrdinalIgnoreCase))
        {
            int metadataSeparator = value.IndexOf(',');
            if (metadataSeparator >= 0)
                value = value.Substring(0, metadataSeparator);
        }
        operands = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim().Trim('"', '\''))
            .Where(path => path.Length > 0)
            .ToArray();
        return true;
    }

    private static bool LooksLikeRootedCompilerResponseFileOperand(string token)
    {
        if (!token.StartsWith("/", StringComparison.Ordinal) || !Path.IsPathRooted(token))
            return false;
        string candidate = token.Substring(1);
        return candidate.IndexOf('/') >= 0 ||
               candidate.IndexOf('\\') >= 0 ||
               (candidate.IndexOf(':') < 0 && Path.HasExtension(candidate));
    }

    private static bool TryGetCompilerResponseFileInputOperands(
        string token,
        out string[] operands,
        out bool rejectSwitch,
        out bool directoryInput)
    {
        operands = Array.Empty<string>();
        rejectSwitch = false;
        directoryInput = false;
        string candidate = token.Trim();
        if (!candidate.StartsWith("-", StringComparison.Ordinal) &&
            !candidate.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        int prefixLength = candidate.StartsWith("--", StringComparison.Ordinal) ? 2 : 1;
        candidate = candidate.Substring(prefixLength);
        int separator = candidate.IndexOfAny(new[] { ':', '=' });
        string name = separator < 0 ? candidate : candidate.Substring(0, separator);
        if (name.Equals("analyzer", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("analyzers", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("generator", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("generators", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("keycontainer", StringComparison.OrdinalIgnoreCase))
        {
            rejectSwitch = true;
            return true;
        }
        if (!CompilerResponseFileInputSwitches.Contains(name))
            return false;
        directoryInput = CompilerResponseFileDirectoryInputSwitches.Contains(name);
        if (separator < 0 || separator == candidate.Length - 1)
            return true;

        string value = candidate.Substring(separator + 1).Trim().Trim('"', '\'');
        if (name.Equals("reference", StringComparison.OrdinalIgnoreCase))
        {
            int aliasSeparator = value.IndexOf('=');
            if (aliasSeparator > 0 && aliasSeparator < value.Length - 1)
                value = value.Substring(aliasSeparator + 1);
        }
        if (name.Equals("resource", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("res", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("linkresource", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("linkres", StringComparison.OrdinalIgnoreCase))
        {
            int metadataSeparator = value.IndexOf(',');
            if (metadataSeparator >= 0)
                value = value.Substring(0, metadataSeparator);
        }

        operands = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim().Trim('"', '\''))
            .Where(path => path.Length > 0)
            .ToArray();
        return true;
    }

    private static bool IsControlledCompilerResponseFileOperand(
        string operand,
        string responseFilePath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot,
        Func<string, bool>? isControlledInput,
        bool directoryInput = false)
    {
        if (operand.IndexOf('*') >= 0 || operand.IndexOf('?') >= 0)
            return false;
        if (!TryResolveControlledTaskInputPath(
                operand,
                responseFilePath,
                taskInputBaseDirectory,
                declaringAllowedRoot,
                taskInputAllowedRoot,
                out string inputPath))
        {
            return false;
        }
        if (directoryInput)
        {
            string allowedRoot = IsSameOrBelowBuildInputPath(inputPath, declaringAllowedRoot)
                ? declaringAllowedRoot
                : taskInputAllowedRoot;
            return HasOnlyControlledDirectoryTaskInput(
                inputPath,
                allowedRoot,
                isControlledInput);
        }
        if (isControlledInput is not null)
            return isControlledInput(inputPath);
        return (!File.Exists(inputPath) && !Directory.Exists(inputPath)) ||
               !HasReparsePointBelowRoot(inputPath, taskInputAllowedRoot);
    }

    private static bool IsControlledCompilerResponseFileOutputOperand(
        string operand,
        string responseFilePath,
        string taskInputBaseDirectory,
        string declaringAllowedRoot,
        string taskInputAllowedRoot)
    {
        if (operand.IndexOf('*') >= 0 || operand.IndexOf('?') >= 0 ||
            !TryResolveControlledTaskInputPath(
                operand,
                responseFilePath,
                taskInputBaseDirectory,
                declaringAllowedRoot,
                taskInputAllowedRoot,
                out string outputPath))
        {
            return false;
        }
        return IsControlledTaskOutputPath(
            outputPath,
            declaringAllowedRoot,
            taskInputAllowedRoot);
    }

    private static IEnumerable<string> TokenizeCompilerResponseFileLine(string line)
    {
        var token = new StringBuilder();
        char quote = '\0';
        foreach (char character in line)
        {
            if ((character == '"' || character == '\'') && (quote == '\0' || quote == character))
            {
                quote = quote == '\0' ? character : '\0';
                continue;
            }
            if (char.IsWhiteSpace(character) && quote == '\0')
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }
                continue;
            }
            token.Append(character);
        }
        if (quote != '\0')
        {
            yield return "@invalid-unclosed-quote";
            yield break;
        }
        if (token.Length > 0)
            yield return token.ToString();
    }
}
