using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    private static void RunModuleAcceptanceScript(PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact, string artifactPath)
        => RunModuleAcceptance(CaptureModuleAcceptance(context, artifact, artifactPath));

    internal static ModuleAcceptanceInvocation CaptureModuleAcceptance(PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact, string artifactPath)
    {
        var declaration = artifact.ModuleTest!;
        var script = context.Resolve(declaration.Script);
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(script,
            "Module acceptance script must not traverse symbolic links or junctions.");
        var bytes = File.ReadAllBytes(script);
        if (!HashAcceptanceBytes(bytes).Equals(declaration.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Module acceptance script differs from its reviewed SHA-256.");
        var directory = declaration.WorkingDirectory is null ? Path.GetDirectoryName(artifactPath)! :
            declaration.WorkingDirectory == "." ? context.Root : context.Resolve(declaration.WorkingDirectory);
        PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(directory,
            "Module acceptance working directory must not traverse symbolic links or junctions.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Module acceptance working directory was not found.");
        var desktop = artifact.Target.TargetFramework.Equals("net472", StringComparison.OrdinalIgnoreCase);
        if (desktop && !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            throw new PlatformNotSupportedException("Testing a net472 module requires Windows PowerShell 5.1 on Windows.");
        if (desktop) PowerShellCompilationPathSafety.EnsureWindowsPowerShellPayloadPath(script);
        var parameters = declaration.Parameters.ToDictionary(static parameter => parameter.Key,
            static parameter => Convert.ToBase64String(Encoding.UTF8.GetBytes(parameter.Value)), StringComparer.OrdinalIgnoreCase);
        var payload = JsonSerializer.Serialize(new
        {
            scriptPath = script,
            sourceBase64 = Convert.ToBase64String(bytes),
            artifactPath,
            parameters
        });
        return new ModuleAcceptanceInvocation(GetModuleTestHost(artifact.Target.TargetFramework), directory, payload);
    }

    internal static void RunModuleAcceptance(ModuleAcceptanceInvocation invocation)
    {
        PowerShellCompilationPathSafety.EnsureProcessLaunchPaths(invocation.Host, invocation.WorkingDirectory);
        var directory = Path.Combine(Path.GetTempPath(), "PowerForge", "ModuleAcceptance");
        PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(directory, "Module acceptance payload directory must not traverse links.");
        Directory.CreateDirectory(directory);
        var payloadPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        var payloadBytes = Encoding.UTF8.GetBytes(invocation.Payload);
        var expected = HashAcceptanceBytes(payloadBytes);
        File.WriteAllBytes(payloadPath, payloadBytes);
        try
        {
            const string bootstrap = """
                $ErrorActionPreference = 'Stop'
                $ProgressPreference = 'SilentlyContinue'
                $env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')
                $bytes = [IO.File]::ReadAllBytes($env:POWERFORGE_MODULE_TEST_PAYLOAD)
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $actual = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
                finally { $sha.Dispose() }
                if ($actual -cne $env:POWERFORGE_MODULE_TEST_PAYLOAD_SHA256) { throw 'Module acceptance snapshot changed.' }
                $payload = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
                $sourceBytes = [Convert]::FromBase64String($payload.sourceBase64)
                $encoding = if ($PSVersionTable.PSEdition -eq 'Desktop') { [Text.Encoding]::Default } else { [Text.Encoding]::UTF8 }
                $reader = [IO.StreamReader]::new([IO.MemoryStream]::new($sourceBytes, $false), $encoding, $true)
                try { $source = $reader.ReadToEnd() } finally { $reader.Dispose() }
                $parameters = @{}
                foreach ($parameter in $payload.parameters.PSObject.Properties) {
                    $parameters[$parameter.Name] = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($parameter.Value))
                }
                $tokens = $null; $errors = $null
                $ast = [Management.Automation.Language.Parser]::ParseInput($source, $payload.scriptPath, [ref]$tokens, [ref]$errors)
                if ($errors.Count) { throw 'Module acceptance snapshot did not parse.' }
                $entry = $ExecutionContext.InvokeCommand.GetCommand($payload.scriptPath, [Management.Automation.CommandTypes]::ExternalScript)
                if ($null -eq $entry) { throw 'Module acceptance script could not be resolved.' }
                $property = [Management.Automation.ExternalScriptInfo].GetProperty('ScriptBlock', [Reflection.BindingFlags]'Instance,Public,NonPublic')
                if ($null -eq $property -or $null -eq $property.GetSetMethod($true)) { throw 'Native script snapshot dispatch is unavailable.' }
                $property.SetValue($entry, $ast.GetScriptBlock(), $null)
                & $entry -ArtifactPath $payload.artifactPath @parameters
                """;
            var run = new ProcessRunner().RunAsync(new ProcessRunRequest(invocation.Host, invocation.WorkingDirectory,
                new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap)) }, TimeSpan.FromMinutes(2),
                new Dictionary<string, string?>
                {
                    ["POWERSHELL_TELEMETRY_OPTOUT"] = "1",
                    ["POWERFORGE_MODULE_TEST_PAYLOAD"] = payloadPath,
                    ["POWERFORGE_MODULE_TEST_PAYLOAD_SHA256"] = expected
                })).GetAwaiter().GetResult();
            if (!run.Succeeded || !string.IsNullOrWhiteSpace(run.StdErr))
                throw new InvalidOperationException($"Module acceptance script failed with exit {run.ExitCode}: {run.StdOut}{Environment.NewLine}{run.StdErr}".Trim());
        }
        finally { File.Delete(payloadPath); }
    }

    private static string HashAcceptanceBytes(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    internal sealed class ModuleAcceptanceInvocation
    {
        internal ModuleAcceptanceInvocation(string host, string workingDirectory, string payload)
        { Host = host; WorkingDirectory = workingDirectory; Payload = payload; }
        internal string Host { get; }
        internal string WorkingDirectory { get; }
        internal string Payload { get; }
    }
}
