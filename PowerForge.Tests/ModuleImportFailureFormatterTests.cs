using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleImportFailureFormatterTests
{
    [Fact]
    public void BuildFailureMessage_PrefersStructuredImportCause()
    {
        var result = new PowerShellRunResult(
            exitCode: 1,
            stdOut:
                "PFIMPORT::FAILED\n" +
                "PFIMPORT::PSVERSION::7.5.4\n" +
                "PFIMPORT::PSEDITION::Core\n" +
                "PFIMPORT::ERROR::The manifest contains one or more members that are not valid ('Prerelease').\n",
            stdErr:
                "Import-Module: some long stack trace line\n" +
                "At line:1 char:1\n",
            executable: "pwsh.exe");

        var message = ModuleImportFailureFormatter.BuildFailureMessage(result, @"C:\Temp\TestModule\TestModule.psd1");

        Assert.Contains("Import-Module failed (exit 1).", message);
        Assert.Contains("Cause: The manifest contains one or more members that are not valid ('Prerelease').", message);
        Assert.Contains("PowerShell: 7.5.4 (Core)", message);
        Assert.Contains(@"Manifest: C:\Temp\TestModule\TestModule.psd1", message);
        Assert.DoesNotContain("PFIMPORT::ERROR::", message);
        Assert.DoesNotContain("StdOut:", message);
    }
}
