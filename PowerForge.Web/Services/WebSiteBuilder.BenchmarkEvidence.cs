using System;
using System.IO;
using System.Linq;

namespace PowerForge.Web;

/// <summary>Benchmark evidence files written into the site output.</summary>
public static partial class WebSiteBuilder
{
    // Writes each evidence view with an Output directory as index.json plus one file per area under the site output.
    private static void WriteBenchmarkEvidenceFiles(SiteSpec spec, string rootPath, string outputRoot)
    {
        foreach (var evidence in spec.BenchmarkEvidence ?? Array.Empty<BenchmarkEvidenceDataSpec>())
        {
            if (string.IsNullOrWhiteSpace(evidence.Output))
                continue;

            var relative = evidence.Output!.Trim().Replace('\\', '/').Trim('/');
            var fullOutputRoot = Path.GetFullPath(outputRoot);
            var directory = Path.GetFullPath(Path.Combine(fullOutputRoot, relative));
            var rootWithSeparator = fullOutputRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? fullOutputRoot
                : fullOutputRoot + Path.DirectorySeparatorChar;
            // Ordinal on every system: a case variant that only matches on a case-insensitive file system fails closed.
            if (relative.Length == 0 || Path.IsPathRooted(evidence.Output) ||
                !directory.StartsWith(rootWithSeparator, StringComparison.Ordinal))
                throw new InvalidOperationException($"BenchmarkEvidence '{evidence.Name}' Output must be a directory inside the site output, such as data/benchmarks.");

            var catalogPath = Path.IsPathRooted(evidence.Catalog) ? evidence.Catalog : Path.Combine(rootPath, evidence.Catalog);
            var view = new PowerForge.BenchmarkComparisonViewBuilder().Build(catalogPath);

            // A linked directory or file would redirect the writes outside the site output, possibly onto the evidence itself.
            var label = $"'{evidence.Name}' output";
            VisualStoryPathGuard.EnsureContainedPath(fullOutputRoot, directory, label, subject: "BenchmarkEvidence");
            foreach (var file in view.Index.Areas.Select(area => area.File).Prepend("index.json"))
                VisualStoryPathGuard.EnsureContainedPath(fullOutputRoot, Path.Combine(directory, file), label, subject: "BenchmarkEvidence");
            PowerForge.BenchmarkComparisonViewBuilder.WriteAreaFiles(view, directory);
        }
    }
}
