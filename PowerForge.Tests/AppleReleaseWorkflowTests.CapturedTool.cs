using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Fact]
    public void CapturedPowerShellToolPreservesArgumentsStreamsAndExitCode()
    {
        if (!CommandExists("pwsh")) return;

        var root = FindRepoRoot();
        var sandbox = Path.Combine(root, ".test-temp", $"captured-powershell-tool-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(sandbox);
            var toolPath = Path.Combine(sandbox, "captured-tool.ps1");
            File.WriteAllText(
                toolPath,
                "param([string] $Value)\n[Console]::Out.Write(\"out:$Value\")\n[Console]::Error.Write(\"error:$Value\")\nexit 7\n");
            var arguments = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { "value with spaces & symbols" })));

            var result = RunWithEnvironment(
                "pwsh",
                sandbox,
                new Dictionary<string, string?>(),
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-File",
                Path.Combine(
                    root,
                    ".github",
                    "actions",
                    "apple-release",
                    "Invoke-CapturedPowerShellTool.ps1"),
                "-ToolPath",
                toolPath,
                "-ArgumentListBase64",
                arguments);

            Assert.Equal(7, result.ExitCode);
            Assert.Equal("out:value with spaces & symbols", result.StandardOutput);
            Assert.Equal("error:value with spaces & symbols", result.StandardError);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }
}
