using System.Text;

namespace PowerForge.Tests;

[Collection("DocumentationPowerShellHost")]
public sealed class DocumentationCollectorBuiltinIsolationTests
{
    public static IEnumerable<object[]> HostExecutables()
    {
        var hosts = OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" };
        foreach (var host in hosts)
        foreach (var exportedName in new[] { "Get-Command", "Where-Object", "Sort-Object", "GetDocumentedModuleCommands" })
            yield return new object[] { host, exportedName };
    }

    [Theory]
    [MemberData(nameof(HostExecutables))]
    public void CollectorCommandDiscovery_IsolatesBuiltinCmdletsFromTargetModuleExports(string host, string exportedName)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-builtin-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string moduleName = "BuiltinIsolationFixture";
            var exportedNames = new[] { exportedName };
            File.WriteAllText(
                Path.Combine(root, moduleName + ".psm1"),
                string.Join(Environment.NewLine, exportedNames.Select(name =>
                    $"function {name} {{ }}")) +
                Environment.NewLine + "Export-ModuleMember -Function " + string.Join(", ", exportedNames),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(root, moduleName + ".psd1"),
                "@{" + Environment.NewLine +
                "RootModule = 'BuiltinIsolationFixture.psm1'" + Environment.NewLine +
                "ModuleVersion = '1.0.0'" + Environment.NewLine +
                "GUID = '50505050-5050-5050-5050-505050505050'" + Environment.NewLine +
                "FunctionsToExport = @(" + string.Join(", ", exportedNames.Select(name => $"'{name}'")) + ")" + Environment.NewLine +
                "CmdletsToExport = @()" + Environment.NewLine +
                "AliasesToExport = @()" + Environment.NewLine +
                "VariablesToExport = @()" + Environment.NewLine +
                "}",
                new UTF8Encoding(false));

            var scriptPath = Path.Combine(root, "ValidateBuiltinIsolation.ps1");
            var outputPath = Path.Combine(root, "commands.txt");
            File.WriteAllText(
                scriptPath,
                "param([string]$ManifestPath, [string]$OutputPath)" + Environment.NewLine +
                EmbeddedScripts.Load("Scripts/Documentation/Export-HelpJson.Protocol.ps1") +
                Environment.NewLine + "try {" + Environment.NewLine +
                "$getDocumentedModuleCommands = (Microsoft.PowerShell.Core\\Get-Command GetDocumentedModuleCommands -CommandType Function).ScriptBlock" + Environment.NewLine +
                "$module = Microsoft.PowerShell.Core\\Import-Module -Name $ManifestPath -Force -PassThru -Function '*' -Cmdlet '*' -Alias '__none__' -Variable '__none__'" + Environment.NewLine +
                "$names = @(& $getDocumentedModuleCommands $module | Microsoft.PowerShell.Core\\ForEach-Object { [string]$_.Name })" + Environment.NewLine +
                "[System.IO.File]::WriteAllLines($OutputPath, $names, [System.Text.UTF8Encoding]::new($false))" + Environment.NewLine +
                "} finally { if ($module) { Microsoft.PowerShell.Core\\Remove-Module $module -Force -ErrorAction SilentlyContinue } }",
                new UTF8Encoding(false));

            var run = new ExecutablePowerShellRunner(host, root).Run(
                new PowerShellRunRequest(
                    scriptPath,
                    new[] { Path.Combine(root, moduleName + ".psd1"), outputPath },
                    TimeSpan.FromSeconds(20)));
            Assert.Equal(0, run.ExitCode);
            Assert.Equal(exportedNames, File.ReadAllLines(outputPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    public static IEnumerable<object[]> PowerShellHosts()
    {
        var hosts = OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" };
        foreach (var host in hosts)
            yield return new object[] { host };
    }

    [Theory]
    [MemberData(nameof(PowerShellHosts))]
    public void RuntimeValueHelpers_IsolateBuiltinCmdletsFromTargetExports(string host)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-helper-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scriptPath = Path.Combine(root, "ValidateRuntimeHelperIsolation.ps1");
            var outputPath = Path.Combine(root, "tokens.txt");
            File.WriteAllText(
                scriptPath,
                "param([string]$OutputPath)" + Environment.NewLine +
                EmbeddedScripts.Load("Scripts/Documentation/Export-HelpJson.RuntimeValueHelpers.ps1") +
                Environment.NewLine + "function global:Out-Null { throw 'Target Out-Null must not be invoked.' }" + Environment.NewLine +
                "$tokens = [System.Collections.Generic.List[object]]::new()" + Environment.NewLine +
                "if (-not (AddRuntimeNumericDefaultValueToken ([double]1.5) $tokens)) { throw 'Numeric token was not handled.' }" + Environment.NewLine +
                "[System.IO.File]::WriteAllText($OutputPath, [string]$tokens[0].kind + ':' + [string]$tokens[0].text, [System.Text.UTF8Encoding]::new($false))",
                new UTF8Encoding(false));

            var run = new ExecutablePowerShellRunner(host, root).Run(
                new PowerShellRunRequest(
                    scriptPath,
                    new[] { outputPath },
                    TimeSpan.FromSeconds(20)));
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("Double:1.5", File.ReadAllText(outputPath));
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
                request.ScriptPath!, request.Arguments, request.Timeout, request.PreferPwsh,
                request.WorkingDirectory ?? _workingDirectory, request.EnvironmentVariables,
                _executable, request.CaptureOutput, request.CaptureError,
                request.OutputLineReceived, request.ErrorLineReceived));
    }
}
