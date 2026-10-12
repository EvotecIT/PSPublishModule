namespace PowerForge;

/// <summary>Binds runtime privileges to Microsoft-authenticated bytes from the exact selected package.</summary>
internal sealed class PowerShellCompilationRuntimePackAuthority
{
    // Microsoft publishes this rotation policy and retains these four author certificates:
    // https://devblogs.microsoft.com/dotnet/microsoft-author-signing-certificate-update-2026/
    private static readonly string[] MicrosoftAuthorFingerprints =
    {
        "3F9001EA83C560D712C24CF213C3D312CB3BFF51EE89435D3430BD06B5D0EECE",
        "AA12DA22A49BCE7D5C1AE64CC1F3D892F150DA76140F210ABD2CBFFCA2C18A27",
        "566A31882BE208BE4422F7CFD66ED09F5D4524A5994F50CCC8B05EC0528C1353",
        "9A1B131BEE0605433056A4EA3815478A8E177961A968C6C0027C1093D1FEB630"
    };
    private readonly string _targetFramework;
    private readonly string _runtimeIdentifier;
    private readonly Dictionary<string, DotNetNuGetClient.AuthenticatedPackage> _packages;
    private readonly string _dependencyLockSha256;
    private readonly Dictionary<string, string> _derivedHashes = new(StringComparer.OrdinalIgnoreCase);
    private string _producerBinding = string.Empty;

    private PowerShellCompilationRuntimePackAuthority(string targetFramework, string runtimeIdentifier,
        string dependencyLockSha256, Dictionary<string, DotNetNuGetClient.AuthenticatedPackage> packages)
    {
        _targetFramework = targetFramework;
        _runtimeIdentifier = runtimeIdentifier;
        _packages = packages;
        _dependencyLockSha256 = dependencyLockSha256;
    }

    internal static PowerShellCompilationRuntimePackAuthority Authenticate(
        PowerShellCompilationDependencyGraph graph, string targetFramework, string runtimeIdentifier,
        string? configuredPackageRoot, CancellationToken cancellationToken)
    {
        var packages = new Dictionary<string, DotNetNuGetClient.AuthenticatedPackage>(StringComparer.OrdinalIgnoreCase);
        var authority = new PowerShellCompilationRuntimePackAuthority(targetFramework, runtimeIdentifier, graph.LockSha256, packages);
        foreach (var node in graph.Nodes.Where(static node => node.Exists &&
                     node.Roles.HasFlag(PowerShellCompilationDependencyGraphRole.Deployment) &&
                     node.Identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal)))
        {
            if (!authority.TryResolveEntry(node.Identity, out var packageKey, out var entryPath))
                throw new InvalidOperationException($"Runtime-pack dependency '{node.Identity.Source}' does not identify the selected target package.");
            if (!packages.ContainsKey(packageKey))
            {
                var segments = packageKey.Split('/');
                packages.Add(packageKey, AuthenticateMicrosoftPackage(segments[0], segments[1], configuredPackageRoot, cancellationToken));
            }
            if (!packages[packageKey].Matches(entryPath, node.Identity.Sha256))
                throw new InvalidOperationException($"Runtime-pack dependency '{node.Identity.Source}' does not match its authenticated package entry SHA-256.");
        }
        return authority;
    }

    internal bool Matches(PowerShellCompilationDependencyIdentity identity)
        => TryResolveEntry(identity, out var packageKey, out var entryPath) &&
           _packages.TryGetValue(packageKey, out var package) && package.Matches(entryPath, identity.Sha256);

    internal IEnumerable<DotNetNuGetClient.AuthenticatedPackage> Packages => _packages.Values;

    internal static void EnsureInputOwnership(PowerShellCompilationDependencyGraph graph, PowerShellCompilationProviderLock? providerLock)
    {
        var runtimeNames = graph.Nodes.Where(static node => node.Exists &&
                node.Kind == PowerShellCompilationDependencyNodeKind.ManagedLibrary &&
                node.Identity.Provenance == "DotNetRuntimePack")
            .Select(static node => node.Identity.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var otherNames = graph.Nodes.Where(static node => node.Exists &&
                node.Kind is PowerShellCompilationDependencyNodeKind.ManagedLibrary or PowerShellCompilationDependencyNodeKind.BinaryModule &&
                node.Identity.Provenance != "DotNetRuntimePack")
            .Select(static node => node.Identity.Name)
            .Concat((providerLock?.Packages ?? Array.Empty<PowerShellCompilationProviderPackageLockEntry>())
                .SelectMany(static package => package.Assemblies ?? Array.Empty<PowerShellCompilationProviderAssembly>())
                .Select(static assembly => assembly.AssemblyName));
        var collision = otherNames.FirstOrDefault(runtimeNames.Contains);
        if (collision is not null)
            throw new InvalidOperationException($"User/provider input '{collision}' cannot replace an authenticated runtime assembly or receive SDK runtime derivation authority.");
    }

    internal static DotNetNuGetClient.AuthenticatedPackage AuthenticateMicrosoftPackage(
        string id, string version, string? configuredPackageRoot, CancellationToken cancellationToken)
    {
        var root = string.IsNullOrWhiteSpace(configuredPackageRoot)
            ? Environment.GetEnvironmentVariable("NUGET_PACKAGES") : configuredPackageRoot;
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        id = id.ToLowerInvariant();
        return DotNetNuGetClient.AuthenticatePackageAsync(Path.Combine(root!, id, version, id + "." + version + ".nupkg"),
            id, version, MicrosoftAuthorFingerprints, cancellationToken).GetAwaiter().GetResult();
    }

    // Called only by the canonical fresh SDK publisher after authenticated staged input
    // and toolchain checks. No graph label, persistent receipt or build-cache hit mints this authority.
    internal void RecordSdkOutputs(IReadOnlyDictionary<string, string> outputHashes, string producerBinding)
    {
        if (_producerBinding.Length > 0 || string.IsNullOrWhiteSpace(producerBinding))
            throw new InvalidOperationException("Authenticated SDK output authority can be captured only once.");
        foreach (var output in outputHashes) _derivedHashes.Add(output.Key, output.Value);
        _producerBinding = producerBinding;
    }

    internal bool MatchesDelivered(PowerShellCompilationDependencyIdentity identity, string deliveredSha256, string dependencyLockSha256)
        => _dependencyLockSha256.Equals(dependencyLockSha256, StringComparison.OrdinalIgnoreCase) && Matches(identity) &&
           (identity.Sha256.Equals(deliveredSha256, StringComparison.OrdinalIgnoreCase) ||
            (_producerBinding.Length > 0 && _derivedHashes.TryGetValue(identity.Source, out var derived) &&
             derived.Equals(deliveredSha256, StringComparison.OrdinalIgnoreCase)));

    private bool TryResolveEntry(PowerShellCompilationDependencyIdentity identity, out string packageKey, out string entryPath)
    {
        packageKey = entryPath = string.Empty;
        if (!identity.Provenance.Equals("DotNetRuntimePack", StringComparison.Ordinal) ||
            !identity.TargetFramework.Equals(_targetFramework, StringComparison.Ordinal) ||
            !identity.RuntimeIdentifier.Equals(_runtimeIdentifier, StringComparison.Ordinal)) return false;
        var segments = identity.Source.Split('/');
        if (segments.Length is not (4 or 5) || segments[0] != "runtime-pack" ||
            !segments[1].Equals("microsoft.netcore.app.runtime." + _runtimeIdentifier, StringComparison.OrdinalIgnoreCase) ||
            !Version.TryParse(segments[2], out _) ||
            (segments.Length == 5 && segments[3] != "native")) return false;
        var fileName = segments[segments.Length - 1];
        if (fileName.Length == 0 || fileName is "." or ".." || fileName.IndexOf('\\') >= 0) return false;
        packageKey = segments[1].ToLowerInvariant() + "/" + segments[2];
        entryPath = "runtimes/" + _runtimeIdentifier + (segments.Length == 5
            ? "/native/" + fileName : "/lib/" + _targetFramework + "/" + fileName);
        return true;
    }
}
