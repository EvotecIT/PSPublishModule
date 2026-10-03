namespace PowerForge.Web;

/// <summary>Resolves file-backed dependencies used by site generation.</summary>
internal static class WebSiteInputDiscovery
{
    internal static IReadOnlyList<string> Discover(string configPath, string? outputRoot)
    {
        var (spec, path) = WebSiteSpecLoader.LoadWithPath(configPath);
        var plan = WebSitePlanner.Plan(spec, path);
        var inputs = new List<string>(WebSiteSpecLoader.DiscoverConfigurationInputs(path));
        void Add(string? input)
        {
            if (!string.IsNullOrWhiteSpace(input))
                inputs.Add(Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(plan.RootPath, input)));
        }
        Add(plan.ContentRoot);
        foreach (var input in plan.ContentRoots) Add(input);
        foreach (var collection in plan.Collections) Add(collection.InputPath);
        Add(plan.ProjectsRoot);
        Add(plan.SharedRoot);
        Add(string.IsNullOrWhiteSpace(spec.ThemesRoot) ? "themes" : spec.ThemesRoot);
        Add(string.IsNullOrWhiteSpace(spec.DataRoot) ? "data" : spec.DataRoot);
        Add("static");
        Add(spec.Versioning?.HubPath);
        foreach (var asset in spec.StaticAssets ?? Array.Empty<StaticAssetSpec>()) Add(asset.Source);
        foreach (var css in spec.AssetRegistry?.CriticalCss ?? Array.Empty<CriticalCssSpec>()) Add(css.Path);
        foreach (var map in spec.Xref?.MapFiles ?? Array.Empty<string>()) Add(map);
        // Root-level fragments/assets can be referenced by front matter.
        inputs.AddRange(Directory.EnumerateFiles(plan.RootPath, "*", SearchOption.TopDirectoryOnly));
        if (!string.IsNullOrWhiteSpace(outputRoot))
        {
            foreach (var apiRoot in spec.Search?.ApiRoots ?? Array.Empty<string>())
                inputs.Add(Path.GetFullPath(Path.Combine(outputRoot, apiRoot)));
        }
        return inputs.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
    }
}
