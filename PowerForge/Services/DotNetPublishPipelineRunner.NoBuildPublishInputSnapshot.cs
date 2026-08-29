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

    private static string BuildPublishEvaluationRequestKey(
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
        return new ProjectEvaluationRequest(
                Path.GetFullPath(target.ProjectPath),
                framework,
                plan.Configuration,
                properties,
                plan.EnvironmentVariables)
            .BuildVisitKey();
    }

    internal sealed class NoBuildPublishInputSnapshot : IDisposable
    {
        private readonly string _root;
        private readonly List<FileStream> _leases;
        private readonly IReadOnlyDictionary<string, string> _expectedHashes;
        private readonly FileSystemWatcher _watcher;
        private int _changed;
        private bool _disposed;

        private NoBuildPublishInputSnapshot(
            string root,
            string targetsPath,
            List<FileStream> leases,
            IReadOnlyDictionary<string, string> expectedHashes,
            FileSystemWatcher watcher)
        {
            _root = root;
            TargetsPath = targetsPath;
            _leases = leases;
            _expectedHashes = expectedHashes;
            _watcher = watcher;
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
            var expectedHashes = new Dictionary<string, string>(
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
                        leases);
                    if (!string.Equals(actualSha256, input.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"A no-build publish input changed after controlled proof: {input.FullPath}.");
                    }
                    expectedHashes[snapshotPath] = actualSha256;
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
                expectedHashes[targetsPath] = ComputeSha256Hex(File.ReadAllBytes(targetsPath));
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.DirectoryName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size |
                                   NotifyFilters.Security
                };
                return new NoBuildPublishInputSnapshot(
                    root,
                    targetsPath,
                    leases,
                    expectedHashes,
                    watcher);
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
            if (Volatile.Read(ref _changed) != 0)
            {
                throw new InvalidOperationException(
                    "A proven no-build publish snapshot was mutated while dotnet publish was running.");
            }
            foreach (KeyValuePair<string, string> entry in _expectedHashes)
            {
                if (!File.Exists(entry.Key) ||
                    !string.Equals(
                        ComputeSha256Hex(File.ReadAllBytes(entry.Key)),
                        entry.Value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"A proven no-build publish snapshot changed while dotnet publish was running: {entry.Key}.");
                }
            }
            if (Volatile.Read(ref _changed) != 0)
            {
                throw new InvalidOperationException(
                    "A proven no-build publish snapshot was mutated while dotnet publish was running.");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            foreach (FileStream lease in _leases)
                lease.Dispose();
            TryDeleteSnapshotRoot(_root);
        }

        private void MarkChanged(object sender, FileSystemEventArgs args)
            => Interlocked.Exchange(ref _changed, 1);

        private void MarkChanged(object sender, RenamedEventArgs args)
            => Interlocked.Exchange(ref _changed, 1);

        private void MarkChanged(object sender, ErrorEventArgs args)
            => Interlocked.Exchange(ref _changed, 1);

        private static string CopyAndHashSnapshot(
            string sourcePath,
            string snapshotPath,
            int? expectedUnixFileMode,
            ICollection<FileStream> leases)
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
            leases.Add(new FileStream(
                snapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read));
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
