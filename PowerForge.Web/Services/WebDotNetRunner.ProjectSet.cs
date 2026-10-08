using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebDotNetRunner
{
    private static WebDotNetResult BuildProjectSet(WebDotNetBuildOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ProjectOrSolution))
            throw new ArgumentException("Specify either Projects or ProjectOrSolution, not both.", nameof(options));
        if (!string.IsNullOrWhiteSpace(options.Runtime))
            throw new ArgumentException("Project-set builds do not support a solution-level runtime identifier. Use separate project build steps or configure the runtime in each project.", nameof(options));
        var projects = options.Projects.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var project in projects)
            if (!File.Exists(project)) throw new FileNotFoundException("Build project was not found.", project);

        // An SDK solution builds shared references once and keeps output beside each project.
        var directory = Path.Combine(Path.GetTempPath(), "pf-web-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var solution = Path.Combine(directory, "projects.slnx");
            new XDocument(new XElement("Solution", projects.Select(project =>
                new XElement("Project", new XAttribute("Path", project.Replace('\\', '/')))))).Save(solution);
            return Build(new WebDotNetBuildOptions
            {
                ProjectOrSolution = solution,
                Configuration = options.Configuration,
                Framework = options.Framework,
                Restore = options.Restore
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
