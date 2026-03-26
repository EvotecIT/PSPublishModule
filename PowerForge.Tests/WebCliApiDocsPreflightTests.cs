using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

public class WebCliApiDocsPreflightTests
{
    [Fact]
    public void HandleSubCommand_ApiDocs_FailsOnPreflightWarningsWhenFailOnWarningsIsSet()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-preflight-fail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var outPath = Path.Combine(root, "_site", "api");

            var args = new[]
            {
                "--type", "CSharp",
                "--xml", xmlPath,
                "--out", outPath,
                "--format", "json",
                "--fail-on-warnings",
                "--source-map", "PowerForge.Web=https://example.invalid/blob/main/{path}#L{line}"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_ApiDocs_AllowsSuppressedPreflightWarnings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-preflight-suppress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var outPath = Path.Combine(root, "_site", "api");

            var args = new[]
            {
                "--type", "CSharp",
                "--xml", xmlPath,
                "--out", outPath,
                "--format", "json",
                "--fail-on-warnings",
                "--suppress-warning", "PFWEB.APIDOCS.SOURCE",
                "--source-map", "PowerForge.Web=https://example.invalid/blob/main/{path}#L{line}"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(outPath, "index.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_ApiDocs_FailsWhenNavSurfaceConfiguredWithoutNav()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-preflight-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var outPath = Path.Combine(root, "_site", "api");

            var args = new[]
            {
                "--type", "CSharp",
                "--xml", xmlPath,
                "--out", outPath,
                "--format", "json",
                "--fail-on-warnings",
                "--nav-surface", "apidocs"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_ApiDocs_FailsWhenSourceRootLooksOneLevelAboveGitHubRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-preflight-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "TestRepo"));
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var outPath = Path.Combine(root, "_site", "api");

            var args = new[]
            {
                "--type", "CSharp",
                "--xml", xmlPath,
                "--out", outPath,
                "--format", "json",
                "--fail-on-warnings",
                "--source-root", root,
                "--source-url", "https://github.com/EvotecIT/TestRepo/blob/main/{path}#L{line}"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_ApiDocs_AllowsSourceRootParentWhenSourceUrlUsesPathNoRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-preflight-pathnoroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "TestRepo"));
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var outPath = Path.Combine(root, "_site", "api");

            var args = new[]
            {
                "--type", "CSharp",
                "--xml", xmlPath,
                "--out", outPath,
                "--format", "json",
                "--fail-on-warnings",
                "--source-root", root,
                "--source-url", "https://github.com/EvotecIT/TestRepo/blob/main/{pathNoRoot}#L{line}"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(Path.Combine(outPath, "index.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_ApiDocs_FailsWhenLegacyAliasModeIsInvalid()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-legacy-mode-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var outPath = Path.Combine(root, "_site", "api");

            var args = new[]
            {
                "--type", "CSharp",
                "--xml", xmlPath,
                "--out", outPath,
                "--format", "json",
                "--legacy-alias-mode", "legacy"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(2, exitCode);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_ApiDocs_FailsWhenPowerShellExampleValidationFindsInvalidScripts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-ps-validate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) }");
            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "BrokenExample.ps1"), "Invoke-SampleFunction -Name (");

            var outPath = Path.Combine(root, "_site", "api");
            var args = new[]
            {
                "--type", "PowerShell",
                "--help-path", helpPath,
                "--out", outPath,
                "--format", "json",
                "--validate-ps-examples",
                "--fail-on-ps-example-validation"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);

            Assert.Equal(2, exitCode);
            Assert.True(File.Exists(Path.Combine(outPath, "powershell-example-validation.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_ApiDocs_FailOnPowerShellExampleValidationImpliesValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-ps-validate-implicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) }");
            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "BrokenExample.ps1"), "Invoke-SampleFunction -Name (");

            var outPath = Path.Combine(root, "_site", "api");
            var args = new[]
            {
                "--type", "PowerShell",
                "--help-path", helpPath,
                "--out", outPath,
                "--format", "json",
                "--fail-on-ps-example-validation"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(2, exitCode);
            Assert.True(File.Exists(Path.Combine(outPath, "powershell-example-validation.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void HandleSubCommand_ApiDocs_FailOnPowerShellExampleExecutionImpliesExecution()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-cli-apidocs-ps-execute-implicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleFunction { param([string]$Name) "Executed $Name" }
                """);
            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleFunction.Fail.ps1"), "Invoke-SampleFunction -Name 'Beta'; throw 'boom'");

            var outPath = Path.Combine(root, "_site", "api");
            var args = new[]
            {
                "--type", "PowerShell",
                "--help-path", helpPath,
                "--out", outPath,
                "--format", "json",
                "--fail-on-ps-example-execution"
            };

            var exitCode = WebCliCommandHandlers.HandleSubCommand("apidocs", args, outputJson: true, logger: new WebConsoleLogger(), outputSchemaVersion: 1);
            Assert.Equal(2, exitCode);
            Assert.True(File.Exists(Path.Combine(outPath, "powershell-example-validation.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string BuildMinimalXml()
    {
        return
            """
            <doc>
              <assembly>
                <name>TestAssembly</name>
              </assembly>
              <members>
                <member name="T:TestNamespace.TestType">
                  <summary>Type summary.</summary>
                </member>
              </members>
            </doc>
            """;
    }

    private static string BuildMinimalPowerShellHelpForValidation()
    {
        return
            """
            <?xml version="1.0" encoding="utf-8"?>
            <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
              <command:command>
                <command:details>
                  <command:name>Invoke-SampleFunction</command:name>
                  <command:commandType>Function</command:commandType>
                  <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                </command:details>
                <command:syntax>
                  <command:syntaxItem>
                    <command:name>Invoke-SampleFunction</command:name>
                    <command:parameter required="true">
                      <maml:name>Name</maml:name>
                      <command:parameterValue required="true">string</command:parameterValue>
                    </command:parameter>
                  </command:syntaxItem>
                </command:syntax>
                <command:parameters>
                  <command:parameter required="true">
                    <maml:name>Name</maml:name>
                    <maml:description><maml:para>Name value.</maml:para></maml:description>
                    <command:parameterValue required="true">string</command:parameterValue>
                  </command:parameter>
                </command:parameters>
              </command:command>
            </helpItems>
            """;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
