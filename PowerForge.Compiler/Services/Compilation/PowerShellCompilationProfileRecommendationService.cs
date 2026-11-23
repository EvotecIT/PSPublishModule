namespace PowerForge;

/// <summary>Shapes opt-in profile advice from canonical analysis and optional measured boundary evidence.</summary>
public sealed class PowerShellCompilationProfileRecommendationService
{
    /// <summary>Creates advice without altering the plan, source, project, or target.</summary>
    public PowerShellCompilationProfileRecommendation Create(
        PowerShellCompilationPlan plan,
        PowerShellCompilationTargetContract target,
        PowerShellCompilationBoundaryRuntimeProfile? boundaryProfile = null)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        target = PowerShellCompilationTargetContractService.Normalize(target ?? throw new ArgumentNullException(nameof(target)));
        ValidateBoundaryProfile(target, boundaryProfile);
        var reasons = new List<string>();
        string action;
        if (!plan.CanProceed)
        {
            action = "ResolveDiagnostics";
            reasons.Add("The selected target has parse, semantic, dependency, or fallback blockers.");
        }
        else if (boundaryProfile?.EstimatedOverheadRatio >= 0.25d)
        {
            action = "CoarsenBoundaryOrKeepHosted";
            reasons.Add("Measured typed/hosted crossing overhead is at least 25% of the equivalent workload.");
        }
        else if (target.Mode == PowerShellCompilationMode.Hybrid && plan.CompilableUnits == 0)
        {
            action = "KeepHosted";
            reasons.Add("This exact input has no currently eligible typed units, so Hybrid adds no typed execution value.");
        }
        else if (target.Mode == PowerShellCompilationMode.Hybrid && plan.RuntimeFallbackUnits == 0)
        {
            action = "MeasureStrictManaged";
            reasons.Add("The complete analyzed input is eligible; a Strict managed candidate can now be measured without changing the project automatically.");
        }
        else if (target.Mode == PowerShellCompilationMode.Strict && target.Deployment == PowerShellCompilationDeploymentModel.NativeAot)
        {
            action = target.SupportLevel == "Supported" ? "RetainStrictNative" : "ValidateExperimentalTarget";
            reasons.Add(target.SupportLevel == "Supported"
                ? "The selected Strict NativeAOT profile is promoted by target-host evidence."
                : "The selected NativeAOT profile requires exact target-host and performance qualification before promotion.");
        }
        else
        {
            action = "RetainSelectedProfile";
            reasons.Add("Current semantic and boundary evidence does not justify an automatic profile change.");
        }
        if (boundaryProfile is null)
            reasons.Add("No runtime boundary profile was supplied; performance advice is limited to static semantic evidence.");
        return new PowerShellCompilationProfileRecommendation
        {
            TargetContractSha256 = target.ContractSha256,
            SupportLevel = target.SupportLevel,
            AnalyzedUnits = plan.TotalUnits,
            EligibleUnits = plan.CompilableUnits,
            EligibleUnitRatio = plan.TotalUnits == 0 ? 0d : plan.CompilableUnits / (double)plan.TotalUnits,
            BoundaryProfile = boundaryProfile,
            Action = action,
            Reasons = reasons.ToArray()
        };
    }

    private static void ValidateBoundaryProfile(
        PowerShellCompilationTargetContract target,
        PowerShellCompilationBoundaryRuntimeProfile? profile)
    {
        if (profile is null) return;
        if (profile.SchemaVersion != 1 || string.IsNullOrWhiteSpace(profile.Workload) ||
            profile.BoundaryInvocations <= 0 || profile.BaselineDurationNanoseconds < 0 ||
            profile.BoundaryDurationNanoseconds < 0 || profile.EstimatedOverheadRatio is < 0d or > 1d ||
            double.IsNaN(profile.EstimatedOverheadRatio) || double.IsInfinity(profile.EstimatedOverheadRatio))
            throw new InvalidDataException("Boundary profile is incomplete or outside its supported numeric contract.");
        if (!string.IsNullOrWhiteSpace(target.RuntimeIdentifier) &&
            !target.RuntimeIdentifier.Equals(profile.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Boundary profile runtime identifier differs from the exact target contract.");
    }
}
