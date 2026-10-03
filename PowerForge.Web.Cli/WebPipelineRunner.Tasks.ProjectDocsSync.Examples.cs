using System.Text;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void MaterializeProjectExampleDocs(string targetExamplesRoot, ProjectDocsCatalogItem project)
    {
        if (string.IsNullOrWhiteSpace(targetExamplesRoot) || !Directory.Exists(targetExamplesRoot))
            return;

        var slug = string.IsNullOrWhiteSpace(project.Slug) ? "project" : project.Slug.Trim().ToLowerInvariant();
        var projectName = GetProjectDisplayName(project.GitHubRepoUrl, slug);
        var sourceRepo = string.IsNullOrWhiteSpace(project.GitHubRepoUrl)
            ? null
            : project.GitHubRepoUrl.Trim();

        var scriptFiles = Directory
            .EnumerateFiles(targetExamplesRoot, "*.ps1", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var scriptPath in scriptFiles)
        {
            var companionMarkdown = Path.ChangeExtension(scriptPath, ".md");
            if (File.Exists(companionMarkdown) && !IsGeneratedExampleMarkdown(companionMarkdown))
                continue;

            var title = HumanizeExampleTitle(Path.GetFileNameWithoutExtension(scriptPath));
            var relativeFolder = Path.GetDirectoryName(Path.GetRelativePath(targetExamplesRoot, scriptPath)) ?? string.Empty;
            var lines = new List<string>
            {
                "---",
                $"title: {YamlQuote(title)}",
                "layout: docs",
                $"meta.generated_by: {YamlQuote("powerforge.project-docs-sync")}",
                "---",
                string.Empty,
                $"An example script from the {projectName} repository.",
                string.Empty
            };

            if (!string.IsNullOrWhiteSpace(sourceRepo))
            {
                lines.Add($"- Source repository: [{sourceRepo}]({sourceRepo})");
                if (!string.IsNullOrWhiteSpace(relativeFolder))
                    lines.Add($"- Example group: `{relativeFolder.Replace('\\', '/')}`");
                lines.Add(string.Empty);
            }

            lines.Add("```powershell");
            lines.AddRange(File.ReadAllLines(scriptPath));
            lines.Add("```");

            File.WriteAllText(companionMarkdown, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        }

        var markdownFiles = Directory
            .EnumerateFiles(targetExamplesRoot, "*", SearchOption.AllDirectories)
            .Where(static path => IsMarkdownExtension(Path.GetExtension(path)))
            .Where(static path => !string.Equals(Path.GetFileName(path), "_index.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var markdownPath in markdownFiles)
        {
            var existingContent = File.ReadAllText(markdownPath);
            if (HasYamlFrontMatter(existingContent))
                continue;

            var title = GetMarkdownHeadingTitle(existingContent) ?? HumanizeExampleTitle(Path.GetFileNameWithoutExtension(markdownPath));
            var relativeFolder = Path.GetDirectoryName(Path.GetRelativePath(targetExamplesRoot, markdownPath)) ?? string.Empty;
            var lines = new List<string>
            {
                "---",
                $"title: {YamlQuote(title)}",
                "layout: docs",
                $"meta.generated_by: {YamlQuote("powerforge.project-docs-sync")}",
                "---",
                string.Empty
            };

            if (!string.IsNullOrWhiteSpace(sourceRepo))
            {
                lines.Add($"- Source repository: [{sourceRepo}]({sourceRepo})");
                if (!string.IsNullOrWhiteSpace(relativeFolder))
                    lines.Add($"- Example group: `{relativeFolder.Replace('\\', '/')}`");
                lines.Add(string.Empty);
            }

            lines.Add(existingContent.TrimStart('\uFEFF'));
            File.WriteAllText(markdownPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        }

        var directories = Directory
            .EnumerateDirectories(targetExamplesRoot, "*", SearchOption.AllDirectories)
            .OrderByDescending(static path => path.Length)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Append(targetExamplesRoot)
            .ToList();

        foreach (var directory in directories)
        {
            var indexPath = Path.Combine(directory, "_index.md");
            if (File.Exists(indexPath) && !IsGeneratedExampleMarkdown(indexPath))
                continue;

            var relativeDirectory = Path.GetRelativePath(targetExamplesRoot, directory);
            var directoryName = directory.Equals(targetExamplesRoot, StringComparison.OrdinalIgnoreCase)
                ? projectName
                : HumanizeExampleTitle(Path.GetFileName(directory));

            var childDirectories = Directory
                .EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var childMarkdown = Directory
                .EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
                .Where(static path => !string.Equals(Path.GetFileName(path), "_index.md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var lines = new List<string>
            {
                "---",
                $"title: {YamlQuote(directory.Equals(targetExamplesRoot, StringComparison.OrdinalIgnoreCase) ? $"{projectName} Examples" : directoryName)}",
                $"description: {YamlQuote(directory.Equals(targetExamplesRoot, StringComparison.OrdinalIgnoreCase) ? $"Examples for {projectName} from its source repository." : $"{directoryName} examples for {projectName}.")}",
                "layout: docs",
                $"meta.generated_by: {YamlQuote("powerforge.project-docs-sync")}",
                "---",
                string.Empty,
                directory.Equals(targetExamplesRoot, StringComparison.OrdinalIgnoreCase)
                    ? $"Browse runnable examples and usage patterns maintained with {projectName}."
                    : $"Examples in the `{relativeDirectory.Replace('\\', '/')}` group.",
                string.Empty
            };

            if (!string.IsNullOrWhiteSpace(sourceRepo))
            {
                lines.Add($"- Source repository: [{sourceRepo}]({sourceRepo})");
                lines.Add(string.Empty);
            }

            if (childDirectories.Count > 0)
            {
                lines.Add("## Groups");
                lines.Add(string.Empty);
                foreach (var childDirectory in childDirectories)
                {
                    var childName = GetProjectExampleDocumentTitle(Path.Combine(childDirectory, "_index.md"), Path.GetFileName(childDirectory));
                    lines.Add($"- [{childName}](./{Path.GetFileName(childDirectory)}/)");
                }
                lines.Add(string.Empty);
            }

            if (childMarkdown.Count > 0)
            {
                lines.Add("## Examples");
                lines.Add(string.Empty);
                foreach (var childFile in childMarkdown)
                {
                    var childName = GetProjectExampleDocumentTitle(childFile, Path.GetFileNameWithoutExtension(childFile));
                    lines.Add($"- [{childName}](./{Path.GetFileNameWithoutExtension(childFile)}/)");
                }
                lines.Add(string.Empty);
            }

            File.WriteAllText(indexPath, string.Join(Environment.NewLine, lines), Encoding.UTF8);
        }

        StampProjectExampleMetadata(targetExamplesRoot, slug, projectName, project.HubPath);
    }

    private static void StampProjectExampleMetadata(string targetExamplesRoot, string slug, string projectName, string? projectHubPath)
    {
        if (string.IsNullOrWhiteSpace(targetExamplesRoot) || !Directory.Exists(targetExamplesRoot) || string.IsNullOrWhiteSpace(slug))
            return;

        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var hubPath = NormalizeProjectDocsHubPath(projectHubPath, normalizedSlug);
        var examplesPath = hubPath + "examples/";

        foreach (var markdownPath in Directory.EnumerateFiles(targetExamplesRoot, "*", SearchOption.AllDirectories).Where(static path => IsMarkdownExtension(Path.GetExtension(path))))
        {
            var content = File.ReadAllText(markdownPath);
            if (!TryGetFrontMatterLines(content, out var allLines))
                continue;

            var closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            if (closingIndex <= 0)
                continue;

            var changed = false;
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_base_slug", normalizedSlug);
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_name", projectName);
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_section", "examples");
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_hub_path", hubPath);
            closingIndex = FindFrontMatterClosingMarkerIndex(allLines);
            changed |= UpsertFrontMatterString(allLines, closingIndex, "meta.project_link_examples", examplesPath);

            if (!changed)
                continue;

            var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            File.WriteAllText(markdownPath, string.Join(newline, allLines), Encoding.UTF8);
        }
    }

    private static string GetProjectExampleDocumentTitle(string markdownPath, string fallbackName)
    {
        if (File.Exists(markdownPath))
        {
            var content = File.ReadAllText(markdownPath);
            var (matter, body) = PowerForge.Web.FrontMatterParser.Parse(content.TrimStart('\uFEFF'));
            if (!string.IsNullOrWhiteSpace(matter?.Title))
                return matter.Title;

            var heading = GetMarkdownHeadingTitle(body);
            if (!string.IsNullOrWhiteSpace(heading))
                return heading;
        }

        return HumanizeExampleTitle(fallbackName);
    }

    // A single lowercase 's' is kept with plural acronyms such as GPOs and APIs.
    private static readonly System.Text.RegularExpressions.Regex ExampleTitleWordBoundary = new(
        "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z](?:[a-rt-z]|s[a-z]))|(?<=[A-Za-z])(?=[0-9])",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex ExampleTitleExtensions = new(
        @"(?:\.(?:ps1|psm1|psd1|cs|md|markdown))+$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Turns an example file or folder name such as <c>Example-BuildingTags.ps1</c> into a readable title
    /// (<c>Building Tags</c>). Existing capitals are kept (TextInfo.ToTitleCase would lowercase "BuildingTags"
    /// to "Buildingtags"), leftover script extensions are removed, and a leading "Example" word is dropped when
    /// other words remain because these titles are already listed under an Examples heading.
    /// </summary>
    internal static string HumanizeExampleTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Example";

        var name = ExampleTitleExtensions.Replace(value.Trim(), string.Empty);

        var words = ExampleTitleWordBoundary
            .Replace(name.Replace('_', ' ').Replace('-', ' ').Replace('.', ' '), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (words.Count == 0)
            return "Example";

        if (words.Count > 1 &&
            (words[0].Equals("Example", StringComparison.OrdinalIgnoreCase) || words[0].Equals("Examples", StringComparison.OrdinalIgnoreCase)) &&
            !words.Skip(1).All(static word => word.All(char.IsDigit)))
        {
            words.RemoveAt(0);
        }

        return string.Join(' ', words.Select(static word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static bool IsGeneratedExampleMarkdown(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        var content = File.ReadAllText(path);
        return content.Contains("meta.generated_by: \"powerforge.project-docs-sync\"", StringComparison.Ordinal);
    }

}
