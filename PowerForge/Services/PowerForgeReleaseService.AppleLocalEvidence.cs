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
                EnsurePathWithinProjectRoot(plan.ProjectRoot, app.ArchivePath, "Configured Apple archive");
                if (!File.Exists(app.ArchivePath) && !Directory.Exists(app.ArchivePath))
                    continue;
                var evidence = receipts.Where(receipt => !receipt.PlanOnly && receipt.SchemaVersion >= 4 &&
                        !string.IsNullOrWhiteSpace(receipt.ReceiptSha256) &&
                        AppleSourceCommitEvidenceMatches(receipt.SourceCommit, plan.SourceCommit))
                    .SelectMany(receipt => receipt.Targets.Where(target => IsMatchingAppleReceiptTargetIdentity(plan, app, target) &&
                            !string.IsNullOrWhiteSpace(target.ArchivePath) && IsSha256(target.ArchiveSha256))
                        .Select(target => new { Receipt = receipt, Target = target }))
                    .FirstOrDefault();
                if (evidence is null)
                    throw new InvalidOperationException($"The configured Apple archive for '{app.Name}' has no receipt bound to this source and target.");
                var receiptArchivePath = ResolveOutputPath(plan.ProjectRoot, evidence.Target.ArchivePath!);
                EnsurePathWithinProjectRoot(plan.ProjectRoot, receiptArchivePath, "Receipt-bound Apple archive");
                if (!AppleReleasePathsEqual(Path.GetFullPath(app.ArchivePath), receiptArchivePath, plan.ProjectRoot))
                    throw new InvalidOperationException($"The receipt archive path for '{app.Name}' differs from its configured archive.");
                AddLocalAppleArchiveFiles(app.ArchivePath, paths, links);
                var before = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(app.ArchivePath, "receipt-bound Apple archive");
                if (!string.Equals(before.Sha256, evidence.Target.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"The receipt-bound Apple archive for '{app.Name}' changed.");
                var after = AppleArchiveUploadSnapshot.CaptureCompleteIdentity(app.ArchivePath, "receipt-bound Apple archive");
                if (!before.Equals(after))
                    throw new InvalidOperationException($"The receipt-bound Apple archive for '{app.Name}' changed during evidence inspection.");
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
