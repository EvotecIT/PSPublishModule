using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Plans and applies only reviewed App Review notes without changing release state.</summary>
public sealed class AppStoreConnectReviewNotesService
{
    private readonly AppStoreConnectClient _client;

    /// <summary>Creates the service.</summary>
    public AppStoreConnectReviewNotesService(AppStoreConnectClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>Validates non-secret declarative notes before reading Apple state.</summary>
    public static void ValidateSpec(AppStoreConnectReviewNotesSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (spec.SchemaVersion != 1 || spec.Target is null ||
            string.IsNullOrWhiteSpace(spec.Target.AppId) || string.IsNullOrWhiteSpace(spec.Target.VersionString) ||
            !Enum.IsDefined(typeof(ApplePlatform), spec.Target.Platform))
            throw new ArgumentException("Review notes require schemaVersion 1 and an exact app, platform, and version.", nameof(spec));
        AppleReleaseMarketingVersionResolver.ValidateExactVersion(spec.Target.VersionString, "Review notes target version");
        AppStoreConnectClient.ValidateReviewNotes(spec.Notes);
    }

    /// <summary>Reads the exact version and hashes current and desired notes; never serializes contact details.</summary>
    public async Task<AppStoreConnectReviewNotesPlan> PlanAsync(
        AppStoreConnectReviewNotesSpec spec, CancellationToken cancellationToken = default)
    {
        ValidateSpec(spec);
        AppStoreConnectVersionInfo[] versions;
        try { versions = await _client.GetVersionsAsync(spec.Target.AppId.Trim(), spec.Target.VersionString.Trim(),
            spec.Target.Platform, cancellationToken: cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        { throw new InvalidOperationException("Could not observe the App Review target; provider response details were suppressed."); }
        var expectedPlatform = AppStoreConnectClient.ToAppStoreConnectPlatform(spec.Target.Platform);
        var version = versions.SingleOrDefault(value => value.VersionString == spec.Target.VersionString.Trim() &&
            value.Platform == expectedPlatform) ?? throw new InvalidOperationException("The exact review-notes target version was not found.");
        AppStoreConnectReviewDetailsInfo? details;
        try { details = await _client.GetReviewDetailsAsync(version.Id, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException)
        { throw new InvalidOperationException("Could not observe App Review notes; private provider response details were suppressed."); }
        if (details is null) throw new InvalidOperationException("The target has no App Review details. Set up its contact and demo-account settings first.");
        var plan = new AppStoreConnectReviewNotesPlan
        {
            AppId = spec.Target.AppId.Trim(), VersionString = version.VersionString!, Platform = spec.Target.Platform,
            VersionId = version.Id, ReviewDetailsId = details.Id,
            DesiredSha256 = Hash(spec.Notes), ObservedSha256 = Hash(details.Notes ?? string.Empty)
        };
        plan.IsConverged = plan.DesiredSha256 == plan.ObservedSha256;
        plan.BindingSha256 = Hash(JsonSerializer.Serialize(new[] { plan.AppId, plan.VersionString, plan.Platform.ToString(),
            plan.VersionId, plan.ReviewDetailsId, plan.DesiredSha256, plan.ObservedSha256 }));
        return plan;
    }

    /// <summary>Rechecks a reviewed plan, patches only notes, and verifies their exact readback.</summary>
    public async Task<AppStoreConnectReviewNotesPlan> ApplyAsync(AppStoreConnectReviewNotesSpec spec,
        AppStoreConnectReviewNotesPlan reviewedPlan, bool confirmApply, CancellationToken cancellationToken = default)
    {
        if (!confirmApply) throw new InvalidOperationException("Review-notes apply requires explicit confirmation.");
        if (reviewedPlan is null) throw new ArgumentNullException(nameof(reviewedPlan));
        ValidateSpec(spec);
        // Snapshot the declaration so a caller cannot change it across the asynchronous preflight.
        var desired = new AppStoreConnectReviewNotesSpec { SchemaVersion = spec.SchemaVersion, Notes = spec.Notes,
            Target = new() { AppId = spec.Target.AppId, VersionString = spec.Target.VersionString, Platform = spec.Target.Platform } };
        var current = await PlanAsync(desired, cancellationToken).ConfigureAwait(false);
        if (current.BindingSha256 != reviewedPlan.BindingSha256)
            throw new InvalidOperationException("Review notes or target changed after review. Generate and review a new plan.");
        if (current.IsConverged) return current;
        try { await _client.UpdateReviewNotesAsync(current.ReviewDetailsId, desired.Notes, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // An ambiguous response may have applied. Observe once; never repeat the patch blindly.
            var observed = await ObserveAfterUpdateAsync(desired, cancellationToken).ConfigureAwait(false);
            if (observed.IsConverged) return observed;
            throw new InvalidOperationException("App Review notes did not converge. Provider details were suppressed; inspect Apple state and generate a new plan.");
        }
        var final = await ObserveAfterUpdateAsync(desired, cancellationToken).ConfigureAwait(false);
        if (!final.IsConverged) throw new InvalidOperationException("App Review notes did not match after update; inspect Apple state before retrying.");
        return final;
    }

    private async Task<AppStoreConnectReviewNotesPlan> ObserveAfterUpdateAsync(
        AppStoreConnectReviewNotesSpec desired, CancellationToken cancellationToken)
    {
        try { return await PlanAsync(desired, cancellationToken).ConfigureAwait(false); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "App Review notes update outcome is unconfirmed; inspect Apple state before generating a new plan.");
        }
    }

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }
}
