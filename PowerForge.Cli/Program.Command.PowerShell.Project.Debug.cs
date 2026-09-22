using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const string PowerShellProjectDebugUsage =
        "Usage: powerforge powershell project debug-plan <project> [--target <name>] [--output json]";

    private static int CommandPowerShellProjectDebugPlan(string[] args, bool outputJson, ILogger logger)
    {
        if (!TryValidatePowerShellArguments(args, new[] { "--project", "--target", "--output" },
                new[] { "--json", "--output-json" }, out var positionalProject, out var argumentError))
            return WritePowerShellError(outputJson, 2, argumentError, logger, "powershell.project.debug-plan");
        var projectPath = TryGetOptionValue(args, "--project") ?? positionalProject;
        if (string.IsNullOrWhiteSpace(projectPath))
            return WritePowerShellError(outputJson, 2, "A PowerShell compilation project path is required.", logger, "powershell.project.debug-plan");
        try
        {
            var targets = GetOptionValues(args, "--target").ToArray();
            if (targets.Length > 1)
                return WritePowerShellError(outputJson, 2, "Debug plan accepts one --target value.", logger, "powershell.project.debug-plan");
            var plan = new PowerShellCompilationProjectWorkflowService().CreateDebugPlan(projectPath, targets.SingleOrDefault());
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "powershell.project.debug-plan",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(plan, options)
                });
            }
            else
            {
                Console.WriteLine(JsonSerializer.Serialize(plan, options));
            }
            return 0;
        }
        catch (Exception exception)
        {
            return WritePowerShellError(outputJson, 1, exception.Message, logger, "powershell.project.debug-plan");
        }
    }
}
