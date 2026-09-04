using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static bool ShouldRefreshLockedRestoreOutputs(DotNetPublishPlan? plan)
        => plan?.NoRestoreInPublish != true;

    private static bool TryRefreshLockedRestoreOutputs(ProjectEvaluationRequest request)
    {
        string projectDirectory = Path.GetDirectoryName(request.ProjectPath)!;
        string temporaryLockFile = Path.Combine(
            Path.GetTempPath(),
            "powerforge-restore-lock-" + Guid.NewGuid().ToString("N") + ".json");
        var arguments = new List<string>
        {
            "restore",
            request.ProjectPath,
            "--force-evaluate",
            "--no-cache",
            "--nologo",
            "-p:RestorePackagesWithLockFile=true",
            "-p:RestoreLockedMode=false",
            "-p:NuGetLockFilePath=" + temporaryLockFile
        };
        if (request.Configuration is not null)
            arguments.Add("-p:Configuration=" + EscapeMsBuildPropertyValue(request.Configuration));
        string? requestedFramework = request.TargetFramework;
        if (!string.IsNullOrEmpty(requestedFramework) &&
            !ProjectDeclaresRequestedTargetFrameworkUnconditionally(
                request.ProjectPath,
                requestedFramework!))
        {
            arguments.Add("-p:TargetFramework=" + EscapeMsBuildPropertyValue(requestedFramework!));
        }
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("NuGetLockFilePath", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("RestoreLockedMode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
        }

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                projectDirectory,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(5));
            return process.ExitCode == 0 && !process.TimedOut;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryLockFile))
                    File.Delete(temporaryLockFile);
            }
            catch
            {
                // A leftover temporary lock cannot make an input trusted.
            }
        }
    }

    private static bool ProjectDeclaresRequestedTargetFrameworkUnconditionally(
        string projectPath,
        string requestedFramework)
    {
        try
        {
            XDocument project = XDocument.Load(projectPath, LoadOptions.None);
            return project.Descendants().Where(element =>
                (element.Name.LocalName.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                 element.Name.LocalName.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(element.Value) &&
                !element.AncestorsAndSelf().Any(candidate => candidate.Attributes().Any(attribute =>
                    attribute.Name.LocalName.Equals("Condition", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(attribute.Value))))
                .SelectMany(element => element.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                .Any(framework => framework.Trim().Equals(
                    requestedFramework,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveNearestDeclaredTargetFrameworkUnconditionally(
        string projectPath,
        string requestedFramework)
    {
        try
        {
            NuGetFramework requested = NuGetFramework.ParseFolder(requestedFramework);
            if (requested.IsUnsupported)
                return null;
            (string Text, NuGetFramework Framework)[] declared = XDocument.Load(projectPath, LoadOptions.None)
                .Descendants()
                .Where(element =>
                    (element.Name.LocalName.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                     element.Name.LocalName.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(element.Value) &&
                    !element.AncestorsAndSelf().Any(candidate => candidate.Attributes().Any(attribute =>
                        attribute.Name.LocalName.Equals("Condition", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(attribute.Value))))
                .SelectMany(element => element.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(value => value.Trim())
                .Where(value => value.Length > 0 && !value.Contains("$(", StringComparison.Ordinal))
                .Select(value => (Text: value, Framework: NuGetFramework.ParseFolder(value)))
                .Where(candidate => !candidate.Framework.IsUnsupported)
                .ToArray();
            if (declared.Length == 0)
                return null;

            NuGetFramework? nearest = new FrameworkReducer().GetNearest(
                requested,
                declared.Select(candidate => candidate.Framework));
            return nearest is null
                ? null
                : declared.First(candidate => candidate.Framework.Equals(nearest)).Text;
        }
        catch
        {
            return null;
        }
    }

    private static void AddEffectiveBuildControlInputs(
        string projectPath,
        JsonElement properties,
        HashSet<string> inputs,
        HashSet<string> sourceInputs)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        foreach (string candidate in EnumerateAncestorBuildControlCandidatePaths(projectPath))
            AddBuildControlCandidate(
                candidate,
                inputs,
                sourceInputs,
                new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));

        string? evaluatedLockFile = ReadEvaluatedPath(
            properties,
            "NuGetLockFilePath",
            projectDirectory);
        if (!string.IsNullOrWhiteSpace(evaluatedLockFile))
            AddBuildControlCandidate(
                evaluatedLockFile!,
                inputs,
                sourceInputs,
                new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));

        string? sdkPackageLockFile = ReadEvaluatedPath(
            properties,
            "PowerForgeSdkPackageLockFile",
            projectDirectory);
        if (!string.IsNullOrWhiteSpace(sdkPackageLockFile))
            AddBuildControlCandidate(
                sdkPackageLockFile!,
                inputs,
                sourceInputs,
                new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));

        string? msBuildToolsPath = ReadEvaluatedPath(properties, "MSBuildToolsPath", projectDirectory);
        if (!string.IsNullOrWhiteSpace(msBuildToolsPath))
            AddBuildControlCandidate(
                Path.Combine(msBuildToolsPath!, "MSBuild.rsp"),
                inputs,
                sourceInputs,
                new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));
    }

    internal static IEnumerable<string> EnumerateAncestorBuildControlCandidatePaths(string projectPath)
    {
        string projectDirectory = Path.GetFullPath(Path.GetDirectoryName(projectPath)!);
        yield return Path.Combine(projectDirectory, "packages.lock.json");

        string[] names =
        [
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "global.json",
            "NuGet.Config",
            "nuget.config",
            "Directory.Build.rsp",
            "MSBuild.rsp"
        ];
        string current = projectDirectory;
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        while (true)
        {
            foreach (string name in names)
                yield return Path.Combine(current, name);

            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, comparison))
                yield break;
            current = parent;
        }
    }

    private static void AddBuildControlCandidate(
        string path,
        HashSet<string> inputs,
        HashSet<string> sourceInputs,
        HashSet<string> visitedResponseFiles)
    {
        string fullPath = Path.GetFullPath(path);
        inputs.Add(fullPath);
        if (File.Exists(fullPath))
        {
            sourceInputs.Add(fullPath);
            if (Path.GetExtension(fullPath).Equals(".rsp", StringComparison.OrdinalIgnoreCase) &&
                visitedResponseFiles.Add(fullPath))
            {
                foreach (string line in File.ReadLines(fullPath))
                {
                    string value = line.Trim();
                    if (!value.StartsWith("@", StringComparison.Ordinal) || value.Length == 1)
                        continue;
                    string nested = value.Substring(1).Trim().Trim('"');
                    if (nested.Length == 0)
                        continue;
                    AddBuildControlCandidate(
                        Path.IsPathRooted(nested)
                            ? nested
                            : Path.Combine(Path.GetDirectoryName(fullPath)!, nested),
                        inputs,
                        sourceInputs,
                        visitedResponseFiles);
                }
            }
        }
    }

    private static string[] ReadDeclaredBuildInputCandidates(
        string projectPath,
        IEnumerable<string> evaluatedImports)
    {
        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var candidates = new HashSet<string>(comparison);
        var pending = new Queue<string>();
        var visited = new HashSet<string>(comparison);
        pending.Enqueue(Path.GetFullPath(projectPath));
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string? gitRoot = ReadGitText(projectDirectory, "rev-parse --show-toplevel");
        foreach (string import in evaluatedImports)
        {
            if (!string.IsNullOrWhiteSpace(gitRoot) &&
                ToGitRelativeExclusion(projectDirectory, gitRoot!, import) is not null)
            {
                pending.Enqueue(Path.GetFullPath(import));
            }
        }

        while (pending.Count > 0)
        {
            string inputFile = pending.Dequeue();
            if (!visited.Add(inputFile) || !File.Exists(inputFile))
                continue;

            try
            {
                XDocument document = XDocument.Load(inputFile, LoadOptions.None);
                foreach (XElement element in document.Descendants())
                {
                    bool isImport = element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase);
                    bool isAnalyzer = element.Name.LocalName.Equals("Analyzer", StringComparison.OrdinalIgnoreCase);
                    if (!isImport && !isAnalyzer)
                        continue;
                    XAttribute? attribute = element.Attribute(isImport ? "Project" : "Include");
                    foreach (string candidate in ResolveDeclaredBuildInputPaths(
                                 attribute?.Value,
                                 inputFile,
                                 projectDirectory))
                    {
                        candidates.Add(candidate);
                        if (isImport && File.Exists(candidate) &&
                            !string.IsNullOrWhiteSpace(gitRoot) &&
                            ToGitRelativeExclusion(projectDirectory, gitRoot!, candidate) is not null)
                        {
                            pending.Enqueue(candidate);
                        }
                    }
                }
            }
            catch
            {
                candidates.Add(inputFile + ".powerforge-provenance-unreadable");
            }
        }

        return candidates.ToArray();
    }

    private static IEnumerable<string> ResolveDeclaredBuildInputPaths(
        string? value,
        string declaringFile,
        string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        string declaringDirectory = Path.GetDirectoryName(declaringFile)!;
        foreach (string rawPath in value!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = rawPath.Trim()
                .Replace("$(MSBuildThisFileDirectory)", declaringDirectory + Path.DirectorySeparatorChar)
                .Replace("$(MSBuildProjectDirectory)", projectDirectory);
            if (candidate.Contains("$(", StringComparison.Ordinal) ||
                candidate.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                continue;
            }

            string fullPath = Path.GetFullPath(Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(declaringDirectory, candidate));
            yield return fullPath;
        }
    }

    private static void AddPackageFoldersFromAssets(
        JsonElement properties,
        string projectDirectory,
        HashSet<string> packageRoots)
    {
        string assetsPath = ReadEvaluatedPath(properties, "ProjectAssetsFile", projectDirectory)
            ?? Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
            return;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            if (!document.RootElement.TryGetProperty("packageFolders", out JsonElement folders) ||
                folders.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (JsonProperty folder in folders.EnumerateObject())
                packageRoots.Add(Path.GetFullPath(folder.Name));
        }
        catch
        {
            // An unreadable assets file leaves package inputs unverified and therefore fail closed.
        }
    }

    private static string? ReadEvaluatedPath(
        JsonElement properties,
        string name,
        string baseDirectory)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return null;
        }

        string value = property.GetString()!;
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value));
    }

    private static void AddClassifiedEvaluatedInput(
        string path,
        bool isSourceInput,
        HashSet<string> inputs,
        HashSet<string> sourceInputs,
        IEnumerable<string> generatedBuildRoots,
        VerifiedPackageInputCatalog? verifiedPackages,
        IEnumerable<string>? trustedBuildInfrastructureRoots)
    {
        string fullPath = Path.GetFullPath(path);
        if (IsGeneratedBuildInfrastructurePath(fullPath, generatedBuildRoots))
            return;

        bool isPackageInput = false;
        if (verifiedPackages?.TryVerify(fullPath, out isPackageInput) is true)
            return;

        inputs.Add(fullPath);
        if (File.Exists(fullPath) &&
            (isPackageInput ||
             (isSourceInput && !IsTrustedExternalBuildInfrastructurePath(
                 fullPath,
                 trustedBuildInfrastructureRoots))))
        {
            sourceInputs.Add(fullPath);
        }
    }

    private sealed partial class VerifiedPackageInputCatalog
    {
        private readonly string[] _packageRoots;
        private readonly IReadOnlyDictionary<string, string> _lockedPackageHashes;
        private readonly VerifiedPackageArchiveCache _archives;
        private readonly IReadOnlyDictionary<string, string> _archivePathsByPackageKey;
        private readonly HashSet<string> _sdkManagedArchivePaths;
        private readonly Dictionary<string, HashSet<string>> _controlledBuildInputsByArchive = new(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private string? _sdkEvidenceFailureReason;

        internal IEnumerable<string> SdkManagedPackageKeys => _archivePathsByPackageKey
            .Where(package => _sdkManagedArchivePaths.Contains(Path.GetFullPath(package.Value)))
            .Select(package => package.Key);

        private VerifiedPackageInputCatalog(
            IEnumerable<string> packageRoots,
            IReadOnlyDictionary<string, string> lockedPackageHashes,
            VerifiedPackageArchiveCache archives,
            IReadOnlyDictionary<string, string> archivePathsByPackageKey,
            IEnumerable<string> sdkManagedPackageKeys)
        {
            _packageRoots = packageRoots
                .Select(Path.GetFullPath)
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
            _lockedPackageHashes = lockedPackageHashes;
            _archives = archives;
            _archivePathsByPackageKey = archivePathsByPackageKey;
            _sdkManagedArchivePaths = new HashSet<string>(
                sdkManagedPackageKeys.Where(archivePathsByPackageKey.ContainsKey)
                    .Select(packageKey => archivePathsByPackageKey[packageKey]),
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        internal void InheritSdkManagedPackageKeys(IEnumerable<string> packageKeys)
        {
            _sdkManagedArchivePaths.UnionWith(
                packageKeys
                    .Where(_archivePathsByPackageKey.ContainsKey)
                    .Select(packageKey => Path.GetFullPath(_archivePathsByPackageKey[packageKey])));
        }

        internal static bool TryCreate(
            string projectPath,
            JsonElement properties,
            IEnumerable<string> packageRoots,
            VerifiedPackageArchiveCache archives,
            out VerifiedPackageInputCatalog? catalog)
            => TryCreateForEvaluation(
                projectPath,
                properties,
                packageRoots,
                archives,
                effectiveGlobalProperties: null,
                environmentVariables: null,
                out catalog);

        internal static bool TryCreateForEvaluation(
            string projectPath,
            JsonElement properties,
            IEnumerable<string> packageRoots,
            VerifiedPackageArchiveCache archives,
            IReadOnlyDictionary<string, string>? effectiveGlobalProperties,
            IReadOnlyDictionary<string, string?>? environmentVariables,
            out VerifiedPackageInputCatalog? catalog)
            => TryCreateForEvaluation(
                projectPath,
                properties,
                packageRoots,
                archives,
                effectiveGlobalProperties,
                environmentVariables,
                includeSdkPackageEvidence: true,
                out catalog);

        internal static bool TryCreateForEvaluation(
            string projectPath,
            JsonElement properties,
            IEnumerable<string> packageRoots,
            VerifiedPackageArchiveCache archives,
            IReadOnlyDictionary<string, string>? effectiveGlobalProperties,
            IReadOnlyDictionary<string, string?>? environmentVariables,
            bool includeSdkPackageEvidence,
            out VerifiedPackageInputCatalog? catalog)
            => TryCreateForEvaluationDetailed(
                projectPath,
                properties,
                packageRoots,
                archives,
                effectiveGlobalProperties,
                environmentVariables,
                includeSdkPackageEvidence,
                out catalog,
                out _);

        internal static bool TryCreateForEvaluationDetailed(
            string projectPath,
            JsonElement properties,
            IEnumerable<string> packageRoots,
            VerifiedPackageArchiveCache archives,
            IReadOnlyDictionary<string, string>? effectiveGlobalProperties,
            IReadOnlyDictionary<string, string?>? environmentVariables,
            bool includeSdkPackageEvidence,
            out VerifiedPackageInputCatalog? catalog,
            out string? failureReason)
        {
            catalog = null;
            failureReason = null;
            string projectDirectory = Path.GetDirectoryName(projectPath)!;
            string lockFilePath = ReadEvaluatedPath(properties, "NuGetLockFilePath", projectDirectory)
                ?? Path.Combine(projectDirectory, "packages.lock.json");
            string[] roots = packageRoots
                .Where(Directory.Exists)
                .Select(Path.GetFullPath)
                .Where(root => !IsSameOrBelowBuildInputPath(projectDirectory, root))
                .ToArray();
            var allRoots = new HashSet<string>(roots, IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            string? environmentPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (!string.IsNullOrWhiteSpace(environmentPackages) && Directory.Exists(environmentPackages))
                allRoots.Add(Path.GetFullPath(environmentPackages));
            string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                string defaultPackages = Path.Combine(userProfile, ".nuget", "packages");
                if (Directory.Exists(defaultPackages))
                    allRoots.Add(Path.GetFullPath(defaultPackages));
            }

            bool hasCommittedLock = TryReadLockedPackageHashes(
                lockFilePath,
                out Dictionary<string, string> hashes);
            string? sdkPackageLockFile = ReadEvaluatedPath(
                properties,
                "PowerForgeSdkPackageLockFile",
                projectDirectory);
            var sdkPackageLockHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(sdkPackageLockFile) &&
                !TryReadPowerForgeSdkPackageHashes(sdkPackageLockFile!, out sdkPackageLockHashes))
            {
                failureReason = "the PowerForge SDK package lock could not be read";
                return false;
            }
            foreach (KeyValuePair<string, string> package in sdkPackageLockHashes)
            {
                if (hashes.TryGetValue(package.Key, out string? existing) &&
                    !string.Equals(existing, package.Value, StringComparison.Ordinal))
                {
                    failureReason = $"package '{package.Key}' has conflicting committed hashes";
                    return false;
                }
                hashes[package.Key] = package.Value;
            }
            var committedPackageHashes = new Dictionary<string, string>(
                hashes,
                StringComparer.OrdinalIgnoreCase);
            var sdkDownloadPackageHashes = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            var sdkManagedPackageKeys = new HashSet<string>(
                sdkPackageLockHashes.Keys,
                StringComparer.OrdinalIgnoreCase);
            if (!TryPrimeLockedPackageArchivesDetailed(
                    allRoots,
                    committedPackageHashes,
                    archives,
                    out Dictionary<string, string> archivePathsByPackageKey,
                    out failureReason))
            {
                return false;
            }
            string? sdkEvidenceFailureReason = null;
            string? sdkEvidenceRoot = includeSdkPackageEvidence
                ? AddSdkManagedPackageHashes(
                    projectPath,
                    properties,
                    allRoots,
                    sdkDownloadPackageHashes,
                    sdkManagedPackageKeys,
                    effectiveGlobalProperties,
                    environmentVariables,
                    archivePathsByPackageKey,
                    archives,
                    out sdkEvidenceFailureReason)
                : null;
            try
            {
                if (allRoots.Count == 0)
                    return !hasCommittedLock && sdkDownloadPackageHashes.Count == 0;
                if (!TryPrimeLockedPackageArchivesDetailed(
                        allRoots,
                        sdkDownloadPackageHashes,
                        archives,
                        out Dictionary<string, string> sdkDownloadArchivePaths,
                        out string? sdkArchiveFailureReason))
                {
                    failureReason = "SDK-managed " + (sdkArchiveFailureReason ?? "package archives could not be verified");
                    return false;
                }
                foreach (KeyValuePair<string, string> entry in sdkDownloadArchivePaths)
                {
                    if (archivePathsByPackageKey.TryGetValue(entry.Key, out string? existingPath) &&
                        !Path.GetFullPath(existingPath).Equals(
                            Path.GetFullPath(entry.Value),
                            IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        if (HaveSameVerifiedPackageHash(
                                entry.Key,
                                committedPackageHashes,
                                sdkDownloadPackageHashes))
                        {
                            // The SDK evidence restore intentionally uses an isolated package
                            // root. Keep the committed archive as canonical when both independently
                            // verified archives contain the same package content.
                            continue;
                        }
                        failureReason = $"package '{entry.Key}' resolved to conflicting archive locations " +
                                        $"with different verified hashes (committed {DescribePackageHash(entry.Key, committedPackageHashes)}, " +
                                        $"SDK evidence {DescribePackageHash(entry.Key, sdkDownloadPackageHashes)})";
                        return false;
                    }
                    archivePathsByPackageKey[entry.Key] = entry.Value;
                }
                foreach (KeyValuePair<string, string> entry in sdkDownloadPackageHashes)
                    AddPackageHash(entry.Key, entry.Value, hashes);
                catalog = new VerifiedPackageInputCatalog(
                    allRoots,
                    hashes,
                    archives,
                    archivePathsByPackageKey,
                    sdkManagedPackageKeys);
                catalog._sdkEvidenceFailureReason = sdkEvidenceFailureReason;
                return true;
            }
            finally
            {
                TryDeleteSdkEvidenceRoot(sdkEvidenceRoot);
            }
        }

        private static bool HaveSameVerifiedPackageHash(
            string packageKey,
            IReadOnlyDictionary<string, string> committedPackageHashes,
            IReadOnlyDictionary<string, string> sdkPackageHashes)
            => committedPackageHashes.TryGetValue(packageKey, out string? committedHash) &&
               sdkPackageHashes.TryGetValue(packageKey, out string? sdkHash) &&
               !string.IsNullOrWhiteSpace(committedHash) &&
               string.Equals(committedHash, sdkHash, StringComparison.Ordinal);

        private static string DescribePackageHash(
            string packageKey,
            IReadOnlyDictionary<string, string> hashes)
        {
            if (!hashes.TryGetValue(packageKey, out string? hash) || string.IsNullOrWhiteSpace(hash))
                return "missing";
            return hash!.Length <= 12 ? hash : hash.Substring(0, 12);
        }

        internal bool TryVerify(string path, out bool isPackageInput)
        {
            string fullPath = Path.GetFullPath(path);
            foreach (string root in _packageRoots)
            {
                if (!IsSameOrBelowBuildInputPath(fullPath, root))
                    continue;

                isPackageInput = true;
                return TryVerifyBelowRoot(fullPath, root);
            }

            isPackageInput = false;
            return false;
        }

        internal bool TryMapControlledPackageInput(
            string controlledPath,
            string controlledPackageRoot,
            out string mappedPath)
        {
            mappedPath = string.Empty;
            try
            {
                string fullControlledPath = Path.GetFullPath(controlledPath);
                if (!IsSameOrBelowBuildInputPath(fullControlledPath, controlledPackageRoot))
                    return false;
                string relative = FrameworkCompatibility.GetRelativePath(
                    controlledPackageRoot,
                    fullControlledPath);
                foreach (string packageRoot in _packageRoots)
                {
                    string candidate = Path.GetFullPath(Path.Combine(packageRoot, relative));
                    if (!IsSameOrBelowBuildInputPath(candidate, packageRoot) ||
                        (!File.Exists(candidate) && !Directory.Exists(candidate)) ||
                        !TryVerifyBelowRoot(candidate, packageRoot))
                    {
                        continue;
                    }
                    mappedPath = candidate;
                    return true;
                }
                return false;
            }
            catch
            {
                mappedPath = string.Empty;
                return false;
            }
        }

        private bool TryVerifyBelowRoot(string path, string root)
        {
            try
            {
                if (IsReparsePoint(root) || HasReparsePointBelowRoot(path, root))
                    return false;

                string relative = FrameworkCompatibility.GetRelativePath(root, path)
                    .Replace('\\', '/')
                    .Trim('/');
                string[] segments = relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length < 3 || segments.Any(segment => segment == ".."))
                    return false;

                string packageId = segments[0];
                string packageVersion = segments[1];
                string packageKey = packageId + "|" + packageVersion;
                if (!_lockedPackageHashes.TryGetValue(packageKey, out string? expectedHash) ||
                    string.IsNullOrWhiteSpace(expectedHash))
                {
                    return false;
                }

                if (!_archivePathsByPackageKey.TryGetValue(packageKey, out string? archivePath) ||
                    string.IsNullOrWhiteSpace(archivePath))
                {
                    return false;
                }
                VerifiedPackageArchive? archive = _archives.TryGetOrOpen(archivePath!, expectedHash);
                if (archive is null)
                    return false;

                string packageRelativePath = string.Join("/", segments.Skip(2));
                return Directory.Exists(path)
                    ? archive.VerifyExtractedDirectory(packageRelativePath, path)
                    : archive.VerifyExtractedFile(packageRelativePath, path);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadLockedPackageHashes(
            string lockFilePath,
            out Dictionary<string, string> hashes)
        {
            hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(lockFilePath));
                if (!document.RootElement.TryGetProperty("dependencies", out JsonElement frameworks) ||
                    frameworks.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (JsonProperty framework in frameworks.EnumerateObject())
                {
                    if (framework.Value.ValueKind != JsonValueKind.Object)
                        continue;
                    foreach (JsonProperty package in framework.Value.EnumerateObject())
                    {
                        if (package.Value.ValueKind != JsonValueKind.Object ||
                            !package.Value.TryGetProperty("resolved", out JsonElement resolved) ||
                            resolved.ValueKind != JsonValueKind.String ||
                            !package.Value.TryGetProperty("contentHash", out JsonElement contentHash) ||
                            contentHash.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        string key = package.Name + "|" + resolved.GetString();
                        string value = contentHash.GetString() ?? string.Empty;
                        if (hashes.TryGetValue(key, out string? existing) &&
                            !string.Equals(existing, value, StringComparison.Ordinal))
                        {
                            hashes[key] = string.Empty;
                        }
                        else
                        {
                            hashes[key] = value;
                        }
                    }
                }

                return hashes.Count > 0;
            }
            catch
            {
                hashes.Clear();
                return false;
            }
        }

        private static bool TryReadPowerForgeSdkPackageHashes(
            string lockFilePath,
            out Dictionary<string, string> hashes)
        {
            hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(lockFilePath));
                if (!document.RootElement.TryGetProperty("version", out JsonElement version) ||
                    version.ValueKind != JsonValueKind.Number ||
                    !version.TryGetInt32(out int schemaVersion) ||
                    schemaVersion != 1 ||
                    !document.RootElement.TryGetProperty(
                        "sdkManagedPackages",
                        out JsonElement packages) ||
                    packages.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (JsonProperty package in packages.EnumerateObject())
                {
                    if (string.IsNullOrWhiteSpace(package.Name) ||
                        package.Name.IndexOf('|') >= 0 ||
                        package.Value.ValueKind != JsonValueKind.Object ||
                        !package.Value.TryGetProperty("version", out JsonElement packageVersion) ||
                        packageVersion.ValueKind != JsonValueKind.String ||
                        !NuGetVersion.TryParse(packageVersion.GetString(), out NuGetVersion? parsedVersion) ||
                        !package.Value.TryGetProperty("contentHash", out JsonElement contentHash) ||
                        contentHash.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(contentHash.GetString()))
                    {
                        hashes.Clear();
                        return false;
                    }

                    string key = package.Name + "|" + parsedVersion!.ToNormalizedString();
                    if (hashes.ContainsKey(key))
                    {
                        hashes.Clear();
                        return false;
                    }
                    hashes.Add(key, contentHash.GetString()!);
                }

                return true;
            }
            catch
            {
                hashes.Clear();
                return false;
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }
    }

    private sealed partial class VerifiedPackageArchiveCache : IDisposable
    {
        private readonly Dictionary<string, CacheEntry> _archives = new(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private readonly Dictionary<string, CacheEntry> _archivesByContentHash = new(StringComparer.Ordinal);

        internal VerifiedPackageArchive? TryGetOrOpen(string path, string expectedContentHash)
        {
            string fullPath = Path.GetFullPath(path);
            if (_archives.TryGetValue(fullPath, out CacheEntry? cached))
            {
                return string.Equals(cached.ExpectedContentHash, expectedContentHash, StringComparison.Ordinal)
                    ? cached.Archive
                    : null;
            }

            if (_archivesByContentHash.TryGetValue(expectedContentHash, out CacheEntry? cachedByHash))
            {
                _archives.Add(fullPath, cachedByHash);
                return cachedByHash.Archive;
            }

            VerifiedPackageArchive? archive = VerifiedPackageArchive.TryOpen(fullPath, expectedContentHash);
            if (archive is not null)
            {
                var entry = new CacheEntry(fullPath, expectedContentHash, archive);
                _archives.Add(fullPath, entry);
                _archivesByContentHash.Add(expectedContentHash, entry);
            }
            return archive;
        }

        public void Dispose()
        {
            foreach (CacheEntry cached in _archivesByContentHash.Values)
                cached.Archive.Dispose();
            _archives.Clear();
            _archivesByContentHash.Clear();
        }

        private sealed class CacheEntry
        {
            internal CacheEntry(
                string sourcePath,
                string expectedContentHash,
                VerifiedPackageArchive archive)
            {
                SourcePath = sourcePath;
                ExpectedContentHash = expectedContentHash;
                Archive = archive;
            }

            internal string SourcePath { get; }

            internal string ExpectedContentHash { get; }

            internal VerifiedPackageArchive Archive { get; }
        }
    }

    private sealed partial class VerifiedPackageArchive : IDisposable
    {
        private readonly FileStream _stream;
        private readonly ZipArchive _archive;
        private readonly IReadOnlyDictionary<string, ZipArchiveEntry> _entries;
        private readonly Dictionary<string, byte[]> _entryHashes;
        private readonly Dictionary<string, ExtractedFileHash> _extractedFileHashes;

        private VerifiedPackageArchive(
            FileStream stream,
            ZipArchive archive,
            IReadOnlyDictionary<string, ZipArchiveEntry> entries)
        {
            _stream = stream;
            _archive = archive;
            _entries = entries;
            _entryHashes = new Dictionary<string, byte[]>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            _extractedFileHashes = new Dictionary<string, ExtractedFileHash>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        internal static VerifiedPackageArchive? TryOpen(string path, string expectedContentHash)
        {
            FileStream? stream = null;
            ZipArchive? archive = null;
            try
            {
                string snapshotPath = Path.Combine(
                    Path.GetTempPath(),
                    "powerforge-package-" + Guid.NewGuid().ToString("N") + ".nupkg");
                stream = new FileStream(
                    snapshotPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    81920,
                    FileOptions.DeleteOnClose | FileOptions.SequentialScan);
                using (FileStream source = File.Open(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.ReadWrite | FileShare.Delete))
                {
                    source.CopyTo(stream);
                }
                stream.Flush(flushToDisk: true);
                stream.Position = 0;
                using (var packageReader = new PackageArchiveReader(stream, leaveStreamOpen: true))
                {
                    PrimarySignature? signature = packageReader
                        .GetPrimarySignatureAsync(CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    if (signature is not null)
                    {
                        packageReader
                            .ValidateIntegrityAsync(signature.SignatureContent, CancellationToken.None)
                            .GetAwaiter()
                            .GetResult();
                    }
                    string actualHash = packageReader.GetContentHash(CancellationToken.None);
                    if (!string.Equals(actualHash, expectedContentHash, StringComparison.Ordinal))
                    {
                        stream.Dispose();
                        stream = null;
                        return null;
                    }
                }

                stream.Position = 0;
                archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                var entries = new Dictionary<string, ZipArchiveEntry>(
                    IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
                {
                    string name = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (entries.ContainsKey(name))
                    {
                        archive.Dispose();
                        stream.Dispose();
                        return null;
                    }
                    entries.Add(name, entry);
                }

                return new VerifiedPackageArchive(stream, archive, entries);
            }
            catch
            {
                archive?.Dispose();
                stream?.Dispose();
                return null;
            }
        }

        internal bool VerifyExtractedFile(string relativePath, string extractedPath)
        {
            string normalizedRelativePath = relativePath.Replace('\\', '/').TrimStart('/');
            if (!_entries.TryGetValue(normalizedRelativePath, out ZipArchiveEntry? entry))
                return false;

            var file = new FileInfo(extractedPath);
            if (!file.Exists || file.Length != entry.Length)
                return false;

            string fullExtractedPath = file.FullName;
            long lastWriteTimeUtcTicks = file.LastWriteTimeUtc.Ticks;
            if (_extractedFileHashes.TryGetValue(fullExtractedPath, out ExtractedFileHash? cached) &&
                cached.Length == file.Length &&
                cached.LastWriteTimeUtcTicks == lastWriteTimeUtcTicks)
            {
                return GetEntryHash(normalizedRelativePath, entry).SequenceEqual(cached.Hash);
            }

            using FileStream fileStream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            using SHA256 fileHasher = SHA256.Create();
            byte[] actual = fileHasher.ComputeHash(fileStream);
            byte[] expected = GetEntryHash(normalizedRelativePath, entry);
            if (expected.SequenceEqual(actual))
            {
                _extractedFileHashes[fullExtractedPath] = new ExtractedFileHash(
                    file.Length,
                    lastWriteTimeUtcTicks,
                    actual);
                return true;
            }

            _extractedFileHashes.Remove(fullExtractedPath);
            return false;
        }

        internal bool VerifyExtractedDirectory(string relativePath, string extractedPath)
        {
            try
            {
                string normalizedPrefix = relativePath.Replace('\\', '/').Trim('/');
                if (normalizedPrefix.Length > 0)
                    normalizedPrefix += "/";
                string[] expectedEntries = _entries.Keys
                    .Where(name => name.StartsWith(
                        normalizedPrefix,
                        IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    .ToArray();
                if (expectedEntries.Length == 0 || !Directory.Exists(extractedPath))
                    return false;

                string fullDirectory = Path.GetFullPath(extractedPath);
                string[] actualFiles = Directory.GetFiles(
                    fullDirectory,
                    "*",
                    SearchOption.AllDirectories);
                if (actualFiles.Length != expectedEntries.Length)
                    return false;

                var expected = new HashSet<string>(
                    expectedEntries,
                    IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                foreach (string actualFile in actualFiles)
                {
                    string entryName = normalizedPrefix + FrameworkCompatibility.GetRelativePath(
                            fullDirectory,
                            actualFile)
                        .Replace('\\', '/');
                    if (!expected.Contains(entryName) ||
                        !VerifyExtractedFile(entryName, actualFile))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private byte[] GetEntryHash(string relativePath, ZipArchiveEntry entry)
        {
            if (_entryHashes.TryGetValue(relativePath, out byte[]? hash))
                return hash;

            using Stream entryStream = entry.Open();
            using SHA256 entryHasher = SHA256.Create();
            hash = entryHasher.ComputeHash(entryStream);
            _entryHashes.Add(relativePath, hash);
            return hash;
        }

        public void Dispose()
        {
            _archive.Dispose();
            _stream.Dispose();
        }

        private sealed class ExtractedFileHash
        {
            internal ExtractedFileHash(long length, long lastWriteTimeUtcTicks, byte[] hash)
            {
                Length = length;
                LastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
                Hash = hash;
            }

            internal long Length { get; }

            internal long LastWriteTimeUtcTicks { get; }

            internal byte[] Hash { get; }
        }
    }
}
