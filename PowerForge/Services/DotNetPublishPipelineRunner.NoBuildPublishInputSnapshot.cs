using System.Security.Cryptography;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static NoBuildPublishInputSnapshot? CreateNoBuildPublishInputSnapshot(
        DotNetPublishPlan plan,
        string targetName,
        string framework,
        string runtime,
        DotNetPublishStyle? styleOverride,
        SourceProvenance provenance)
    {
        if (provenance.NoBuildPublishInputs.Length == 0)
            return null;

        DotNetPublishTargetPlan target = plan.Targets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, targetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Target not found: {targetName}");
        DotNetPublishStyle style = styleOverride ?? target.Publish.Style;
        string effectiveFramework = string.IsNullOrWhiteSpace(framework)
            ? target.Publish.Framework
            : framework.Trim();
        string evaluationKey = BuildPublishEvaluationRequestKey(
            plan,
            target,
            effectiveFramework,
            runtime,
            style);
        NoBuildPublishInput[] inputs = SelectPublishInputSnapshotCandidates(
                plan.NoBuildInPublish,
                provenance.NoBuildPublishInputs)
            .Where(input => string.Equals(input.EvaluationKey, evaluationKey, StringComparison.Ordinal))
            .ToArray();
        if (inputs.Length == 0)
            return null;

        string[] evaluatedCustomAfterTargets = inputs
            .Select(input => input.CustomAfterMicrosoftCommonTargets)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        if (evaluatedCustomAfterTargets.Length > 1)
        {
            throw new InvalidOperationException(
                "The no-build publish inputs disagree about the evaluated CustomAfterMicrosoftCommonTargets value.");
        }
        string? existingCustomAfterTargets = evaluatedCustomAfterTargets.Length == 1
            ? evaluatedCustomAfterTargets[0]
            : null;
        return NoBuildPublishInputSnapshot.Create(inputs, existingCustomAfterTargets);
    }

    internal static NoBuildPublishInput[] SelectPublishInputSnapshotCandidates(
        bool noBuildInPublish,
        IEnumerable<NoBuildPublishInput> inputs)
        => inputs
            .Where(input => noBuildInPublish || input.IsPackageBacked)
            .ToArray();

    internal static string BuildPublishEvaluationRequestKey(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        string framework,
        string runtime,
        DotNetPublishStyle style)
    {
        var combination = new DotNetPublishTargetCombination
        {
            Framework = framework,
            Runtime = runtime,
            Style = style
        };
        Dictionary<string, string> properties = BuildPublishEvaluationProperties(
            plan,
            target,
            combination);
        IReadOnlyDictionary<string, string>? sdkPackageEvidenceGlobalProperties =
            style == DotNetPublishStyle.SelfContained
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PublishSingleFile"] = "true"
                }
                : null;
        return new ProjectEvaluationRequest(
                Path.GetFullPath(target.ProjectPath),
                framework,
                plan.Configuration,
                properties,
                plan.EnvironmentVariables,
                plan.ControlledBuildEnvironmentVariableNames,
                plan.TrustedBuildPackages,
                sdkPackageEvidenceGlobalProperties: sdkPackageEvidenceGlobalProperties,
                requiresPrebuiltProjectReferenceOutputProof:
                    RequiresPrebuiltProjectReferenceOutputProof(plan, target, combination),
                evaluationScopeRootPath: target.ProjectPath)
            .BuildVisitKey();
    }

    internal sealed class NoBuildPublishInputSnapshot : IDisposable
    {
        private readonly string _root;
        private readonly List<FileStream> _leases;
        private readonly IReadOnlyDictionary<string, SnapshotFileState> _expectedStates;
        private readonly FileSystemWatcher? _watcher;
#if NET8_0_OR_GREATER
        private readonly MacOsVnodeMutationMonitor? _macOsMonitor;
#endif
        private string? _changeDescription;
        private int _changed;
        private bool _disposed;

        private NoBuildPublishInputSnapshot(
            string root,
            string targetsPath,
            List<FileStream> leases,
            IReadOnlyDictionary<string, SnapshotFileState> expectedStates,
            IReadOnlyDictionary<string, FileStream> leasedFiles)
        {
            _root = root;
            TargetsPath = targetsPath;
            _leases = leases;
            _expectedStates = expectedStates;
#if NET8_0_OR_GREATER
            if (OperatingSystem.IsMacOS())
            {
                _macOsMonitor = new MacOsVnodeMutationMonitor(leasedFiles, RecordChange);
                return;
            }
#endif
            _watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.Security
            };
            _watcher.Changed += MarkChanged;
            _watcher.Created += MarkChanged;
            _watcher.Deleted += MarkChanged;
            _watcher.Renamed += MarkChanged;
            _watcher.Error += MarkChanged;
            _watcher.EnableRaisingEvents = true;
        }

        internal string TargetsPath { get; }

        internal static NoBuildPublishInputSnapshot Create(
            IReadOnlyCollection<NoBuildPublishInput> inputs,
            string? existingCustomAfterTargets)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "powerforge-no-build-publish-" + Guid.NewGuid().ToString("N"));
            string inputRoot = Path.Combine(root, "inputs");
            var leases = new List<FileStream>();
            var leasedFiles = new Dictionary<string, FileStream>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var expectedStates = new Dictionary<string, SnapshotFileState>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            try
            {
                Directory.CreateDirectory(inputRoot);
                var mappedInputs = new List<(NoBuildPublishInput[] Inputs, string SnapshotPath)>();
                int index = 0;
                foreach (IGrouping<string, NoBuildPublishInput> inputGroup in inputs.GroupBy(
                             input => Path.GetFullPath(input.FullPath),
                             IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
                {
                    NoBuildPublishInput[] groupedInputs = inputGroup.ToArray();
                    NoBuildPublishInput input = groupedInputs[0];
                    if (groupedInputs.Any(candidate =>
                            !string.Equals(candidate.Sha256, input.Sha256, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate no-build publish inputs disagree about the proven hash: {input.FullPath}.");
                    }
                    if (groupedInputs.Any(candidate => candidate.UnixFileMode != input.UnixFileMode))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate no-build publish inputs disagree about the proven Unix mode: {input.FullPath}.");
                    }
                    string snapshotDirectory = Path.Combine(
                        inputRoot,
                        index++.ToString("D6", System.Globalization.CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(snapshotDirectory);
                    string snapshotPath = Path.Combine(
                        snapshotDirectory,
                        Path.GetFileName(input.FullPath));
                    string actualSha256 = CopyAndHashSnapshot(
                        input.FullPath,
                        snapshotPath,
                        input.UnixFileMode,
                        leases,
                        leasedFiles);
                    if (!string.Equals(actualSha256, input.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"A no-build publish input changed after controlled proof: {input.FullPath}.");
                    }
                    expectedStates[snapshotPath] = SnapshotFileState.Capture(
                        snapshotPath,
                        actualSha256);
                    mappedInputs.Add((groupedInputs, snapshotPath));
                }

                string targetsPath = Path.Combine(root, "PowerForge.NoBuildPublishInputs.targets");
                WriteSnapshotTargets(targetsPath, mappedInputs, existingCustomAfterTargets);
                FileStream targetsLease = new(
                    targetsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                leases.Add(targetsLease);
                leasedFiles[targetsPath] = targetsLease;
                expectedStates[targetsPath] = SnapshotFileState.Capture(targetsPath);
                return new NoBuildPublishInputSnapshot(
                    root,
                    targetsPath,
                    leases,
                    expectedStates,
                    leasedFiles);
            }
            catch
            {
                foreach (FileStream lease in leases)
                    lease.Dispose();
                TryDeleteSnapshotRoot(root);
                throw;
            }
        }

        internal void ValidateUnchanged()
        {
#if NET8_0_OR_GREATER
            _macOsMonitor?.Synchronize();
#endif
            if (Volatile.Read(ref _changed) != 0)
            {
                throw new InvalidOperationException(
                    "A proven no-build publish snapshot was mutated while dotnet publish was running: " +
                    (_changeDescription ?? "the filesystem watcher reported an unspecified change") + ".");
            }
            foreach (KeyValuePair<string, SnapshotFileState> entry in _expectedStates)
            {
                if (!entry.Value.Matches(entry.Key))
                {
                    throw new InvalidOperationException(
                        $"A proven no-build publish snapshot changed while dotnet publish was running: {entry.Key}.");
                }
            }
#if NET8_0_OR_GREATER
            _macOsMonitor?.Synchronize();
#endif
            if (Volatile.Read(ref _changed) != 0)
            {
                throw new InvalidOperationException(
                    "A proven no-build publish snapshot was mutated while dotnet publish was running: " +
                    (_changeDescription ?? "the filesystem watcher reported an unspecified change") + ".");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
#if NET8_0_OR_GREATER
            _macOsMonitor?.Dispose();
#endif
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }
            foreach (FileStream lease in _leases)
                lease.Dispose();
            TryDeleteSnapshotRoot(_root);
        }

        private void MarkChanged(object sender, FileSystemEventArgs args)
            => RecordChange($"{args.ChangeType} '{args.FullPath}'");

        private void MarkChanged(object sender, RenamedEventArgs args)
            => RecordChange($"renamed '{args.OldFullPath}' to '{args.FullPath}'");

        private void MarkChanged(object sender, ErrorEventArgs args)
            => RecordChange(
                "the filesystem watcher failed: " +
                (args.GetException()?.Message ?? "its buffer overflowed"));

        private void RecordChange(string description)
        {
            Interlocked.CompareExchange(ref _changeDescription, description, null);
            Interlocked.Exchange(ref _changed, 1);
        }

        private sealed class SnapshotFileState
        {
            private SnapshotFileState(string sha256, long length, DateTime lastWriteTimeUtc, int? unixFileMode)
            {
                Sha256 = sha256;
                Length = length;
                LastWriteTimeUtc = lastWriteTimeUtc;
                UnixFileMode = unixFileMode;
            }

            private string Sha256 { get; }

            private long Length { get; }

            private DateTime LastWriteTimeUtc { get; }

            private int? UnixFileMode { get; }

            internal static SnapshotFileState Capture(string path, string? sha256 = null)
            {
                var info = new FileInfo(path);
                return new SnapshotFileState(
                    sha256 ?? ComputeSha256Hex(File.ReadAllBytes(path)),
                    info.Length,
                    info.LastWriteTimeUtc,
                    ReadUnixFileMode(path));
            }

            internal bool Matches(string path)
            {
                try
                {
                    var info = new FileInfo(path);
                    return info.Exists &&
                           info.Length == Length &&
                           info.LastWriteTimeUtc == LastWriteTimeUtc &&
                           ReadUnixFileMode(path) == UnixFileMode &&
                           string.Equals(
                               ComputeSha256Hex(File.ReadAllBytes(path)),
                               Sha256,
                               StringComparison.OrdinalIgnoreCase);
                }
                catch (IOException)
                {
                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }

            private static int? ReadUnixFileMode(string path)
            {
#if NET8_0_OR_GREATER
                return OperatingSystem.IsWindows() ? null : (int)File.GetUnixFileMode(path);
#else
                return null;
#endif
            }
        }

        private static string CopyAndHashSnapshot(
            string sourcePath,
            string snapshotPath,
            int? expectedUnixFileMode,
            ICollection<FileStream> leases,
            IDictionary<string, FileStream> leasedFiles)
        {
            DateTime sourceLastWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath);
#if NET8_0_OR_GREATER
            UnixFileMode? sourceUnixFileMode = null;
            if (!OperatingSystem.IsWindows())
            {
                sourceUnixFileMode = File.GetUnixFileMode(sourcePath);
                if (expectedUnixFileMode.HasValue &&
                    (int)sourceUnixFileMode.Value != expectedUnixFileMode.Value)
                {
                    throw new InvalidOperationException(
                        $"A no-build publish input Unix mode changed after controlled proof: {sourcePath}.");
                }
            }
#endif
            using FileStream source = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var snapshot = new FileStream(
                snapshotPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                snapshot.Write(buffer, 0, read);
            }
            snapshot.Flush(flushToDisk: true);
            snapshot.Dispose();
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows() &&
                expectedUnixFileMode.HasValue &&
                (int)File.GetUnixFileMode(sourcePath) != expectedUnixFileMode.Value)
            {
                throw new InvalidOperationException(
                    $"A no-build publish input Unix mode changed while it was snapshotted: {sourcePath}.");
            }
#endif
            File.SetLastWriteTimeUtc(snapshotPath, sourceLastWriteTimeUtc);
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows() && sourceUnixFileMode.HasValue)
                File.SetUnixFileMode(snapshotPath, sourceUnixFileMode.Value);
#endif
            var lease = new FileStream(
                snapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            leases.Add(lease);
            leasedFiles[snapshotPath] = lease;
            return ToUpperHex(hash.GetHashAndReset());
        }

        private static void WriteSnapshotTargets(
            string targetsPath,
            IReadOnlyCollection<(NoBuildPublishInput[] Inputs, string SnapshotPath)> mappedInputs,
            string? existingCustomAfterTargets)
        {
            var project = new XElement("Project");
            if (!string.IsNullOrWhiteSpace(existingCustomAfterTargets))
            {
                project.Add(new XElement(
                    "Import",
                    new XAttribute("Project", existingCustomAfterTargets!)));
            }

            string targetSuffix = Guid.NewGuid().ToString("N");
            var validationTarget = new XElement(
                "Target",
                new XAttribute(
                    "Name",
                    "_PowerForgeValidateNoBuildPublishInputs_" + targetSuffix),
                new XAttribute(
                    "BeforeTargets",
                    "_ComputeResolvedFilesToPublishTypes;_ComputeFilesToBundle"));
            var copyBindingTarget = new XElement(
                "Target",
                new XAttribute("Name", "_PowerForgeBindNoBuildPublishCopies_" + targetSuffix),
                new XAttribute("AfterTargets", "_ComputeResolvedFilesToPublishTypes"),
                new XAttribute(
                    "BeforeTargets",
                    "_CopyResolvedFilesToPublishPreserveNewest;" +
                    "_CopyResolvedFilesToPublishAlways;" +
                    "_CopyResolvedFilesToPublishIfDifferent"));
            var bundleBindingTarget = new XElement(
                "Target",
                new XAttribute("Name", "_PowerForgeBindNoBuildBundleInputs_" + targetSuffix),
                new XAttribute("AfterTargets", "_ComputeFilesToBundle"),
                new XAttribute("BeforeTargets", "PrepareForBundle;GenerateSingleFileBundle"));
            var copyItems = new XElement("ItemGroup");
            var bundleItems = new XElement("ItemGroup");
            int index = 0;
            foreach ((NoBuildPublishInput[] inputs, string snapshotPath) in mappedInputs)
            {
                NoBuildPublishInput input = inputs[0];
                string itemName = "_PowerForgeProvenNoBuildInput" +
                    index++.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string originalPath = EscapeMsBuildConditionLiteral(input.FullPath);
                validationTarget.Add(new XElement(
                    "ItemGroup",
                    new XElement(
                        itemName,
                        new XAttribute("Include", "@(ResolvedFileToPublish)"),
                        new XAttribute(
                            "Condition",
                            $"'%(ResolvedFileToPublish.FullPath)' == '{originalPath}'"))));
                validationTarget.Add(new XElement(
                    "Error",
                    new XAttribute("Condition", $"'@({itemName})' == ''"),
                    new XAttribute(
                        "Text",
                        "A proven no-build publish input was not present in ResolvedFileToPublish: " +
                        input.FullPath)));

                string[] copyBuckets =
                [
                    "_ResolvedFileToPublishPreserveNewest",
                    "_ResolvedFileToPublishAlways",
                    "_ResolvedFileToPublishIfDifferent"
                ];
                foreach (string bucket in copyBuckets)
                {
                    copyItems.Add(new XElement(
                        bucket,
                        new XAttribute("Remove", $"@({bucket})"),
                        new XAttribute(
                            "Condition",
                            $"'%({bucket}.FullPath)' == '{originalPath}'")));
                }

                string bundleMatch = itemName + "Bundle";
                bundleItems.Add(new XElement(
                    bundleMatch,
                    new XAttribute("Include", "@(_FilesToBundle)"),
                    new XAttribute(
                        "Condition",
                        $"'%(_FilesToBundle.FullPath)' == '{originalPath}'")));
                bundleItems.Add(new XElement(
                    "_FilesToBundle",
                    new XAttribute("Remove", "@(_FilesToBundle)"),
                    new XAttribute(
                        "Condition",
                        $"'%(_FilesToBundle.FullPath)' == '{originalPath}'")));

                foreach (NoBuildPublishInput replacementInput in inputs)
                {
                    string copyToPublishDirectory = replacementInput.Metadata.TryGetValue(
                        "CopyToPublishDirectory",
                        out string? copyValue)
                        ? copyValue ?? string.Empty
                        : string.Empty;
                    string? bucket = copyToPublishDirectory.Equals(
                        "PreserveNewest",
                        StringComparison.OrdinalIgnoreCase)
                        ? "_ResolvedFileToPublishPreserveNewest"
                        : copyToPublishDirectory.Equals("Always", StringComparison.OrdinalIgnoreCase)
                            ? "_ResolvedFileToPublishAlways"
                            : copyToPublishDirectory.Equals("IfDifferent", StringComparison.OrdinalIgnoreCase)
                                ? "_ResolvedFileToPublishIfDifferent"
                                : null;
                    if (bucket is not null)
                    {
                        copyItems.Add(CreateSnapshotReplacement(
                            bucket,
                            snapshotPath,
                            replacementInput,
                            condition: null));
                    }
                    bundleItems.Add(CreateSnapshotReplacement(
                        "_FilesToBundle",
                        snapshotPath,
                        replacementInput,
                        $"'@({bundleMatch})' != ''"));
                }
            }
            copyBindingTarget.Add(copyItems);
            bundleBindingTarget.Add(bundleItems);
            project.Add(validationTarget);
            project.Add(copyBindingTarget);
            project.Add(bundleBindingTarget);
            new XDocument(project).Save(targetsPath, SaveOptions.DisableFormatting);
        }

        private static XElement CreateSnapshotReplacement(
            string itemName,
            string snapshotPath,
            NoBuildPublishInput input,
            string? condition)
        {
            var replacement = new XElement(itemName, new XAttribute("Include", snapshotPath));
            if (!string.IsNullOrWhiteSpace(condition))
                replacement.Add(new XAttribute("Condition", condition!));
            foreach (KeyValuePair<string, string> metadata in input.Metadata)
            {
                if (IsIntrinsicItemMetadata(metadata.Key) || !TryVerifyXmlName(metadata.Key))
                    continue;
                replacement.Add(new XElement(metadata.Key, metadata.Value ?? string.Empty));
            }
            replacement.SetElementValue("RelativePath", input.RelativePath);
            return replacement;
        }

        private static bool IsIntrinsicItemMetadata(string name)
            => name.Equals("Identity", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("FullPath", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RootDir", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Filename", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Extension", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RelativeDir", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Directory", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("RecursiveDir", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ModifiedTime", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("CreatedTime", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("AccessedTime", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("DefiningProjectFullPath", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("DefiningProjectDirectory", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("DefiningProjectName", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("DefiningProjectExtension", StringComparison.OrdinalIgnoreCase);

        private static bool TryVerifyXmlName(string name)
        {
            try
            {
                _ = System.Xml.XmlConvert.VerifyName(name);
                return !name.Contains(':');
            }
            catch
            {
                return false;
            }
        }

        private static string EscapeMsBuildConditionLiteral(string value)
            => EscapeMsBuildPropertyValue(value).Replace("'", "%27");

        private static void TryDeleteSnapshotRoot(string root)
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Temporary snapshot cleanup is best effort.
            }
        }
    }
}
