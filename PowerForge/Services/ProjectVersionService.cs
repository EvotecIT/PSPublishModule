using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Discovers and updates project versions across <c>.csproj</c>, <c>.psd1</c>, and build-script files.
/// </summary>
public sealed class ProjectVersionService
{
    /// <summary>
    /// Discovers project versions under the specified root path.
    /// </summary>
    public IReadOnlyList<ProjectVersionInfo> Discover(ProjectVersionQueryRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var root = ResolveRootPath(request.RootPath);
        var moduleName = NormalizeModuleName(request.ModuleName);
        var excludeFragments = BuildExcludeFragments(request.ExcludeFolders);
        var candidates = FindCandidateFiles(root, excludeFragments);
        var results = new List<ProjectVersionInfo>();

        foreach (var file in FilterByModuleName(candidates.CsprojFiles, moduleName))
        {
            if (TryGetVersionFromCsproj(file, out var version))
                results.Add(new ProjectVersionInfo(version, file, ProjectVersionSourceKind.Csproj));
        }

        foreach (var file in FilterByModuleName(candidates.Psd1Files, moduleName))
        {
            if (TryGetVersionFromPsd1(file, out var version))
                results.Add(new ProjectVersionInfo(version, file, ProjectVersionSourceKind.PowerShellModule));
        }

        foreach (var file in candidates.Ps1Files)
        {
            if (!LooksLikeBuildScript(file))
                continue;
            if (TryGetVersionFromBuildScript(file, out var version))
                results.Add(new ProjectVersionInfo(version, file, ProjectVersionSourceKind.BuildScript));
        }

        return results;
    }

    /// <summary>
    /// Updates project versions under the specified root path.
    /// </summary>
    public IReadOnlyList<ProjectVersionUpdateResult> Update(
        ProjectVersionUpdateRequest request,
        Func<string, string, bool>? shouldProcess = null,
        Action<string>? verbose = null)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var root = ResolveRootPath(request.RootPath);
        var moduleName = NormalizeModuleName(request.ModuleName);
        var excludeFragments = BuildExcludeFragments(request.ExcludeFolders);
        var candidates = FindCandidateFiles(root, excludeFragments);

        var targetCsprojFiles = FilterByModuleName(candidates.CsprojFiles, moduleName);
        var targetPsd1Files = FilterByModuleName(candidates.Psd1Files, moduleName);
        var buildScriptFiles = candidates.Ps1Files.Where(LooksLikeBuildScript).ToList();

        var currentVersion = FindCurrentVersion(targetCsprojFiles, targetPsd1Files, buildScriptFiles);
        if (string.IsNullOrWhiteSpace(currentVersion))
            throw new InvalidOperationException("Could not determine current version from any project files.");

        var newVersion = ResolveNewVersion(currentVersion!, request.NewVersion, request.IncrementKind);

        var currentVersionByFile = Discover(new ProjectVersionQueryRequest
        {
            RootPath = root,
            ExcludeFolders = request.ExcludeFolders
        }).ToDictionary(item => item.Source, item => item.Version, StringComparer.OrdinalIgnoreCase);

        var results = new List<ProjectVersionUpdateResult>();
        foreach (var file in targetCsprojFiles)
            results.Add(UpdateFile(file, ProjectVersionSourceKind.Csproj, newVersion, currentVersionByFile.TryGetValue(file, out var oldVersion) ? oldVersion : null, shouldProcess, verbose));
        foreach (var file in targetPsd1Files)
            results.Add(UpdateFile(file, ProjectVersionSourceKind.PowerShellModule, newVersion, currentVersionByFile.TryGetValue(file, out var oldVersion) ? oldVersion : null, shouldProcess, verbose));
        foreach (var file in buildScriptFiles)
            results.Add(UpdateFile(file, ProjectVersionSourceKind.BuildScript, newVersion, currentVersionByFile.TryGetValue(file, out var oldVersion) ? oldVersion : null, shouldProcess, verbose));

        return results;
    }

    private static string ResolveRootPath(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path is required.", nameof(rootPath));

        var fullPath = Path.GetFullPath(rootPath.Trim().Trim('"'));
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Project path '{fullPath}' not found or is not a directory.");

        return fullPath;
    }

    private static string? NormalizeModuleName(string? moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            return null;

        return moduleName!.Trim();
    }

    private static IReadOnlyCollection<string> BuildExcludeFragments(IEnumerable<string>? excludeFolders)
    {
        var excludeFragments = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "obj", "bin" };
        if (excludeFolders is null)
            return excludeFragments;

        foreach (var entry in excludeFolders)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;
            excludeFragments.Add(entry.Trim().Trim('"'));
        }

        return excludeFragments;
    }

    private static CandidateFiles FindCandidateFiles(string root, IReadOnlyCollection<string> excludeFragments)
    {
        var csprojs = new List<string>();
        var psd1s = new List<string>();
        var ps1s = new List<string>();

        foreach (var file in EnumerateFiles(root, excludeFragments))
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                csprojs.Add(file);
            else if (extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase))
                psd1s.Add(file);
            else if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                ps1s.Add(file);
        }

        return new CandidateFiles(csprojs, psd1s, ps1s);
    }

    private static List<string> FilterByModuleName(IReadOnlyList<string> files, string? moduleName)
    {
        if (moduleName is null)
            return files.ToList();

        return files
            .Where(file => string.Equals(Path.GetFileNameWithoutExtension(file), moduleName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? FindCurrentVersion(IReadOnlyList<string> csprojFiles, IReadOnlyList<string> psd1Files, IReadOnlyList<string> buildScriptFiles)
    {
        foreach (var csproj in csprojFiles)
            if (TryGetVersionFromCsproj(csproj, out var version))
                return version;

        foreach (var manifest in psd1Files)
            if (TryGetVersionFromPsd1(manifest, out var version))
                return version;

        foreach (var script in buildScriptFiles)
            if (TryGetVersionFromBuildScript(script, out var version))
                return version;

        return null;
    }

    private static string ResolveNewVersion(string currentVersion, string? newVersion, ProjectVersionIncrementKind? incrementKind)
    {
        if (!string.IsNullOrWhiteSpace(newVersion))
            return newVersion!.Trim();
        if (incrementKind is null)
            throw new ArgumentException("Specify -NewVersion or -VersionType.", nameof(incrementKind));

        return UpdateVersionNumber(currentVersion, incrementKind.Value);
    }

    private static ProjectVersionUpdateResult UpdateFile(
        string filePath,
        ProjectVersionSourceKind kind,
        string newVersion,
        string? oldVersion,
        Func<string, string, bool>? shouldProcess,
        Action<string>? verbose)
    {
        if (!File.Exists(filePath))
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.Error, "File not found.");

        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.Error, ex.Message);
        }

        string updatedContent;
        try
        {
            updatedContent = ApplyVersionUpdate(content, kind, newVersion);
        }
        catch (Exception ex)
        {
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.Error, ex.Message);
        }

        if (string.Equals(content, updatedContent, StringComparison.Ordinal))
        {
            verbose?.Invoke($"No version change needed for {filePath}");
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.NoChange, null);
        }

        var action = $"Update version from '{oldVersion ?? string.Empty}' to '{newVersion}'";
        if (shouldProcess is not null && !shouldProcess(filePath, action))
            return new ProjectVersionUpdateResult(filePath, kind, oldVersion, newVersion, ProjectVersionUpdateStatus.Skipped, null);

        try
        {
            File.WriteAllText(filePath, updatedContent);
            verbose?.Invoke($"Updated version in {filePath} to {newVersion}");
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

        return Regex.Replace(content, "ModuleVersion\\s*=\\s*['\\\"][\\d\\.]+['\\\"]", $"ModuleVersion        = '{version}'", RegexOptions.IgnoreCase);
    }

    private static string UpdateVersionNumber(string version, ProjectVersionIncrementKind type)
    {
        var originalParts = version.Split('.');
        var versionParts = originalParts.ToList();

        while (versionParts.Count < 3)
            versionParts.Add("0");
        if (type == ProjectVersionIncrementKind.Revision && versionParts.Count < 4)
            versionParts.Add("0");

        switch (type)
        {
            case ProjectVersionIncrementKind.Major:
                versionParts[0] = (int.Parse(versionParts[0]) + 1).ToString();
                versionParts[1] = "0";
                versionParts[2] = "0";
                if (versionParts.Count > 3)
                    versionParts[3] = "0";
                break;
            case ProjectVersionIncrementKind.Minor:
                versionParts[1] = (int.Parse(versionParts[1]) + 1).ToString();
                versionParts[2] = "0";
                if (versionParts.Count > 3)
                    versionParts[3] = "0";
                break;
            case ProjectVersionIncrementKind.Build:
                versionParts[2] = (int.Parse(versionParts[2]) + 1).ToString();
                if (versionParts.Count > 3)
                    versionParts[3] = "0";
                break;
            case ProjectVersionIncrementKind.Revision:
                if (versionParts.Count < 4)
                    versionParts.Add("1");
                else
                    versionParts[3] = (int.Parse(versionParts[3]) + 1).ToString();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown version increment type.");
        }

        var newVersion = string.Join(".", versionParts);
        if (originalParts.Length == 3 && type != ProjectVersionIncrementKind.Revision)
            newVersion = string.Join(".", versionParts.Take(3));

        return newVersion;
    }

    private static bool LooksLikeBuildScript(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return text.IndexOf("Invoke-ModuleBuild", StringComparison.OrdinalIgnoreCase) >= 0
                   || text.IndexOf("Build-Module", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetVersionFromCsproj(string projectFile, out string version)
    {
        version = string.Empty;
        try
        {
            var content = File.ReadAllText(projectFile);
            if (TryMatchVersionTag(content, "VersionPrefix", out version)) return true;
            if (TryMatchVersionTag(content, "Version", out version)) return true;
            if (TryMatchVersionTag(content, "PackageVersion", out version)) return true;
            if (TryMatchVersionTag(content, "AssemblyVersion", out version)) return true;
            if (TryMatchVersionTag(content, "FileVersion", out version)) return true;
            if (TryMatchVersionTag(content, "InformationalVersion", out version)) return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetVersionFromPsd1(string manifestFile, out string version)
    {
        version = string.Empty;
        try
        {
            var content = File.ReadAllText(manifestFile);
            var match = Regex.Match(content, "ModuleVersion\\s*=\\s*['\\\"]?([\\d\\.]+)['\\\"]?", RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;
            version = match.Groups[1].Value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetVersionFromBuildScript(string scriptFile, out string version)
    {
        version = string.Empty;
        try
        {
            var content = File.ReadAllText(scriptFile);
            var match = Regex.Match(content, "ModuleVersion\\s*=\\s*['\\\"]?([\\d\\.]+)['\\\"]?", RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;
            version = match.Groups[1].Value;
            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryMatchVersionTag(string content, string tag, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrEmpty(content))
            return false;

        var regex = new Regex("<" + Regex.Escape(tag) + ">([\\d\\.]+)</" + Regex.Escape(tag) + ">", RegexOptions.IgnoreCase);
        var match = regex.Match(content);
        if (!match.Success)
            return false;
        version = match.Groups[1].Value;
        return !string.IsNullOrWhiteSpace(version);
    }

    private static IEnumerable<string> EnumerateFiles(string root, IReadOnlyCollection<string> excludeFragments)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (IsExcludedPath(current, excludeFragments))
                continue;

            IEnumerable<string> files = Array.Empty<string>();
            try { files = Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly); } catch { }
            foreach (var file in files)
            {
                if (IsExcludedPath(file, excludeFragments))
                    continue;
                var extension = Path.GetExtension(file);
                if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }

            IEnumerable<string> directories = Array.Empty<string>();
            try { directories = Directory.EnumerateDirectories(current); } catch { }
            foreach (var directory in directories)
                stack.Push(directory);
        }
    }

    private static bool IsExcludedPath(string path, IReadOnlyCollection<string> excludeFragments)
    {
        foreach (var fragment in excludeFragments)
        {
            if (string.IsNullOrWhiteSpace(fragment))
                continue;
            if (path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private sealed class CandidateFiles
    {
        public IReadOnlyList<string> CsprojFiles { get; }
        public IReadOnlyList<string> Psd1Files { get; }
        public IReadOnlyList<string> Ps1Files { get; }

        public CandidateFiles(IReadOnlyList<string> csprojFiles, IReadOnlyList<string> psd1Files, IReadOnlyList<string> ps1Files)
        {
            CsprojFiles = csprojFiles;
            Psd1Files = psd1Files;
            Ps1Files = ps1Files;
        }
    }
}
