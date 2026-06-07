using System;

namespace PowerForge;

internal sealed partial class MarkdownHelpWriter
{
    private static bool StartsWithPowerShellStatementKeyword(string trimmed)
    {
        var token = GetLeadingToken(trimmed);
        switch (token.ToLowerInvariant())
        {
            case "begin":
            case "break":
            case "catch":
            case "class":
            case "continue":
            case "data":
            case "do":
            case "dynamicparam":
            case "else":
            case "elseif":
            case "end":
            case "enum":
            case "filter":
            case "finally":
            case "for":
            case "foreach":
            case "function":
            case "if":
            case "param":
            case "process":
            case "return":
            case "switch":
            case "throw":
            case "trap":
            case "try":
            case "using":
            case "while":
            case "workflow":
                return true;
            default:
                return false;
        }
    }

    private static bool StartsWithKnownNativeCommand(string trimmed)
    {
        var token = GetLeadingToken(trimmed);
        if (token.Length == 0 || token.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
            return false;

        switch (token.ToLowerInvariant())
        {
            case "az":
            case "cat":
            case "cd":
            case "choco":
            case "cmake":
            case "cmd":
            case "copy":
            case "cp":
            case "del":
            case "dir":
            case "docker":
            case "dotnet":
            case "echo":
            case "git":
            case "go":
            case "java":
            case "javac":
            case "kubectl":
            case "ls":
            case "make":
            case "mkdir":
            case "move":
            case "msbuild":
            case "mv":
            case "node":
            case "npm":
            case "npx":
            case "nuget":
            case "pnpm":
            case "powershell":
            case "pwsh":
            case "py":
            case "python":
            case "python3":
            case "robocopy":
            case "rm":
            case "rmdir":
            case "rustc":
            case "where":
            case "winget":
            case "xcopy":
            case "yarn":
                return true;
            default:
                return token.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                    || token.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                    || token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || token.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string GetLeadingToken(string trimmed)
    {
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (char.IsWhiteSpace(trimmed[i]))
                return trimmed.Substring(0, i);
        }

        return trimmed;
    }

    private static bool IsPowerShellContinuationOperator(string token)
    {
        switch (token.ToLowerInvariant())
        {
            case "-and":
            case "-as":
            case "-band":
            case "-bor":
            case "-bxor":
            case "-contains":
            case "-ccontains":
            case "-ceq":
            case "-cge":
            case "-cgt":
            case "-cle":
            case "-clike":
            case "-clt":
            case "-cmatch":
            case "-cne":
            case "-cnotcontains":
            case "-cnotin":
            case "-cnotlike":
            case "-cnotmatch":
            case "-eq":
            case "-f":
            case "-ge":
            case "-gt":
            case "-icontains":
            case "-ieq":
            case "-ige":
            case "-igt":
            case "-ile":
            case "-ilike":
            case "-ilt":
            case "-imatch":
            case "-in":
            case "-ine":
            case "-inotcontains":
            case "-inotin":
            case "-inotlike":
            case "-inotmatch":
            case "-is":
            case "-isnot":
            case "-join":
            case "-le":
            case "-like":
            case "-lt":
            case "-match":
            case "-ne":
            case "-notcontains":
            case "-notin":
            case "-notlike":
            case "-notmatch":
            case "-not":
            case "-or":
            case "-replace":
            case "-creplace":
            case "-ireplace":
            case "-shl":
            case "-shr":
            case "-split":
            case "-csplit":
            case "-isplit":
            case "-xor":
                return true;
            default:
                return false;
        }
    }
}
