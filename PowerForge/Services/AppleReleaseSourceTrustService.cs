using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Binds an Apple release checkpoint to source inputs represented by one exact Git commit.
/// </summary>
internal sealed partial class AppleReleaseSourceTrustService
{
    private static readonly HashSet<string> AlwaysRejectedIgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".swift", ".m", ".mm", ".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp",
        ".metal", ".xcconfig", ".entitlements", ".xcprivacy", ".plist", ".storyboard", ".xib",
        ".strings", ".stringsdict", ".xcstrings", ".intentdefinition", ".mlmodel", ".xcscheme",
        ".xcfilelist", ".modulemap", ".s", ".a", ".dylib"
    };

    private readonly HomeAssistantReleaseGitService _git;
    private readonly GitClient _gitClient;
    private readonly Func<string, string, string>? _remotePackageCheckoutResolver;
    private readonly object _validationGate = new();
    private readonly Dictionary<string, string> _gitObjectFormats = new(
        Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _remotePackagesUnderValidation = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _validatedRemotePackages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _validatedTrackedFileBlobs = new(GetPathComparer());
    private readonly HashSet<string> _validatedSourceIncludeFiles = new(GetPathComparer());
    private readonly HashSet<string> _validatedAssemblerInputFiles = new(GetPathComparer());
    private readonly HashSet<string> _validatedSourceSemanticInputs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _inactiveRemoteSystemLibraryRoots = new(GetPathComparer());
    private readonly HashSet<string> _approvedHeaderSearchRoots = new(GetPathComparer());
    private readonly Dictionary<string, List<string>> _approvedAssemblerSearchRoots = new(GetPathComparer());

    internal AppleReleaseSourceTrustService(
        HomeAssistantReleaseGitService? git = null,
        GitClient? gitClient = null,
        Func<string, string, string>? remotePackageCheckoutResolver = null)
    {
        _gitClient = gitClient ?? GitClient.CreateTrustedSystemClient(defaultTimeout: TimeSpan.FromMinutes(2));
        _git = git ?? new HomeAssistantReleaseGitService(_gitClient);
        _remotePackageCheckoutResolver = remotePackageCheckoutResolver;
    }

    internal string ResolveExactCommit(string repositoryRoot, string configPath)
        => Capture(repositoryRoot, configPath).SourceCommit;

    internal AppleReleaseSourceTrustSnapshot Capture(string repositoryRoot, string configPath)
    {
        lock (_validationGate)
        {
            ResetValidationState();
            try
            {
                return CaptureCore(repositoryRoot, configPath);
            }
            finally
            {
                ResetValidationState();
            }
        }
    }

    private AppleReleaseSourceTrustSnapshot CaptureCore(string repositoryRoot, string configPath)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var releaseConfigPath = Path.GetFullPath(configPath);
        EnsureNoGitReplacementRefs(root);
        _git.EnsureClean(root);
        var sourceCommitBeforeValidation = ReadExactHead(root);
        var releaseConfigBytes = File.ReadAllBytes(releaseConfigPath);
        EnsureTrackedFile(
            root,
            releaseConfigPath,
            "Apple release configuration",
            ComputeRawGitBlobId(root, releaseConfigBytes),
            releaseConfigBytes);
        var releaseConfigContent = DecodeTrackedText(releaseConfigBytes);
        var spec = PowerForgeReleaseService.LoadConfigurationContent(releaseConfigContent, releaseConfigPath);
        var options = spec.AppleApps
            ?? throw new InvalidOperationException("The release configuration does not contain an AppleApps contract.");
        var generatedOutputs = ResolveGeneratedOutputPaths(releaseConfigPath, options);
        ValidateAppleInputs(root, releaseConfigPath, options, generatedOutputs);

        EnsureNoGitReplacementRefs(root);
        _git.EnsureClean(root);
        var sourceCommitAfterValidation = ReadExactHead(root);
        if (!sourceCommitAfterValidation.Equals(sourceCommitBeforeValidation, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed while Apple release inputs were being validated. Rebuild from the new exact source commit.");
        }
        return new AppleReleaseSourceTrustSnapshot(
            sourceCommitAfterValidation,
            generatedOutputs,
            releaseConfigContent,
            ComputeSha256(releaseConfigBytes));
    }

    internal void ValidateAfterBuild(
        string repositoryRoot,
        string configPath,
        AppleReleaseSourceTrustSnapshot snapshot)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        lock (_validationGate)
        {
            ResetValidationState();
            try
            {
                ValidateAfterBuildCore(repositoryRoot, configPath, snapshot);
            }
            finally
            {
                ResetValidationState();
            }
        }
    }

    private void ValidateAfterBuildCore(
        string repositoryRoot,
        string configPath,
        AppleReleaseSourceTrustSnapshot snapshot)
    {

        var root = Path.GetFullPath(repositoryRoot);
        var releaseConfigPath = Path.GetFullPath(configPath);
        EnsureNoGitReplacementRefs(root);
        EnsureNoUnexpectedWorktreeChanges(root, snapshot.GeneratedOutputPaths);
        if (!ReadExactHead(root).Equals(snapshot.SourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed while the Apple release checkpoint was being built. Rebuild from the new exact source commit.");
        }

        EnsureTrackedFile(root, releaseConfigPath, "Apple release configuration");
        var options = PowerForgeReleaseService.LoadConfigurationContent(
                snapshot.ExactConfigurationContent ?? File.ReadAllText(releaseConfigPath),
                releaseConfigPath).AppleApps
            ?? throw new InvalidOperationException("The release configuration does not contain an AppleApps contract.");
        var generatedOutputs = ResolveGeneratedOutputPaths(releaseConfigPath, options);
        if (!PathsEqual(snapshot.GeneratedOutputPaths, generatedOutputs))
        {
            throw new InvalidOperationException(
                "Apple generated output paths changed while the release checkpoint was being built. Rebuild from the updated release contract.");
        }

        ValidateAppleInputs(root, releaseConfigPath, options, generatedOutputs);
        EnsureNoGitReplacementRefs(root);
        EnsureNoUnexpectedWorktreeChanges(root, generatedOutputs);
        if (!ReadExactHead(root).Equals(snapshot.SourceCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed while the Apple release checkpoint was being built. Rebuild from the new exact source commit.");
        }
    }

    private void ResetValidationState()
    {
        _remotePackagesUnderValidation.Clear();
        _validatedRemotePackages.Clear();
        _validatedTrackedFileBlobs.Clear();
        _validatedSourceIncludeFiles.Clear();
        _validatedAssemblerInputFiles.Clear();
        _validatedSourceSemanticInputs.Clear();
        _inactiveRemoteSystemLibraryRoots.Clear();
        _approvedHeaderSearchRoots.Clear();
        _approvedAssemblerSearchRoots.Clear();
    }

    private void EnsureNoGitReplacementRefs(string repositoryRoot)
    {
        var replacements = RunGit(repositoryRoot, "replace", "-l").StdOut
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (replacements.Length > 0)
        {
            throw new InvalidOperationException(
                "Git replacement refs are not accepted for exact-source Apple checkpoints because HEAD would not identify the effective source tree.");
        }
    }

    private void ValidateAppleInputs(
        string repositoryRoot,
        string configPath,
        PowerForgeAppleReleaseOptions options,
        IReadOnlyCollection<string> generatedOutputPaths)
    {
        ValidateTrustedAppleToolExecutables(options);
        var configDirectory = Path.GetDirectoryName(configPath) ?? repositoryRoot;
        var projectRoot = ResolvePath(
            configDirectory,
            string.IsNullOrWhiteSpace(options.ProjectRoot) ? "." : options.ProjectRoot!);
        EnsureDirectoryWithinRepository(repositoryRoot, projectRoot, "AppleApps.ProjectRoot");

        foreach (var input in EnumerateConfiguredInputs(options))
        {
            var inputPath = ResolvePath(projectRoot, input.Path);
            EnsureNoGeneratedOutputOverlap(inputPath, generatedOutputPaths, input.Name);
            EnsureTrackedFile(repositoryRoot, inputPath, input.Name);
        }

        var metadataPaths = new HashSet<string>(GetPathComparer());
        foreach (var app in options.Apps ?? Array.Empty<AppleAppConfiguration>())
        {
            if (!app.Enabled || string.IsNullOrWhiteSpace(app.ProjectPath))
                continue;

            if (app.GenerateProjectIfMissing || app.RegenerateProject)
            {
                throw new InvalidOperationException(
                    $"Studio exact-source Apple checkpoints do not accept generated Xcode project metadata for '{app.Name}'. " +
                    "Generate the project first, review it, and commit the shared project and scheme metadata before building the checkpoint.");
            }

            var configuredProjectPath = ResolvePath(projectRoot, app.ProjectPath);
            EnsureNoGeneratedOutputOverlap(configuredProjectPath, generatedOutputPaths, "AppleApps.Apps.ProjectPath");
            EnsurePathWithinRepository(repositoryRoot, configuredProjectPath, "AppleApps.Apps.ProjectPath");
            if (File.Exists(configuredProjectPath))
            {
                EnsureTrackedFile(repositoryRoot, configuredProjectPath, "AppleApps.Apps.ProjectPath");
                metadataPaths.Add(configuredProjectPath);
            }
            else if (Directory.Exists(configuredProjectPath))
            {
                var metadataName = configuredProjectPath.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase)
                    ? "contents.xcworkspacedata"
                    : "project.pbxproj";
                var metadataPath = Path.Combine(configuredProjectPath, metadataName);
                EnsureTrackedFile(repositoryRoot, metadataPath, $"AppleApps.Apps.ProjectPath/{metadataName}");
                metadataPaths.Add(metadataPath);
            }
            else
            {
                throw new FileNotFoundException(
                    $"AppleApps.Apps.ProjectPath was not found inside the exact checked-out source: {configuredProjectPath}",
                    configuredProjectPath);
            }
        }

        AddReferencedWorkspaceProjects(repositoryRoot, metadataPaths);
        AddReferencedXcodeProjects(repositoryRoot, metadataPaths, generatedOutputPaths);
        ValidateXcodeBuildGraph(
            repositoryRoot,
            projectRoot,
            options.Apps ?? Array.Empty<AppleAppConfiguration>(),
            metadataPaths,
            generatedOutputPaths);
        RejectIgnoredAppleInputs(repositoryRoot, projectRoot, metadataPaths, generatedOutputPaths);
    }

    private static string[] ResolveGeneratedOutputPaths(
        string configPath,
        PowerForgeAppleReleaseOptions options)
    {
        var configDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        var projectRoot = ResolvePath(
            configDirectory,
            string.IsNullOrWhiteSpace(options.ProjectRoot) ? "." : options.ProjectRoot!);
        var automation = options.Automation ?? new PowerForgeAppleReleaseAutomationOptions();
        var archiveRoot = ResolvePath(projectRoot, string.IsNullOrWhiteSpace(options.ArchiveRoot)
            ? Path.Combine("Artifacts", "Apple", "Archives")
            : options.ArchiveRoot!);
        var exportRoot = ResolvePath(projectRoot, string.IsNullOrWhiteSpace(options.ExportRoot)
            ? Path.Combine("Artifacts", "Apple", "Exports")
            : options.ExportRoot!);
        var artifactPaths = (options.Apps ?? Array.Empty<AppleAppConfiguration>())
            .Where(static app => app.Enabled && !string.IsNullOrWhiteSpace(app.Scheme))
            .SelectMany(app =>
            {
                var name = string.IsNullOrWhiteSpace(app.Name) ? app.Scheme!.Trim() : app.Name!.Trim();
                var safeName = PowerForgeReleaseService.SanitizeStageEntryName(name).Replace(' ', '-');
                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = "AppleApp";
                return new[]
                {
                    Path.Combine(archiveRoot, app.Platform.ToString(), $"{safeName}.xcarchive"),
                    Path.Combine(exportRoot, app.Platform.ToString(), safeName)
                };
            });
        return artifactPaths
            .Concat(new[]
            {
                ResolvePath(projectRoot, automation.ReceiptPath),
                ResolvePath(projectRoot, automation.ReceiptHistoryPath),
                ResolvePath(projectRoot, automation.PlanReceiptPath),
                ResolvePath(projectRoot, automation.LockPath)
            })
            .Distinct(GetPathComparer())
            .OrderBy(static path => path, GetPathComparer())
            .ToArray();
    }

    private void EnsureNoUnexpectedWorktreeChanges(
        string repositoryRoot,
        IReadOnlyCollection<string> generatedOutputPaths)
    {
        var status = RunGit(
                repositoryRoot,
                "status",
                "--porcelain=v1",
                "--untracked-files=all",
                "-z")
            .StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in status)
        {
            if (entry.Length < 4)
                throw new InvalidOperationException("Git returned an invalid worktree status while validating Apple release source trust.");
            var statusCode = entry.Substring(0, 2);
            var pathText = entry.Substring(3);
            var candidate = Path.GetFullPath(Path.Combine(repositoryRoot, pathText.Replace('/', Path.DirectorySeparatorChar)));
            if (statusCode == "??" &&
                (generatedOutputPaths.Any(output => IsPathAtOrWithin(candidate, output)) ||
                 IsBenignIgnoredXcodeUserState(pathText.Replace('\\', '/'))))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Apple release source changed while the checkpoint was being built: {pathText}. " +
                "Only declared generated Apple artifact and receipt outputs may change during the build.");
        }
    }

    private static void EnsureNoGeneratedOutputOverlap(
        string sourcePath,
        IReadOnlyCollection<string> generatedOutputPaths,
        string name)
    {
        var overlap = generatedOutputPaths.FirstOrDefault(output =>
            IsPathAtOrWithin(sourcePath, output) || IsPathAtOrWithin(output, sourcePath));
        if (overlap is not null)
        {
            throw new InvalidOperationException(
                $"{name} overlaps a declared generated Apple output and cannot be proven as exact source: {sourcePath} ({overlap})");
        }
    }

    private static bool PathsEqual(
        IReadOnlyCollection<string> left,
        IReadOnlyCollection<string> right)
        => left.Count == right.Count &&
           new HashSet<string>(left, GetPathComparer()).SetEquals(right);

    private static IEnumerable<(string Name, string Path)> EnumerateConfiguredInputs(
        PowerForgeAppleReleaseOptions options)
    {
        foreach (var entry in EnumeratePathValues("AppleApps.ScreenshotConfigPath", options.ScreenshotConfigPath, options.ScreenshotConfigPaths))
            yield return entry;
        foreach (var entry in EnumeratePathValues("AppleApps.MetadataConfigPath", options.MetadataConfigPath, options.MetadataConfigPaths))
            yield return entry;
        foreach (var entry in EnumeratePathValues("AppleApps.AppInfoConfigPath", options.AppInfoConfigPath, options.AppInfoConfigPaths))
            yield return entry;
        foreach (var entry in EnumeratePathValues("AppleApps.GovernanceConfigPath", options.GovernanceConfigPath, options.GovernanceConfigPaths))
            yield return entry;
        var versionSourcePath = options.Automation?.VersionSourcePath;
        if (!string.IsNullOrWhiteSpace(versionSourcePath))
            yield return ("AppleApps.Automation.VersionSourcePath", versionSourcePath!);
    }

    private static IEnumerable<(string Name, string Path)> EnumeratePathValues(
        string name,
        string? single,
        string[]? many)
    {
        if (!string.IsNullOrWhiteSpace(single))
            yield return (name, single!);
        foreach (var path in many ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(path))
                yield return (name, path);
        }
    }

    private void AddReferencedWorkspaceProjects(
        string repositoryRoot,
        HashSet<string> metadataPaths)
    {
        var pending = new Queue<string>(metadataPaths.Where(path =>
            path.EndsWith("contents.xcworkspacedata", StringComparison.OrdinalIgnoreCase)));
        while (pending.Count > 0)
        {
            var workspaceMetadata = pending.Dequeue();
            var workspaceContainer = Path.GetDirectoryName(workspaceMetadata)!;
            var workspaceRoot = Path.GetDirectoryName(workspaceContainer)!;
            var document = XDocument.Load(workspaceMetadata, LoadOptions.None);
            foreach (var candidate in EnumerateWorkspaceReferences(document.Root, workspaceRoot, workspaceRoot))
            {
                EnsurePathWithinRepository(repositoryRoot, candidate, "Apple workspace referenced input");

                string? referencedMetadata = null;
                if (candidate.EndsWith(".xcodeproj", StringComparison.OrdinalIgnoreCase))
                    referencedMetadata = Path.Combine(candidate, "project.pbxproj");
                else if (candidate.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase))
                    referencedMetadata = Path.Combine(candidate, "contents.xcworkspacedata");
                if (referencedMetadata is null)
                    continue;

                EnsureTrackedFile(repositoryRoot, referencedMetadata, "Apple workspace referenced project");
                if (!metadataPaths.Add(referencedMetadata))
                    continue;
                if (referencedMetadata.EndsWith("contents.xcworkspacedata", StringComparison.OrdinalIgnoreCase))
                    pending.Enqueue(referencedMetadata);
            }
        }
    }

    private static IEnumerable<string> EnumerateWorkspaceReferences(
        XElement? element,
        string workspaceRoot,
        string groupRoot)
    {
        if (element is null)
            yield break;

        var currentGroupRoot = groupRoot;
        if (element.Name.LocalName.Equals("Group", StringComparison.Ordinal))
        {
            var location = element.Attribute("location")?.Value;
            if (!string.IsNullOrWhiteSpace(location))
                currentGroupRoot = ResolveWorkspaceLocation(location!, workspaceRoot, groupRoot);
        }
        else if (element.Name.LocalName.Equals("FileRef", StringComparison.Ordinal))
        {
            var location = element.Attribute("location")?.Value;
            if (!string.IsNullOrWhiteSpace(location))
                yield return ResolveWorkspaceLocation(location!, workspaceRoot, groupRoot);
        }

        foreach (var child in element.Elements())
        {
            foreach (var reference in EnumerateWorkspaceReferences(child, workspaceRoot, currentGroupRoot))
                yield return reference;
        }
    }

    private static string ResolveWorkspaceLocation(string location, string workspaceRoot, string groupRoot)
    {
        var separator = location.IndexOf(':');
        var kind = separator < 0 ? "group" : location.Substring(0, separator);
        var value = separator < 0 ? location : location.Substring(separator + 1);
        return kind.ToLowerInvariant() switch
        {
            "absolute" => throw new InvalidOperationException(
                $"Absolute Xcode workspace references are not accepted for exact-source snapshot builds: {location}"),
            "container" => ResolvePath(workspaceRoot, value),
            "group" => ResolvePath(groupRoot, value),
            _ => throw new InvalidOperationException($"Unsupported Xcode workspace location kind '{kind}'.")
        };
    }

    private void RejectIgnoredAppleInputs(
        string repositoryRoot,
        string projectRoot,
        IReadOnlyCollection<string> metadataPaths,
        IReadOnlyCollection<string> generatedOutputPaths)
    {
        var metadata = metadataPaths
            .Where(File.Exists)
            .Select(File.ReadAllText)
            .ToArray();
        var synchronizedRoots = ResolveSynchronizedRoots(metadataPaths);
        var relativeProjectRoot = FrameworkCompatibility.GetRelativePath(repositoryRoot, projectRoot).Replace('\\', '/');
        var arguments = new List<string> { "ls-files", "--others", "--ignored", "--exclude-standard", "-z", "--" };
        arguments.Add(string.IsNullOrWhiteSpace(relativeProjectRoot) || relativeProjectRoot == "."
            ? "."
            : relativeProjectRoot);
        var ignored = RunGit(repositoryRoot, arguments.ToArray()).StdOut
            .Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var gitPath in ignored)
        {
            var normalized = gitPath.Replace('\\', '/');
            if (IsBenignIgnoredXcodeUserState(normalized))
                continue;

            var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, normalized));
            if (generatedOutputPaths.Any(output => IsPathAtOrWithin(fullPath, output)))
                continue;
            if (!File.Exists(fullPath) ||
                !IsPotentialAppleBuildInput(projectRoot, fullPath, metadata, synchronizedRoots))
                continue;

            throw new InvalidOperationException(
                $"Ignored Apple build input is not represented by the exact source commit: {normalized}. " +
                "Track the input, remove it from the Xcode build, or generate it only after the source-bound checkpoint begins.");
        }
    }

    private static bool IsBenignIgnoredXcodeUserState(string path)
    {
        if (!path.Contains("/xcuserdata/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("xcuserdata/", StringComparison.OrdinalIgnoreCase))
            return false;

        var fileName = Path.GetFileName(path);
        return fileName.Equals("xcschememanagement.plist", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("UserInterfaceState.xcuserstate", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Breakpoints_v2.xcbkptlist", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPotentialAppleBuildInput(
        string projectRoot,
        string fullPath,
        IReadOnlyCollection<string> metadata,
        IReadOnlyCollection<string> synchronizedRoots)
    {
        var relative = FrameworkCompatibility.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
        var fileName = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath);
        if (synchronizedRoots.Any(root => IsPathAtOrWithin(fullPath, root)))
            return true;
        if (AlwaysRejectedIgnoredExtensions.Contains(extension))
            return true;
        if (relative.Contains(".xcassets/", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains(".xcdatamodel", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains(".framework/", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains(".xcframework/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Sources/", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains("/Sources/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase) ||
            relative.Contains("/Plugins/", StringComparison.OrdinalIgnoreCase))
            return true;

        return metadata.Any(text =>
            text.Contains(relative, StringComparison.Ordinal) ||
            text.Contains(fileName, StringComparison.Ordinal));
    }

    private static string[] ResolveSynchronizedRoots(IEnumerable<string> metadataPaths)
        => ResolveObjectAwareSynchronizedRoots(metadataPaths);

    internal void EnsureTrackedFile(
        string repositoryRoot,
        string path,
        string name,
        string? capturedWorktreeBlob = null,
        byte[]? capturedWorktreeBytes = null,
        bool validateSwiftDeterminism = false,
        string? effectiveSourceExtension = null,
        string? assemblerWorkingDirectory = null)
    {
        var candidate = Path.GetFullPath(path);
        EnsurePathWithinRepository(repositoryRoot, candidate, name);
        if (!File.Exists(candidate))
            throw new FileNotFoundException($"{name} was not found: {candidate}", candidate);
        EnsureNoLinkedTraversal(repositoryRoot, candidate, name);

        if (capturedWorktreeBlob is null &&
            capturedWorktreeBytes is null &&
            _validatedTrackedFileBlobs.TryGetValue(candidate, out var validatedWorktreeBlob))
        {
            var currentWorktreeBlob = ComputeRawGitBlobId(repositoryRoot, candidate);
            if (!currentWorktreeBlob.Equals(validatedWorktreeBlob, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{name} changed after it was validated against the exact source commit: " +
                    FrameworkCompatibility.GetRelativePath(repositoryRoot, candidate).Replace('\\', '/'));
            }
            ValidateSourceLevelIncludes(
                repositoryRoot,
                candidate,
                validateSwiftDeterminism,
                currentWorktreeBlob,
                effectiveSourceExtension,
                assemblerWorkingDirectory);
            return;
        }

        var relative = FrameworkCompatibility.GetRelativePath(repositoryRoot, candidate).Replace('\\', '/');
        var tracked = RunGitAllowFailure(repositoryRoot, "ls-files", "-v", "--error-unmatch", "--", relative);
        if (!tracked.Succeeded)
            throw new InvalidOperationException($"{name} must be tracked at the exact source commit: {relative}");
        var indexEntry = tracked.StdOut
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (HasHiddenGitIndexState(indexEntry))
        {
            throw new InvalidOperationException(
                $"{name} uses a skip-worktree or assume-unchanged Git index flag and cannot be attested to the exact source commit: {relative}");
        }
        var headBlob = RunGitAllowFailure(repositoryRoot, "rev-parse", "--verify", $"HEAD:{relative}");
        if (!headBlob.Succeeded || string.IsNullOrWhiteSpace(headBlob.StdOut))
            throw new InvalidOperationException($"{name} is not present in the exact source commit: {relative}");
        EnsureNoCustomGitFilter(repositoryRoot, relative, name);
        var worktreeBlob = capturedWorktreeBlob ?? ComputeRawGitBlobId(repositoryRoot, candidate);
        var expectedBlob = headBlob.StdOut.Trim();
        if (!expectedBlob.Equals(worktreeBlob, StringComparison.OrdinalIgnoreCase))
        {
            var filteredWorktreeBlob = capturedWorktreeBytes is null
                ? ComputePathAwareGitBlobId(repositoryRoot, candidate, relative)
                : ComputePathAwareGitBlobId(repositoryRoot, capturedWorktreeBytes, relative);
            if (!expectedBlob.Equals(filteredWorktreeBlob, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{name} differs from the exact source commit: {relative}");
        }
        _validatedTrackedFileBlobs[candidate] = worktreeBlob;
        ValidateSourceLevelIncludes(
            repositoryRoot,
            candidate,
            validateSwiftDeterminism,
            worktreeBlob,
            effectiveSourceExtension,
            assemblerWorkingDirectory);
    }

    private static string DecodeTrackedText(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private Dictionary<string, string> ReadHeadTreeBlobIds(string repositoryRoot, string relativeRoot)
    {
        var comparer = GetPathComparer();
        var result = new Dictionary<string, string>(comparer);
        var entries = RunGit(repositoryRoot, "ls-tree", "-r", "-z", "HEAD", "--", relativeRoot)
            .StdOut.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0)
                continue;
            var header = entry.Substring(0, tab).Split(' ');
            if (header.Length != 3 || !header[1].Equals("blob", StringComparison.Ordinal))
                continue;
            var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, entry.Substring(tab + 1)));
            result[fullPath] = header[2];
        }
        return result;
    }

    private static bool HasHiddenGitIndexState(string? entry)
        => !string.IsNullOrWhiteSpace(entry) &&
           entry!.Length > 2 &&
           entry[1] == ' ' &&
           (entry[0] == 'S' || char.IsLower(entry[0]));

    private static void EnsureDirectoryWithinRepository(string repositoryRoot, string path, string name)
    {
        EnsurePathWithinRepository(repositoryRoot, path, name);
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"{name} was not found inside the exact checked-out source: {path}");
        EnsureNoLinkedTraversal(repositoryRoot, path, name);
    }

    private static void EnsurePathWithinRepository(string repositoryRoot, string path, string name)
    {
        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(path);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!candidate.Equals(root, comparison) &&
            !candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException($"{name} must resolve inside the exact checked-out source: {candidate}");
    }

    private static void EnsureNoLinkedTraversal(string repositoryRoot, string path, string name)
    {
        var root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(path);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"{name} must not traverse a symbolic link or reparse point: {current}");
            if (current.Equals(root, comparison))
                return;
            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException($"{name} escaped the checked-out source while validating path traversal.");
        }
    }

    private static string ResolvePath(string basePath, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));

    private static bool IsPathAtOrWithin(string path, string root)
    {
        var candidate = Path.GetFullPath(path);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return candidate.Equals(normalizedRoot, comparison) ||
               candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private string ReadExactHead(string repositoryRoot)
    {
        var sourceCommit = _git.GetHeadSha(repositoryRoot).Trim();
        var objectFormat = ReadGitObjectFormat(repositoryRoot);
        if (!GitObjectId.IsFullForObjectFormat(sourceCommit, objectFormat))
            throw new InvalidOperationException($"Apple release checkpoints require an exact repository HEAD for Git object format '{objectFormat}'.");
        return sourceCommit.ToLowerInvariant();
    }

    private string ReadGitObjectFormat(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        if (_gitObjectFormats.TryGetValue(root, out var objectFormat))
            return objectFormat;
        objectFormat = RunGit(root, "rev-parse", "--show-object-format").StdOut.Trim();
        _gitObjectFormats[root] = objectFormat;
        return objectFormat;
    }

    private ProcessRunResult RunGit(string repositoryRoot, params string[] arguments)
    {
        var result = RunGitAllowFailure(repositoryRoot, arguments);
        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new InvalidOperationException(
                $"git {string.Join(" ", arguments)} failed with exit code {result.ExitCode}. {detail.Trim()}");
        }
        return result;
    }

    private ProcessRunResult RunGitAllowFailure(string repositoryRoot, params string[] arguments)
        => _gitClient.RunRawAsync(repositoryRoot, arguments, TimeSpan.FromMinutes(2))
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static StringComparer GetPathComparer()
        => Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

internal sealed class AppleReleaseSourceTrustSnapshot
{
    internal AppleReleaseSourceTrustSnapshot(
        string sourceCommit,
        string[] generatedOutputPaths,
        string? exactConfigurationContent = null,
        string? exactConfigurationSha256 = null)
    {
        SourceCommit = sourceCommit;
        GeneratedOutputPaths = generatedOutputPaths;
        ExactConfigurationContent = exactConfigurationContent;
        ExactConfigurationSha256 = exactConfigurationSha256;
    }

    internal string SourceCommit { get; }

    internal string[] GeneratedOutputPaths { get; }

    internal string? ExactConfigurationContent { get; }

    internal string? ExactConfigurationSha256 { get; }
}
