using PowerForge;

namespace PowerForge.Tests;

public sealed class ApprovedModuleCallbackTests
{
    [Theory]
    [InlineData("param([scriptblock]$Filter) Get-ApprovedValue; & $Filter", true)]
    [InlineData("function Invoke-Consumer { param([System.Management.Automation.ScriptBlock]$Filter) Get-ApprovedValue; & $Filter }", true)]
    [InlineData("param([string]$Filter) Get-ApprovedValue; & $Filter", false)]
    [InlineData("param($Filter) Get-ApprovedValue; & $Filter", false)]
    [InlineData("param([scriptblock]$Filter) Get-ApprovedValue; . $Filter", false)]
    [InlineData("param([scriptblock]$Filter) $Filter = 'Get-UnknownHelper'; Get-ApprovedValue; & $Filter", false)]
    [InlineData("param([scriptblock]$Filter = { Get-UnknownHelper }) Get-ApprovedValue; & $Filter", false)]
    [InlineData("param([scriptblock]$Filter) Set-Variable Filter 'Get-UnknownHelper'; Get-ApprovedValue; & $Filter", false)]
    [InlineData("param([scriptblock]$Filter) $local:Filter = 'Get-UnknownHelper'; Get-ApprovedValue; & $Filter", false)]
    [InlineData("param([scriptblock]$Filter) Microsoft.PowerShell.Utility\\Set-Variable Filter 'Get-UnknownHelper'; Get-ApprovedValue; & $Filter", false)]
    [InlineData("param([scriptblock]$Filter) Get-ApprovedValue; $reference = [ref]$Filter; & $Filter", false)]
    public void Analyze_OnlyCallerScriptBlockCallbacksAllowApprovedDependencyPruning(string code, bool fullyInlined)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "PowerForge.CallbackDonor";
            var modulePath = Path.Combine(root.FullName, moduleName);
            Directory.CreateDirectory(modulePath);
            File.WriteAllText(Path.Combine(modulePath, moduleName + ".psm1"),
                "function Get-ApprovedValue { Get-PrivateValue }; function Get-PrivateValue { 'embedded' }; Export-ModuleMember -Function Get-ApprovedValue");
            File.WriteAllText(Path.Combine(modulePath, moduleName + ".psd1"),
                "@{ RootModule = 'PowerForge.CallbackDonor.psm1'; ModuleVersion = '1.0.0'; FunctionsToExport = @('Get-ApprovedValue') }");

            var result = new PowerShellMissingFunctionAnalysisService().Analyze(null, code,
                new MissingFunctionsOptions(approvedModules: new[] { moduleName }, includeFunctionsRecursively: true,
                    approvedModuleSources: new[] { new ApprovedModuleSource(moduleName, "1.0.0", modulePath) },
                    requireApprovedModuleSources: true));

            Assert.Contains(result.Functions, function => function.Contains("function Get-PrivateValue", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(fullyInlined, result.FullyInlinedApprovedModules.Contains(moduleName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
