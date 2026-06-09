using PowerForge;
using PowerForge.Cli;
using System;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private static int CommandTemplate(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);

        var scriptPath = TryGetOptionValue(argv, "--script") ?? TryGetOptionValue(argv, "--build-script");
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "template",
                    Success = false,
                    ExitCode = 2,
                    Error = "Missing --script <Build-Module.ps1>."
                });
            }
            Console.WriteLine("Usage: powerforge template --script <Build-Module.ps1> [--out <path>] [--project-root <path>] [--powershell <path>] [--output json]");
            return 2;
        }

        try
        {
            var fullScriptPath = ResolveExistingFilePath(scriptPath);
            var projectRoot = TryGetProjectRoot(argv);
            if (!string.IsNullOrWhiteSpace(projectRoot))
                projectRoot = Path.GetFullPath(projectRoot.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(projectRoot))
                projectRoot = Path.GetDirectoryName(fullScriptPath) ?? Directory.GetCurrentDirectory();

            var outPath = TryGetOptionValue(argv, "--out") ?? TryGetOptionValue(argv, "--out-path") ?? TryGetOptionValue(argv, "--output-path");
            if (string.IsNullOrWhiteSpace(outPath))
                outPath = Path.Combine(projectRoot, "powerforge.json");
            else
                outPath = Path.GetFullPath(outPath.Trim().Trim('"'));

            var shell = TryGetOptionValue(argv, "--powershell");
            if (string.IsNullOrWhiteSpace(shell))
                shell = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";

            var psScript = BuildTemplateScript(fullScriptPath, outPath, projectRoot);
            var psArgs = BuildPowerShellArgs(psScript);

            var result = RunProcess(shell, psArgs, projectRoot);
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "template",
                    Success = result.ExitCode == 0,
                    ExitCode = result.ExitCode,
                    Error = result.ExitCode == 0 ? null : (string.IsNullOrWhiteSpace(result.Error) ? "Template generation failed." : result.Error),
                    Config = "pipeline",
                    ConfigPath = outPath
                });
                return result.ExitCode;
            }

            if (result.ExitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(result.Error))
                    logger.Error(result.Error);
                return result.ExitCode;
            }

            logger.Success($"Generated {outPath}");
            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "template",
                    Success = false,
                    ExitCode = 1,
                    Error = ex.Message
                });
                return 1;
            }

            logger.Error(ex.Message);
            return 1;
        }
    }
}

