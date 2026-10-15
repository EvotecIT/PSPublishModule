using System.Text;
using PowerForge;

namespace PowerForge.Net472SmokeTests;

public sealed class PowerShellCompilationHostNet472SmokeTests
{
    [Fact]
    public async Task WindowsPowerShellBuildsAndExecutesHybridModuleThroughLegacyCompilerLibrary()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) return;
        var root = Path.Combine(Path.GetTempPath(), "PF5_" + Guid.NewGuid().ToString("N").Substring(0, 12));
        Directory.CreateDirectory(root);
        try
        {
            // Resolve the fixture beside the original test assembly, outside xUnit shadow copies.
            var testAssembly = new Uri(typeof(PowerShellCompilationHostNet472SmokeTests).Assembly.CodeBase!).LocalPath;
            var sdkPolicy = Path.Combine(Path.GetDirectoryName(testAssembly)!, "global.json");
            Assert.True(File.Exists(sdkPolicy), "The repository SDK policy fixture is missing: " + sdkPolicy);
            File.Copy(sdkPolicy, Path.Combine(root, "global.json"));
            var phases = Path.Combine(root, "phases.log");
            var source = Path.Combine(root, "Example.psm1");
            File.WriteAllText(source, """
function Get-MappedTotal { param([int[]] $Values) [int] $total = 0; $Values | ForEach-Object { $total += $_ }; return $total }
function Set-CompiledCallback { [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }; return 42 }
function Get-HostedYear { return [int](Get-Date -Format yyyy) }
""", new UTF8Encoding(true));
            var script = Path.Combine(root, "Observe.ps1");
            File.WriteAllText(script, """
param([string]$CompilerAssembly, [string]$Source, [string]$Output, [string]$Phases)
$ErrorActionPreference = 'Stop'
function Write-Phase([string]$Name) {
    [IO.File]::AppendAllText($Phases, [DateTime]::UtcNow.ToString('o') + "`t" + $Name + [Environment]::NewLine)
}
Write-Phase ('host-start PID=' + $PID + '; PS=' + $PSVersionTable.PSVersion + '; CLR=' + [Environment]::Version)
Write-Phase ('sdk-policy=' + [IO.File]::ReadAllText((Join-Path (Split-Path $Source) 'global.json')))
Write-Phase 'assembly-load-start'
[void][Reflection.Assembly]::LoadFrom($CompilerAssembly)
[void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $CompilerAssembly) 'PowerForge.dll'))
Write-Phase 'assembly-load-complete'
$spec = [PowerForge.PowerShellCompilationBuildSpec]::new($Source, $Output, 'Example', [PowerForge.PowerShellCompilationArtifactKind]::BinaryModule, [PowerForge.PowerShellCompilationMode]::Hybrid, $true)
$spec.TargetFramework = 'net472'
$spec.ResourceMode = [PowerForge.PowerShellCompilationResourceMode]::None
Write-Phase 'compiler-build-start'
$result = [PowerForge.PowerShellCompilationArtifactBuilder]::new().Build($spec)
Write-Phase ('compiler-build-complete Succeeded=' + $result.Succeeded)
if (-not $result.Succeeded) { throw ($result.Error + [Environment]::NewLine + $result.BuildOutput) }
if ($result.Manifest.CompiledMethods -lt 2) { throw 'The mapping and delegate workflows were not compiled.' }
Write-Phase 'module-import-start'
Import-Module $result.ArtifactPath -Force
Write-Phase 'module-import-complete'
foreach ($command in @('Get-MappedTotal', 'Set-CompiledCallback')) {
    $entry = @($result.Manifest.UnitDispositionLedger.Entries | Where-Object Name -eq $command)
    if ($entry.Count -ne 1 -or -not $entry[0].EmittedClrMethod) { throw ($command + ' has no emitted CLR method: ' + ($entry | ConvertTo-Json -Depth 6 -Compress)) }
}
Write-Phase 'execution-contracts-start'
if ((Get-MappedTotal -Values 40,2) -ne 42) { throw 'Compiled mapping contract failed.' }
try {
    if ((Set-CompiledCallback) -ne 42 -or -not [Net.ServicePointManager]::ServerCertificateValidationCallback.Invoke($null,$null,$null,[Net.Security.SslPolicyErrors]::None)) { throw 'Compiled delegate contract failed.' }
} finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $null }
if ((Get-HostedYear) -lt 2026) { throw 'Hosted command contract failed.' }
function global:Get-Date { '2031' }
if ((Get-HostedYear) -ne 2031) { throw 'Host command shadowing contract failed.' }
Write-Phase 'execution-contracts-complete'
$policy = [PowerForge.PowerShellCompilationArtifactBuilder].Assembly.GetType('PowerForge.PowerShellGeneratedTypePolicy')
$isSupported = $policy.GetMethod('IsSupported', [Reflection.BindingFlags]'Static,NonPublic')
if ($isSupported.Invoke($null, @([PowerForge.PowerShellCompilationBuildSpec], 'net472'))) { throw 'A consumer assembly was admitted as a framework type.' }
[IO.File]::WriteAllText((Join-Path (Split-Path $Source) 'Helper.psm1'), 'function Get-HelperValue { return 9 }')
$usingSource = Join-Path (Split-Path $Source) 'using.ps1'
[IO.File]::WriteAllText($usingSource, 'using module ./Helper.psm1; Get-HelperValue')
$usingSpec = [PowerForge.PowerShellCompilationBuildSpec]::new($usingSource, ($Output + '-using'), 'UsingRefusal', [PowerForge.PowerShellCompilationArtifactKind]::Executable, [PowerForge.PowerShellCompilationMode]::Package, $true)
$usingSpec.ResourceMode = [PowerForge.PowerShellCompilationResourceMode]::None
Write-Phase 'using-refusal-start'
$usingResult = [PowerForge.PowerShellCompilationArtifactBuilder]::new().Build($usingSpec)
if ($usingResult.Succeeded -or $usingResult.Error -notlike '*using module/assembly directives*') { throw ('File-resolved using declarations lost their refusal: ' + $usingResult.Error) }
Write-Phase 'all-contracts-complete'
'LEGACY_COMPILER_AND_MODULE_PASSED'
""", new UTF8Encoding(true));
            var host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            // The net472 test host shadow-copies referenced assemblies into separate folders.
            // Use the original build output so the child host sees the real dependency closure.
            var compiler = new Uri(typeof(PowerShellCompilationArtifactBuilder).Assembly.CodeBase!).LocalPath;
            var result = await new ProcessRunner(ownProcessTree: true).RunAsync(new ProcessRunRequest(
                host, root, new[] { "-NoProfile", "-NonInteractive", "-File", script,
                    "-CompilerAssembly", compiler, "-Source", source, "-Output", Path.Combine(root, "out"), "-Phases", phases },
                TimeSpan.FromMinutes(6)));
            var phaseOutput = File.Exists(phases) ? File.ReadAllText(phases) : "No child phase was recorded.";
            Assert.True(result.Succeeded,
                $"Legacy compiler smoke failed. TimedOut={result.TimedOut}; ExitCode={result.ExitCode}; Duration={result.Duration}" +
                Environment.NewLine + "SDK policy:" + Environment.NewLine + File.ReadAllText(sdkPolicy) +
                Environment.NewLine + "Phases:" + Environment.NewLine + phaseOutput +
                Environment.NewLine + "stderr:" + Environment.NewLine + result.StdErr +
                Environment.NewLine + "stdout:" + Environment.NewLine + result.StdOut);
            Assert.Contains("LEGACY_COMPILER_AND_MODULE_PASSED", result.StdOut);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
