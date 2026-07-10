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
            if (!TryValidateProjectPackagePayloads(project, spec, projectPackages, logger, out error))
                return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Validates package assembly payloads for one project without requiring build output for metadata-only packages.
    /// </summary>
    internal static bool TryValidateProjectPackagePayloads(
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

        var packagesWithPayloads = new List<string>();
        foreach (var packagePath in packagePaths)
        {
            if (!TryCountPackagePayloads(packagePath, payloadNames, out var payloadCount, out error))
            {
                error = $"{project.ProjectName}: package payload provenance validation failed. {error}";
                return false;
            }

            if (payloadCount == 0)
                logger.Verbose($"{project.ProjectName}: package {Path.GetFileName(packagePath)} contains no primary lib/runtime assembly payload to verify.");
            else
                packagesWithPayloads.Add(packagePath);
        }

        if (packagesWithPayloads.Count == 0)
            return true;

        var outputDirectories = ResolveBuildOutputDirectories(
            project.CsprojPath,
            projectDirectory,
            configuration,
            project.ProjectName,
            logger,
            payloadNames.ToArray());
        var outputHashes = BuildOutputHashLookup(outputDirectories, payloadNames);

        foreach (var packagePath in packagesWithPayloads)
        {
            if (!TryValidatePackagePayload(packagePath, payloadNames, outputHashes, out var validatedPayloads, out error))
            {
                error = $"{project.ProjectName}: package payload provenance validation failed. {error}";
                return false;
            }

            if (validatedPayloads > 0)
                logger.Success($"{project.ProjectName}: verified {validatedPayloads} package payload(s) against the fresh release build in {Path.GetFileName(packagePath)}.");
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

    private static bool TryCountPackagePayloads(
        string packagePath,
        HashSet<string> payloadNames,
        out int payloadCount,
        out string error)
    {
        payloadCount = 0;
        error = string.Empty;

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            payloadCount = archive.Entries.Count(entry =>
                IsRuntimePackagePayload(entry) && payloadNames.Contains(Path.GetFileName(entry.FullName)));
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
        foreach (var directory in outputDirectories
                     .Where(Directory.Exists)
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
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

    private static string?[] ResolveConfiguredTargetFrameworks(
        string csproj,
        string workingDirectory,
        string configuration,
        string projectName,
        ILogger logger)
    {
        var declaredFrameworks = ReadTargetFrameworks(csproj)
            .Where(static framework => !string.IsNullOrWhiteSpace(framework))
            .Select(static framework => framework!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (declaredFrameworks.Length > 0)
            return declaredFrameworks.Cast<string?>().ToArray();

        foreach (var propertyName in new[] { "TargetFrameworks", "TargetFramework" })
        {
            var exitCode = RunDotnetMsBuildGetProperty(
                csproj,
                workingDirectory,
                configuration,
                null,
                propertyName,
                projectName,
                logger,
                out var value,
                out _,
                out _,
                out _);
            if (exitCode != 0 || string.IsNullOrWhiteSpace(value) || value!.IndexOf("$(", StringComparison.Ordinal) >= 0)
                continue;

            var evaluatedFrameworks = value!
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static framework => framework.Trim())
                .Where(static framework => framework.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (evaluatedFrameworks.Length > 0)
                return evaluatedFrameworks.Cast<string?>().ToArray();
        }

        return new string?[] { null };
    }

    private static bool IsRuntimePackagePayload(ZipArchiveEntry entry)
    {
        var segments = entry.FullName.Replace('\\', '/').Split('/');
        if (segments.Length >= 3 && string.Equals(segments[0], "lib", StringComparison.OrdinalIgnoreCase))
            return true;

        if (segments.Length >= 4 && string.Equals(segments[0], "tools", StringComparison.OrdinalIgnoreCase))
            return true;

        return segments.Length >= 5 &&
               string.Equals(segments[0], "runtimes", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(segments[2], "lib", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputePackagePayloadSha256(Stream stream)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
    }
}
