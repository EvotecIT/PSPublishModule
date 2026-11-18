namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    private static void RunModuleAcceptanceScript(PowerShellCompilationProjectManifestService.ProjectContext context,
        PowerShellCompilationProjectArtifact artifact, string artifactPath)
    {
        var declaration = artifact.ModuleTest!;
        var script = context.Resolve(declaration.Script);
        PowerShellCompilationPathSafety.EnsureNoLinksFromFileSystemRoot(script,
            "Module acceptance script must not traverse symbolic links or junctions.");
        if (!File.Exists(script) || !PowerShellCompilationProjectManifestService.ComputeSha256(script)
                .Equals(declaration.Sha256, StringComparison.OrdinalIgnoreCase))
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
        var host = GetModuleTestHost(artifact.Target.TargetFramework);
        PowerShellCompilationPathSafety.EnsureProcessLaunchPaths(host, directory);
        var arguments = new List<string>
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-ArtifactPath", artifactPath
        };
        arguments.AddRange(declaration.Arguments);
        var run = new ProcessRunner().RunAsync(new ProcessRunRequest(host, directory, arguments.ToArray(),
            TimeSpan.FromMinutes(2), new Dictionary<string, string?> { ["POWERSHELL_TELEMETRY_OPTOUT"] = "1" }))
            .GetAwaiter().GetResult();
        if (!run.Succeeded || !string.IsNullOrWhiteSpace(run.StdErr))
            throw new InvalidOperationException($"Module acceptance script failed with exit {run.ExitCode}: {run.StdOut}{Environment.NewLine}{run.StdErr}".Trim());
    }
}
