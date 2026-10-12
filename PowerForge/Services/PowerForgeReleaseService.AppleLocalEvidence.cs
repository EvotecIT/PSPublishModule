namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    /// <summary>
    /// Validates existing local release evidence without executing a release action,
    /// loading Apple authentication, generating projects, or creating runtime files.
    /// The caller retains the normal checkout cleanliness and qualification gates.
    /// </summary>
    internal AppleReleaseLocalEvidence ValidateLocalAppleEvidence(string configPath, string sourceCommit)
    {
        var phase = AppleReleaseLocalEvidenceRejectionPhase.ConfigurationValidation;
        try
        {
            var path = Path.GetFullPath(configPath);
            var spec = LoadConfiguration(path);
            var plan = PrepareLocalAppleEvidencePlan(spec, path, sourceCommit);
            phase = AppleReleaseLocalEvidenceRejectionPhase.SourceValidation;
            if (!GitObjectId.IsFull(sourceCommit))
                throw new InvalidOperationException("Local Apple evidence requires a full Git source commit.");
            var observed = new HomeAssistantReleaseGitService(GitClient.CreateTrustedSystemClient()).GetHeadSha(plan.ProjectRoot);
            if (!string.Equals(observed, sourceCommit, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Local Apple evidence source commit does not match the current checkout.");
            return ValidateLocalAppleEvidence(plan);
        }
        catch (Exception exception)
        {
            return RejectLocalAppleEvidence(sourceCommit, phase, exception.Message);
        }
    }

    private static PowerForgeAppleReleasePlan PrepareLocalAppleEvidencePlan(
        PowerForgeReleaseSpec spec,
        string configPath,
        string sourceCommit)
    {
        var options = spec.AppleApps ?? throw new InvalidOperationException("The release configuration does not contain AppleApps.");
        var root = ResolveOutputPath(Path.GetDirectoryName(configPath)!, string.IsNullOrWhiteSpace(options.ProjectRoot) ? "." : options.ProjectRoot!);
        EnsureInputFileWithinProjectRoot(root, configPath, "Apple release configuration");
        var archiveRoot = ResolveOutputPath(root, string.IsNullOrWhiteSpace(options.ArchiveRoot) ? Path.Combine("Artifacts", "Apple", "Archives") : options.ArchiveRoot!);
        var exportRoot = ResolveOutputPath(root, string.IsNullOrWhiteSpace(options.ExportRoot) ? Path.Combine("Artifacts", "Apple", "Exports") : options.ExportRoot!);
        var configuration = NormalizeAppleConfiguration(options.Configuration) ?? "Release";
        var apps = (options.Apps ?? Array.Empty<AppleAppConfiguration>()).Where(static app => app.Enabled)
            .Select(app => PrepareAppleAppPlan(app, options, root, archiveRoot, exportRoot, configuration,
                sharedReleaseVersion: null, allowUnresolvedResolvedVersion: true, allowMissingProject: true))
            .ToArray();
        if (apps.Length == 0 || apps.GroupBy(static app => app.Name, StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() > 1))
            throw new InvalidOperationException("Local Apple evidence requires enabled targets with unique names.");
        ValidateAppleProductTopology(apps);
        ValidateUniqueAppleArtifactPaths(apps, static app => app.ArchivePath, "archive");
        var automation = options.Automation ?? new PowerForgeAppleReleaseAutomationOptions();
        return new PowerForgeAppleReleasePlan
        {
            ProjectRoot = root,
            SourceCommit = sourceCommit,
            Configuration = configuration,
            Apps = apps,
            ReceiptPath = ResolveOutputPath(root, automation.ReceiptPath),
            ReceiptHistoryPath = ResolveOutputPath(root, automation.ReceiptHistoryPath)
        };
    }

    /// <summary>Inspects an already resolved local target plan; does not confer source qualification.</summary>
    internal AppleReleaseLocalEvidence ValidateLocalAppleEvidence(PowerForgeAppleReleasePlan plan)
    {
        var phase = AppleReleaseLocalEvidenceRejectionPhase.SourceValidation;
        try
        {
            if (!GitObjectId.IsFull(plan.SourceCommit))
                throw new InvalidOperationException("Local Apple evidence requires a full Git source commit.");
            phase = AppleReleaseLocalEvidenceRejectionPhase.JournalLockValidation;
            using var journal = AppleReleaseReceiptJournalLease.InspectExisting(plan);
            phase = AppleReleaseLocalEvidenceRejectionPhase.ReceiptValidation;
            var receipts = _appleReceiptStore.ReadAll(plan, out var receiptPaths);
            var paths = new List<string>(receiptPaths);
            var links = new List<AppleArtifactSymbolicLink>();
            paths.AddRange(journal.Paths);
            foreach (var app in plan.Apps)
            {
                phase = AppleReleaseLocalEvidenceRejectionPhase.ArchiveValidation;
                AddLocalAppleArtifactEvidence(plan, app, receipts, app.ArchivePath,
                    static target => target.ArchivePath, static target => target.ArchiveSha256, paths, links);
                if (UsesAppStoreConnect(app))
                    AddLocalAppleArtifactEvidence(plan, app, receipts, app.ExportPath,
                        static target => target.UploadExportPath, static target => target.UploadExportSha256, paths, links);
            }
            return new AppleReleaseLocalEvidence
            {
                Success = true,
                SourceCommit = plan.SourceCommit!,
                EvidencePaths = paths.Distinct(StringComparer.Ordinal).OrderBy(static path => path, StringComparer.Ordinal).ToArray(),
                ArchiveLinks = links.OrderBy(static link => link.Path, StringComparer.Ordinal).ToArray()
            };
        }
        catch (Exception exception)
        {
            return RejectLocalAppleEvidence(plan.SourceCommit, phase, exception.Message);
        }
    }

    private static (string? Path, string? Sha256) CaptureAppleUploadExportEvidence(
        PowerForgeAppleReleasePlan plan, PowerForgeAppleAppReleaseTargetPlan app, PowerForgeAppleAppReleaseResult? result)
    {
        if (!UsesAppStoreConnect(app) || result?.Upload is null || !Directory.Exists(app.ExportPath)) return (null, null);
        EnsurePathWithinProjectRoot(plan.ProjectRoot, app.ExportPath, "Apple upload export");
        return (FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ExportPath).Replace('\\', '/'),
            AppleArchiveUploadSnapshot.CaptureCompleteIdentity(app.ExportPath, "Apple upload export").Sha256);
    }

    private static void AddLocalAppleArtifactEvidence(PowerForgeAppleReleasePlan plan, PowerForgeAppleAppReleaseTargetPlan app,
        IReadOnlyCollection<PowerForgeAppleReleaseReceipt> receipts, string artifactPath,
        Func<PowerForgeAppleReleaseTargetReceipt, string?> receiptPath, Func<PowerForgeAppleReleaseTargetReceipt, string?> receiptSha256,
        ICollection<string> paths, ICollection<AppleArtifactSymbolicLink> links)
    {
        if (string.IsNullOrWhiteSpace(artifactPath)) return;
        EnsurePathWithinProjectRoot(plan.ProjectRoot, artifactPath, "Configured Apple artifact");
        if (!File.Exists(artifactPath) && !Directory.Exists(artifactPath)) return;
        var before = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(artifactPath, "receipt-bound Apple artifact");
        var candidates = receipts.Where(receipt => !receipt.PlanOnly && receipt.SchemaVersion >= 4 &&
                !string.IsNullOrWhiteSpace(receipt.ReceiptSha256) && GitObjectId.IsFull(receipt.SourceCommit))
            .SelectMany(receipt => receipt.Targets.Where(target => IsMatchingAppleReceiptTargetIdentity(plan, app, target) &&
                    !string.IsNullOrWhiteSpace(receiptPath(target)) && IsSha256(receiptSha256(target)))
                .Select(target => new { Receipt = receipt, Target = target }))
            .OrderByDescending(value => AppleSourceCommitEvidenceMatches(value.Receipt.SourceCommit, plan.SourceCommit));
        var matched = false;
        foreach (var candidate in candidates)
        {
            try
            {
                var boundPath = ResolveOutputPath(plan.ProjectRoot, receiptPath(candidate.Target)!);
                EnsurePathWithinProjectRoot(plan.ProjectRoot, boundPath, "Receipt-bound Apple artifact");
                if (!AppleReleasePathsEqual(Path.GetFullPath(artifactPath), boundPath, plan.ProjectRoot) ||
                    !string.Equals(before.Sha256, receiptSha256(candidate.Target), StringComparison.OrdinalIgnoreCase)) continue;
                // Historical artifacts are inert evidence. Execution/recovery
                // still requires exact-source equality and never reuses an export.
                if (!AppleSourceCommitEvidenceMatches(candidate.Receipt.SourceCommit, plan.SourceCommit))
                    new HomeAssistantReleaseGitService(GitClient.CreateTrustedSystemClient()).EnsureContainsMerge(plan.ProjectRoot, candidate.Receipt.SourceCommit!);
                matched = true;
                break;
            }
            catch (InvalidOperationException) { /* A different valid receipt may bind this artifact. */ }
        }
        if (!matched) throw new InvalidOperationException($"The configured Apple artifact for '{app.Name}' has no receipt bound to its verified source ancestry, target, path and bytes.");
        AddLocalAppleArchiveFiles(artifactPath, paths, links);
        var after = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(artifactPath, "receipt-bound Apple artifact");
        if (!before.Equals(after)) throw new InvalidOperationException($"The receipt-bound Apple artifact for '{app.Name}' changed during evidence inspection.");
    }

    private static void AddLocalAppleArchiveFiles(
        string archivePath,
        ICollection<string> paths,
        ICollection<AppleArtifactSymbolicLink> links)
    {
        var pending = new Stack<string>();
        pending.Push(archivePath);
        while (pending.Count > 0)
        {
            var path = pending.Pop();
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                links.Add(AppleArtifactCopy.InspectContainedSymbolicLink(archivePath, path));
                continue;
            }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(path))
                    pending.Push(entry);
            }
            else
                paths.Add(Path.GetFullPath(path));
        }
    }

    private static AppleReleaseLocalEvidence RejectLocalAppleEvidence(
        string? sourceCommit,
        AppleReleaseLocalEvidenceRejectionPhase phase,
        string message)
        => new() { SourceCommit = sourceCommit ?? string.Empty, RejectionPhase = phase, ErrorMessage = message };
}
