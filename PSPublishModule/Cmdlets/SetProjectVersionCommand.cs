using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PSPublishModule;

/// <summary>
/// Updates version numbers across multiple project files.
/// </summary>
[Cmdlet(VerbsCommon.Set, "ProjectVersion", SupportsShouldProcess = true)]
[OutputType(typeof(ProjectVersionUpdateResult))]
public sealed class SetProjectVersionCommand : PSCmdlet
{
    /// <summary>The type of version increment: Major, Minor, Build, or Revision.</summary>
    [Parameter]
    public ProjectVersionIncrementKind? VersionType { get; set; }

    /// <summary>Specific version number to set (format: x.x.x or x.x.x.x).</summary>
    [Parameter]
    [ValidatePattern("^\\d+\\.\\d+\\.\\d+(\\.\\d+)?$")]
    public string? NewVersion { get; set; }

    /// <summary>Optional module name to filter updates to specific projects/modules.</summary>
    [Parameter]
    public string? ModuleName { get; set; }

    /// <summary>The root path to search for project files. Defaults to current directory.</summary>
    [Parameter]
    public string Path { get; set; } = string.Empty;

    /// <summary>Path fragments (or folder names) to exclude from the search (in addition to default 'obj' and 'bin'). This matches against the full path, case-insensitively.</summary>
    [Parameter]
    public string[] ExcludeFolders { get; set; } = Array.Empty<string>();

    /// <summary>Returns per-file update results when specified.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>
    /// Executes the update across discovered project files.
    /// </summary>
    protected override void ProcessRecord()
    {
        var root = string.IsNullOrWhiteSpace(Path) ? SessionState.Path.CurrentFileSystemLocation.Path : Path;
        root = System.IO.Path.GetFullPath(root.Trim().Trim('"'));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Project path '{root}' not found or is not a directory.");

        var moduleName = string.IsNullOrWhiteSpace(ModuleName) ? null : ModuleName!.Trim();
        var excludeFragments = ProjectVersionScanner.BuildExcludeFragments(ExcludeFolders);
        var candidates = ProjectVersionScanner.FindCandidateFiles(root, excludeFragments);

        var targetCsprojFiles = FilterByModuleName(candidates.CsprojFiles, moduleName);
        var targetPsd1Files = FilterByModuleName(candidates.Psd1Files, moduleName);
        var buildScriptFiles = candidates.Ps1Files.Where(ProjectVersionScanner.LooksLikeBuildScript).ToList();

        var currentVersion = FindCurrentVersion(targetCsprojFiles, targetPsd1Files, buildScriptFiles);
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException("Could not determine current version from any project files."),
                "NoProjectVersionFound",
                ErrorCategory.InvalidData,
                root));
            return;
        }

        var newVersion = ResolveNewVersion(currentVersion!, NewVersion, VersionType);

        // Legacy behavior: compute a current-version map across all discovered items (no ModuleName filter).
        var currentVersions = ProjectVersionScanner.Discover(root, moduleName: null, excludeFragments);
        var currentVersionByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in currentVersions)
            currentVersionByFile[v.Source] = v.Version;

        var results = new List<ProjectVersionUpdateResult>();
        foreach (var file in targetCsprojFiles)
            results.Add(UpdateFile(file, ProjectVersionSourceKind.Csproj, newVersion, currentVersionByFile.TryGetValue(file, out var oldV) ? oldV : null));
        foreach (var file in targetPsd1Files)
            results.Add(UpdateFile(file, ProjectVersionSourceKind.PowerShellModule, newVersion, currentVersionByFile.TryGetValue(file, out var oldV) ? oldV : null));
        foreach (var file in buildScriptFiles)
            results.Add(UpdateFile(file, ProjectVersionSourceKind.BuildScript, newVersion, currentVersionByFile.TryGetValue(file, out var oldV) ? oldV : null));

        if (PassThru)
        {
            foreach (var r in results) WriteObject(r);
        }
    }

    private static List<string> FilterByModuleName(IReadOnlyList<string> files, string? moduleName)
    {
        if (moduleName == null) return files.ToList();
        return files.Where(f => string.Equals(System.IO.Path.GetFileNameWithoutExtension(f), moduleName, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static string? FindCurrentVersion(IReadOnlyList<string> csprojFiles, IReadOnlyList<string> psd1Files, IReadOnlyList<string> buildScriptFiles)
    {
        foreach (var csproj in csprojFiles)
            if (ProjectVersionScanner.TryGetVersionFromCsproj(csproj, out var v))
                return v;

        foreach (var psd1 in psd1Files)
            if (ProjectVersionScanner.TryGetVersionFromPsd1(psd1, out var v))
                return v;

        foreach (var script in buildScriptFiles)
            if (ProjectVersionScanner.TryGetVersionFromBuildScript(script, out var v))
                return v;

        return null;
    }

    private string ResolveNewVersion(string currentVersion, string? newVersion, ProjectVersionIncrementKind? versionType)
    {
        if (!string.IsNullOrWhiteSpace(newVersion))
            return newVersion!.Trim();

        if (versionType == null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Specify -NewVersion or -VersionType."),
                "MissingVersionInput",
                ErrorCategory.InvalidArgument,
                null));
        }

        return UpdateVersionNumber(currentVersion, versionType!.Value);
    }

    private ProjectVersionUpdateResult UpdateFile(string filePath, ProjectVersionSourceKind kind, string newVersion, string? oldVersion)
    {
        if (!File.Exists(filePath))
        {
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.Error, "File not found.");
        }

        string content;
        try { content = File.ReadAllText(filePath); }
        catch (Exception ex)
        {
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.Error, ex.Message);
        }

        string updatedContent;
        try { updatedContent = ApplyVersionUpdate(content, kind, newVersion); }
        catch (Exception ex)
        {
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.Error, ex.Message);
        }

        if (string.Equals(content, updatedContent, StringComparison.Ordinal))
        {
            WriteVerbose($"No version change needed for {filePath}");
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.NoChange, null);
        }

        var action = $"Update version from '{oldVersion ?? ""}' to '{newVersion}'";
        if (!ShouldProcess(filePath, action))
        {
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.Skipped, null);
        }

        try
        {
            File.WriteAllText(filePath, updatedContent);
            WriteVerbose($"Updated version in {filePath} to {newVersion}");
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.Updated, null);
        }
        catch (Exception ex)
        {
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.Error, ex.Message);
        }
    }

    private static string ApplyVersionUpdate(string content, ProjectVersionSourceKind kind, string version)
    {
        if (kind == ProjectVersionSourceKind.Csproj)
        {
            var updated = content;
            updated = Regex.Replace(updated, "<Version>[\\d\\.]+</Version>", $"<Version>{version}</Version>", RegexOptions.IgnoreCase);
            updated = Regex.Replace(updated, "<VersionPrefix>[\\d\\.]+</VersionPrefix>", $"<VersionPrefix>{version}</VersionPrefix>", RegexOptions.IgnoreCase);
            updated = Regex.Replace(updated, "<PackageVersion>[\\d\\.]+</PackageVersion>", $"<PackageVersion>{version}</PackageVersion>", RegexOptions.IgnoreCase);
            updated = Regex.Replace(updated, "<AssemblyVersion>[\\d\\.]+</AssemblyVersion>", $"<AssemblyVersion>{version}</AssemblyVersion>", RegexOptions.IgnoreCase);
            updated = Regex.Replace(updated, "<FileVersion>[\\d\\.]+</FileVersion>", $"<FileVersion>{version}</FileVersion>", RegexOptions.IgnoreCase);
            updated = Regex.Replace(updated, "<InformationalVersion>[\\d\\.]+</InformationalVersion>", $"<InformationalVersion>{version}</InformationalVersion>", RegexOptions.IgnoreCase);
            return updated;
        }

        // PowerShell module manifests and build scripts use the same ModuleVersion assignment pattern.
        return Regex.Replace(content, "ModuleVersion\\s*=\\s*['\\\"][\\d\\.]+['\\\"]", $"ModuleVersion        = '{version}'", RegexOptions.IgnoreCase);
    }

    private static string UpdateVersionNumber(string version, ProjectVersionIncrementKind type)
    {
        var originalParts = version.Split('.');
        var versionParts = originalParts.ToList();

        while (versionParts.Count < 3) versionParts.Add("0");
        if (type == ProjectVersionIncrementKind.Revision && versionParts.Count < 4) versionParts.Add("0");

        switch (type)
        {
            case ProjectVersionIncrementKind.Major:
                versionParts[0] = (int.Parse(versionParts[0]) + 1).ToString();
                versionParts[1] = "0";
                versionParts[2] = "0";
                if (versionParts.Count > 3) versionParts[3] = "0";
                break;
            case ProjectVersionIncrementKind.Minor:
                versionParts[1] = (int.Parse(versionParts[1]) + 1).ToString();
                versionParts[2] = "0";
                if (versionParts.Count > 3) versionParts[3] = "0";
                break;
            case ProjectVersionIncrementKind.Build:
                versionParts[2] = (int.Parse(versionParts[2]) + 1).ToString();
                if (versionParts.Count > 3) versionParts[3] = "0";
                break;
            case ProjectVersionIncrementKind.Revision:
                if (versionParts.Count < 4)
                {
                    versionParts.Add("1");
                }
                else
                {
                    versionParts[3] = (int.Parse(versionParts[3]) + 1).ToString();
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown version increment type.");
        }

        var newVersion = string.Join(".", versionParts);
        if (originalParts.Length == 3 && type != ProjectVersionIncrementKind.Revision)
            newVersion = string.Join(".", versionParts.Take(3));

        return newVersion;
    }
}

/// <summary>Version component to increment when computing a new version.</summary>
public enum ProjectVersionIncrementKind
{
    /// <summary>Increment major, reset minor/build(/revision).</summary>
    Major,
    /// <summary>Increment minor, reset build(/revision).</summary>
    Minor,
    /// <summary>Increment build, reset revision.</summary>
    Build,
    /// <summary>Increment revision (adding one if missing).</summary>
    Revision,
}

/// <summary>Outcome status for a per-file version update.</summary>
public enum ProjectVersionUpdateStatus
{
    /// <summary>The file was updated on disk.</summary>
    Updated,
    /// <summary>No changes were needed.</summary>
    NoChange,
    /// <summary>Change was skipped (e.g., WhatIf/Confirm).</summary>
    Skipped,
    /// <summary>An error occurred while reading or writing.</summary>
    Error,
}

/// <summary>Represents a per-file version update result.</summary>
public sealed class ProjectVersionUpdateResult
{
    /// <summary>Path of the file that was considered.</summary>
    public string Source { get; }
    /// <summary>The kind of file being updated.</summary>
    public ProjectVersionSourceKind Kind { get; }
    /// <summary>Version detected before the update (when known).</summary>
    public string? OldVersion { get; }
    /// <summary>The target version requested.</summary>
    public string NewVersion { get; }
    /// <summary>Status of the update attempt.</summary>
    public ProjectVersionUpdateStatus Status { get; }
    /// <summary>Error message when Status is Error.</summary>
    public string? Error { get; }

    /// <summary>
    /// Creates a new update result.
    /// </summary>
    public ProjectVersionUpdateResult(string source, ProjectVersionSourceKind kind, string? oldVersion, string newVersion, ProjectVersionUpdateStatus status, string? error)
    {
        Source = source;
        Kind = kind;
        OldVersion = oldVersion;
        NewVersion = newVersion;
        Status = status;
        Error = error;
    }
}
