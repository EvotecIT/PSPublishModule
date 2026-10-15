namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    /// <summary>
    /// Observes a source-bound delivery when Status cannot see its build. Delivery evidence
    /// remains informational and never supplies private-input qualification or resume authority.
    /// </summary>
    private (string? Id, string? State, PowerForgeAppleReleaseDiagnostic[] Diagnostics) ReadAppleStatusDelivery(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        PowerForgeAppleAppReleaseResult? result,
        string? version,
        string? build)
    {
        if (plan.Action != PowerForgeAppleReleaseAction.Status ||
            result?.Success != true ||
            result.RemoteState?.Platforms.SingleOrDefault(value => value.Platform == app.Platform) is not { MatchedBuild: null, SelectedBuild: null } ||
            string.IsNullOrWhiteSpace(plan.SourceCommit) ||
            string.IsNullOrWhiteSpace(version) ||
            string.IsNullOrWhiteSpace(build))
        {
            return (null, null, Array.Empty<PowerForgeAppleReleaseDiagnostic>());
        }

        var receipts = _appleReceiptStore.ReadAll(plan);
        var delivery = receipts
            .Select(receipt => FindMatchingAppleUploadAttestationTarget(
                receipts, receipt, plan, app, version, build, requireCurrentExecutionPolicy: false))
            .FirstOrDefault(target => !string.IsNullOrWhiteSpace(target?.BuildUploadId));
        if (delivery is null)
            return (null, null, Array.Empty<PowerForgeAppleReleaseDiagnostic>());

        var uploadId = delivery.BuildUploadId!;
        AppStoreConnectBuildUploadInfo? upload;
        try
        {
            upload = _getAppleBuildUpload(CreateAppStoreConnectCredential(plan), uploadId);
        }
        catch (Exception exception)
        {
            return (uploadId, null, new[] { CreateAppleDeliveryDiagnostic(
                "APPLE_BUILD_UPLOAD_OBSERVATION_UNAVAILABLE", "warning",
                $"Unable to observe recorded delivery for '{app.Name}' {version} ({build}).",
                exception.Message) });
        }

        if (upload is null)
        {
            return (uploadId, null, new[] { CreateAppleDeliveryDiagnostic(
                "APPLE_BUILD_UPLOAD_OBSERVATION_UNAVAILABLE", "warning",
                $"App Store Connect did not return recorded delivery '{uploadId}' for '{app.Name}' {version} ({build}).",
                null) });
        }

        if ((!string.IsNullOrWhiteSpace(upload.Id) &&
             !string.Equals(upload.Id, uploadId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(upload.MarketingVersion) &&
             !string.Equals(upload.MarketingVersion, version, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(upload.BuildNumber) &&
             !string.Equals(upload.BuildNumber, build, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(upload.Platform) &&
             !string.Equals(upload.Platform, AppStoreConnectClient.ToAppStoreConnectPlatform(app.Platform), StringComparison.OrdinalIgnoreCase)))
        {
            return (uploadId, null, new[] { CreateAppleDeliveryDiagnostic(
                "APPLE_BUILD_UPLOAD_IDENTITY_MISMATCH", "warning",
                $"App Store Connect returned a different delivery identity for '{app.Name}' {version} ({build}); its state was not used.",
                $"Requested build-upload '{uploadId}'.") });
        }

        var failed = IsTerminalAppleBuildFailure(upload.State);
        var issues = upload.Errors
            .Select(static issue => FormatAppleBuildUploadIssue(issue))
            .Where(static issue => !string.IsNullOrWhiteSpace(issue))
            .ToArray();
        return (uploadId, upload.State, new[] { CreateAppleDeliveryDiagnostic(
            failed ? "APPLE_BUILD_UPLOAD_REJECTED" : "APPLE_BUILD_UPLOAD_STATE",
            failed ? "error" : "info",
            $"Recorded delivery '{uploadId}' for '{app.Name}' {version} ({build}) is '{upload.State ?? "unknown"}'; no matching build is visible.",
            issues.Length == 0 ? null : string.Join(" ", issues)) });
    }

    private static PowerForgeAppleReleaseDiagnostic CreateAppleDeliveryDiagnostic(
        string code,
        string severity,
        string summary,
        string? evidence)
        => new()
        {
            Code = code,
            Severity = severity,
            Category = "delivery",
            Summary = summary,
            Evidence = evidence,
            Action = "Inspect the retained delivery evidence and reconcile App Store Connect before another upload.",
            Retryable = false
        };
}
