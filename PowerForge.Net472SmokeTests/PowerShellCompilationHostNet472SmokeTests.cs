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
            var input = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
            var source = Path.Combine(input, "Example.psm1");
            File.WriteAllText(source, """
function Get-MappedTotal { param([int[]] $Values) [int] $total = 0; $Values | ForEach-Object { $total += $_ }; return $total }
function Set-CompiledCallback { [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }; return 42 }
function Get-HostedYear { return [int](Get-Date -Format yyyy) }
""", new UTF8Encoding(true));
            var script = Path.Combine(root, "Observe.ps1");
            File.WriteAllText(script, """
param([string]$CompilerAssembly, [string]$Source, [string]$Output)
$ErrorActionPreference = 'Stop'
function Write-Checkpoint([string] $Phase) {
    $line = [DateTime]::UtcNow.ToString('O') + ' ' + $Phase
    [IO.File]::AppendAllText((Join-Path (Split-Path $Source) 'phases.log'), $line + [Environment]::NewLine)
    Write-Output $line
}
Write-Checkpoint 'Load compiler'
[void][Reflection.Assembly]::LoadFrom($CompilerAssembly)
[void][Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $CompilerAssembly) 'PowerForge.dll'))
$spec = [PowerForge.PowerShellCompilationBuildSpec]::new($Source, $Output, 'Example', [PowerForge.PowerShellCompilationArtifactKind]::BinaryModule, [PowerForge.PowerShellCompilationMode]::Hybrid, $true)
$spec.TargetFramework = 'net472'
$spec.ResourceMode = [PowerForge.PowerShellCompilationResourceMode]::None
$spec.KeepBuildWorkspace = $true
# Observe the same SDK owner before Build. The in-process hash cache avoids
# hashing twice while preserving the real legacy-host fingerprint operation.
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$fingerprint = [PowerForge.PowerShellCompilationArtifactBuilder].Assembly.GetType('PowerForge.PowerShellCompilationToolchainFingerprint', $true)
Write-Checkpoint 'Resolve selected dotnet SDK'
$sdk = $fingerprint.GetMethod('ResolveSelectedSdk', $flags).Invoke($null, @())
$sdkVersion = $sdk.GetType().GetProperty('Version', [Reflection.BindingFlags]'Instance,NonPublic').GetValue($sdk)
$sdkRoot = $sdk.GetType().GetProperty('RootPath', [Reflection.BindingFlags]'Instance,NonPublic').GetValue($sdk)
Write-Checkpoint ('Selected SDK: ' + $sdkVersion + ' at ' + $sdkRoot)
# Distinguish repeated discovery from content hashing on the legacy host.
Write-Checkpoint 'Repeat SDK discovery'
[void]$fingerprint.GetMethod('ResolveSelectedSdk', $flags).Invoke($null, @())
Write-Checkpoint 'Repeated SDK discovery complete; inventory selected SDK'
$fileCount = 0
$totalBytes = [long]0
$largest = $null
foreach ($path in [IO.Directory]::EnumerateFiles($sdkRoot, '*', [IO.SearchOption]::AllDirectories)) {
    $file = [IO.FileInfo]::new($path)
    $fileCount++
    $totalBytes += $file.Length
    if ($null -eq $largest -or $file.Length -gt $largest.Length) { $largest = $file }
}
Write-Checkpoint ('SDK inventory: ' + $fileCount + ' files, ' + $totalBytes + ' bytes')
if ($null -ne $largest) {
    $defaultSha = [Security.Cryptography.SHA256]::Create()
    try { Write-Checkpoint ('Default SHA-256 provider: ' + $defaultSha.GetType().FullName) }
    finally { $defaultSha.Dispose() }
    Write-Checkpoint ('Hash SDK sample: ' + $largest.Name + ', ' + $largest.Length + ' bytes')
    $sampleHash = $fingerprint.GetMethod('ComputeFileSha256', $flags).Invoke($null, @($largest.FullName))
    Write-Checkpoint 'SDK sample hash complete'
    Write-Checkpoint 'Hash same SDK sample with Windows CNG SHA-256'
    $nativeSha = [Security.Cryptography.SHA256Cng]::new()
    $sampleStream = [IO.File]::OpenRead($largest.FullName)
    try { $nativeHash = [BitConverter]::ToString($nativeSha.ComputeHash($sampleStream)).Replace('-', '').ToLowerInvariant() }
    finally { $sampleStream.Dispose(); $nativeSha.Dispose() }
    if ($sampleHash -ne $nativeHash) { throw 'SDK sample hash providers disagree.' }
    Write-Checkpoint 'Windows CNG SDK sample hash complete; bytes agree'
}
$dotnetRoot = [IO.Directory]::GetParent([IO.Directory]::GetParent($sdkRoot).FullName).FullName
$packsRoot = Join-Path $dotnetRoot 'packs'
if ([IO.Directory]::Exists($packsRoot)) {
    foreach ($pattern in @('Microsoft.NETCore.App.Ref', 'NETStandard.Library.Ref', 'Microsoft.NETCore.App.Host.*')) {
        foreach ($packRoot in [IO.Directory]::EnumerateDirectories($packsRoot, $pattern, [IO.SearchOption]::TopDirectoryOnly)) {
            $count = 0; $bytes = [long]0
            foreach ($path in [IO.Directory]::EnumerateFiles($packRoot, '*', [IO.SearchOption]::AllDirectories)) {
                $count++; $bytes += [IO.FileInfo]::new($path).Length
            }
            Write-Checkpoint ('Reference pack inventory: ' + [IO.Path]::GetFileName($packRoot) + ', ' + $count + ' files, ' + $bytes + ' bytes')
        }
    }
}
Write-Checkpoint 'Compute selected SDK and reference-pack fingerprint'
[void]$fingerprint.GetMethod('ComputeSdkSha256', $flags).Invoke($null, @($sdkVersion, $false))
Write-Checkpoint 'SDK fingerprint complete; build hybrid module'
$result = [PowerForge.PowerShellCompilationArtifactBuilder]::new().Build($spec)
Write-Checkpoint 'Hybrid build returned'
if (-not $result.Succeeded -and $null -ne $result.Failure) { Write-Checkpoint ('Failure stage: ' + $result.Failure.Stage) }
if (-not $result.Succeeded) { throw ($result.Error + [Environment]::NewLine + $result.BuildOutput) }
if ($result.Manifest.CompiledMethods -lt 2) { throw 'The mapping and delegate workflows were not compiled.' }
Import-Module $result.ArtifactPath -Force
Write-Checkpoint 'Module imported'
foreach ($command in @('Get-MappedTotal', 'Set-CompiledCallback')) {
    $entry = @($result.Manifest.UnitDispositionLedger.Entries | Where-Object Name -eq $command)
    if ($entry.Count -ne 1 -or -not $entry[0].EmittedClrMethod) { throw ($command + ' has no emitted CLR method: ' + ($entry | ConvertTo-Json -Depth 6 -Compress)) }
}
if ((Get-MappedTotal -Values 40,2) -ne 42) { throw 'Compiled mapping contract failed.' }
Write-Checkpoint 'Mapping passed'
try {
    if ((Set-CompiledCallback) -ne 42 -or -not [Net.ServicePointManager]::ServerCertificateValidationCallback.Invoke($null,$null,$null,[Net.Security.SslPolicyErrors]::None)) { throw 'Compiled delegate contract failed.' }
} finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $null }
Write-Checkpoint 'Delegate passed'
if ((Get-HostedYear) -lt 2026) { throw 'Hosted command contract failed.' }
function global:Get-Date { '2031' }
if ((Get-HostedYear) -ne 2031) { throw 'Host command shadowing contract failed.' }
Write-Checkpoint 'Hosted execution passed'
$policy = [PowerForge.PowerShellCompilationArtifactBuilder].Assembly.GetType('PowerForge.PowerShellGeneratedTypePolicy')
$isSupported = $policy.GetMethod('IsSupported', [Reflection.BindingFlags]'Static,NonPublic')
if ($isSupported.Invoke($null, @([PowerForge.PowerShellCompilationBuildSpec], 'net472'))) { throw 'A consumer assembly was admitted as a framework type.' }
[IO.File]::WriteAllText((Join-Path (Split-Path $Source) 'Helper.psm1'), 'function Get-HelperValue { return 9 }')
$usingSource = Join-Path (Split-Path $Source) 'using.ps1'
[IO.File]::WriteAllText($usingSource, 'using module ./Helper.psm1; Get-HelperValue')
$usingSpec = [PowerForge.PowerShellCompilationBuildSpec]::new($usingSource, ($Output + '-using'), 'UsingRefusal', [PowerForge.PowerShellCompilationArtifactKind]::Executable, [PowerForge.PowerShellCompilationMode]::Package, $true)
$usingSpec.ResourceMode = [PowerForge.PowerShellCompilationResourceMode]::None
$usingSpec.KeepBuildWorkspace = $true
Write-Checkpoint 'Build refusal case'
$usingResult = [PowerForge.PowerShellCompilationArtifactBuilder]::new().Build($usingSpec)
Write-Checkpoint 'Refusal case returned'
if ($usingResult.Succeeded -or $usingResult.Error -notlike '*using module/assembly directives*') { throw ('File-resolved using declarations lost their refusal: ' + $usingResult.Error) }
'LEGACY_COMPILER_AND_MODULE_PASSED'
""", new UTF8Encoding(true));
            var host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            // The net472 test host shadow-copies referenced assemblies into separate folders.
            // Use the original build output so the child host sees the real dependency closure.
            var compiler = new Uri(typeof(PowerShellCompilationArtifactBuilder).Assembly.CodeBase!).LocalPath;
            var childTemp = Directory.CreateDirectory(Path.Combine(root, "temp")).FullName;
            var result = await new ProcessRunner(ownProcessTree: true).RunAsync(new ProcessRunRequest(
                host, root, new[] { "-NoProfile", "-NonInteractive", "-File", script,
                    "-CompilerAssembly", compiler, "-Source", source, "-Output", Path.Combine(root, "out") },
                TimeSpan.FromMinutes(6), new Dictionary<string, string?>
                {
                    ["TEMP"] = childTemp, ["TMP"] = childTemp,
                    ["NUGET_SCRATCH"] = Environment.GetEnvironmentVariable("NUGET_SCRATCH") ??
                        NuGet.Common.NuGetEnvironment.GetFolderPath(NuGet.Common.NuGetFolderPath.Temp)
                }));
            var failureEvidence = string.Empty;
            if (!result.Succeeded)
            {
                var phasePath = Path.Combine(input, "phases.log");
                var phases = File.Exists(phasePath) ? File.ReadAllText(phasePath) : "No child checkpoint was written.";
                // Generated workspace milestones distinguish SDK fingerprinting from restore/build.
                // All files are synthetic and contained in this test's child TEMP directory.
                var inventory = Directory.EnumerateFiles(childTemp, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetFileName(path) is "global.json" or "project.assets.json" or "PowerForge.TargetContract.json" ||
                                   path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".powerforge-target.json", StringComparison.OrdinalIgnoreCase))
                    .Take(40).Select(path => path.Substring(childTemp.Length + 1) + " " + new FileInfo(path).Length + " bytes");
                failureEvidence = Environment.NewLine + phases + Environment.NewLine + string.Join(Environment.NewLine, inventory);
            }
            Assert.True(result.Succeeded, result.StdErr + Environment.NewLine + result.StdOut + failureEvidence);
            Assert.Contains("LEGACY_COMPILER_AND_MODULE_PASSED", result.StdOut);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
