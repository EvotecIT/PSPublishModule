using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Generic.External.Binary.Dependency;
using Generic.External.Binary.Module;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationSignedExternalBinaryModuleTests
{
    private const string ExternalModuleGuid = "f57aa65b-a38b-4d41-a45f-6ace6923d7b7";

    [Fact]
    public void AcceptanceRunnerEnforcesCombinedProcessAndOutputDeadline()
    {
        using var fixture = ExternalBinaryModuleFixture.Create();
        var stopwatch = Stopwatch.StartNew();

        var exception = Assert.Throws<TimeoutException>(fixture.ExecuteTimeoutProbe);

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), stopwatch.Elapsed.ToString());
    }

    [Fact]
    public void HybridExecutesExternalBinaryModuleDependencyMetadataStreamsErrorsCancellationAndCleanup()
    {
        using var fixture = ExternalBinaryModuleFixture.Create();
        var result = fixture.Build();

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.UsesPowerShellRuntimeFallback);
        Assert.True(result.Manifest.DependencyLockReviewed);
        Assert.Contains(result.Manifest.DependencyGraph!.Nodes, static node =>
            node.Identity.Source.EndsWith("Generic.External.Binary.Module.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Manifest.DependencyGraph.Nodes, static node =>
            node.Identity.Source.EndsWith("Generic.External.Binary.Dependency.dll", StringComparison.OrdinalIgnoreCase));

        using var observation = fixture.Execute(result.ArtifactPath!);
        AssertObservation(observation.RootElement, result.ArtifactPath!, fixture, expectSignatures: false, expectedThumbprint: null);
    }

    [WindowsCodeSigningFact]
    public void HybridPreservesAuthenticodeSignedExternalBinaryModuleAndDependencyThroughCleanTargetExecution()
    {
        var thumbprint = Environment.GetEnvironmentVariable(WindowsCodeSigningFactAttribute.ThumbprintEnvironmentVariable)!;
        using var fixture = ExternalBinaryModuleFixture.Create(thumbprint);
        var result = fixture.Build();

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.True(result.Manifest!.DependencyLockReviewed);
        using var observation = fixture.Execute(result.ArtifactPath!);
        AssertObservation(observation.RootElement, result.ArtifactPath!, fixture, expectSignatures: true, expectedThumbprint: thumbprint);
    }

    private static void AssertObservation(
        JsonElement observation,
        string artifactManifestPath,
        ExternalBinaryModuleFixture fixture,
        bool expectSignatures,
        string? expectedThumbprint)
    {
        var externalRoot = Path.Combine(Path.GetDirectoryName(artifactManifestPath)!, "External");
        Assert.Equal("2.3.4", observation.GetProperty("ModuleVersion").GetString());
        Assert.Equal(ExternalModuleGuid, observation.GetProperty("ModuleGuid").GetString(), ignoreCase: true);
        Assert.Equal("4.5.6.0", observation.GetProperty("ModuleAssemblyVersion").GetString());
        Assert.Equal("3.2.1.0", observation.GetProperty("DependencyAssemblyVersion").GetString());
        Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString(), observation.GetProperty("Architecture").GetString(), ignoreCase: true);
        Assert.Equal("Default", observation.GetProperty("ModuleLoadContext").GetString());
        Assert.Equal("Default", observation.GetProperty("DependencyLoadContext").GetString());
        Assert.True(observation.GetProperty("ModuleUsesDefaultLoadContext").GetBoolean());
        Assert.True(observation.GetProperty("DependencyUsesDefaultLoadContext").GetBoolean());
        AssertPathEqual(
            Path.Combine(externalRoot, "Generic.External.Binary.Module.dll"),
            observation.GetProperty("ModuleAssemblyLocation").GetString()!);
        AssertPathEqual(
            Path.Combine(externalRoot, "Generic.External.Binary.Dependency.dll"),
            observation.GetProperty("DependencyAssemblyLocation").GetString()!);
        AssertPathEqual(fixture.IsolatedModulePath, observation.GetProperty("ModulePathEnvironment").GetString()!);
        Assert.Equal(ComputeSha256(fixture.PowerShellExecutable), observation.GetProperty("HostSha256").GetString(), ignoreCase: true);
        Assert.Equal("dependency:41", observation.GetProperty("DependencyValue").GetString());
        Assert.Equal("dependency:41", observation.GetProperty("TypeAliasValue").GetString());
        Assert.True(observation.GetProperty("TypeDataLoaded").GetBoolean());
        Assert.True(observation.GetProperty("FormatDataLoaded").GetBoolean());
        Assert.Contains("external binary value", observation.GetProperty("HelpSynopsis").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("external-information", observation.GetProperty("Information").EnumerateArray().Select(static item => item.GetString()));
        Assert.Contains("external-warning", observation.GetProperty("Warnings").EnumerateArray().Select(static item => item.GetString()));
        Assert.Contains("external-verbose", observation.GetProperty("Verbose").EnumerateArray().Select(static item => item.GetString()));
        Assert.Contains("external-debug", observation.GetProperty("Debug").EnumerateArray().Select(static item => item.GetString()));
        Assert.StartsWith("Generic.External.NonTerminating", Assert.Single(observation.GetProperty("NonTerminatingErrorIds").EnumerateArray()).GetString(), StringComparison.Ordinal);
        Assert.Equal("dependency:42", Assert.Single(observation.GetProperty("NonTerminatingOutput").EnumerateArray()).GetString());
        Assert.StartsWith("Generic.External.Terminating", Assert.Single(observation.GetProperty("TerminatingErrorIds").EnumerateArray()).GetString(), StringComparison.Ordinal);
        Assert.True(observation.GetProperty("CancellationCompleted").GetBoolean());
        Assert.True(observation.GetProperty("LeaseWasExclusive").GetBoolean());
        Assert.True(observation.GetProperty("LeaseReleased").GetBoolean());
        Assert.True(
            observation.GetProperty("Signed").GetBoolean() == expectSignatures,
            observation.GetRawText());
        if (expectSignatures)
        {
            Assert.All(observation.GetProperty("SignatureStatuses").EnumerateArray(), static item => Assert.Equal("Valid", item.GetString()));
            Assert.All(observation.GetProperty("SignatureThumbprints").EnumerateArray(), item =>
                Assert.Equal(expectedThumbprint, item.GetString(), ignoreCase: true));
        }
    }

    private static void AssertPathEqual(string expected, string actual)
        => Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(actual), ignoreCase: OperatingSystem.IsWindows());

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class ExternalBinaryModuleFixture : IDisposable
    {
        private ExternalBinaryModuleFixture(
            string root,
            string scriptPath,
            string manifestPath,
            string outputPath,
            string powerShellExecutable,
            string isolatedModulePath)
        {
            Root = root;
            ScriptPath = scriptPath;
            ManifestPath = manifestPath;
            OutputPath = outputPath;
            PowerShellExecutable = powerShellExecutable;
            IsolatedModulePath = isolatedModulePath;
        }

        private string Root { get; }
        private string ScriptPath { get; }
        private string ManifestPath { get; }
        private string OutputPath { get; }
        internal string PowerShellExecutable { get; }
        internal string IsolatedModulePath { get; }

        public static ExternalBinaryModuleFixture Create(string? signingThumbprint = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeExternalBinaryModuleTests", Guid.NewGuid().ToString("N"));
            var externalRoot = Path.Combine(root, "External");
            var cultureRoot = Path.Combine(externalRoot, "en-US");
            var output = Path.Combine(root, "out");
            var isolatedModulePath = Path.Combine(root, "isolated-modules");
            var powerShellExecutable = ResolvePowerShellExecutable();
            Directory.CreateDirectory(cultureRoot);
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(isolatedModulePath);
            var script = Path.Combine(root, "input.psm1");
            var manifest = Path.Combine(root, "input.psd1");
            File.WriteAllText(script, """
function Invoke-ExternalBinaryBoundary {
    [CmdletBinding()]
    param(
        [int] $Value = 42,
        [ValidateSet('Normal','Error','Terminating','Wait')]
        [string] $Behavior = 'Normal',
        [string] $LeasePath
    )
    Get-GenericExternalValue @PSBoundParameters
}
Export-ModuleMember -Function Invoke-ExternalBinaryBoundary
""");
            File.WriteAllText(manifest, $$"""
@{
    RootModule = 'input.psm1'
    ModuleVersion = '1.0.0'
    GUID = '7b5468f4-c02f-4e13-93ca-3f3f1187590e'
    NestedModules = @('External/Generic.External.Binary.Module.psd1')
    FunctionsToExport = @('Invoke-ExternalBinaryBoundary')
    CmdletsToExport = @('Get-GenericExternalValue')
    VariablesToExport = @()
    AliasesToExport = @()
}
""");
            var moduleAssembly = Path.Combine(externalRoot, "Generic.External.Binary.Module.dll");
            var dependencyAssembly = Path.Combine(externalRoot, "Generic.External.Binary.Dependency.dll");
            File.Copy(typeof(GetGenericExternalValueCommand).Assembly.Location, moduleAssembly);
            File.Copy(typeof(GenericValueSource).Assembly.Location, dependencyAssembly);
            var externalManifest = Path.Combine(externalRoot, "Generic.External.Binary.Module.psd1");
            File.WriteAllText(externalManifest, $$"""
@{
    RootModule = 'Generic.External.Binary.Module.dll'
    ModuleVersion = '2.3.4'
    GUID = '{{ExternalModuleGuid}}'
    CompatiblePSEditions = @('Core')
    PowerShellVersion = '7.6'
    RequiredAssemblies = @('Generic.External.Binary.Dependency.dll')
    CmdletsToExport = @('Get-GenericExternalValue')
    FunctionsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    TypesToProcess = @('Generic.External.Binary.Module.types.ps1xml')
    FormatsToProcess = @('Generic.External.Binary.Module.format.ps1xml')
    FileList = @(
        'Generic.External.Binary.Module.dll',
        'Generic.External.Binary.Dependency.dll',
        'Generic.External.Binary.Module.types.ps1xml',
        'Generic.External.Binary.Module.format.ps1xml',
        'en-US/Generic.External.Binary.Module.dll-Help.xml'
    )
}
""");
            File.WriteAllText(Path.Combine(externalRoot, "Generic.External.Binary.Module.types.ps1xml"), """
<?xml version="1.0" encoding="utf-8"?>
<Types>
  <Type>
    <Name>Generic.External.Binary.Module.GenericExternalRecord</Name>
    <Members>
      <AliasProperty>
        <Name>Label</Name>
        <ReferencedMemberName>DependencyValue</ReferencedMemberName>
      </AliasProperty>
    </Members>
  </Type>
</Types>
""");
            File.WriteAllText(Path.Combine(externalRoot, "Generic.External.Binary.Module.format.ps1xml"), """
<?xml version="1.0" encoding="utf-8"?>
<Configuration>
  <ViewDefinitions>
    <View>
      <Name>GenericExternalRecord</Name>
      <ViewSelectedBy><TypeName>Generic.External.Binary.Module.GenericExternalRecord</TypeName></ViewSelectedBy>
      <TableControl>
        <TableHeaders><TableColumnHeader><Label>Label</Label></TableColumnHeader></TableHeaders>
        <TableRowEntries><TableRowEntry><TableColumnItems><TableColumnItem><PropertyName>Label</PropertyName></TableColumnItem></TableColumnItems></TableRowEntry></TableRowEntries>
      </TableControl>
    </View>
  </ViewDefinitions>
</Configuration>
""");
            File.WriteAllText(Path.Combine(cultureRoot, "Generic.External.Binary.Module.dll-Help.xml"), """
<?xml version="1.0" encoding="utf-8"?>
<helpItems schema="maml" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
  <command:command>
    <command:details>
      <command:name>Get-GenericExternalValue</command:name>
      <maml:description><maml:para>Returns an external binary value through a transitive managed dependency.</maml:para></maml:description>
      <maml:copyright><maml:para /></maml:copyright>
      <command:verb>Get</command:verb>
      <command:noun>GenericExternalValue</command:noun>
    </command:details>
    <maml:description><maml:para>Returns an external binary value through a transitive managed dependency.</maml:para></maml:description>
    <command:syntax><command:syntaxItem><maml:name>Get-GenericExternalValue</maml:name></command:syntaxItem></command:syntax>
    <command:parameters />
    <command:inputTypes><command:inputType><dev:type><maml:name>None</maml:name></dev:type></command:inputType></command:inputTypes>
    <command:returnValues><command:returnValue><dev:type><maml:name>GenericExternalRecord</maml:name></dev:type></command:returnValue></command:returnValues>
  </command:command>
</helpItems>
""");
            if (!string.IsNullOrWhiteSpace(signingThumbprint))
                Sign(powerShellExecutable, root, signingThumbprint!, moduleAssembly, dependencyAssembly, externalManifest);
            return new ExternalBinaryModuleFixture(
                root,
                script,
                manifest,
                output,
                powerShellExecutable,
                isolatedModulePath);
        }

        public PowerShellCompilationBuildResult Build()
        {
            var spec = new PowerShellCompilationBuildSpec(
                ScriptPath,
                OutputPath,
                "Generic.External.Host",
                PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Hybrid)
            {
                TargetFramework = "net10.0",
                ModuleManifestPath = ManifestPath
            };
            spec.ExpectedDependencyLock = new PowerShellCompilationDependencyPlanner().AnalyzeGraph(spec);
            return new PowerShellCompilationArtifactBuilder().Build(spec);
        }

        public JsonDocument Execute(string artifactManifestPath)
        {
            var leasePath = Path.Combine(Root, "external-provider.lease");
            var scriptPath = Path.Combine(Root, "Observe-ExternalBinaryModule.ps1");
            File.WriteAllText(scriptPath, ObservationScript);
            var run = RunPowerShellFile(scriptPath, artifactManifestPath, leasePath);
            Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
            return JsonDocument.Parse(run.StandardOutput.Trim());
        }

        internal void ExecuteTimeoutProbe()
            => RunPowerShell(
                new[] { "-NoProfile", "-NonInteractive", "-Command", "[Threading.Thread]::Sleep(30000)" },
                PowerShellExecutable,
                Root,
                Root,
                IsolatedModulePath,
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromSeconds(5));

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Sign(string powerShellExecutable, string root, string thumbprint, params string[] paths)
        {
            static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));

            var encodedPaths = string.Join(",", paths.Select(path => $"'{Encode(path)}'"));
            var command =
                "$decode = { param([string] $Value) [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Value)) }; " +
                $"$thumbprint = & $decode '{Encode(thumbprint)}'; " +
                $"$paths = @({encodedPaths}) | ForEach-Object {{ & $decode $_ }}; " +
                "$certificate = Get-Item -LiteralPath ('Cert:\\CurrentUser\\My\\' + $thumbprint); " +
                "$results = @($paths | ForEach-Object { " +
                "  $result = $null; " +
                "  foreach ($attempt in 1..10) { " +
                "    $result = Set-AuthenticodeSignature -LiteralPath $_ -Certificate $certificate -HashAlgorithm SHA256; " +
                "    if ($result.Status -eq 'Valid') { break }; " +
                "    Start-Sleep -Milliseconds 250 " +
                "  }; " +
                "  $result " +
                "}); " +
                "if ($results.Count -ne $paths.Count) { throw 'Not every requested fixture file produced a signing result.' }; " +
                "$invalid = @($results | Where-Object Status -ne Valid); if ($invalid.Count -ne 0) { throw ($invalid | Out-String) }";
            var arguments = new[] { "-NoProfile", "-NonInteractive", "-Command", command };
            var run = RunPowerShell(
                arguments,
                powerShellExecutable,
                root,
                workingDirectory: root,
                isolatedModulePath: null);
            Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
        }

        private (int ExitCode, string StandardOutput, string StandardError) RunPowerShellFile(
            string scriptPath,
            string artifactManifestPath,
            string leasePath)
            => RunPowerShell(new[]
            {
                "-NoProfile", "-NonInteractive", "-File", scriptPath,
                "-ManifestPath", artifactManifestPath,
                "-LeasePath", leasePath,
                "-ExpectedModulePath", IsolatedModulePath
            }, PowerShellExecutable, Root, Root, IsolatedModulePath);

        private static (int ExitCode, string StandardOutput, string StandardError) RunPowerShell(
            IEnumerable<string> arguments,
            string powerShellExecutable,
            string root,
            string? workingDirectory,
            string? isolatedModulePath,
            TimeSpan? executionTimeout = null,
            TimeSpan? settlementTimeout = null)
        {
            executionTimeout ??= TimeSpan.FromSeconds(120);
            settlementTimeout ??= TimeSpan.FromSeconds(10);
            var start = new ProcessStartInfo
            {
                FileName = powerShellExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? root
            };
            if (isolatedModulePath is not null)
            {
                var profileRoot = Path.Combine(root, "isolated-profile");
                var packageRoot = Path.Combine(root, "isolated-packages");
                var configRoot = Path.Combine(root, "isolated-config");
                Directory.CreateDirectory(profileRoot);
                Directory.CreateDirectory(packageRoot);
                Directory.CreateDirectory(configRoot);
                start.Environment["PSModulePath"] = isolatedModulePath;
                start.Environment["HOME"] = profileRoot;
                start.Environment["USERPROFILE"] = profileRoot;
                start.Environment["APPDATA"] = configRoot;
                start.Environment["LOCALAPPDATA"] = configRoot;
                start.Environment["XDG_CONFIG_HOME"] = configRoot;
                start.Environment["XDG_DATA_HOME"] = profileRoot;
                start.Environment["XDG_CACHE_HOME"] = Path.Combine(root, "isolated-cache");
                start.Environment["DOTNET_CLI_HOME"] = profileRoot;
                start.Environment["NUGET_PACKAGES"] = packageRoot;
                start.Environment["POWERSHELL_UPDATECHECK"] = "Off";
            }
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            var completion = Task.WhenAll(exitTask, outputTask, errorTask);
            try
            {
                completion.WaitAsync(executionTimeout.Value).GetAwaiter().GetResult();
            }
            catch (TimeoutException timeoutException)
            {
                Exception? killFailure = null;
                try { process.Kill(entireProcessTree: true); }
                catch (Exception exception) { killFailure = exception; }
                try { completion.WaitAsync(settlementTimeout.Value).GetAwaiter().GetResult(); }
                catch (Exception exception)
                {
                    throw new TimeoutException(
                        $"External binary-module acceptance timed out and its process/output pipes did not settle within the {settlementTimeout.Value.TotalSeconds:0.###}-second termination window.",
                        killFailure is null ? exception : new AggregateException(killFailure, exception));
                }
                throw new TimeoutException(
                    $"External binary-module acceptance timed out after {executionTimeout.Value.TotalSeconds:0.###} seconds and the process tree was terminated.",
                    killFailure ?? timeoutException);
            }
            return (process.ExitCode, outputTask.Result, errorTask.Result);
        }

        private static string ResolvePowerShellExecutable()
        {
            var executableName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
            var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => Path.Combine(directory.Trim().Trim('"'), executableName));
            if (OperatingSystem.IsWindows())
                candidates = candidates.Append(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "PowerShell",
                    "7",
                    executableName));
            var resolved = candidates
                .Select(Path.GetFullPath)
                .FirstOrDefault(File.Exists);
            return resolved ?? throw new FileNotFoundException("A PowerShell 7 executable could not be resolved to an exact path.");
        }

        private const string ObservationScript = """
param(
    [Parameter(Mandatory)][string] $ManifestPath,
    [Parameter(Mandatory)][string] $LeasePath,
    [Parameter(Mandatory)][string] $ExpectedModulePath
)
$env:PSModulePath = $ExpectedModulePath
$ErrorActionPreference = 'Stop'
if ($env:PSModulePath -cne $ExpectedModulePath) { throw 'The child process did not preserve the isolated module path.' }
$hostPath = [Environment]::ProcessPath
$hostStream = [IO.File]::OpenRead($hostPath)
try { $hostSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($hostStream)) }
finally { $hostStream.Dispose() }

function New-Invocation([string] $Script) {
    $instance = [powershell]::Create()
    [void] $instance.AddScript($Script).AddArgument($ManifestPath).AddArgument($ExpectedModulePath)
    return $instance
}

$normal = New-Invocation @'
param($ManifestPath,$ExpectedModulePath)
$env:PSModulePath = $ExpectedModulePath
$module = Import-Module -Name $ManifestPath -Force -PassThru
$nested = Get-Module -All | Where-Object Guid -eq 'f57aa65b-a38b-4d41-a45f-6ace6923d7b7' | Select-Object -First 1
$record = Invoke-ExternalBinaryBoundary -Value 41 -Verbose -Debug -InformationAction Continue -WarningAction Continue
$externalRoot = Join-Path (Split-Path -Parent $ManifestPath) 'External'
$signaturePaths = @(
    (Join-Path $externalRoot 'Generic.External.Binary.Module.dll'),
    (Join-Path $externalRoot 'Generic.External.Binary.Dependency.dll'),
    (Join-Path $externalRoot 'Generic.External.Binary.Module.psd1')
)
$signatures = @($signaturePaths | ForEach-Object { Get-AuthenticodeSignature -LiteralPath $_ })
[pscustomobject]@{
    ModuleVersion = [string] $nested.Version
    ModuleGuid = [string] $nested.Guid
    ModuleAssemblyVersion = [string] ([Generic.External.Binary.Module.GetGenericExternalValueCommand].Assembly.GetName().Version)
    DependencyAssemblyVersion = $record.DependencyAssemblyVersion
    Architecture = $record.Architecture
    ModuleLoadContext = $record.ModuleLoadContext
    ModuleUsesDefaultLoadContext = $record.ModuleUsesDefaultLoadContext
    ModuleAssemblyLocation = $record.ModuleAssemblyLocation
    DependencyAssemblyLocation = $record.DependencyAssemblyLocation
    DependencyLoadContext = $record.DependencyLoadContext
    DependencyUsesDefaultLoadContext = $record.DependencyUsesDefaultLoadContext
    ModulePathEnvironment = $env:PSModulePath
    DependencyValue = $record.DependencyValue
    TypeAliasValue = $record.Label
    TypeDataLoaded = [bool] ((Get-TypeData -TypeName 'Generic.External.Binary.Module.GenericExternalRecord').Members.ContainsKey('Label'))
    FormatDataLoaded = [bool] (@(Get-FormatData -TypeName 'Generic.External.Binary.Module.GenericExternalRecord').Count -eq 1)
    HelpSynopsis = [string] (Get-Help Get-GenericExternalValue).Synopsis
    Signed = [bool] (@($signatures | Where-Object Status -eq Valid).Count -eq $signatures.Count)
    SignatureStatuses = @($signatures.Status | ForEach-Object ToString)
    SignatureThumbprints = @($signatures.SignerCertificate.Thumbprint)
}
'@
try {
    $normalOutput = @($normal.Invoke())
    if ($normal.HadErrors) { throw ($normal.Streams.Error | Out-String) }
    $identity = $normalOutput[-1]
    $information = @($normal.Streams.Information.MessageData | ForEach-Object ToString)
    $warnings = @($normal.Streams.Warning.Message)
    $verbose = @($normal.Streams.Verbose.Message)
    $debug = @($normal.Streams.Debug.Message)
} finally { $normal.Dispose() }

$nonTerminating = New-Invocation @'
param($ManifestPath,$ExpectedModulePath)
$env:PSModulePath = $ExpectedModulePath
Import-Module -Name $ManifestPath -Force
Invoke-ExternalBinaryBoundary -Behavior Error -ErrorAction Continue | ForEach-Object DependencyValue
'@
try {
    $nonTerminatingOutput = @($nonTerminating.Invoke() | ForEach-Object ToString)
    $nonTerminatingErrorIds = @($nonTerminating.Streams.Error.FullyQualifiedErrorId)
} finally { $nonTerminating.Dispose() }

$terminating = New-Invocation @'
param($ManifestPath,$ExpectedModulePath)
$env:PSModulePath = $ExpectedModulePath
Import-Module -Name $ManifestPath -Force
try { Invoke-ExternalBinaryBoundary -Behavior Terminating -ErrorAction Stop } catch { $_.FullyQualifiedErrorId }
'@
try {
    $terminatingErrorIds = @($terminating.Invoke() | ForEach-Object ToString)
} finally { $terminating.Dispose() }

$wait = [powershell]::Create()
[void] $wait.AddScript(@'
param($ManifestPath,$LeasePath,$ExpectedModulePath)
$env:PSModulePath = $ExpectedModulePath
Import-Module -Name $ManifestPath -Force
Invoke-ExternalBinaryBoundary -Behavior Wait -LeasePath $LeasePath
'@).AddArgument($ManifestPath).AddArgument($LeasePath).AddArgument($ExpectedModulePath)
$async = $wait.BeginInvoke()
$deadline = [DateTime]::UtcNow.AddSeconds(20)
while (-not (Test-Path -LiteralPath $LeasePath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 20 }
if (-not (Test-Path -LiteralPath $LeasePath)) { throw 'Cancellation fixture did not acquire its lease.' }
$leaseWasExclusive = $false
try {
    $probe = [IO.File]::Open($LeasePath, 'Open', 'ReadWrite', 'None')
    $probe.Dispose()
} catch [IO.IOException] { $leaseWasExclusive = $true }
$wait.Stop()
try { [void] $wait.EndInvoke($async) } catch { }
$cancellationCompleted = $async.IsCompleted
$wait.Dispose()
$leaseReleased = $false
try { Remove-Item -LiteralPath $LeasePath -Force; $leaseReleased = -not (Test-Path -LiteralPath $LeasePath) } catch { }

[pscustomobject]@{
    ModuleVersion = $identity.ModuleVersion
    ModuleGuid = $identity.ModuleGuid
    ModuleAssemblyVersion = $identity.ModuleAssemblyVersion
    DependencyAssemblyVersion = $identity.DependencyAssemblyVersion
    Architecture = $identity.Architecture
    ModuleLoadContext = $identity.ModuleLoadContext
    ModuleUsesDefaultLoadContext = $identity.ModuleUsesDefaultLoadContext
    ModuleAssemblyLocation = $identity.ModuleAssemblyLocation
    DependencyAssemblyLocation = $identity.DependencyAssemblyLocation
    DependencyLoadContext = $identity.DependencyLoadContext
    DependencyUsesDefaultLoadContext = $identity.DependencyUsesDefaultLoadContext
    ModulePathEnvironment = $identity.ModulePathEnvironment
    HostProcessPath = $hostPath
    HostSha256 = $hostSha256
    DependencyValue = $identity.DependencyValue
    TypeAliasValue = $identity.TypeAliasValue
    TypeDataLoaded = $identity.TypeDataLoaded
    FormatDataLoaded = $identity.FormatDataLoaded
    HelpSynopsis = $identity.HelpSynopsis
    Information = $information
    Warnings = $warnings
    Verbose = $verbose
    Debug = $debug
    NonTerminatingOutput = $nonTerminatingOutput
    NonTerminatingErrorIds = $nonTerminatingErrorIds
    TerminatingErrorIds = $terminatingErrorIds
    CancellationCompleted = $cancellationCompleted
    LeaseWasExclusive = $leaseWasExclusive
    LeaseReleased = $leaseReleased
    Signed = $identity.Signed
    SignatureStatuses = $identity.SignatureStatuses
    SignatureThumbprints = $identity.SignatureThumbprints
} | ConvertTo-Json -Depth 6 -Compress
""";
    }
}
