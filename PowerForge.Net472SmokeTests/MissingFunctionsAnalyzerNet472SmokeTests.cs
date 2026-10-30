using System.Text;
using PowerForge;

namespace PowerForge.Net472SmokeTests;

public sealed class MissingFunctionsAnalyzerNet472SmokeTests
{
    [Fact]
    public void WindowsPowerShellResolvesSelectedRuntimeThroughLegacyAnalyzer()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PF5_" + Guid.NewGuid().ToString("N")));
        try
        {
            const string name = "Legacy.Runtime";
            const string guid = "83bbf0ad-4a28-43b7-bd5c-5d6dd0007f03";
            foreach (var module in new[] { "Legacy.First", name })
            {
                var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, module));
                var functions = new StringBuilder();
                for (var i = 0; i < 2500; i++)
                    functions.AppendLine($"function Get-{module.Replace(".", string.Empty)}Value{i} {{ {i} }}");
                if (module == name) functions.AppendLine("function Get-LegacyRuntimeValue { 'runtime' }");
                File.WriteAllText(Path.Combine(moduleRoot.FullName, module + ".psm1"), functions.ToString(), new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(moduleRoot.FullName, module + ".psd1"),
                    $"@{{ RootModule='{module}.psm1'; ModuleVersion='1.0.0'; GUID='{guid}'; FunctionsToExport=@('*') }}", new UTF8Encoding(true));
            }
            var script = Path.Combine(root.FullName, "Observe.ps1");
            File.WriteAllText(script, """
param([string]$AnalyzerAssembly, [string]$ModuleRoot)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'This proof requires Windows PowerShell.' }
[void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $AnalyzerAssembly) 'PowerForge.dll'))
[void][Reflection.Assembly]::LoadFrom($AnalyzerAssembly)
$selected = foreach ($name in @('Legacy.First', 'Legacy.Runtime')) {
    [PowerForge.ApprovedModuleSource]::new($name, '1.0.0', (Join-Path $ModuleRoot $name), '83bbf0ad-4a28-43b7-bd5c-5d6dd0007f03', $null)
}
$options = [PowerForge.MissingFunctionsOptions]::new([string[]]@(), [string[]]@(), [string[]]@(), $true, [PowerForge.ApprovedModuleSource[]]@(), $true, [PowerForge.ApprovedModuleSource[]]$selected)
$report = ([PowerForge.MissingFunctionsAnalyzer]::new()).Analyze($null, 'Get-LegacyRuntimeValue', $options)
if ($report.Summary.Count -ne 1 -or $report.Summary[0].Error -or $report.Summary[0].Source -ne 'Legacy.Runtime' -or -not $report.Summary[0].CommandType) {
    throw ('Selected runtime was not resolved: ' + ($report.Summary | ConvertTo-Json -Depth 4))
}
'LEGACY_SELECTED_SCOPE_OK'
""", new UTF8Encoding(true));
            var assembly = new Uri(typeof(MissingFunctionsAnalyzer).Assembly.CodeBase!).LocalPath;
            var result = new PowerShellRunner().Run(new PowerShellRunRequest(
                script, new[] { assembly, root.FullName }, TimeSpan.FromSeconds(30), preferPwsh: false));
            Assert.True(result.ExitCode == 0, result.StdOut + Environment.NewLine + result.StdErr);
            Assert.Contains("LEGACY_SELECTED_SCOPE_OK", result.StdOut);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
