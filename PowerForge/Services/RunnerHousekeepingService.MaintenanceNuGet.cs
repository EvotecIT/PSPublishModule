using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Packaging;

namespace PowerForge;

public sealed partial class RunnerHousekeepingService
{
    private static void MaintenanceNuGet(MaintenanceRun run)
    {
        var http = run.Spec.NuGetHttpCachePath;
        if (!string.IsNullOrEmpty(http) && Directory.Exists(http))
        {
            foreach (var directory in Directory.EnumerateDirectories(http))
            {
                if (Path.GetFileName(directory) == MaintenanceQuarantine) continue;
                MaintenancePhysicalPath(directory);
                foreach (var path in Directory.EnumerateFiles(directory, "*.dat"))
                    if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddMinutes(-30))
                        MaintenanceIsolate(run, path, http!, directory: false, sensitive: false);
            }
        }
        var packages = run.Spec.NuGetPackagesPath;
        if (!run.Deep || string.IsNullOrEmpty(packages) || !Directory.Exists(packages)) return;
        if (string.IsNullOrEmpty(run.Spec.NuGetScratchPath) || !PathsEqual(run.Spec.NuGetScratchPath!, NuGetEnvironment.GetFolderPath(NuGetFolderPath.Temp)))
            throw new IOException("NuGet maintenance and runners must use the enrolled shared NUGET_SCRATCH directory.");

        MaintenancePackageReferences(run, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", ".store"));

        var candidates = new List<(string Path, DateTime LastUse)>();
        foreach (var id in Directory.EnumerateDirectories(packages))
        {
            if (run.Expired) return;
            if (Path.GetFileName(id) == MaintenanceQuarantine) continue;
            MaintenancePhysicalPath(id);
            foreach (var version in Directory.EnumerateDirectories(id))
            {
                var key = Path.GetFileName(id) + "/" + Path.GetFileName(version);
                var metadata = Path.Combine(version, ".nupkg.metadata");
                if (!File.Exists(metadata)) continue;
                var lastUse = File.GetLastWriteTimeUtc(metadata);
                if (run.State.PackageUse.TryGetValue(key.ToLowerInvariant(), out var observed) && observed > lastUse) lastUse = observed;
                candidates.Add((version, lastUse));
            }
        }
        foreach (var candidate in candidates.OrderBy(p => p.LastUse))
        {
            if (run.Expired) break;
            var pressure = run.State.PressureTargetBytes > run.SimulatedFree;
            if (!pressure && candidate.LastUse >= DateTime.UtcNow.AddDays(-run.Spec.NuGetRetentionDays)) continue;
            var name = Path.GetFileName(Path.GetDirectoryName(candidate.Path)!) + "." + Path.GetFileName(candidate.Path) + ".nupkg";
            var archive = Path.Combine(candidate.Path, name);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, run.Spec.MaximumRunSeconds - run.Clock.Elapsed.TotalSeconds)));
            try
            {
                ConcurrencyUtilities.ExecuteWithFileLockedAsync(archive, token =>
                {
                    if (MaintenancePublicPackage(candidate.Path, archive))
                        MaintenanceIsolate(run, candidate.Path, packages!, directory: true, sensitive: false);
                    else run.Retained.Add(candidate.Path + " — private, incomplete or unknown-source package");
                    return Task.FromResult(0);
                }, cancellation.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { run.State.Due = true; break; }
            catch (Exception error) when (error is IOException || error is JsonException || error is UnauthorizedAccessException)
            { run.Retained.Add(candidate.Path + " — " + error.GetType().Name); }
        }
        foreach (var key in run.State.PackageUse.Keys.Where(key => !Directory.Exists(Path.Combine(packages!, key.Replace('/', Path.DirectorySeparatorChar)))).ToArray())
            run.State.PackageUse.Remove(key);
    }

    private static bool MaintenancePublicPackage(string directory, string archive)
    {
        MaintenancePhysicalPath(directory);
        var metadataPath = Path.Combine(directory, ".nupkg.metadata");
        if (!File.Exists(archive) || !File.Exists(archive + ".sha512") || !File.Exists(metadataPath)) return false;
        MaintenancePhysicalPath(metadataPath);
        using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
        if (!metadata.RootElement.TryGetProperty("source", out var source) ||
            !string.Equals(source.GetString()?.TrimEnd('/'), "https://api.nuget.org/v3/index.json", StringComparison.OrdinalIgnoreCase)) return false;
        using var reader = new PackageArchiveReader(archive);
        var identity = reader.GetIdentity();
        if (!string.Equals(identity.Id, Path.GetFileName(Path.GetDirectoryName(directory)!), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(identity.Version.ToNormalizedString(), Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase)) return false;
        var expected = new HashSet<string>(reader.GetFiles().Select(p => p.Replace('/', Path.DirectorySeparatorChar)), GetPathStringComparer());
        expected.Add(Path.GetFileName(archive));
        expected.Add(Path.GetFileName(archive) + ".sha512");
        expected.Add(".nupkg.metadata");
        expected.Add(identity.Id.ToLowerInvariant() + ".nuspec");
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            if (!expected.Contains(path.Substring(AppendDirectorySeparator(directory).Length))) return false;
        return true;
    }
}
