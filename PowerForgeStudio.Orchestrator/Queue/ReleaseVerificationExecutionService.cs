using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Verification;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed class ReleaseVerificationExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PowerShellCommandRunner _powerShellCommandRunner = new();
    private readonly Func<string, string, CancellationToken, Task<PowerShellExecutionResult>> _runPowerShellAsync;

    public ReleaseVerificationExecutionService()
        : this(new HttpClient(new HttpClientHandler {
            AllowAutoRedirect = true
        }) {
            Timeout = TimeSpan.FromSeconds(20)
        }, null)
    {
    }

    internal ReleaseVerificationExecutionService(
        HttpClient httpClient,
        Func<string, string, CancellationToken, Task<PowerShellExecutionResult>>? runPowerShellAsync)
    {
        _httpClient = httpClient;
        _runPowerShellAsync = runPowerShellAsync ?? ((workingDirectory, script, cancellationToken) => _powerShellCommandRunner.RunCommandAsync(workingDirectory, script, cancellationToken));
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForgeStudio/0.1");
        }
    }

    public IReadOnlyList<ReleaseVerificationTarget> BuildPendingTargets(IEnumerable<ReleaseQueueItem> queueItems)
    {
        ArgumentNullException.ThrowIfNull(queueItems);

        var targets = new List<ReleaseVerificationTarget>();
        foreach (var item in queueItems.Where(candidate => candidate.Stage == ReleaseQueueStage.Verify && candidate.Status == ReleaseQueueItemStatus.ReadyToRun))
        {
            var publishResult = TryDeserializePublishResult(item);
            if (publishResult is null)
            {
                continue;
            }

            foreach (var receipt in publishResult.Receipts)
            {
                targets.Add(new ReleaseVerificationTarget(
                    RootPath: receipt.RootPath,
                    RepositoryName: receipt.RepositoryName,
                    AdapterKind: receipt.AdapterKind,
                    TargetName: receipt.TargetName,
                    TargetKind: receipt.TargetKind,
                    Destination: receipt.Destination,
                    SourcePath: receipt.SourcePath));
            }
        }

        return targets
            .DistinctBy(target => $"{target.RootPath}|{target.AdapterKind}|{target.TargetName}|{target.TargetKind}", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ReleaseVerificationExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueItem);

        var publishResult = TryDeserializePublishResult(queueItem);
        if (publishResult is null)
        {
            return new ReleaseVerificationExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Verification checkpoint could not be read from queue state.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    FailedReceipt(queueItem.RootPath, queueItem.RepositoryName, "Verify", "Queue checkpoint", null, "Queue state is missing the publish checkpoint.", "Checkpoint")
                ]);
        }

        if (publishResult.Receipts.Count == 0)
        {
            return new ReleaseVerificationExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Verification cannot run because no publish receipts were captured.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    FailedReceipt(queueItem.RootPath, queueItem.RepositoryName, "Verify", "Publish receipts", null, "No publish receipts were captured for verification.", "Publish")
                ]);
        }

        var receipts = new List<ReleaseVerificationReceipt>(publishResult.Receipts.Count);
        foreach (var receipt in publishResult.Receipts)
        {
            receipts.Add(await VerifyReceiptAsync(receipt, cancellationToken));
        }

        var verified = receipts.Count(receipt => receipt.Status == ReleaseVerificationReceiptStatus.Verified);
        var skipped = receipts.Count(receipt => receipt.Status == ReleaseVerificationReceiptStatus.Skipped);
        var failed = receipts.Count(receipt => receipt.Status == ReleaseVerificationReceiptStatus.Failed);
        var summary = failed > 0
            ? $"Verification completed with {verified} verified, {skipped} skipped, and {failed} failed check(s)."
            : $"Verification completed with {verified} verified and {skipped} skipped check(s).";

        return new ReleaseVerificationExecutionResult(
            RootPath: queueItem.RootPath,
            Succeeded: failed == 0,
            Summary: summary,
            SourceCheckpointStateJson: queueItem.CheckpointStateJson,
            Receipts: receipts);
    }

    private async Task<ReleaseVerificationReceipt> VerifyReceiptAsync(ReleasePublishReceipt publishReceipt, CancellationToken cancellationToken)
    {
        if (publishReceipt.Status != ReleasePublishReceiptStatus.Published)
        {
            return FailedReceipt(
                publishReceipt.RootPath,
                publishReceipt.RepositoryName,
                publishReceipt.AdapterKind,
                publishReceipt.TargetName,
                publishReceipt.Destination,
                $"Publish receipt status was {publishReceipt.Status}; verification cannot pass.",
                publishReceipt.TargetKind);
        }

        return publishReceipt.TargetKind switch
        {
            "GitHub" => await VerifyGitHubAsync(publishReceipt, cancellationToken),
            "NuGet" => await VerifyNuGetAsync(publishReceipt, cancellationToken),
            "PowerShellRepository" => await VerifyPowerShellRepositoryAsync(publishReceipt, cancellationToken),
            _ => SkippedReceipt(publishReceipt, $"Verification is not implemented for {publishReceipt.TargetKind} targets yet.")
        };
    }

    private async Task<ReleaseVerificationReceipt> VerifyGitHubAsync(ReleasePublishReceipt publishReceipt, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(publishReceipt.Destination, UriKind.Absolute, out var uri))
        {
            return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, "GitHub destination URL was not recorded.", publishReceipt.TargetKind);
        }

        var response = await SendProbeAsync(uri, cancellationToken);
        if (response is null)
        {
            return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, "GitHub release probe did not return a success status.", publishReceipt.TargetKind);
        }

        return VerifiedReceipt(publishReceipt, $"GitHub release probe succeeded with {(int)response.StatusCode} {response.StatusCode}.");
    }

    private async Task<ReleaseVerificationReceipt> VerifyNuGetAsync(ReleasePublishReceipt publishReceipt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publishReceipt.SourcePath) || !File.Exists(publishReceipt.SourcePath))
        {
            return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, "NuGet package path is missing or no longer exists locally.", publishReceipt.TargetKind);
        }

        if (string.IsNullOrWhiteSpace(publishReceipt.Destination))
        {
            return SkippedReceipt(publishReceipt, "NuGet destination URL was not recorded, so remote verification was skipped.");
        }

        var identity = TryReadPackageIdentity(publishReceipt.SourcePath);
        if (identity is null)
        {
            return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, "NuGet package identity could not be read from the .nupkg.", publishReceipt.TargetKind);
        }

        var probeUri = await ResolveNuGetPackageProbeUriAsync(publishReceipt.Destination, identity, cancellationToken);
        if (probeUri is null)
        {
            return SkippedReceipt(publishReceipt, $"PowerForgeStudio could not derive a probeable package endpoint from {publishReceipt.Destination}.");
        }

        var response = await SendProbeAsync(probeUri, cancellationToken);
        if (response is null)
        {
            return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, $"Package probe failed for {identity.Id} {identity.Version} against {probeUri.Host}.", publishReceipt.TargetKind);
        }

        return VerifiedReceipt(publishReceipt, $"Package probe succeeded for {identity.Id} {identity.Version} against {probeUri.Host}.");
    }

    private async Task<ReleaseVerificationReceipt> VerifyPowerShellRepositoryAsync(ReleasePublishReceipt publishReceipt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publishReceipt.SourcePath) || !Directory.Exists(publishReceipt.SourcePath))
        {
            return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, "Module package path is missing or no longer exists locally.", publishReceipt.TargetKind);
        }

        var manifestPath = Directory.EnumerateFiles(publishReceipt.SourcePath, "*.psd1", SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}en-US{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, "Module manifest was not found for PSGallery verification.", publishReceipt.TargetKind);
        }

        var moduleInfo = await ReadModuleManifestAsync(publishReceipt.RootPath, manifestPath, cancellationToken);
        if (moduleInfo is null)
        {
            return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, "Module manifest could not be read for PSGallery verification.", publishReceipt.TargetKind);
        }

        var destination = publishReceipt.Destination ?? "PSGallery";
        if (destination.Equals("PSGallery", StringComparison.OrdinalIgnoreCase))
        {
            var galleryVersion = moduleInfo.VersionWithPreRelease;
            var url = new Uri($"https://www.powershellgallery.com/packages/{moduleInfo.ModuleName}/{galleryVersion}");
            var galleryResponse = await SendProbeAsync(url, cancellationToken);
            if (galleryResponse is null)
            {
                return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, $"PSGallery probe failed for {moduleInfo.ModuleName} {galleryVersion}.", publishReceipt.TargetKind);
            }

            return VerifiedReceipt(publishReceipt, $"PSGallery probe succeeded for {moduleInfo.ModuleName} {galleryVersion}.");
        }

        var resolvedRepository = await ResolvePowerShellRepositoryAsync(publishReceipt.RootPath, destination, cancellationToken);
        if (resolvedRepository is null)
        {
            return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, $"PowerShell repository '{destination}' could not be resolved to a probeable endpoint.", publishReceipt.TargetKind);
        }

        var probeUri = await ResolveNuGetPackageProbeUriAsync(
            resolvedRepository.SourceUri ?? resolvedRepository.PublishUri ?? destination,
            new NuGetPackageIdentity(moduleInfo.ModuleName, moduleInfo.VersionWithPreRelease),
            cancellationToken);
        if (probeUri is null)
        {
            return SkippedReceipt(publishReceipt, $"PowerForgeStudio could not derive a probeable package endpoint from {resolvedRepository.DisplaySource}.");
        }

        var probeResponse = await SendProbeAsync(probeUri, cancellationToken);
        if (probeResponse is null)
        {
            return FailedReceipt(publishReceipt.RootPath, publishReceipt.RepositoryName, publishReceipt.AdapterKind, publishReceipt.TargetName, publishReceipt.Destination, $"Repository probe failed for {moduleInfo.ModuleName} {moduleInfo.VersionWithPreRelease} against {probeUri.Host}.", publishReceipt.TargetKind);
        }

        return VerifiedReceipt(publishReceipt, $"Repository probe succeeded for {moduleInfo.ModuleName} {moduleInfo.VersionWithPreRelease} against {probeUri.Host}.");
    }

    private async Task<Uri?> ResolveNuGetPackageProbeUriAsync(string destination, NuGetPackageIdentity identity, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(destination, UriKind.Absolute, out var destinationUri))
        {
            return null;
        }

        if (destinationUri.Host.Contains("nuget.org", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFlatContainerPackageUri(new Uri("https://api.nuget.org/v3-flatcontainer/"), identity);
        }

        if (destinationUri.AbsolutePath.Contains("/v3-flatcontainer", StringComparison.OrdinalIgnoreCase) ||
            destinationUri.AbsolutePath.Contains("/flatcontainer", StringComparison.OrdinalIgnoreCase))
        {
            return BuildFlatContainerPackageUri(destinationUri, identity);
        }

        if (destinationUri.AbsolutePath.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase) ||
            destinationUri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var packageBaseUri = await ResolvePackageBaseAddressAsync(destinationUri, cancellationToken);
            return packageBaseUri is null ? null : BuildFlatContainerPackageUri(packageBaseUri, identity);
        }

        return null;
    }

    private async Task<Uri?> ResolvePackageBaseAddressAsync(Uri serviceIndexUri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, serviceIndexUri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode >= 400)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var resource in resources.EnumerateArray())
            {
                if (!resource.TryGetProperty("@type", out var typeElement) ||
                    typeElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeElement.GetString();
                if (string.IsNullOrWhiteSpace(type) ||
                    !type.StartsWith("PackageBaseAddress/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!resource.TryGetProperty("@id", out var idElement) ||
                    idElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var id = idElement.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (Uri.TryCreate(serviceIndexUri, id, out var resolved))
                {
                    return resolved;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task<ResolvedPowerShellRepository?> ResolvePowerShellRepositoryAsync(string repositoryRoot, string repositoryName, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(repositoryName, UriKind.Absolute, out var directUri))
        {
            return new ResolvedPowerShellRepository(
                repositoryName,
                directUri.AbsoluteUri,
                null);
        }

        var script = string.Join(Environment.NewLine, new[] {
            "$ErrorActionPreference = 'Stop'",
            $"$name = {QuoteLiteral(repositoryName)}",
            "$psResourceRepo = Get-Command -Name Get-PSResourceRepository -ErrorAction SilentlyContinue",
            "if ($null -ne $psResourceRepo) {",
            "  $repo = Get-PSResourceRepository -Name $name -ErrorAction SilentlyContinue | Select-Object -First 1",
            "  if ($null -ne $repo) {",
            "    @{ Name = $repo.Name; SourceUri = $repo.Uri; PublishUri = $repo.PublishUri } | ConvertTo-Json -Compress",
            "    exit 0",
            "  }",
            "}",
            "$psRepo = Get-PSRepository -Name $name -ErrorAction SilentlyContinue | Select-Object -First 1",
            "if ($null -ne $psRepo) {",
            "  @{ Name = $psRepo.Name; SourceUri = $psRepo.SourceLocation; PublishUri = $psRepo.PublishLocation } | ConvertTo-Json -Compress",
            "  exit 0",
            "}",
            "exit 1"
        });

        var execution = await _runPowerShellAsync(repositoryRoot, script, cancellationToken);
        if (execution.ExitCode != 0 || string.IsNullOrWhiteSpace(execution.StandardOutput))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ResolvedPowerShellRepository>(execution.StandardOutput.Trim(), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static Uri BuildFlatContainerPackageUri(Uri baseUri, NuGetPackageIdentity identity)
    {
        var builder = new StringBuilder(baseUri.AbsoluteUri.TrimEnd('/'));
        builder.Append('/');
        builder.Append(Uri.EscapeDataString(identity.Id.ToLowerInvariant()));
        builder.Append('/');
        builder.Append(Uri.EscapeDataString(identity.Version.ToLowerInvariant()));
        builder.Append('/');
        builder.Append(Uri.EscapeDataString(identity.Id.ToLowerInvariant()));
        builder.Append('.');
        builder.Append(Uri.EscapeDataString(identity.Version.ToLowerInvariant()));
        builder.Append(".nupkg");
        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private async Task<HttpResponseMessage?> SendProbeAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, uri);
        try
        {
            var headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)headResponse.StatusCode < 400)
            {
                return headResponse;
            }
        }
        catch
        {
            // Fall back to GET.
        }

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        try
        {
            var getResponse = await _httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return (int)getResponse.StatusCode < 400 ? getResponse : null;
        }
        catch
        {
            return null;
        }
    }

    private static NuGetPackageIdentity? TryReadPackageIdentity(string packagePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var nuspecEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            if (nuspecEntry is null)
            {
                return null;
            }

            using var stream = nuspecEntry.Open();
            using var reader = new StreamReader(stream);
            var xml = System.Xml.Linq.XDocument.Load(reader);
            var metadata = xml.Root?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase));
            var id = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))?.Value;
            var version = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("version", StringComparison.OrdinalIgnoreCase))?.Value;
            return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version)
                ? null
                : new NuGetPackageIdentity(id.Trim(), version.Trim());
        }
        catch
        {
            return null;
        }
    }

    private async Task<ModuleManifestInfo?> ReadModuleManifestAsync(string repositoryRoot, string manifestPath, CancellationToken cancellationToken)
    {
        var script = string.Join("; ", new[] {
            "$ErrorActionPreference = 'Stop'",
            $"$manifest = Import-PowerShellDataFile -Path {QuoteLiteral(manifestPath)}",
            "$preRelease = $null",
            "if ($null -ne $manifest.PrivateData -and $null -ne $manifest.PrivateData.PSData) { $preRelease = $manifest.PrivateData.PSData.Prerelease }",
            "@{ ModuleName = [System.IO.Path]::GetFileNameWithoutExtension($manifest.RootModule); ModuleVersion = $manifest.ModuleVersion.ToString(); PreRelease = $preRelease } | ConvertTo-Json -Compress"
        });

        var execution = await _runPowerShellAsync(repositoryRoot, script, cancellationToken);
        if (execution.ExitCode != 0)
        {
            return null;
        }

        var module = JsonSerializer.Deserialize<ModuleManifestInfo>(execution.StandardOutput.Trim(), JsonOptions);
        return module;
    }

    private static ReleasePublishExecutionResult? TryDeserializePublishResult(ReleaseQueueItem queueItem)
    {
        if (string.IsNullOrWhiteSpace(queueItem.CheckpointStateJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReleasePublishExecutionResult>(queueItem.CheckpointStateJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static ReleaseVerificationReceipt VerifiedReceipt(ReleasePublishReceipt publishReceipt, string summary)
        => new(
            RootPath: publishReceipt.RootPath,
            RepositoryName: publishReceipt.RepositoryName,
            AdapterKind: publishReceipt.AdapterKind,
            TargetName: publishReceipt.TargetName,
            TargetKind: publishReceipt.TargetKind,
            Destination: publishReceipt.Destination,
            Status: ReleaseVerificationReceiptStatus.Verified,
            Summary: summary,
            VerifiedAtUtc: DateTimeOffset.UtcNow);

    private static ReleaseVerificationReceipt SkippedReceipt(ReleasePublishReceipt publishReceipt, string summary)
        => new(
            RootPath: publishReceipt.RootPath,
            RepositoryName: publishReceipt.RepositoryName,
            AdapterKind: publishReceipt.AdapterKind,
            TargetName: publishReceipt.TargetName,
            TargetKind: publishReceipt.TargetKind,
            Destination: publishReceipt.Destination,
            Status: ReleaseVerificationReceiptStatus.Skipped,
            Summary: summary,
            VerifiedAtUtc: DateTimeOffset.UtcNow);

    private static ReleaseVerificationReceipt FailedReceipt(string rootPath, string repositoryName, string adapterKind, string targetName, string? destination, string summary, string? targetKind = null)
        => new(
            RootPath: rootPath,
            RepositoryName: repositoryName,
            AdapterKind: adapterKind,
            TargetName: targetName,
            TargetKind: string.IsNullOrWhiteSpace(targetKind) ? targetName : targetKind!,
            Destination: destination,
            Status: ReleaseVerificationReceiptStatus.Failed,
            Summary: summary,
            VerifiedAtUtc: DateTimeOffset.UtcNow);

    private static string QuoteLiteral(string value)
        => "'" + value.Replace("'", "''") + "'";

    private sealed record NuGetPackageIdentity(string Id, string Version);

    private sealed class ModuleManifestInfo
    {
        public string ModuleName { get; set; } = string.Empty;
        [JsonPropertyName("ModuleVersion")]
        public string Version { get; set; } = string.Empty;
        public string? PreRelease { get; set; }
        public string VersionWithPreRelease => string.IsNullOrWhiteSpace(PreRelease) ? Version : $"{Version}-{PreRelease}";
    }

    private sealed record ResolvedPowerShellRepository(string Name, string? SourceUri, string? PublishUri)
    {
        public string DisplaySource => SourceUri ?? PublishUri ?? Name;
    }
}
