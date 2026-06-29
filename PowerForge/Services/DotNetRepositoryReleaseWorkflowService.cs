namespace PowerForge;

internal sealed class DotNetRepositoryReleaseWorkflowService
{
    private readonly Func<DotNetRepositoryReleaseSpec, Action<DotNetReleaseBuildAssemblySigningRequest>?, DotNetRepositoryReleaseResult> _executeRelease;
    private readonly Action<DotNetReleaseBuildAssemblySigningRequest>? _signAssemblies;

    public DotNetRepositoryReleaseWorkflowService(
        ILogger logger,
        Func<DotNetRepositoryReleaseSpec, DotNetRepositoryReleaseResult>? executeRelease = null,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies = null)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        _executeRelease = executeRelease is null
            ? (spec, signing) => new DotNetRepositoryReleaseService(logger).Execute(spec, signing)
            : (spec, _) => executeRelease(spec);
        _signAssemblies = signAssemblies;
    }

    public DotNetRepositoryReleaseResult Execute(DotNetRepositoryReleasePreparedContext context, bool executeBuild)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (context.Spec is null)
            throw new ArgumentException("Prepared spec is required.", nameof(context));

        context.Spec.WhatIf = true;
        var plan = _executeRelease(context.Spec, _signAssemblies);
        if (!executeBuild)
            return plan;

        context.Spec.WhatIf = false;
        return _executeRelease(context.Spec, _signAssemblies);
    }
}
