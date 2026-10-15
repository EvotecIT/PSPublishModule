namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static PowerShellModuleCompilationConfiguration? ClonePowerShellCompilationConfiguration(
        PowerShellModuleCompilationConfiguration? source)
    {
        if (source is null) return null;
        return new PowerShellModuleCompilationConfiguration
        {
            Enabled = source.Enabled,
            Mode = source.Mode,
            TargetFramework = source.TargetFramework,
            ResourceMode = source.ResourceMode,
            IncludeResource = source.IncludeResource?.ToArray() ?? Array.Empty<string>(),
            ExcludeResource = source.ExcludeResource?.ToArray() ?? Array.Empty<string>(),
            UseBuildCache = source.UseBuildCache,
            BuildCacheDirectory = source.BuildCacheDirectory,
            EmitIrSnapshots = source.EmitIrSnapshots,
            ExpectedPublicAbiSha256 = source.ExpectedPublicAbiSha256,
            DependencyLock = source.DependencyLock,
            AllowUnreviewedDependencies = source.AllowUnreviewedDependencies,
            TimeoutSeconds = source.TimeoutSeconds
        };
    }

    private static void ValidatePowerShellModuleCompilation(ModuleBuildSpec buildSpec)
    {
        var compilation = buildSpec.PowerShellCompilation;
        if (compilation?.Enabled != true) return;
        var artifactName = PowerShellCompilationArtifactName.Normalize(buildSpec.Name, nameof(buildSpec.Name));
        if (!artifactName.Equals(buildSpec.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"PowerShell module compilation must preserve its published module name. Rename module '{buildSpec.Name}' and its manifest/root file to the canonical basename '{artifactName}' before compiling.");
        if (buildSpec.RefreshManifestOnly)
            throw new InvalidOperationException("PowerShell module compilation cannot be combined with RefreshPSD1Only.");
        if (!string.IsNullOrWhiteSpace(buildSpec.CsprojPath))
            throw new InvalidOperationException("PowerShell module compilation and an authored .NET binary project cannot own the same module build. Choose one binary-module source.");
        if (compilation.Mode is not PowerShellCompilationMode.Hybrid and not PowerShellCompilationMode.Strict)
            throw new InvalidOperationException("Build-Module PowerShell compilation supports Hybrid or Strict mode. Use Build-PowerShellArtifact for executable artifacts.");
        if (string.IsNullOrWhiteSpace(compilation.TargetFramework))
            throw new InvalidOperationException("PowerShell module compilation requires a target framework.");
        if (compilation.TimeoutSeconds < 1)
            throw new InvalidOperationException("PowerShell module compilation timeout must be at least one second.");
        if (compilation.DependencyLock is not null && compilation.AllowUnreviewedDependencies)
            throw new InvalidOperationException("PowerShell module compilation cannot combine a reviewed dependency lock with AllowUnreviewedDependencies.");
        if (!string.IsNullOrWhiteSpace(compilation.ExpectedPublicAbiSha256) && compilation.Mode != PowerShellCompilationMode.Strict)
            throw new InvalidOperationException("PowerShell module compilation ExpectedPublicAbiSha256 requires Strict mode.");
    }
}
