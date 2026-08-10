using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private void ValidateRemotePackageSource(
        string repositoryUrl,
        string revision,
        IReadOnlyCollection<string> packageLockPaths)
    {
        if (!LooksLikeRepositoryLocation(repositoryUrl) ||
            !(repositoryUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
              repositoryUrl.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ||
              repositoryUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Remote Swift package '{repositoryUrl}' must use an inspectable HTTPS or SSH Git repository.");
        }
        if (!Regex.IsMatch(revision, "^(?:[A-Fa-f0-9]{40}|[A-Fa-f0-9]{64})$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException($"Remote Swift package '{repositoryUrl}' is not bound to an exact Git revision.");

        var identity = NormalizePackageLocation(repositoryUrl) + "@" + revision.ToLowerInvariant();
        if (!_validatedRemotePackages.Add(identity))
            return;

        if (_remotePackageCheckoutResolver is not null)
        {
            var resolvedCheckout = Path.GetFullPath(_remotePackageCheckoutResolver(repositoryUrl, revision));
            ValidateRemotePackageCheckout(resolvedCheckout, repositoryUrl, revision, packageLockPaths);
            return;
        }

        var cacheRoot = ResolveRemotePackageCacheRoot();
        Directory.CreateDirectory(cacheRoot);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(cacheRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        var mirrorPath = Path.Combine(cacheRoot, ComputeStablePathToken(repositoryUrl) + ".git");
        EnsureRemotePackageMirror(mirrorPath, repositoryUrl, revision);

        var checkoutParent = Path.Combine(Path.GetTempPath(), "PowerForge", "apple-swiftpm-source-trust");
        Directory.CreateDirectory(checkoutParent);
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(checkoutParent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
        var checkoutPath = Path.Combine(checkoutParent, Guid.NewGuid().ToString("N"));
        try
        {
            RunGit(mirrorPath, "-c", "core.hooksPath=/dev/null", "worktree", "add", "--detach", checkoutPath, revision);
            ValidateRemotePackageCheckout(checkoutPath, repositoryUrl, revision, packageLockPaths);
        }
        finally
        {
            if (Directory.Exists(checkoutPath))
            {
                var removed = RunGitAllowFailure(mirrorPath, "worktree", "remove", "--force", checkoutPath);
                if (!removed.Succeeded && Directory.Exists(checkoutPath))
                    Directory.Delete(checkoutPath, recursive: true);
            }
        }
    }

    private void ValidateRemotePackageCheckout(
        string checkoutPath,
        string repositoryUrl,
        string revision,
        IReadOnlyCollection<string> packageLockPaths)
    {
        var head = RunGit(checkoutPath, "rev-parse", "HEAD").StdOut.Trim();
        if (!head.Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Remote Swift package '{repositoryUrl}' did not materialize the approved revision '{revision}'.");
        EnsureNoGitReplacementRefs(checkoutPath);
        _git.EnsureClean(checkoutPath);
        var locks = packageLockPaths.Where(File.Exists).Select(Path.GetFullPath).ToList();
        ValidateCheckedOutPackageRoot(
            checkoutPath,
            checkoutPath,
            locks,
            new HashSet<string>(GetPathComparer()));
        _git.EnsureClean(checkoutPath);
        var headAfter = RunGit(checkoutPath, "rev-parse", "HEAD").StdOut.Trim();
        if (!headAfter.Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Remote Swift package '{repositoryUrl}' changed during source inspection.");
    }

    private void ValidateCheckedOutPackageRoot(
        string checkoutRoot,
        string packageRoot,
        IReadOnlyCollection<string> effectiveLockPaths,
        ISet<string> validatedRoots)
    {
        packageRoot = Path.GetFullPath(packageRoot);
        EnsureDirectoryWithinRepository(checkoutRoot, packageRoot, "remote Swift package root");
        if (!validatedRoots.Add(packageRoot))
            return;

        var manifests = Directory.EnumerateFiles(packageRoot, "Package*.swift", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Equals("Package.swift", StringComparison.Ordinal) ||
                           Regex.IsMatch(Path.GetFileName(path), "^Package@swift-[0-9]+(?:\\.[0-9]+)*\\.swift$", RegexOptions.CultureInvariant))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (!manifests.Any(path => Path.GetFileName(path).Equals("Package.swift", StringComparison.Ordinal)))
            throw new FileNotFoundException($"Remote Swift package manifest was not found at the approved revision: {packageRoot}");

        foreach (var conventionalInput in new[]
                 {
                     Path.Combine(packageRoot, "Sources"),
                     Path.Combine(packageRoot, "Plugins")
                 })
        {
            if (Directory.Exists(conventionalInput))
                EnsureTrackedDirectoryTree(checkoutRoot, conventionalInput, "remote Swift package source input");
        }

        var locks = effectiveLockPaths.ToList();
        var localLock = Path.Combine(packageRoot, "Package.resolved");
        if (File.Exists(localLock))
        {
            EnsureTrackedFile(checkoutRoot, localLock, "remote Swift package resolution lock");
            locks.Add(localLock);
        }

        foreach (var manifestPath in manifests)
        {
            EnsureTrackedFile(checkoutRoot, manifestPath, "remote Swift package manifest");
            var source = RemoveSwiftComments(File.ReadAllText(manifestPath));
            EnsureNoExecutableSwiftStringInterpolation(packageRoot, source);
            var syntax = MaskSwiftStringLiterals(source);
            ValidateLocalPackageExecutableSafety(
                packageRoot,
                source,
                syntax,
                allowInactiveNonAppleSystemLibraries: true);
            ValidateDirectSwiftPackageDependencyFactories(packageRoot, syntax);
            ValidateLiteralSwiftPackagePaths(checkoutRoot, packageRoot, source, syntax);
            foreach (var dependency in ParseDirectSwiftPackageDependencyCalls(source, syntax))
            {
                if (dependency.Arguments.TryGetValue("path", out var pathArgument))
                {
                    if (!TryReadLiteralSwiftString(pathArgument, out var nestedPath))
                        throw new InvalidOperationException($"Remote Swift package '{packageRoot}' uses a computed local dependency path.");
                    var nestedRoot = ResolvePbxPath(packageRoot, nestedPath, "remote Swift package local dependency");
                    ValidateCheckedOutPackageRoot(checkoutRoot, nestedRoot, locks, validatedRoots);
                    continue;
                }

                if (!dependency.Arguments.TryGetValue("url", out var urlArgument) &&
                    !dependency.Arguments.TryGetValue("id", out urlArgument))
                    continue;
                if (!TryReadLiteralSwiftString(urlArgument, out var dependencyUrl) ||
                    !LooksLikeRepositoryLocation(dependencyUrl))
                {
                    throw new InvalidOperationException($"Remote Swift package '{packageRoot}' declares an uninspectable external dependency.");
                }
                var dependencyRevision = string.Empty;
                var hasRevision = dependency.Arguments.TryGetValue("revision", out var revisionArgument) &&
                                  TryReadLiteralSwiftString(revisionArgument, out dependencyRevision) &&
                                  Regex.IsMatch(dependencyRevision, "^(?:[A-Fa-f0-9]{40}|[A-Fa-f0-9]{64})$", RegexOptions.CultureInvariant);
                var resolved = ResolvePackageRevision(locks, dependencyUrl, hasRevision ? dependencyRevision : null);
                ValidateRemotePackageSource(dependencyUrl, resolved, locks);
            }
        }
    }

    private void EnsureRemotePackageMirror(string mirrorPath, string repositoryUrl, string revision)
    {
        var createdMirror = false;
        if (!Directory.Exists(mirrorPath))
        {
            var temporary = mirrorPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(temporary);
            try
            {
                RunGit(temporary, "init", "--bare");
                try
                {
                    Directory.Move(temporary, mirrorPath);
                    createdMirror = true;
                }
                catch (IOException) when (Directory.Exists(mirrorPath))
                {
                    Directory.Delete(temporary, recursive: true);
                }
            }
            finally
            {
                if (Directory.Exists(temporary))
                    Directory.Delete(temporary, recursive: true);
            }
        }

        var exists = RunGitAllowFailure(mirrorPath, "cat-file", "-e", revision + "^{commit}");
        if (!exists.Succeeded)
        {
            try
            {
                RunGit(
                    mirrorPath,
                    "-c", "core.hooksPath=/dev/null",
                    "-c", "protocol.file.allow=never",
                    "fetch", "--force", "--no-tags", "--depth=1", repositoryUrl, revision);
            }
            catch
            {
                var concurrentlyAvailable = RunGitAllowFailure(mirrorPath, "cat-file", "-e", revision + "^{commit}");
                if (createdMirror && !concurrentlyAvailable.Succeeded && Directory.Exists(mirrorPath))
                    Directory.Delete(mirrorPath, recursive: true);
                throw;
            }
        }
        RunGit(mirrorPath, "cat-file", "-e", revision + "^{commit}");
    }

    private static string ResolveRemotePackageCacheRoot()
    {
        var configured = Environment.GetEnvironmentVariable("POWERFORGE_APPLE_SWIFTPM_TRUST_CACHE");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".powerforge", "apple-swiftpm-trust-cache")
            : configured!);
    }

    private static string ComputeStablePathToken(string value)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(value)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }
}
