using System.Text;
using System.Xml.Linq;

namespace PowerForge.Tests;

[Collection("DocumentationPowerShellHost")]
public sealed class DocumentationCommandIdentityCompatibilityTests
{
    [Fact]
    public void DocumentationEngine_PreservesXmlValidPercentCommandNamesAcrossBothPowerShellHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-command-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var scriptPath = Path.Combine(root, "ValidateCommandNames.ps1");
            var outputPath = Path.Combine(root, "validated.txt");
            File.WriteAllText(
                scriptPath,
                "param([string]$OutputPath)" + Environment.NewLine +
                EmbeddedScripts.Load("Scripts/Documentation/Export-HelpJson.TypeIdentity.ps1") +
                Environment.NewLine +
                EmbeddedScripts.Load("Scripts/Documentation/Export-HelpJson.OutputMatching.ps1") +
                Environment.NewLine + """
$invalidName = 'Get-A' + [char]1
$literalName = 'Get-A%u0001'
$valid = @(
    TestXmlSafeIdentityText $invalidName
    TestXmlSafeIdentityText $literalName)
[System.IO.File]::WriteAllLines($OutputPath, $valid, [System.Text.UTF8Encoding]::new($false))
""",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var run = new ExecutablePowerShellRunner(host, root).Run(
                    new PowerShellRunRequest(scriptPath, new[] { outputPath }, TimeSpan.FromMinutes(1)));
                Assert.Equal(0, run.ExitCode);
                Assert.Equal(["False", "True"], File.ReadAllLines(outputPath));
            }

            var payload = new DocumentationExtractionPayload
            {
                ModuleName = "CommandIdentityFixture",
                Commands =
                [
                    new DocumentationCommandHelp { Name = "Get-A%u0001", CommandType = "Function" },
                    new DocumentationCommandHelp { Name = "Get-A%25u0001", CommandType = "Function" }
                ]
            };
            var docsPath = Path.Combine(root, "Docs");
            new MarkdownHelpWriter().WriteCommandHelpFiles(payload, "CommandIdentityFixture", docsPath);
            Assert.True(File.Exists(Path.Combine(docsPath, "Get-A%u0001.md")));
            Assert.True(File.Exists(Path.Combine(docsPath, "Get-A%25u0001.md")));

            var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(
                payload,
                "CommandIdentityFixture",
                root);
            var names = XDocument.Load(mamlPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "details")
                .SelectMany(element => element.Descendants())
                .Where(element => element.Name.LocalName == "name")
                .Select(element => element.Value)
                .ToArray();
            Assert.Contains("Get-A%u0001", names);
            Assert.Contains("Get-A%25u0001", names);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DocumentationEngine_PreservesBoundaryWhitespaceInExtendedOutputTypesAcrossBothPowerShellHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-output-whitespace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scriptPath = Path.Combine(root, "ValidateOutputNames.ps1");
            var outputPath = Path.Combine(root, "validated.txt");
            File.WriteAllText(
                scriptPath,
                "param([string]$OutputPath)" + Environment.NewLine +
                EmbeddedScripts.Load("Scripts/Documentation/Export-HelpJson.TypeIdentity.ps1") +
                Environment.NewLine +
                EmbeddedScripts.Load("Scripts/Documentation/Export-HelpJson.OutputMatching.ps1") +
                Environment.NewLine + """
$values = foreach ($name in @(' A ', 'A')) {
    $metadata = GetOutputTypeMetadata ([pscustomobject]@{
        Name = $name
        TypeName = [pscustomobject]@{ FullName = $name }
    })
    $metadata.name + '|' + $metadata.clrTypeName + '|' + $metadata.identity
}
[System.IO.File]::WriteAllLines($OutputPath, $values, [System.Text.UTF8Encoding]::new($false))
""",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var run = new ExecutablePowerShellRunner(host, root).Run(
                    new PowerShellRunRequest(scriptPath, new[] { outputPath }, TimeSpan.FromMinutes(1)));
                Assert.Equal(0, run.ExitCode);
                Assert.Equal([" A | A | A ", "A|A|A"], File.ReadAllLines(outputPath));
            }

            var payload = new DocumentationExtractionPayload
            {
                ModuleName = "OutputWhitespaceFixture",
                Commands =
                [
                    new DocumentationCommandHelp
                    {
                        Name = "Get-WhitespaceOutput",
                        CommandType = "Function",
                        Outputs =
                        [
                            new DocumentationTypeHelp { Name = " A ", ClrTypeName = " A " },
                            new DocumentationTypeHelp { Name = "A", ClrTypeName = "A" }
                        ]
                    }
                ]
            };
            var docsPath = Path.Combine(root, "Docs");
            new MarkdownHelpWriter().WriteCommandHelpFiles(payload, "OutputWhitespaceFixture", docsPath);
            var markdown = File.ReadAllText(Path.Combine(docsPath, "Get-WhitespaceOutput.md"));
            Assert.Contains("`' A '`", markdown, StringComparison.Ordinal);
            Assert.Contains("`A`", markdown, StringComparison.Ordinal);

            var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(payload, "OutputWhitespaceFixture", root);
            var outputNames = XDocument.Load(mamlPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "returnValue")
                .SelectMany(element => element.Descendants())
                .Where(element => element.Name.LocalName == "name")
                .Select(element => element.Value)
                .ToArray();
            Assert.Contains(" A ", outputNames);
            Assert.Contains("A", outputNames);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DocumentationEngine_EncodesParameterSetIdentitiesInjectivelyAcrossBothPowerShellHosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-parameter-set-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scriptPath = Path.Combine(root, "ValidateParameterSets.ps1");
            var outputPath = Path.Combine(root, "validated.txt");
            File.WriteAllText(
                scriptPath,
                "param([string]$OutputPath)" + Environment.NewLine +
                EmbeddedScripts.Load("Scripts/Documentation/Export-HelpJson.OutputMatching.ps1") +
                Environment.NewLine +
                EmbeddedScripts.Load("Scripts/Documentation/Export-HelpJson.ParameterMetadata.ps1") +
                Environment.NewLine + """
$invalid = 'S' + [char]0xD800
$literal = 'S%uD800'
$syntax = @(
    [pscustomobject]@{ name = $invalid; isDefault = $true; text = 'Get-Test -Value <string>' },
    [pscustomobject]@{ name = $literal; isDefault = $false; text = 'Get-Test -Value <string>' })
$parameter = [pscustomobject]@{
    parameterSets = @($invalid, $literal)
    parameterSetRequired = [ordered]@{ $invalid = $true; $literal = $false }
}
$normalized = ConvertParameterSetIdentitiesToXmlSafeDocumentationText $invalid $syntax @($parameter)
$values = @($normalized.DefaultSet) + @($normalized.Syntax.name) +
    @($normalized.Parameters[0].parameterSets) +
    @($normalized.Parameters[0].parameterSetRequired.Keys)
[System.IO.File]::WriteAllLines($OutputPath, $values, [System.Text.UTF8Encoding]::new($false))
""",
                new UTF8Encoding(false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var run = new ExecutablePowerShellRunner(host, root).Run(
                    new PowerShellRunRequest(scriptPath, new[] { outputPath }, TimeSpan.FromMinutes(1)));
                Assert.Equal(0, run.ExitCode);
                Assert.Equal(
                    [
                        "S%uD800 [encoded 1]",
                        "S%uD800 [encoded 1]", "S%uD800",
                        "S%uD800 [encoded 1]", "S%uD800",
                        "S%uD800 [encoded 1]", "S%uD800"
                    ],
                    File.ReadAllLines(outputPath));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ExecutablePowerShellRunner : IPowerShellRunner
    {
        private readonly string _executable;
        private readonly string _workingDirectory;
        private readonly PowerShellRunner _inner = new();

        public ExecutablePowerShellRunner(string executable, string workingDirectory)
        {
            _executable = executable;
            _workingDirectory = workingDirectory;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _inner.Run(new PowerShellRunRequest(
                request.ScriptPath!,
                request.Arguments,
                request.Timeout,
                request.PreferPwsh,
                request.WorkingDirectory ?? _workingDirectory,
                request.EnvironmentVariables,
                _executable,
                request.CaptureOutput,
                request.CaptureError,
                request.OutputLineReceived,
                request.ErrorLineReceived));
    }
}
