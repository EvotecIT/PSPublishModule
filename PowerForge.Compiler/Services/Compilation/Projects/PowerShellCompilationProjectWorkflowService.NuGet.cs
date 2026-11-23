namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    /// <summary>
    /// Packages one tested Strict library through the shared independent-rebuild NuGet packer.
    /// Uses explicit project metadata and the verified isolated restore environment. Cancellation
    /// before publication preserves an existing package; committed publication returns success.
    /// </summary>
    public PowerShellCompilationProjectResult PackNuGet(
        string projectPath,
        IEnumerable<string>? targetNames = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = PowerShellCompilationProjectManifestService.Open(projectPath);
        var selected = SelectArtifacts(context, targetNames);
        if (selected.Length != 1)
            throw new InvalidOperationException("NuGet packing requires exactly one library target; select it with --target. Combining target frameworks into one package is not supported.");
        var artifact = selected[0];
        try
        {
            if (artifact.Target.ArtifactKind != PowerShellCompilationArtifactKind.Library ||
                artifact.Target.Mode != PowerShellCompilationMode.Strict)
                throw new InvalidOperationException("NuGet packing requires a Strict library target. Use the ZIP format for executables and PowerShell modules.");
            if (!artifact.EmitSource)
                throw new InvalidOperationException("NuGet packing requires emitSource=true before lock, restore, build and test so the library can be independently rebuilt.");
            var metadata = context.Manifest.NuGet
                ?? throw new InvalidOperationException("NuGet packing requires explicit nuGet metadata in the project manifest.");
            var validated = ValidateBuildReceipt(context, artifact);
            _ = ReadTestEvidence(context, artifact, validated);
            var environment = ReadEnvironment(context);
            var baseline = string.IsNullOrWhiteSpace(metadata.CompatibilityBaseline) ? null :
                ReadJson<PowerShellCompilationAbiManifest>(context.Resolve(metadata.CompatibilityBaseline!));
            var packagePath = context.Resolve($".powerforge/packages/nuget/{artifact.Name}/{metadata.PackageId}.{metadata.PackageVersion}.nupkg");
            var package = new PowerShellCompilationLibraryPackageBuilder().Build(
                new PowerShellCompilationLibraryPackageBuildRequest(validated.Receipt, packagePath,
                    metadata.PackageId, metadata.PackageVersion)
                {
                    Authors = metadata.Authors,
                    Description = metadata.Description,
                    LicenseExpression = metadata.LicenseExpression,
                    RepositoryUrl = metadata.RepositoryUrl,
                    RepositoryCommit = metadata.RepositoryCommit,
                    CompatibilityBaseline = baseline,
                    NuGetPackageRoot = environment.PackageRoot
                }, cancellationToken);
            var result = Pass(artifact,
                "Tested Strict library was independently rebuilt and packaged as NuGet with verified ABI, provider closure and build evidence.",
                package.PackagePath, validated.Manifest.DependencyGraph?.LockSha256, validated.Manifest.ArtifactSha256);
            result.PackageSha256 = package.PackageSha256;
            result.PublicAbiSha256 = package.PublicAbiSha256;
            return Complete("pack", context.ProjectPath, new[] { result });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            return Complete("pack", context.ProjectPath, new[] { Fail(artifact, exception) });
        }
    }
}
