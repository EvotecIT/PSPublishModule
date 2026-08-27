using System.Security.Cryptography;
using System.Text;
using System.Xml;
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
        if (!plan.NoBuildInPublish || provenance.NoBuildPublishInputs.Length == 0)
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
        NoBuildPublishInput[] inputs = provenance.NoBuildPublishInputs
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
        private static readonly HashSet<string> IntrinsicMetadataNames = new(
            new[]
            {
                "Identity",
                "FullPath",
                "RootDir",
                "Filename",
                "Extension",
                "RelativeDir",
                "Directory",
                "RecursiveDir",
                "ModifiedTime",
                "CreatedTime",
                "AccessedTime",
                "DefiningProjectFullPath",
                "DefiningProjectDirectory",
                "DefiningProjectName",
                "DefiningProjectExtension"
            },
            StringComparer.OrdinalIgnoreCase);

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
                    string extension = Path.GetExtension(input.FullPath);
                    string snapshotPath = Path.Combine(
                        inputRoot,
                        index++.ToString("D6", System.Globalization.CultureInfo.InvariantCulture) + extension);
                    string actualSha256 = CopyAndHashSnapshot(input.FullPath, snapshotPath, leases);
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
            ICollection<FileStream> leases)
        {
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

            var target = new XElement(
                "Target",
                new XAttribute(
                    "Name",
                    "_PowerForgeBindNoBuildPublishInputs_" + Guid.NewGuid().ToString("N")),
                new XAttribute("AfterTargets", "ComputeFilesToPublish"));
            var itemGroup = new XElement("ItemGroup");
            int index = 0;
            foreach ((NoBuildPublishInput[] inputs, string snapshotPath) in mappedInputs)
            {
                NoBuildPublishInput input = inputs[0];
                string itemName = "_PowerForgeProvenNoBuildInput" +
                    index++.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string originalPath = EscapeMsBuildConditionLiteral(input.FullPath);
                target.Add(new XElement(
                    "ItemGroup",
                    new XElement(
                        itemName,
                        new XAttribute("Include", "@(ResolvedFileToPublish)"),
                        new XAttribute(
                            "Condition",
                            $"'%(ResolvedFileToPublish.FullPath)' == '{originalPath}'"))));
                target.Add(new XElement(
                    "Error",
                    new XAttribute("Condition", $"'@({itemName})' == ''"),
                    new XAttribute(
                        "Text",
                        "A proven no-build publish input was not present in ResolvedFileToPublish: " +
                        input.FullPath)));
                itemGroup.Add(new XElement(
                    "ResolvedFileToPublish",
                    new XAttribute("Remove", "@(ResolvedFileToPublish)"),
                    new XAttribute(
                        "Condition",
                        $"'%(ResolvedFileToPublish.FullPath)' == '{originalPath}'")));
                foreach (NoBuildPublishInput replacementInput in inputs)
                {
                    var replacement = new XElement(
                        "ResolvedFileToPublish",
                        new XAttribute("Include", snapshotPath));
                    foreach (KeyValuePair<string, string> metadata in replacementInput.Metadata)
                    {
                        if (IntrinsicMetadataNames.Contains(metadata.Key) ||
                            !TryVerifyXmlName(metadata.Key))
                        {
                            continue;
                        }
                        replacement.Add(new XElement(metadata.Key, metadata.Value ?? string.Empty));
                    }
                    replacement.SetElementValue("RelativePath", replacementInput.RelativePath);
                    itemGroup.Add(replacement);
                }
            }
            target.Add(itemGroup);
            project.Add(target);
            new XDocument(project).Save(targetsPath, SaveOptions.DisableFormatting);
        }

        private static bool TryVerifyXmlName(string name)
        {
            try
            {
                _ = XmlConvert.VerifyName(name);
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
