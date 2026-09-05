namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationMaturityTests
{
    [Fact]
    public void SupportMatrix_UsesOneExactPolicyForAdvertisedAndExperimentalProfiles()
    {
        var matrix = PowerShellCompilationSupportMatrixService.Create();

        Assert.Equal(1, matrix.SchemaVersion);
        Assert.Equal(matrix.Profiles.Length, matrix.Profiles.Select(static profile => profile.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(matrix.Profiles, profile => Assert.Equal(
            profile.SupportLevel,
            PowerShellCompilationSupportMatrixService.Evaluate(
                profile.ArtifactKind,
                profile.Mode,
                profile.Deployment,
                profile.TargetFramework,
                profile.RuntimeIdentifier)));
        Assert.Contains(matrix.Profiles, static profile => profile.Id == "executable-strict-net10.0-win-x64-nativeaot" && profile.Advertised);
        Assert.Contains(matrix.Profiles, static profile => profile.Id == "executable-strict-net10.0-linux-x64-frameworkdependent" && profile.Advertised);
        Assert.Contains(matrix.Profiles, static profile => profile.Id == "executable-strict-net10.0-osx-arm64-nativeaot" && !profile.Advertised && profile.SupportLevel == "Experimental");
        Assert.DoesNotContain(matrix.Profiles, static profile => profile.SupportLevel == "Experimental" && profile.Advertised);
    }

    [Fact]
    public void ProfileRecommendation_IsOptInAndDistinguishesStaticFromMeasuredAdvice()
    {
        var plan = CreateEligiblePlan(PowerShellCompilationMode.Hybrid);
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            "net8.0",
            null,
            false,
            false,
            PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        var service = new PowerShellCompilationProfileRecommendationService();

        var staticAdvice = service.Create(plan, target);
        Assert.Equal("MeasureStrictManaged", staticAdvice.Action);
        Assert.Equal(1d, staticAdvice.EligibleUnitRatio);
        Assert.Contains(staticAdvice.Reasons, static reason => reason.Contains("No runtime boundary profile", StringComparison.Ordinal));

        var measuredAdvice = service.Create(plan, target, new PowerShellCompilationBoundaryRuntimeProfile
        {
            Workload = "bounded-test",
            RuntimeIdentifier = "win-x64",
            BaselineDurationNanoseconds = 100,
            BoundaryDurationNanoseconds = 400,
            BoundaryInvocations = 10,
            EstimatedOverheadNanosecondsPerBoundary = 30,
            EstimatedOverheadRatio = 0.75
        });
        Assert.Equal("CoarsenBoundaryOrKeepHosted", measuredAdvice.Action);
        Assert.Equal(target.ContractSha256, measuredAdvice.TargetContractSha256);
    }

    private static PowerShellCompilationPlan CreateEligiblePlan(PowerShellCompilationMode mode)
        => new(
            mode,
            new[]
            {
                new PowerShellCompilationFilePlan(
                    "input.ps1",
                    "input.ps1",
                    new[]
                    {
                        new PowerShellCompilationUnitPlan(
                            "Get-Value",
                            PowerShellCompilationUnitKind.Function,
                            1,
                            typeof(int).FullName!,
                            Array.Empty<PowerShellCompilationParameter>(),
                            Array.Empty<PowerShellCompilationDiagnostic>())
                    },
                    Array.Empty<PowerShellCompilationDiagnostic>())
            },
            "net8.0");
}
