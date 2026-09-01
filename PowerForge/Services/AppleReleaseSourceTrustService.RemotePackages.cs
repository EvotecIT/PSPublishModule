using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
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
        ValidateRemotePackageIdentity(repositoryUrl, revision);

        var identity = NormalizePackageLocation(repositoryUrl) + "@" + revision.ToLowerInvariant();
        if (_validatedRemotePackages.Contains(identity))
            return;
        if (!_remotePackagesUnderValidation.Add(identity))
            return;

        try
        {
            if (_remotePackageCheckoutResolver is not null)
            {
                var resolvedCheckout = Path.GetFullPath(_remotePackageCheckoutResolver(repositoryUrl, revision));
                ValidateRemotePackageCheckout(resolvedCheckout, repositoryUrl, revision, packageLockPaths, validateRemoteDependencies: true);
                _validatedRemotePackages.Add(identity);
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
                ValidateRemotePackageCheckout(checkoutPath, repositoryUrl, revision, packageLockPaths, validateRemoteDependencies: true);
                _validatedRemotePackages.Add(identity);
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
        finally
        {
            _remotePackagesUnderValidation.Remove(identity);
        }
    }

    private static void ValidateRemotePackageIdentity(
        string repositoryUrl,
        string revision)
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
    }

    private void ValidateRemotePackageCheckout(
        string checkoutPath,
        string repositoryUrl,
        string revision,
        IReadOnlyCollection<string> packageLockPaths,
        bool validateRemoteDependencies)
    {
        var head = RunGit(checkoutPath, "rev-parse", "HEAD").StdOut.Trim();
        if (!head.Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Remote Swift package '{repositoryUrl}' did not materialize the approved revision '{revision}'.");
        EnsureNoGitReplacementRefs(checkoutPath);
        EnsureRemotePackageHasNoGitLinks(checkoutPath, repositoryUrl);
        _git.EnsureClean(checkoutPath);
        var locks = packageLockPaths.Where(File.Exists).Select(Path.GetFullPath).ToList();
        ValidateCheckedOutPackageRoot(
            checkoutPath,
            checkoutPath,
            locks,
            new HashSet<string>(GetPathComparer()),
            validateRemoteDependencies);
        _git.EnsureClean(checkoutPath);
        var headAfter = RunGit(checkoutPath, "rev-parse", "HEAD").StdOut.Trim();
        if (!headAfter.Equals(revision, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Remote Swift package '{repositoryUrl}' changed during source inspection.");
    }

    /// <summary>
    /// Validates the exact Swift package checkouts that Xcode will consume for an archive.
    /// </summary>
    internal void ValidateMaterializedPackageCheckouts(
        string sourcePackagesRoot,
        IReadOnlyDictionary<string, string> approvedRevisions)
    {
        var root = Path.GetFullPath(sourcePackagesRoot);
        var checkouts = Path.Combine(root, "checkouts");
        if (!Directory.Exists(checkouts))
        {
            if (approvedRevisions.Count > 0)
                throw new InvalidOperationException("Xcode did not materialize the complete approved Swift package graph.");
            return;
        }
        EnsureNoLinkedTraversal(checkouts, checkouts, "Xcode materialized Swift package checkout root");

        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Directory.EnumerateFileSystemEntries(checkouts).OrderBy(static path => path, GetPathComparer()))
        {
            if (!Directory.Exists(entry))
                throw new InvalidOperationException($"Xcode materialized Swift package checkout root contains an unsupported entry: {entry}");
            EnsureNoLinkedTraversal(checkouts, entry, "Xcode materialized Swift package checkout");
            var originResult = RunGitAllowFailure(entry, "remote", "get-url", "origin");
            if (!originResult.Succeeded || string.IsNullOrWhiteSpace(originResult.StdOut))
                throw new InvalidOperationException($"Xcode materialized Swift package checkout has no approved origin: {entry}");
            var origin = ResolveMaterializedPackageOrigin(root, entry, originResult.StdOut.Trim());
            var normalizedOrigin = NormalizePackageLocation(origin);
            if (!approvedRevisions.TryGetValue(normalizedOrigin, out var approvedRevision))
                throw new InvalidOperationException($"Xcode materialized an additional Swift package checkout outside the approved graph: {origin}");
            if (!observed.Add(normalizedOrigin))
                throw new InvalidOperationException($"Xcode materialized duplicate Swift package checkouts for approved origin '{origin}'.");
            ValidateRemotePackageCheckout(
                entry,
                origin,
                approvedRevision,
                Array.Empty<string>(),
                validateRemoteDependencies: false);
        }

        var missing = approvedRevisions.Keys.FirstOrDefault(key => !observed.Contains(key));
        if (missing is not null)
            throw new InvalidOperationException($"Xcode did not materialize approved Swift package checkout '{missing}'.");
    }

    private string ResolveMaterializedPackageOrigin(
        string sourcePackagesRoot,
        string checkoutPath,
        string origin)
    {
        if (origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            origin.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ||
            origin.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            return origin;
        }

        var resolvedOrigin = Path.IsPathRooted(origin)
            ? Path.GetFullPath(origin)
            : Path.GetFullPath(Path.Combine(checkoutPath, origin));
        var repositories = Path.Combine(sourcePackagesRoot, "repositories");
        if (!Directory.Exists(repositories) || !Directory.Exists(resolvedOrigin))
            return origin;

        // Xcode can record the checkout origin through an existing path alias
        // such as macOS's /var -> /private/var mapping. Compare the physical
        // directories before deciding whether this is the owned repository
        // mirror, then validate and inspect only that physical mirror.
        var physicalRepositories = AppleReleaseArtifactService.ResolvePhysicalPath(repositories);
        var physicalOrigin = AppleReleaseArtifactService.ResolvePhysicalPath(resolvedOrigin);
        if (!GetPathComparer().Equals(
                Path.GetDirectoryName(physicalOrigin),
                physicalRepositories))
        {
            return origin;
        }

        ValidateXcodeRepositoryMirrorMetadata(physicalRepositories, physicalOrigin);
        var canonicalOrigin = RunGitAllowFailure(physicalOrigin, "remote", "get-url", "origin");
        if (!canonicalOrigin.Succeeded || string.IsNullOrWhiteSpace(canonicalOrigin.StdOut))
        {
            throw new InvalidOperationException(
                $"Xcode materialized Swift package repository mirror has no approved origin: {physicalOrigin}");
        }

        return canonicalOrigin.StdOut.Trim();
    }

    private void ValidateXcodeRepositoryMirrorMetadata(string repositoriesRoot, string mirrorPath)
    {
        EnsureNoLinkedTraversal(repositoriesRoot, mirrorPath, "Xcode materialized Swift package repository mirror");

        var configPath = Path.Combine(mirrorPath, "config");
        if (!File.Exists(configPath))
        {
            throw new InvalidOperationException(
                $"Xcode materialized Swift package repository mirror config was not found: {configPath}");
        }
        EnsureNoLinkedTraversal(mirrorPath, configPath, "Xcode materialized Swift package repository mirror config");
        var includes = RunGitAllowFailure(
            mirrorPath,
            "config",
            "--file",
            configPath,
            "--no-includes",
            "--get-regexp",
            "^include");
        if (includes.Succeeded)
        {
            throw new InvalidOperationException(
                $"Xcode materialized Swift package repository mirror config must not include external Git configuration: {configPath}");
        }
        if (includes.ExitCode != 1)
        {
            throw new InvalidOperationException(
                $"Xcode materialized Swift package repository mirror config could not be inspected safely: {configPath}");
        }

        var objectsPath = Path.Combine(mirrorPath, "objects");
        var objectsInfoPath = Path.Combine(objectsPath, "info");
        EnsureDirectoryWithinRepository(mirrorPath, objectsPath, "Xcode materialized Swift package repository mirror object database");
        EnsureDirectoryWithinRepository(mirrorPath, objectsInfoPath, "Xcode materialized Swift package repository mirror object metadata");
        foreach (var alternateName in new[] { "alternates", "http-alternates" })
        {
            var alternatesPath = Path.Combine(objectsInfoPath, alternateName);
            if (PathEntryExistsOrIsLink(alternatesPath))
            {
                throw new InvalidOperationException(
                    $"Xcode materialized Swift package repository mirror must not use Git object alternates: {alternatesPath}");
            }
        }

        var bare = RunGitAllowFailure(mirrorPath, "rev-parse", "--is-bare-repository");
        if (!bare.Succeeded || !bare.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Xcode materialized Swift package repository mirror is not a self-contained bare repository: {mirrorPath}");
        }

        var gitDirectory = RunGitAllowFailure(mirrorPath, "rev-parse", "--git-dir");
        var commonDirectory = RunGitAllowFailure(mirrorPath, "rev-parse", "--git-common-dir");
        if (!gitDirectory.Succeeded || !commonDirectory.Succeeded)
        {
            throw new InvalidOperationException(
                $"Xcode materialized Swift package repository mirror has unresolved Git metadata: {mirrorPath}");
        }

        if (!gitDirectory.StdOut.Trim().Equals(".", StringComparison.Ordinal) ||
            !commonDirectory.StdOut.Trim().Equals(".", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Xcode materialized Swift package repository mirror Git metadata must be self-contained: {mirrorPath}");
        }

    }

    private static bool PathEntryExistsOrIsLink(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            return true;
#if NET8_0_OR_GREATER
        try
        {
            return !string.IsNullOrWhiteSpace(new FileInfo(path).LinkTarget) ||
                   !string.IsNullOrWhiteSpace(new DirectoryInfo(path).LinkTarget);
        }
        catch (IOException)
        {
            return true;
        }
#else
        return false;
#endif
    }

    private void EnsureRemotePackageHasNoGitLinks(string checkoutPath, string repositoryUrl)
    {
        var gitLinks = RunGit(checkoutPath, "ls-files", "--stage", "-z").StdOut
            .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(static entry => entry.StartsWith("160000 ", StringComparison.Ordinal))
            .Select(static entry =>
            {
                var separator = entry.IndexOf('\t');
                return separator >= 0 ? entry.Substring(separator + 1) : entry;
            })
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        if (gitLinks.Length == 0)
            return;

        throw new InvalidOperationException(
            $"Remote Swift package '{repositoryUrl}' contains Git submodule input '{gitLinks[0]}'. " +
            "Exact-source Apple checkpoints reject remote-package gitlinks because SwiftPM materializes their bytes outside the attested parent revision.");
    }

    private void ValidateCheckedOutPackageRoot(
        string checkoutRoot,
        string packageRoot,
        IReadOnlyCollection<string> effectiveLockPaths,
        ISet<string> validatedRoots,
        bool validateRemoteDependencies)
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

        var locks = effectiveLockPaths.ToList();
        var localLock = Path.Combine(packageRoot, "Package.resolved");
        if (File.Exists(localLock))
        {
            EnsureTrackedFile(checkoutRoot, localLock, "remote Swift package resolution lock");
            locks.Add(localLock);
        }

        HashSet<string>? inactiveSystemLibraryRoots = null;
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
            var manifestInactiveRoots = ReadInactiveNonAppleSystemLibraryRoots(packageRoot, source, syntax);
            if (inactiveSystemLibraryRoots is null)
                inactiveSystemLibraryRoots = manifestInactiveRoots;
            else
                inactiveSystemLibraryRoots.IntersectWith(manifestInactiveRoots);
            ValidateDirectSwiftPackageDependencyFactories(packageRoot, syntax);
            ValidatePackageDescriptionCalls(packageRoot, syntax);
            ValidateSwiftPackageLinkedDependencies(packageRoot, source, syntax);
            ValidateLiteralSwiftPackagePaths(checkoutRoot, packageRoot, source, syntax);
            foreach (var dependency in ParseDirectSwiftPackageDependencyCalls(source, syntax))
            {
                if (dependency.Arguments.TryGetValue("path", out var pathArgument))
                {
                    if (!TryReadLiteralSwiftString(pathArgument, out var nestedPath))
                        throw new InvalidOperationException($"Remote Swift package '{packageRoot}' uses a computed local dependency path.");
                    var nestedRoot = ResolvePbxPath(packageRoot, nestedPath, "remote Swift package local dependency");
                    ValidateCheckedOutPackageRoot(checkoutRoot, nestedRoot, locks, validatedRoots, validateRemoteDependencies);
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
                if (!validateRemoteDependencies)
                    continue;
                var dependencyRevision = string.Empty;
                var hasRevision = dependency.Arguments.TryGetValue("revision", out var revisionArgument) &&
                                  TryReadLiteralSwiftString(revisionArgument, out dependencyRevision) &&
                                  Regex.IsMatch(dependencyRevision, "^(?:[A-Fa-f0-9]{40}|[A-Fa-f0-9]{64})$", RegexOptions.CultureInvariant);
                var resolved = ResolvePackageRevision(locks, dependencyUrl, hasRevision ? dependencyRevision : null);
                ValidateRemotePackageSource(dependencyUrl, resolved, locks);
            }
        }

        if (inactiveSystemLibraryRoots is not null)
        {
            foreach (var root in inactiveSystemLibraryRoots)
                _inactiveRemoteSystemLibraryRoots.Add(root);
        }
        foreach (var conventionalInput in new[]
                 {
                     Path.Combine(packageRoot, "Sources"),
                     Path.Combine(packageRoot, "Plugins")
                 })
        {
            if (Directory.Exists(conventionalInput))
                EnsureTrackedDirectoryTree(
                    checkoutRoot,
                    conventionalInput,
                    "remote Swift package source input",
                    assemblerWorkingDirectory: packageRoot);
        }
    }

    internal void EnsureRemotePackageMirror(string mirrorPath, string repositoryUrl, string revision)
    {
        using var mirrorLease = AcquireRemotePackageMirrorLease(mirrorPath);
        var createdMirror = false;
        if (!Directory.Exists(mirrorPath))
        {
            var temporary = mirrorPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(temporary);
            try
            {
                InitializeRemotePackageMirror(temporary, revision);
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

        var expectedObjectFormat = GetObjectFormatForRevision(revision);
        var observedObjectFormat = RunGit(mirrorPath, "rev-parse", "--show-object-format").StdOut.Trim();
        if (!observedObjectFormat.Equals(expectedObjectFormat, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Remote Swift package mirror '{mirrorPath}' uses Git object format '{observedObjectFormat}', " +
                $"but revision '{revision}' requires '{expectedObjectFormat}'. Remove the incompatible private mirror and retry.");
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
                if (concurrentlyAvailable.Succeeded)
                    return;
                if (createdMirror && Directory.Exists(mirrorPath))
                    Directory.Delete(mirrorPath, recursive: true);
                throw;
            }
        }
        RunGit(mirrorPath, "cat-file", "-e", revision + "^{commit}");
    }

    internal void InitializeRemotePackageMirror(string mirrorPath, string revision)
    {
        var objectFormat = GetObjectFormatForRevision(revision);
        RunGit(mirrorPath, "init", "--bare", $"--object-format={objectFormat}");
    }

    private static string GetObjectFormatForRevision(string revision)
        => revision.Trim().Length == 64 ? "sha256" : "sha1";

    internal static FileStream AcquireRemotePackageMirrorLease(string mirrorPath)
    {
        var lockPath = mirrorPath + ".lock";
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (true)
        {
            try
            {
#if NET8_0_OR_GREATER
                if (!OperatingSystem.IsWindows())
                    return OpenUnixRemotePackageMirrorLease(lockPath);
#endif
                RejectLinkedRemotePackageMirrorLock(lockPath);
                var lease = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
                try
                {
                    ValidateRemotePackageMirrorLockIdentity(lockPath, lease);
                    return lease;
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static void RejectLinkedRemotePackageMirrorLock(string lockPath)
    {
        if ((File.Exists(lockPath) || Directory.Exists(lockPath)) &&
            (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Remote Swift package mirror lock must not be a symbolic link or reparse point: {lockPath}");
        }
    }

    private static void ValidateRemotePackageMirrorLockIdentity(string lockPath, FileStream lease)
    {
        RejectLinkedRemotePackageMirrorLock(lockPath);
        var opened = ExistingFilePathIdentityResolver.ResolveStatus(lease.SafeFileHandle);
        var replaced = false;
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            using var currentHandle = OpenExistingUnixRemotePackageMirrorLock(lockPath);
            var current = ExistingFilePathIdentityResolver.ResolveStatus(currentHandle);
            replaced = !opened.Identity.Equals(current.Identity, StringComparison.Ordinal);
        }
#endif
        int hardLinkCount;
#if NET8_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
            hardLinkCount = ExistingFilePathIdentityResolver.ResolveHardLinkCounts(new[] { lockPath })[0];
        else
#endif
            hardLinkCount = ExistingFilePathIdentityResolver.ResolveHardLinkCount(lease.SafeFileHandle);
        if (replaced || hardLinkCount != 1)
        {
            throw new InvalidOperationException(
                $"Remote Swift package mirror lock was linked or replaced while it was being acquired " +
                $"(replaced={replaced}, hardLinks={hardLinkCount}): {lockPath}");
        }
    }

#if NET8_0_OR_GREATER
    private static SafeFileHandle OpenExistingUnixRemotePackageMirrorLock(string lockPath)
    {
        var noFollow = OperatingSystem.IsMacOS() ? 0x0100 : 0x20000;
        var descriptor = OpenUnix(lockPath, noFollow, 0);
        if (descriptor >= 0)
            return new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        var error = Marshal.GetLastWin32Error();
        if ((OperatingSystem.IsMacOS() && error == 62) || (!OperatingSystem.IsMacOS() && error == 40))
        {
            throw new InvalidOperationException(
                $"Remote Swift package mirror lock must not be a symbolic link: {lockPath}");
        }
        throw new IOException($"Unable to verify remote Swift package mirror lock '{lockPath}'.", new Win32Exception(error));
    }

    private static FileStream OpenUnixRemotePackageMirrorLease(string lockPath)
    {
        var create = OperatingSystem.IsMacOS() ? 0x0200 : 0x0040;
        var noFollow = OperatingSystem.IsMacOS() ? 0x0100 : 0x20000;
        const int readWrite = 0x0002;
        const uint userReadWrite = 0x0180;
        var descriptor = OpenUnix(lockPath, readWrite | create | noFollow, userReadWrite);
        if (descriptor < 0)
        {
            var error = Marshal.GetLastWin32Error();
            if ((OperatingSystem.IsMacOS() && error == 62) || (!OperatingSystem.IsMacOS() && error == 40))
            {
                throw new InvalidOperationException(
                    $"Remote Swift package mirror lock must not be a symbolic link: {lockPath}");
            }
            throw new IOException($"Unable to open remote Swift package mirror lock '{lockPath}'.", new Win32Exception(error));
        }

        var handle = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        FileStream? lease = null;
        try
        {
            const int exclusiveNonBlocking = 0x0002 | 0x0004;
            if (FlockUnix(descriptor, exclusiveNonBlocking) != 0)
                throw new IOException($"Remote Swift package mirror lock is already leased: {lockPath}");
            lease = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 1, isAsync: false);
            handle = null!;
            ValidateRemotePackageMirrorLockIdentity(lockPath, lease);
            if (FchmodUnix(descriptor, userReadWrite) != 0)
                throw new IOException(
                    $"Unable to restrict remote Swift package mirror lock permissions: {lockPath}",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            ValidateRemotePackageMirrorLockIdentity(lockPath, lease);
            return lease;
        }
        catch
        {
            lease?.Dispose();
            handle?.Dispose();
            throw;
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenUnix(string path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int FlockUnix(int descriptor, int operation);

    [DllImport("libc", EntryPoint = "fchmod", SetLastError = true)]
    private static extern int FchmodUnix(int descriptor, uint mode);
#endif

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
