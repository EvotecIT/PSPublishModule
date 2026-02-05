namespace PowerForge.Web;

internal static class EditLinkResolver
{
    internal static string? Resolve(
        SiteSpec site,
        ProjectSpec? project,
        string rootPath,
        string sourcePath,
        string? editPathOverride)
    {
        if (site is null) return null;

        var editSpec = project?.EditLinks ?? site.EditLinks;
        if (editSpec is null || !editSpec.Enabled)
            return null;

        if (!string.IsNullOrWhiteSpace(editPathOverride))
        {
            var trimmed = editPathOverride.Trim();
            if (IsAbsoluteUrl(trimmed))
                return trimmed;
        }

        var repo = project?.Repository;
        var template = ResolveTemplate(editSpec, repo);
        if (string.IsNullOrWhiteSpace(template))
            return null;

        var path = ResolvePath(editSpec, repo, rootPath, sourcePath, editPathOverride);
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var rendered = ReplaceTokens(template, repo, path);
        if (!ContainsPathToken(template))
            rendered = AppendPath(rendered, path);
        return rendered;
    }

    private static string? ResolveTemplate(EditLinksSpec editSpec, RepositorySpec? repo)
    {
        if (!string.IsNullOrWhiteSpace(editSpec.Template))
            return editSpec.Template;

        if (repo is null)
            return null;

        if (repo.Provider == RepositoryProvider.GitHub &&
            !string.IsNullOrWhiteSpace(repo.Owner) &&
            !string.IsNullOrWhiteSpace(repo.Name))
        {
            var branch = string.IsNullOrWhiteSpace(repo.Branch) ? "main" : repo.Branch;
            return $"https://github.com/{repo.Owner}/{repo.Name}/edit/{branch}/{{path}}";
        }

        return null;
    }

    private static string ResolvePath(
        EditLinksSpec editSpec,
        RepositorySpec? repo,
        string rootPath,
        string sourcePath,
        string? editPathOverride)
    {
        string path;
        var explicitPath = editPathOverride?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            path = explicitPath.TrimStart('\\', '/');
        }
        else
        {
            path = Path.GetRelativePath(rootPath, sourcePath);
        }

        var basePath = !string.IsNullOrWhiteSpace(editSpec.PathBase)
            ? editSpec.PathBase
            : repo?.PathBase;

        if (!string.IsNullOrWhiteSpace(basePath))
            path = Path.Combine(basePath, path);

        return NormalizePath(path);
    }

    private static string ReplaceTokens(string template, RepositorySpec? repo, string path)
    {
        var branch = repo?.Branch ?? "main";
        var owner = repo?.Owner ?? string.Empty;
        var name = repo?.Name ?? string.Empty;
        var provider = repo?.Provider.ToString().ToLowerInvariant() ?? string.Empty;

        return template
            .Replace("{path}", path, StringComparison.OrdinalIgnoreCase)
            .Replace("{branch}", branch, StringComparison.OrdinalIgnoreCase)
            .Replace("{owner}", owner, StringComparison.OrdinalIgnoreCase)
            .Replace("{repo}", name, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", name, StringComparison.OrdinalIgnoreCase)
            .Replace("{provider}", provider, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsPathToken(string template)
        => template.Contains("{path}", StringComparison.OrdinalIgnoreCase);

    private static string AppendPath(string template, string path)
    {
        var trimmed = template.TrimEnd('/');
        return $"{trimmed}/{path}";
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

    private static bool IsAbsoluteUrl(string value)
        => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
           value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
