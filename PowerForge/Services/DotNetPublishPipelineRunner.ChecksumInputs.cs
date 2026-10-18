namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    /// <summary>
    /// Maps the clean staging inventory to deployed paths and includes files emitted
    /// by service packaging. Destination-only logs, databases and stale output do not
    /// belong to this publish. Enumeration failures must not produce a partial catalog.
    /// </summary>
    internal static string[] CaptureStagedPublishChecksumFiles(
        string publishDir,
        string outputDir,
        DotNetPublishServicePackageResult? servicePackage)
    {
        var files = Directory.EnumerateFiles(publishDir, "*", SearchOption.AllDirectories)
            .Select(path => Path.Combine(outputDir, GetRelativePath(publishDir, path)))
            .ToList();
        if (servicePackage is not null)
        {
            foreach (string? path in new[]
                     {
                         servicePackage.InstallScriptPath,
                         servicePackage.UninstallScriptPath,
                         servicePackage.RunOnceScriptPath,
                         servicePackage.MetadataPath
                     })
            {
                if (!string.IsNullOrWhiteSpace(path)) files.Add(path!);
            }
            files.AddRange(servicePackage.ConfigBootstrapFiles.Select(path => ResolvePath(outputDir, path)));
        }

        return files.Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
