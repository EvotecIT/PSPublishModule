using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class CatalogUpdateService
{
    public async Task<CatalogUpdateReceipt> SubmitAsync(CatalogUpdateSpec profile, string profilePath,
        PowerForgeReleaseSpec release, string releaseConfigPath, string output, string channel,
        StoreSubmissionSpec? storeSpec, string? storeConfigPath, bool submit,
        CancellationToken cancellationToken = default, string? reservationKey = null, bool requireAuthentication = false)
    {
        RequireProfile(profile);
        if (channel is not ("winget" or "store" or "all"))
            throw new ArgumentException("Channel must be winget, store or all.");
        // Lock is held across preflight and mutation. A killed process releases it, but its Attempting receipt remains.
        using var fileLock = new FileStream(Path.Combine(output, "catalog-update.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var receipt = Read(output, profilePath, releaseConfigPath);
        var doWinget = channel is "winget" or "all";
        var doStore = channel is "store" or "all";
        PowerForgeWingetSubmissionPlan? wingetPlan = null;
        StoreSubmissionPlan? storePlan = null;
        if (doWinget && receipt.Winget.State != "Submitted")
        {
            RequirePrepared(receipt.Winget, "WinGet", reservationKey);
            var manifest = receipt.Files.Keys.Single(path => path.EndsWith(".installer.yaml", StringComparison.Ordinal));
            var manifests = new[] { new PowerForgeWingetManifestArtifact
            {
                PackageIdentifier = receipt.PackageIdentifier, PackageVersion = receipt.PackageVersion,
                ManifestPath = ContainedFile(output, manifest), ManifestDirectory = Path.GetDirectoryName(ContainedFile(output, manifest)),
                InstallerUrls = receipt.Artifacts.Select(artifact => artifact.WingetUrl).ToArray()
            } };
            var configDirectory = Path.GetDirectoryName(Path.GetFullPath(releaseConfigPath))!;
            var request = new PowerForgeReleaseRequest
            { SubmitWinget = submit || requireAuthentication, WingetSubmitMode = PowerForgeWingetSubmissionMode.Manifest };
            wingetPlan = submit || requireAuthentication
                ? await _winget.PlanAuthenticatedAsync(release.Winget!, manifests, configDirectory, request, cancellationToken).ConfigureAwait(false)
                : _winget.Plan(release.Winget!, manifests, configDirectory, request);
            _winget.ValidateManifestDirectory(Path.GetDirectoryName(ContainedFile(output, manifest))!);
        }
        if (doStore && receipt.Store.State != "Submitted")
        {
            RequirePrepared(receipt.Store, "Store", reservationKey);
            RequireStoreConfiguration(profile, storeSpec, storeConfigPath, output);
            if (receipt.Store.State == "Reserved" && receipt.Store.ConfigurationSha256 != Hash(storeConfigPath!))
                throw new InvalidOperationException("Store configuration changed after the archived reservation.");
            storePlan = _store.Plan(storeSpec!, storeConfigPath!, new StoreSubmissionRequest
            { TargetName = profile.StoreTargetName, Commit = true, WaitForCommit = false });
            if (!string.IsNullOrWhiteSpace(storePlan.SubmissionId))
                throw new InvalidOperationException("New catalog updates require a Store target without an existing submission ID. Use status to inspect an existing receipt.");
            if (submit || requireAuthentication)
            {
                var errors = _store.Validate(storeSpec!, storeConfigPath!, new StoreSubmissionRequest { TargetName = profile.StoreTargetName });
                if (errors.Length != 0) throw new InvalidOperationException(string.Join(" ", errors));
                await _store.RequireDesktopReadyAsync(storeSpec!, storePlan, cancellationToken).ConfigureAwait(false);
            }
        }
        // Qualify all selected remote inputs before the first catalog side effect.
        foreach (var artifact in receipt.Artifacts)
        {
            if (wingetPlan is not null) await _verifyDownload(artifact, false, cancellationToken).ConfigureAwait(false);
            if (storePlan is not null) await _verifyDownload(artifact, true, cancellationToken).ConfigureAwait(false);
        }
        if (!submit) return receipt;
        cancellationToken.ThrowIfCancellationRequested();
        if (wingetPlan is not null)
        {
            Begin(output, receipt, receipt.Winget, null);
            var result = _winget.Run(wingetPlan);
            if (!result.Succeeded)
                throw new InvalidOperationException("WinGet submission did not confirm success. Inspect wingetcreate/GitHub and reconcile this receipt before retrying.");
            receipt.Winget.State = "Submitted";
            receipt.Winget.Reference = result.Entries.Select(entry => Regex.Match(entry.StdOut,
                @"https://github\.com/microsoft/winget-pkgs/pull/[0-9]+", RegexOptions.CultureInvariant).Value)
                .FirstOrDefault(value => value.Length != 0);
            Save(output, receipt);
        }
        if (storePlan is not null)
        {
            Begin(output, receipt, receipt.Store, Hash(storeConfigPath!));
            var result = await _store.RunAsync(storeSpec!, storeConfigPath!, new StoreSubmissionRequest
            { TargetName = profile.StoreTargetName, Commit = true, WaitForCommit = false }, cancellationToken).ConfigureAwait(false);
            if (result.CommittedSubmission && !string.IsNullOrWhiteSpace(result.SubmissionId))
            {
                receipt.Store.State = "Submitted";
                receipt.Store.Reference = result.SubmissionId;
                receipt.Store.RemoteStatus = result.FinalStatus;
                Save(output, receipt);
            }
            else if (!result.DesktopPackageMutationStarted)
            {
                receipt.Store.State = "Prepared";
                Save(output, receipt);
            }
            if (!result.Succeeded)
                throw new InvalidOperationException("Store submission did not confirm success. Inspect Partner Center and reconcile this receipt before retrying.");
        }
        return receipt;
    }

    public async Task<CatalogUpdateReceipt> StatusAsync(CatalogUpdateSpec profile, string profilePath,
        string releaseConfigPath, string output, StoreSubmissionSpec? storeSpec, string? storeConfigPath,
        CancellationToken cancellationToken = default)
    {
        using var fileLock = new FileStream(Path.Combine(output, "catalog-update.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var receipt = Read(output, profilePath, releaseConfigPath);
        if (receipt.Winget.State == "Submitted" && !string.IsNullOrWhiteSpace(receipt.Winget.Reference))
            receipt.Winget.RemoteStatus = await _readWingetStatus(receipt.Winget.Reference!, cancellationToken).ConfigureAwait(false);
        if (receipt.Store.State == "Submitted" && !string.IsNullOrWhiteSpace(receipt.Store.Reference))
        {
            RequireStoreConfiguration(profile, storeSpec, storeConfigPath, output);
            if (Hash(storeConfigPath!) != receipt.Store.ConfigurationSha256)
                throw new InvalidOperationException("Store configuration changed since submission. Restore the configuration used for this receipt.");
            var plan = _store.Plan(storeSpec!, storeConfigPath!, new StoreSubmissionRequest
            { TargetName = profile.StoreTargetName, SubmissionId = receipt.Store.Reference, WaitForCommit = false });
            var result = await _store.GetDesktopStatusAsync(storeSpec!, plan, cancellationToken).ConfigureAwait(false);
            receipt.Store.RemoteStatus = result.FinalStatus;
        }
        Save(output, receipt);
        return receipt;
    }

    private static void RequireStoreConfiguration(CatalogUpdateSpec profile, StoreSubmissionSpec? spec, string? path, string output)
    {
        if (spec is null || string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(profile.StoreTargetName))
            throw new InvalidOperationException("Store submission requires StoreConfigPath (or --store-config) and StoreTargetName.");
        if (!string.IsNullOrWhiteSpace(spec.Authentication.ClientSecret) || !string.IsNullOrWhiteSpace(spec.Authentication.AccessToken))
            throw new InvalidOperationException("Catalog automation requires Store credentials through environment variables, not inline secrets.");
        var target = spec.Targets.Single(value => string.Equals(value.Name, profile.StoreTargetName, StringComparison.OrdinalIgnoreCase));
        if (target.Provider != StoreSubmissionProviderKind.DesktopInstaller)
            throw new InvalidOperationException("Catalog automation currently supports desktop MSI Store targets.");
        target.DesktopPackages = Array.Empty<StoreSubmissionDesktopPackage>();
        target.DesktopPackagesPath = Path.GetFullPath(Path.Combine(output, "desktop-packages.json"));
    }

    private static void RequirePrepared(CatalogChannelReceipt channel, string name, string? reservationKey = null)
    {
        if (channel.State == "Reserved" && !string.IsNullOrWhiteSpace(reservationKey) && channel.ReservationKey == reservationKey) return;
        if (channel.State != "Prepared")
            throw new InvalidOperationException(name + " has an uncertain previous submission attempt. Inspect the remote service before reconciling; automatic replay is disabled.");
    }

    private static void Begin(string output, CatalogUpdateReceipt receipt, CatalogChannelReceipt channel, string? configurationHash)
    {
        channel.State = "Attempting";
        channel.ReservationKey = null;
        channel.AttemptedUtc = DateTimeOffset.UtcNow;
        channel.ConfigurationSha256 = configurationHash;
        Save(output, receipt);
    }
}
