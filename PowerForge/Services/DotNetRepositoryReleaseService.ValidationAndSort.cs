using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static HashSet<string> BuildNameSet(IEnumerable<string>? items)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (items is null) return set;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            set.Add(item.Trim());
        }
        return set;
    }

    private static IReadOnlyList<string> BuildExcludeDirectories(IEnumerable<string>? items)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in DefaultExcludeDirectories)
            set.Add(dir);

        if (items is not null)
        {
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                set.Add(item.Trim());
            }
        }

        return set.ToArray();
    }

    private (bool Success, string? ErrorMessage) ValidatePublishPreflight(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec)
    {
        foreach (var project in projects)
        {
            if (!string.IsNullOrWhiteSpace(project.ErrorMessage))
                return (false, $"Publish preflight failed: {project.ProjectName} has errors: {project.ErrorMessage}");

            if (string.IsNullOrWhiteSpace(project.NewVersion))
                return (false, $"Publish preflight failed: {project.ProjectName} has no resolved version.");

            if (project.Packages.Count == 0)
                return (false, $"Publish preflight failed: {project.ProjectName} has no packages to publish.");

            foreach (var pkg in project.Packages)
            {
                if (!File.Exists(pkg))
                    return (false, $"Publish preflight failed: package not found: {pkg}");
            }

            var latest = _resolver.ResolveLatest(
                packageId: project.ProjectName,
                sources: spec.VersionSources,
                credential: spec.VersionSourceCredential,
                includePrerelease: spec.IncludePrerelease);

            if (latest is not null && Version.TryParse(project.NewVersion, out var target))
            {
                if (latest >= target && !spec.SkipDuplicate)
                    return (false, $"Publish preflight failed: {project.ProjectName} version {target} already exists (latest {latest}). Use -SkipDuplicate to allow.");
            }
        }

        return (true, null);
    }

    private IReadOnlyList<DotNetRepositoryProjectResult> SortProjectsForPublish(IReadOnlyList<DotNetRepositoryProjectResult> projects)
    {
        var byName = projects.ToDictionary(p => p.ProjectName, StringComparer.OrdinalIgnoreCase);
        var deps = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            deps[project.ProjectName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var doc = XDocument.Load(project.CsprojPath);
                foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase)))
                {
                    var include = pr.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(include)) continue;

                    var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
                    var depPath = Path.GetFullPath(Path.Combine(csprojDir, include));
                    var depName = Path.GetFileNameWithoutExtension(depPath);
                    if (string.IsNullOrWhiteSpace(depName)) continue;
                    if (byName.ContainsKey(depName))
                        deps[project.ProjectName].Add(depName);
                }
            }
            catch
            {
                // Ignore dependency parsing errors; fall back to original order.
            }
        }

        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
            inDegree[p.ProjectName] = 0;

        foreach (var kvp in deps)
        {
            foreach (var dep in kvp.Value)
            {
                if (inDegree.ContainsKey(dep))
                    inDegree[dep]++;
            }
        }

        var queue = new Queue<string>(inDegree.Where(k => k.Value == 0).Select(k => k.Key));
        var ordered = new List<DotNetRepositoryProjectResult>();

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (byName.TryGetValue(name, out var proj))
                ordered.Add(proj);

            if (!deps.TryGetValue(name, out var children)) continue;
            foreach (var child in children)
            {
                if (!inDegree.ContainsKey(child)) continue;
                inDegree[child]--;
                if (inDegree[child] == 0)
                    queue.Enqueue(child);
            }
        }

        if (ordered.Count != projects.Count)
        {
            _logger.Warn("Publish order dependency analysis detected a cycle; falling back to name order.");
            return projects.OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        return ordered;
    }

}
