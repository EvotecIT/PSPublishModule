using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PowerForge;

namespace PSPublishModule;

internal static class ProjectVersionScanner
{
    internal sealed class CandidateFiles
    {
        internal IReadOnlyList<string> CsprojFiles { get; }
        internal IReadOnlyList<string> Psd1Files { get; }
        internal IReadOnlyList<string> Ps1Files { get; }

        internal CandidateFiles(IReadOnlyList<string> csprojFiles, IReadOnlyList<string> psd1Files, IReadOnlyList<string> ps1Files)
        {
            CsprojFiles = csprojFiles;
            Psd1Files = psd1Files;
            Ps1Files = ps1Files;
        }
    }

    internal static IReadOnlyCollection<string> BuildExcludeFragments(IEnumerable<string>? excludeFolders)
    {
        var excludeFragments = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "obj", "bin" };
        if (excludeFolders == null) return excludeFragments;

        foreach (var e in excludeFolders)
        {
            if (string.IsNullOrWhiteSpace(e)) continue;
            excludeFragments.Add(e.Trim().Trim('"'));
        }

        return excludeFragments;
    }

    internal static CandidateFiles FindCandidateFiles(string root, IReadOnlyCollection<string> excludeFragments)
    {
        var csprojs = new List<string>();
        var psd1s = new List<string>();
        var ps1s = new List<string>();

        foreach (var file in EnumerateFiles(root, excludeFragments))
        {
            var ext = System.IO.Path.GetExtension(file);
            if (ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)) csprojs.Add(file);
            else if (ext.Equals(".psd1", StringComparison.OrdinalIgnoreCase)) psd1s.Add(file);
            else if (ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase)) ps1s.Add(file);
        }

        return new CandidateFiles(csprojs, psd1s, ps1s);
    }

    internal static IReadOnlyList<ProjectVersionInfo> Discover(string root, string? moduleName, IReadOnlyCollection<string> excludeFragments)
    {
        var candidates = FindCandidateFiles(root, excludeFragments);
        var results = new List<ProjectVersionInfo>();

        foreach (var file in candidates.CsprojFiles)
        {
            var baseName = System.IO.Path.GetFileNameWithoutExtension(file);
            if (moduleName != null && !string.Equals(baseName, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryGetVersionFromCsproj(file, out var ver))
                results.Add(new ProjectVersionInfo(ver, file, ProjectVersionSourceKind.Csproj));
        }

        foreach (var file in candidates.Psd1Files)
        {
            var baseName = System.IO.Path.GetFileNameWithoutExtension(file);
            if (moduleName != null && !string.Equals(baseName, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryGetVersionFromPsd1(file, out var ver))
                results.Add(new ProjectVersionInfo(ver, file, ProjectVersionSourceKind.PowerShellModule));
        }

        foreach (var file in candidates.Ps1Files)
        {
            if (!LooksLikeBuildScript(file)) continue;
            if (TryGetVersionFromBuildScript(file, out var ver))
                results.Add(new ProjectVersionInfo(ver, file, ProjectVersionSourceKind.BuildScript));
        }

        return results;
    }

    internal static bool LooksLikeBuildScript(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return text.IndexOf("Invoke-ModuleBuild", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Build-Module", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    internal static bool TryGetVersionFromCsproj(string projectFile, out string version)
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
        catch { return false; }
    }

    internal static bool TryGetVersionFromPsd1(string manifestFile, out string version)
    {
        version = string.Empty;
        try
        {
            if (!ManifestEditor.TryGetTopLevelString(manifestFile, "ModuleVersion", out var v) || string.IsNullOrWhiteSpace(v))
                return false;
            version = v!;
            return true;
        }
        catch { return false; }
    }

    internal static bool TryGetVersionFromBuildScript(string scriptFile, out string version)
    {
        version = string.Empty;
        try
        {
            var content = File.ReadAllText(scriptFile);
            var m = Regex.Match(content, "ModuleVersion\\s*=\\s*['\\\"]?([\\d\\.]+)['\\\"]?", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            version = m.Groups[1].Value;
            return !string.IsNullOrWhiteSpace(version);
        }
        catch { return false; }
    }

    private static bool TryMatchVersionTag(string content, string tag, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrEmpty(content)) return false;
        var re = new Regex("<" + Regex.Escape(tag) + ">([\\d\\.]+)</" + Regex.Escape(tag) + ">", RegexOptions.IgnoreCase);
        var m = re.Match(content);
        if (!m.Success) return false;
        version = m.Groups[1].Value;
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
            foreach (var f in files)
            {
                if (IsExcludedPath(f, excludeFragments)) continue;
                var ext = System.IO.Path.GetExtension(f);
                if (ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".psd1", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                {
                    yield return f;
                }
            }

            IEnumerable<string> dirs = Array.Empty<string>();
            try { dirs = Directory.EnumerateDirectories(current); } catch { }
            foreach (var d in dirs) stack.Push(d);
        }
    }

    private static bool IsExcludedPath(string path, IReadOnlyCollection<string> excludeFragments)
    {
        foreach (var fragment in excludeFragments)
        {
            if (string.IsNullOrWhiteSpace(fragment)) continue;
            if (path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }
}
