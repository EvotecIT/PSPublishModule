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

    [Fact]
    public void BuildFailureMessage_PrefersLoaderExceptionAndValidationTarget()
    {
        var result = new PowerShellRunResult(
            exitCode: 1,
            stdOut:
                "PFIMPORT::FAILED\n" +
                "PFIMPORT::PSVERSION::5.1.26100.1\n" +
                "PFIMPORT::PSEDITION::Desktop\n" +
                "PFIMPORT::PSMODULEPATH::BEGIN\n" +
                @"C:\Users\Test\Documents\WindowsPowerShell\Modules;C:\Program Files\WindowsPowerShell\Modules" + "\n" +
                "PFIMPORT::PSMODULEPATH::END\n" +
                "PFIMPORT::ERRORTYPE::System.Reflection.ReflectionTypeLoadException\n" +
                "PFIMPORT::ERROR::Unable to load one or more of the requested types. Retrieve the LoaderExceptions property for more information.\n" +
                "PFIMPORT::LOADERERROR::Could not load file or assembly 'System.Data.SQLite, Version=1.0.119.0, Culture=neutral, PublicKeyToken=db937bc2d44ff139' or one of its dependencies.\n",
            stdErr:
                "Import-Module: some long stack trace line\n" +
                "At line:1 char:1\n",
            executable: "powershell.exe");

        var message = ModuleImportFailureFormatter.BuildFailureMessage(
            result,
            @"C:\Temp\TestModule\TestModule.psd1",
            validationTarget: "Windows PowerShell/Desktop");

        Assert.Contains("Import-Module failed during Windows PowerShell/Desktop validation (exit 1).", message);
        Assert.Contains("Cause: Could not load file or assembly 'System.Data.SQLite, Version=1.0.119.0, Culture=neutral, PublicKeyToken=db937bc2d44ff139' or one of its dependencies.", message);
        Assert.Contains("PowerShell: 5.1.26100.1 (Desktop)", message);
        Assert.Contains(@"PSModulePath: C:\Users\Test\Documents\WindowsPowerShell\Modules | C:\Program Files\WindowsPowerShell\Modules", message);
        Assert.DoesNotContain("Cause: Unable to load one or more of the requested types.", message);
    }
}
