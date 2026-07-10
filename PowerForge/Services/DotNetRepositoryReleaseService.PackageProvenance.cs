using System.IO.Compression;
using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static bool TryRunFreshReleaseBuild(
        DotNetRepositoryProjectResult project,
        string configuration,
        ILogger logger,
        out TimeSpan duration,
        out string error)
    {
        var projectDirectory = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        logger.Info($"{project.ProjectName}: rebuilding assemblies before no-build pack...");
        var exitCode = RunDotnetBuild(
            project.CsprojPath,
            projectDirectory,
            configuration,
            project.ProjectName,
            logger,
            out var standardError,
            out var standardOutput,
            out duration);
        if (exitCode != 0)
        {
            error = $"dotnet build failed for {project.ProjectName} (exit {exitCode}). {SummarizeProcessFailureOutput(standardError, standardOutput)}".Trim();
            return false;
        }

        logger.Success($"{project.ProjectName}: release rebuild completed in {FormatDuration(duration)}.");
        error = string.Empty;
        return true;
    }

    private static bool TryValidatePackagePayloads(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        IReadOnlyList<string> packagePaths,
        ILogger logger,
        out string error)
    {
        foreach (var project in projects)
        {
            var projectPackages = FilterPackages(packagePaths, project.PackageId, project.NewVersion!);
            if (!TryValidatePackagePayloads(project, spec, projectPackages, logger, out error))
                return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidatePackagePayloads(
        DotNetRepositoryProjectResult project,
        DotNetRepositoryReleaseSpec spec,
        IReadOnlyList<string> packagePaths,
        ILogger logger,
        out string error)
    {
        error = string.Empty;
        if (packagePaths.Count == 0)
            return true;

        var projectDirectory = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var assemblyNames = ResolveAssemblyNames(project.CsprojPath, project.ProjectName, projectDirectory, configuration, logger);
        var payloadNames = new HashSet<string>(
            assemblyNames.SelectMany(static name => new[] { name + ".dll", name + ".exe" }),
            StringComparer.OrdinalIgnoreCase);
        if (payloadNames.Count == 0)
            return true;

        var outputDirectories = ResolveBuildOutputDirectories(
            project.CsprojPath,
            projectDirectory,
            configuration,
            project.ProjectName,
            logger,
            payloadNames.ToArray());
        var outputHashes = BuildOutputHashLookup(outputDirectories, payloadNames);

        foreach (var packagePath in packagePaths)
        {
            if (!TryValidatePackagePayload(packagePath, payloadNames, outputHashes, out var validatedPayloads, out error))
            {
                error = $"{project.ProjectName}: package payload provenance validation failed. {error}";
                return false;
            }

            if (validatedPayloads > 0)
                logger.Success($"{project.ProjectName}: verified {validatedPayloads} package payload(s) against the fresh release build in {Path.GetFileName(packagePath)}.");
            else
                logger.Verbose($"{project.ProjectName}: package {Path.GetFileName(packagePath)} contains no primary lib/runtime assembly payload to verify.");
        }

        return true;
    }

    /// <summary>
    /// Verifies primary runtime assemblies in a NuGet package against hashes from the release build outputs.
    /// </summary>
    internal static bool TryValidatePackagePayload(
        string packagePath,
        HashSet<string> payloadNames,
        IReadOnlyDictionary<string, HashSet<string>> outputHashes,
        out int validatedPayloads,
        out string error)
    {
        validatedPayloads = 0;
        error = string.Empty;

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            foreach (var entry in archive.Entries.Where(IsRuntimePackagePayload))
            {
                var fileName = Path.GetFileName(entry.FullName);
                if (!payloadNames.Contains(fileName))
                    continue;

                if (!outputHashes.TryGetValue(fileName, out var expectedHashes) || expectedHashes.Count == 0)
                {
                    error = $"No fresh build output was found for packaged payload '{entry.FullName}' in {Path.GetFileName(packagePath)}.";
                    return false;
                }

                using var stream = entry.Open();
                var packageHash = ComputePackagePayloadSha256(stream);
                if (!expectedHashes.Contains(packageHash))
                {
                    error = $"Packaged payload '{entry.FullName}' in {Path.GetFileName(packagePath)} does not match any freshly rebuilt output.";
                    return false;
                }

                validatedPayloads++;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Could not inspect {Path.GetFileName(packagePath)}: {ex.Message}";
            return false;
        }
    }

    private static Dictionary<string, HashSet<string>> BuildOutputHashLookup(
        IEnumerable<string> outputDirectories,
        HashSet<string> payloadNames)
    {
        var hashes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in outputDirectories)
        {
            if (!Directory.Exists(directory))
                continue;

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(path);
                if (!payloadNames.Contains(fileName))
                    continue;

                if (!hashes.TryGetValue(fileName, out var fileHashes))
                {
                    fileHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    hashes[fileName] = fileHashes;
                }

                using var stream = File.OpenRead(path);
                fileHashes.Add(ComputePackagePayloadSha256(stream));
            }
        }

        return hashes;
    }

    private static bool IsRuntimePackagePayload(ZipArchiveEntry entry)
    {
        var path = entry.FullName.Replace('\\', '/');
        return path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputePackagePayloadSha256(Stream stream)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
    }
}
