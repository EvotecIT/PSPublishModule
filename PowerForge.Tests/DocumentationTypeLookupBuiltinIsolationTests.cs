using System.Text;

namespace PowerForge.Tests;

[Collection("DocumentationPowerShellHost")]
public sealed class DocumentationTypeLookupBuiltinIsolationTests
{
    public static IEnumerable<object[]> PowerShellHosts()
    {
        var hosts = OperatingSystem.IsWindows() ? new[] { "pwsh.exe", "powershell.exe" } : new[] { "pwsh" };
        foreach (var host in hosts)
            yield return new object[] { host };
    }

    [Theory]
    [MemberData(nameof(PowerShellHosts))]
    public void GeneratedTypeLookup_IsolatesPipelineCmdletsFromTargetAliases(string host)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-type-lookup-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string moduleName = "TypeLookupIsolationFixture";
            var manifestPath = Path.Combine(root, moduleName + ".psd1");
            File.WriteAllText(Path.Combine(root, moduleName + ".psm1"), """
function New-TypeLookupAssembly([System.Reflection.AssemblyName]$assemblyName) {
    $factory = [System.Reflection.Emit.AssemblyBuilder].GetMethods(
        [System.Reflection.BindingFlags]'Public,Static') |
        Microsoft.PowerShell.Core\Where-Object { $_.Name -eq 'DefineDynamicAssembly' -and $_.GetParameters().Count -eq 2 } |
        Microsoft.PowerShell.Utility\Select-Object -First 1
    if ($factory) {
        return [System.Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly(
            $assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
    }
    return [System.AppDomain]::CurrentDomain.DefineDynamicAssembly(
        $assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
}

$assemblyName = [System.Reflection.AssemblyName]::new('TypeLookupIsolationFixture.Dynamic')
$assemblyBuilder = New-TypeLookupAssembly $assemblyName
$moduleBuilder = $assemblyBuilder.DefineDynamicModule('TypeLookupIsolationFixture.Dynamic')
$typeBuilder = $moduleBuilder.DefineType(
    'TypeLookupIsolationFixture.A-B', [System.Reflection.TypeAttributes]::Public)
$script:unsafeType = if ($typeBuilder.PSObject.Methods['CreateTypeInfo']) {
    $typeBuilder.CreateTypeInfo().AsType()
} else {
    $typeBuilder.CreateType()
}

function Get-TypeLookupIsolationFixture {
    [CmdletBinding()]
    param()
    dynamicparam {
        $attributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $default = [System.Management.Automation.PSDefaultValueAttribute]::new()
        $default.Value = $script:unsafeType
        $attributes.Add($default)
        $parameters = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()
        $parameters.Add('TypeValue', [System.Management.Automation.RuntimeDefinedParameter]::new(
            'TypeValue', [type], $attributes))
        $parameters
    }
}

""", new UTF8Encoding(false));
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'TypeLookupIsolationFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '84848484-8484-8484-8484-848484848484'
    FunctionsToExport = @('Get-TypeLookupIsolationFixture')
    CmdletsToExport = @()
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(false));

            var runner = new ExecutablePowerShellRunner(host, root);
            var payload = new DocumentationEngine(runner, new NullLogger())
                .ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(2));
            var expression = Assert.Single(Assert.Single(payload.Commands).Parameters).DefaultValue;
            Assert.Contains("Microsoft.PowerShell.Core\\Where-Object", expression, StringComparison.Ordinal);
            Assert.Contains("Microsoft.PowerShell.Utility\\Select-Object", expression, StringComparison.Ordinal);

            var evaluatorPath = Path.Combine(root, "EvaluateTypeLookup.ps1");
            var outputPath = Path.Combine(root, "result.txt");
            File.WriteAllText(evaluatorPath, """
param([string]$Expression, [string]$OutputPath)
function New-TypeLookupAssembly([System.Reflection.AssemblyName]$assemblyName) {
    $factory = [System.Reflection.Emit.AssemblyBuilder].GetMethods(
        [System.Reflection.BindingFlags]'Public,Static') |
        Microsoft.PowerShell.Core\Where-Object { $_.Name -eq 'DefineDynamicAssembly' -and $_.GetParameters().Count -eq 2 } |
        Microsoft.PowerShell.Utility\Select-Object -First 1
    if ($factory) {
        return [System.Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly(
            $assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
    }
    return [System.AppDomain]::CurrentDomain.DefineDynamicAssembly(
        $assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::Run)
}
$assemblyBuilder = New-TypeLookupAssembly ([System.Reflection.AssemblyName]::new('TypeLookupIsolationFixture.Dynamic'))
$moduleBuilder = $assemblyBuilder.DefineDynamicModule('TypeLookupIsolationFixture.Dynamic')
$typeBuilder = $moduleBuilder.DefineType(
    'TypeLookupIsolationFixture.A-B', [System.Reflection.TypeAttributes]::Public)
if ($typeBuilder.PSObject.Methods['CreateTypeInfo']) { [void]$typeBuilder.CreateTypeInfo() } else { [void]$typeBuilder.CreateType() }
function global:Invoke-ShadowedWhereObject { throw 'Target Where-Object alias must not be invoked.' }
function global:Invoke-ShadowedSelectObject { throw 'Target Select-Object alias must not be invoked.' }
Set-Alias -Name Where-Object -Value Invoke-ShadowedWhereObject -Scope Global
Set-Alias -Name Select-Object -Value Invoke-ShadowedSelectObject -Scope Global
$result = & ([scriptblock]::Create($Expression))
[System.IO.File]::WriteAllText($OutputPath, [string]$result.FullName, [System.Text.UTF8Encoding]::new($false))
""", new UTF8Encoding(false));
            var execution = runner.Run(new PowerShellRunRequest(
                evaluatorPath,
                new[] { expression, outputPath },
                TimeSpan.FromMinutes(1)));
            Assert.True(execution.ExitCode == 0, execution.StdErr);
            Assert.Equal("TypeLookupIsolationFixture.A-B", File.ReadAllText(outputPath));
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
