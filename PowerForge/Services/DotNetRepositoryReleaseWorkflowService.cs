namespace PowerForge;

internal sealed class DotNetRepositoryReleaseWorkflowService
{
    private readonly Func<DotNetRepositoryReleaseSpec, DotNetRepositoryReleaseResult> _executeRelease;

    public DotNetRepositoryReleaseWorkflowService(
        ILogger logger,
        Func<DotNetRepositoryReleaseSpec, DotNetRepositoryReleaseResult>? executeRelease = null)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        _executeRelease = executeRelease ?? (spec => new DotNetRepositoryReleaseService(logger).Execute(spec));
    }

    public DotNetRepositoryReleaseResult Execute(DotNetRepositoryReleasePreparedContext context, bool executeBuild)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (context.Spec is null)
            throw new ArgumentException("Prepared spec is required.", nameof(context));

        context.Spec.WhatIf = true;
        var plan = _executeRelease(context.Spec);
        if (!executeBuild)
            return plan;

        context.Spec.WhatIf = false;
        return _executeRelease(context.Spec);
    }
}
