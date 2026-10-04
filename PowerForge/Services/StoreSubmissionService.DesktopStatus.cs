namespace PowerForge;

internal sealed partial class StoreSubmissionService
{
    /// <summary>Verifies authenticated desktop draft readiness without changing its packages.</summary>
    public async Task RequireDesktopReadyAsync(StoreSubmissionSpec spec, StoreSubmissionPlan plan, CancellationToken cancellationToken = default)
    {
        var token = await ResolveAccessTokenAsync(spec.Authentication, StoreSubmissionProviderKind.DesktopInstaller, cancellationToken).ConfigureAwait(false);
        var payload = await GetDesktopDraftStatusAsync(plan.ApplicationId, token, ResolveSellerId(spec.Authentication), cancellationToken).ConfigureAwait(false);
        if (!ParseDesktopDraftStatus(payload).IsReady)
            throw new InvalidOperationException("The Store product is not ready for another package update. Inspect the current submission in Partner Center.");
    }

    /// <summary>Reads one desktop submission snapshot without changing packages or submitting again.</summary>
    public async Task<StoreSubmissionResult> GetDesktopStatusAsync(
        StoreSubmissionSpec spec, StoreSubmissionPlan plan, CancellationToken cancellationToken = default)
    {
        if (plan.Provider != StoreSubmissionProviderKind.DesktopInstaller || string.IsNullOrWhiteSpace(plan.SubmissionId))
            throw new InvalidOperationException("A desktop Store submission ID is required for status.");
        var token = await ResolveAccessTokenAsync(spec.Authentication, plan.Provider, cancellationToken).ConfigureAwait(false);
        return await ReadDesktopStatusAsync(spec.Authentication, plan, token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StoreSubmissionResult> ReadDesktopStatusAsync(
        StoreSubmissionAuthenticationOptions authentication, StoreSubmissionPlan plan, string token,
        CancellationToken cancellationToken)
    {
        var payload = await GetDesktopSubmissionStatusAsync(plan.ApplicationId, plan.SubmissionId!, token,
            ResolveSellerId(authentication), cancellationToken).ConfigureAwait(false);
        var (status, failed, details) = ParseDesktopSubmissionStatus(payload);
        return new StoreSubmissionResult
        {
            Plan = plan, SubmissionId = plan.SubmissionId, CommittedSubmission = true,
            Succeeded = !failed && !string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase),
            FinalStatus = status, StatusDetails = details,
            ErrorMessage = failed ? "The existing desktop Store submission failed. Inspect Partner Center for details." : null,
            StatusHistory = new[] { new StoreSubmissionStatusSnapshot { CheckedUtc = DateTimeOffset.UtcNow, Status = status, Details = details } }
        };
    }

    private async Task<StoreSubmissionResult> ResumeDesktopSubmissionAsync(
        StoreSubmissionAuthenticationOptions authentication, StoreSubmissionPlan plan, string token,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(plan.PollTimeoutMinutes);
        while (true)
        {
            var result = await ReadDesktopStatusAsync(authentication, plan, token, cancellationToken).ConfigureAwait(false);
            if (!plan.WaitForCommit || !result.Succeeded ||
                result.FinalStatus is not ("INPROGRESS" or "UNKNOWN"))
                return result;
            if (DateTimeOffset.UtcNow >= deadline)
            {
                result.Succeeded = false;
                result.ErrorMessage = "The existing desktop Store submission is still pending. Read its status again later.";
                return result;
            }
            await Task.Delay(TimeSpan.FromSeconds(plan.PollIntervalSeconds), cancellationToken).ConfigureAwait(false);
        }
    }
}
