using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void BindSynchronizedReleasePayloadFingerprint(
        ModuleBuildResult buildResult,
        ModulePipelineRunState state)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint;
        if (checkpoint is null)
            return;

        var payloadComponents = CreateSynchronizedReleasePayloadComponents(buildResult, state);
        var payloadFingerprint = CreateSynchronizedReleaseFingerprint(payloadComponents);
        if (string.IsNullOrWhiteSpace(checkpoint.PayloadFingerprint))
        {
            checkpoint.PayloadFingerprint = payloadFingerprint;
            checkpoint.PayloadComponents = payloadComponents;
            SaveSynchronizedReleaseCheckpoint(state);
            return;
        }

        if (!string.Equals(
                checkpoint.PayloadFingerprint,
                payloadFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            var changedComponents = DescribeChangedSynchronizedReleasePayloadComponents(
                checkpoint.PayloadComponents,
                payloadComponents);
            throw new InvalidOperationException(
                $"The coordinated release payload differs from the snapshot bound before its first publish attempt ({changedComponents}). Restore the original module and package artifacts or abandon the incomplete release checkpoint explicitly.");
        }
    }

    private static string[] CreateSynchronizedReleasePayloadComponents(
        ModuleBuildResult buildResult,
        ModulePipelineRunState state)
    {
        var records = new List<string>();
        AddSynchronizedReleasePayloadDirectoryEntries(records, "module", buildResult.StagingPath);

        for (int index = 0; index < state.ArtefactResults.Count; index++)
        {
            AddSynchronizedReleasePayloadPath(
                records,
                $"artefact/{index}",
                state.ArtefactResults[index].OutputPath);
        }

        for (int laneIndex = 0; laneIndex < state.ProjectBuildResults.Count; laneIndex++)
        {
            var release = state.ProjectBuildResults[laneIndex].Result.Release;
            if (release is null)
                continue;

            for (int projectIndex = 0; projectIndex < release.Projects.Count; projectIndex++)
            {
                var project = release.Projects[projectIndex];
                var label = $"package-lane/{laneIndex}/project/{projectIndex}";
                for (int packageIndex = 0; packageIndex < project.Packages.Count; packageIndex++)
                {
                    AddSynchronizedReleasePayloadPath(
                        records,
                        $"{label}/package/{packageIndex}",
                        project.Packages[packageIndex]);
                }
                for (int packageIndex = 0; packageIndex < project.SymbolPackages.Count; packageIndex++)
                {
                    AddSynchronizedReleasePayloadPath(
                        records,
                        $"{label}/symbols/{packageIndex}",
                        project.SymbolPackages[packageIndex]);
                }
                if (!string.IsNullOrWhiteSpace(project.ReleaseZipPath))
                {
                    AddSynchronizedReleasePayloadPath(
                        records,
                        $"{label}/release-zip",
                        project.ReleaseZipPath!);
                }
            }
        }

        records.Sort(StringComparer.Ordinal);
        return records.ToArray();
    }

    private static void AddSynchronizedReleasePayloadDirectoryEntries(
        ICollection<string> records,
        string label,
        string path)
    {
        if (!Directory.Exists(path))
        {
            AddSynchronizedReleasePayloadPath(records, label, path);
            return;
        }

        var entries = Directory.EnumerateFileSystemEntries(path)
            .OrderBy(static entry => entry, StringComparer.Ordinal)
            .ToArray();
        if (entries.Length == 0)
        {
            records.Add($"{label}|directory|empty");
            return;
        }

        foreach (var entry in entries)
        {
            AddSynchronizedReleasePayloadPath(
                records,
                $"{label}/{Path.GetFileName(entry)}",
                entry);
        }
    }

    private static void AddSynchronizedReleasePayloadPath(
        ICollection<string> records,
        string label,
        string path)
    {
        if (File.Exists(path))
        {
            records.Add($"{label}|file|{Path.GetFileName(path)}|{CreateSynchronizedReleaseFileFingerprint(path)}");
            return;
        }

        if (!Directory.Exists(path))
        {
            records.Add($"{label}|missing");
            return;
        }

        var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            records.Add($"{label}|directory|empty");
            return;
        }

        var fileRecords = new List<string>();
        foreach (var file in files)
        {
            var relativePath = FrameworkCompatibility.GetRelativePath(path, file)
                .Replace('\\', '/');
            fileRecords.Add($"{relativePath}|{CreateSynchronizedReleaseFileFingerprint(file)}");
        }
        records.Add($"{label}|directory|{CreateSynchronizedReleaseFingerprint(fileRecords.ToArray())}");
    }

    private static string CreateSynchronizedReleaseFileFingerprint(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".nupkg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".snupkg", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                var entries = new List<string>();
                foreach (var entry in archive.Entries
                             .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name))
                             .OrderBy(static entry => entry.FullName, StringComparer.Ordinal))
                {
                    using var entryStream = entry.Open();
                    entries.Add($"{entry.FullName.Replace('\\', '/')}|{ComputeSynchronizedReleaseStreamHash(entryStream)}");
                }

                return CreateSynchronizedReleaseFingerprint(entries.ToArray());
            }
            catch (InvalidDataException)
            {
                // Test doubles and legacy producers can use these extensions for non-ZIP payloads.
            }
        }

        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ComputeSynchronizedReleaseStreamHash(file);
    }

    private static string ComputeSynchronizedReleaseStreamHash(Stream stream)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static string DescribeChangedSynchronizedReleasePayloadComponents(
        IReadOnlyCollection<string>? expected,
        IReadOnlyCollection<string> actual)
    {
        if (expected is null || expected.Count == 0)
            return "stored payload components are unavailable";

        var expectedByLabel = expected.ToDictionary(
            GetSynchronizedReleasePayloadComponentLabel,
            static value => value,
            StringComparer.Ordinal);
        var actualByLabel = actual.ToDictionary(
            GetSynchronizedReleasePayloadComponentLabel,
            static value => value,
            StringComparer.Ordinal);
        var changed = expectedByLabel.Keys
            .Union(actualByLabel.Keys, StringComparer.Ordinal)
            .Where(label =>
                !expectedByLabel.TryGetValue(label, out var expectedValue) ||
                !actualByLabel.TryGetValue(label, out var actualValue) ||
                !string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
            .OrderBy(static label => label, StringComparer.Ordinal)
            .Take(5)
            .ToArray();

        return changed.Length == 0
            ? "payload fingerprint changed"
            : $"changed component(s): {string.Join(", ", changed)}";
    }

    private static string GetSynchronizedReleasePayloadComponentLabel(string record)
    {
        var separator = record.IndexOf('|');
        return separator < 0 ? record : record.Substring(0, separator);
    }
}
