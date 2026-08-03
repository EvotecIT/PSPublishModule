using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>Copies private App Review contact settings between exact versions without serializing the values.</summary>
public sealed class AppStoreConnectReviewDetailsCopyService
{
    private static readonly HashSet<string> SafeProviderErrorCodes = new(StringComparer.Ordinal)
    {
        "ENTITY_ERROR.ATTRIBUTE.INVALID",
        "ENTITY_ERROR.ATTRIBUTE.REQUIRED",
        "ENTITY_ERROR.ATTRIBUTE.UNMODIFIABLE",
        "ENTITY_ERROR.RELATIONSHIP.INVALID",
        "ENTITY_ERROR.RELATIONSHIP.REQUIRED"
    };

    private static readonly HashSet<string> SafeProviderErrorPointers = new(StringComparer.Ordinal)
    {
        "/data/attributes/platform",
        "/data/attributes/versionString",
        "/data/attributes/contactFirstName",
        "/data/attributes/contactLastName",
        "/data/attributes/contactPhone",
        "/data/attributes/contactEmail",
        "/data/attributes/demoAccountRequired",
        "/data/attributes/demoAccountName",
        "/data/attributes/demoAccountPassword",
        "/data/relationships/app",
        "/data/relationships/app/data",
        "/data/relationships/app/data/id",
        "/data/relationships/appStoreVersion",
        "/data/relationships/appStoreVersion/data",
        "/data/relationships/appStoreVersion/data/id"
    };

    private readonly AppStoreConnectClient _client;

    /// <summary>Creates the service.</summary>
    public AppStoreConnectReviewDetailsCopyService(AppStoreConnectClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>Creates a privacy-preserving plan for an exact source and target version.</summary>
    public async Task<AppStoreConnectReviewDetailsCopyPlan> PlanAsync(
        AppStoreConnectReviewDetailsCopySpec spec,
        CancellationToken cancellationToken = default)
    {
        ValidateSpec(spec);
        var sourceVersion = await ResolveRequiredVersionAsync(spec.Source, "source", cancellationToken).ConfigureAwait(false);
        var targetVersion = await ResolveTargetVersionAsync(spec, cancellationToken).ConfigureAwait(false);
        var source = await _client.GetReviewDetailsAsync(sourceVersion.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected source version has no App Review details to copy.");
        ValidateCompleteSource(source);
        var target = targetVersion is null
            ? null
            : await _client.GetReviewDetailsAsync(targetVersion.Id, cancellationToken).ConfigureAwait(false);
        var desiredFingerprint = ComputeDetailsFingerprint(source);
        var observedFingerprint = target is null ? null : ComputeDetailsFingerprint(target);
        var plan = new AppStoreConnectReviewDetailsCopyPlan
        {
            AppId = spec.Target.AppId.Trim(),
            VersionString = spec.Target.VersionString.Trim(),
            Platform = spec.Target.Platform,
            SourceVersionId = sourceVersion.Id,
            TargetVersionId = targetVersion?.Id,
            TargetVersionExists = targetVersion is not null,
            TargetExists = target is not null,
            DemoAccountRequired = source.DemoAccountRequired!.Value,
            IsConverged = targetVersion is not null && string.Equals(desiredFingerprint, observedFingerprint, StringComparison.Ordinal),
            DesiredFingerprint = desiredFingerprint,
            ObservedFingerprint = observedFingerprint,
            CheckedAtUtc = DateTimeOffset.UtcNow
        };
        plan.BindingSha256 = ComputePlanBinding(spec, plan);
        return plan;
    }

    /// <summary>Revalidates an approved plan and applies the exact contact copy.</summary>
    public async Task<AppStoreConnectReviewDetailsCopyResult> ApplyAsync(
        AppStoreConnectReviewDetailsCopySpec spec,
        AppStoreConnectReviewDetailsCopyPlan reviewedPlan,
        bool confirmApply,
        CancellationToken cancellationToken = default)
    {
        if (!confirmApply)
            throw new InvalidOperationException("App Review details apply requires explicit confirmation.");
        if (reviewedPlan is null || string.IsNullOrWhiteSpace(reviewedPlan.BindingSha256))
            throw new ArgumentException("A reviewed plan with an exact binding is required.", nameof(reviewedPlan));

        var current = await PlanAsync(spec, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(reviewedPlan.BindingSha256, current.BindingSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("App Review details changed after review. Generate and review a new plan before applying.");
        if (current.IsConverged)
        {
            return new AppStoreConnectReviewDetailsCopyResult
            {
                Success = true,
                InitialPlan = current,
                FinalPlan = current
            };
        }

        var source = await _client.GetReviewDetailsAsync(current.SourceVersionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected source App Review details disappeared after planning.");
        ValidateCompleteSource(source);
        if (!string.Equals(ComputeDetailsFingerprint(source), current.DesiredFingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("Source App Review details changed after review. Generate and review a new plan before applying.");

        var targetVersionId = current.TargetVersionId;
        var createdVersion = false;
        var operation = "revalidate-target";
        AppStoreConnectReviewDetailsInfo? existing = null;
        try
        {
            if (!current.TargetVersionExists)
            {
                operation = "create-target-version";
                var created = await _client.CreateVersionAsync(
                    spec.Target.AppId.Trim(),
                    spec.Target.VersionString.Trim(),
                    spec.Target.Platform,
                    cancellationToken).ConfigureAwait(false);
                targetVersionId = created.Id;
                createdVersion = true;
            }
            if (string.IsNullOrWhiteSpace(targetVersionId))
                throw new InvalidOperationException("The exact target App Store version could not be resolved for App Review details.");

            operation = "read-target-details";
            existing = await _client.GetReviewDetailsAsync(targetVersionId!, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(existing is null ? null : ComputeDetailsFingerprint(existing), current.ObservedFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("Target App Review details changed after review.");

            if (existing is null)
            {
                operation = "create-review-details";
                await _client.CreateReviewDetailsAsync(targetVersionId!, source, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                operation = "update-review-details";
                await _client.UpdateReviewDetailsAsync(existing.Id, source, cancellationToken).ConfigureAwait(false);
            }

            operation = "verify-final-state";
            var final = await PlanAsync(spec, cancellationToken).ConfigureAwait(false);
            return new AppStoreConnectReviewDetailsCopyResult
            {
                Success = final.IsConverged,
                Created = existing is null,
                CreatedVersion = createdVersion,
                Updated = existing is not null,
                ErrorCode = final.IsConverged ? null : "APPLE_REVIEW_DETAILS_NOT_CONVERGED",
                ErrorMessage = final.IsConverged
                    ? null
                    : "App Review details did not converge after mutation. Contact values were suppressed; generate a new Plan before retrying.",
                FailureOperation = final.IsConverged ? null : operation,
                InitialPlan = current,
                FinalPlan = final
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppStoreConnectReviewDetailsCopyPlan finalAfterFailure;
            try
            {
                finalAfterFailure = await PlanAsync(spec, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception replanError) when (replanError is not OperationCanceledException)
            {
                finalAfterFailure = current;
            }
            var providerError = ExtractProviderError(ex);
            var convergedAfterFailure = finalAfterFailure.IsConverged;
            return new AppStoreConnectReviewDetailsCopyResult
            {
                Success = convergedAfterFailure,
                CreatedVersion = createdVersion || (!current.TargetVersionExists && finalAfterFailure.TargetVersionExists),
                Created = existing is null && finalAfterFailure.TargetExists,
                Updated = existing is not null && finalAfterFailure.IsConverged,
                ErrorCode = convergedAfterFailure ? null : "APPLE_REVIEW_DETAILS_APPLY_FAILED",
                ErrorMessage = convergedAfterFailure
                    ? null
                    : "App Review details did not converge. Contact values and provider messages were suppressed; use the operation, status, codes, and pointers in this receipt before retrying.",
                FailureOperation = convergedAfterFailure ? null : operation,
                ProviderStatusCode = convergedAfterFailure ? null : providerError.StatusCode,
                ProviderErrorCodes = convergedAfterFailure ? Array.Empty<string>() : providerError.Codes,
                ProviderErrorPointers = convergedAfterFailure ? Array.Empty<string>() : providerError.Pointers,
                InitialPlan = current,
                FinalPlan = finalAfterFailure
            };
        }
    }

    private async Task<AppStoreConnectVersionInfo> ResolveRequiredVersionAsync(
        AppStoreConnectReviewDetailsVersionRef reference,
        string role,
        CancellationToken cancellationToken)
    {
        var matches = await _client.GetVersionsAsync(
            reference.AppId.Trim(),
            reference.VersionString.Trim(),
            reference.Platform,
            limit: 10,
            cancellationToken).ConfigureAwait(false);
        if (matches.Length != 1)
            throw new InvalidOperationException($"The exact {role} App Store version resolved to {matches.Length} records; expected one.");
        return matches[0];
    }

    private async Task<AppStoreConnectVersionInfo?> ResolveTargetVersionAsync(
        AppStoreConnectReviewDetailsCopySpec spec,
        CancellationToken cancellationToken)
    {
        var matches = await _client.GetVersionsAsync(
            spec.Target.AppId.Trim(),
            spec.Target.VersionString.Trim(),
            spec.Target.Platform,
            limit: 10,
            cancellationToken).ConfigureAwait(false);
        if (matches.Length > 1)
            throw new InvalidOperationException($"The exact target App Store version resolved to {matches.Length} records; expected at most one.");
        if (matches.Length == 0 && !spec.CreateTargetVersion)
            throw new InvalidOperationException("The exact target App Store version does not exist and createTargetVersion is false.");
        return matches.SingleOrDefault();
    }

    private static void ValidateSpec(AppStoreConnectReviewDetailsCopySpec? spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));
        if (spec.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported App Review details config schemaVersion '{spec.SchemaVersion}'.");
        ValidateReference(spec.Source, "source");
        ValidateReference(spec.Target, "target");
        if (string.Equals(spec.Source.AppId.Trim(), spec.Target.AppId.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(spec.Source.VersionString.Trim(), spec.Target.VersionString.Trim(), StringComparison.OrdinalIgnoreCase) &&
            spec.Source.Platform == spec.Target.Platform)
            throw new InvalidOperationException("Source and target App Review versions must be different.");
    }

    private static void ValidateReference(AppStoreConnectReviewDetailsVersionRef? reference, string role)
    {
        if (reference is null || string.IsNullOrWhiteSpace(reference.AppId) || string.IsNullOrWhiteSpace(reference.VersionString))
            throw new InvalidOperationException($"App Review details {role} requires appId and versionString.");
    }

    private static void ValidateCompleteSource(AppStoreConnectReviewDetailsInfo source)
    {
        if (new[] { source.ContactFirstName, source.ContactLastName, source.ContactPhone, source.ContactEmail }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("The selected source App Review contact is incomplete.");
        if (!source.DemoAccountRequired.HasValue)
            throw new InvalidOperationException("The selected source does not declare whether App Review needs a demo account.");
        if (source.DemoAccountRequired == true &&
            (string.IsNullOrWhiteSpace(source.DemoAccountName) || string.IsNullOrWhiteSpace(source.DemoAccountPassword)))
            throw new InvalidOperationException("The selected source requires a demo account but its credentials are incomplete.");
    }

    private static string ComputeDetailsFingerprint(AppStoreConnectReviewDetailsInfo details)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            details.ContactFirstName.Trim(),
            details.ContactLastName.Trim(),
            details.ContactPhone.Trim(),
            details.ContactEmail.Trim().ToLowerInvariant(),
            details.DemoAccountRequired.HasValue ? (details.DemoAccountRequired.Value ? "true" : "false") : "<undeclared>",
            details.DemoAccountRequired == true ? details.DemoAccountName?.Trim() ?? string.Empty : string.Empty,
            details.DemoAccountRequired == true ? details.DemoAccountPassword ?? string.Empty : string.Empty
        });
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(canonical)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string ComputePlanBinding(
        AppStoreConnectReviewDetailsCopySpec spec,
        AppStoreConnectReviewDetailsCopyPlan plan)
    {
        var binding = string.Join("\n", new[]
        {
            spec.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            spec.Source.AppId.Trim(), spec.Source.VersionString.Trim(), spec.Source.Platform.ToString(),
            spec.Target.AppId.Trim(), spec.Target.VersionString.Trim(), spec.Target.Platform.ToString(),
            spec.CreateTargetVersion ? "create-target" : "require-target",
            plan.SourceVersionId, plan.TargetVersionId ?? "<missing>", plan.DesiredFingerprint, plan.ObservedFingerprint ?? "<missing>"
        });
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(binding))).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static ProviderErrorSummary ExtractProviderError(Exception exception)
    {
        var message = exception.Message ?? string.Empty;
        int? statusCode = null;
        var status = Regex.Match(message, @"failed \((?<status>[0-9]{3})(?:\s|\))", RegexOptions.CultureInvariant);
        if (status.Success && int.TryParse(status.Groups["status"].Value, out var parsedStatus))
            statusCode = parsedStatus;

        var codes = new HashSet<string>(StringComparer.Ordinal);
        var pointers = new HashSet<string>(StringComparer.Ordinal);
        var jsonStart = message.IndexOf('{');
        if (jsonStart >= 0)
        {
            try
            {
                using var document = JsonDocument.Parse(message.Substring(jsonStart));
                if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                {
                    foreach (var error in errors.EnumerateArray())
                    {
                        AddAllowlistedValue(error, "code", codes, SafeProviderErrorCodes);
                        if (error.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
                            AddAllowlistedValue(source, "pointer", pointers, SafeProviderErrorPointers);
                    }
                }
            }
            catch (JsonException)
            {
                // Provider messages are never copied when the structured response cannot be parsed safely.
            }
        }

        return new ProviderErrorSummary(
            statusCode,
            codes.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray(),
            pointers.OrderBy(static value => value, StringComparer.Ordinal).Take(10).ToArray());
    }

    private static void AddAllowlistedValue(
        JsonElement element,
        string propertyName,
        HashSet<string> destination,
        HashSet<string> allowlist)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return;
        var value = property.GetString();
        if (!string.IsNullOrWhiteSpace(value) && allowlist.Contains(value!))
            destination.Add(value!);
    }

    private sealed class ProviderErrorSummary
    {
        public ProviderErrorSummary(int? statusCode, string[] codes, string[] pointers)
        {
            StatusCode = statusCode;
            Codes = codes;
            Pointers = pointers;
        }

        public int? StatusCode { get; }

        public string[] Codes { get; }

        public string[] Pointers { get; }
    }
}
