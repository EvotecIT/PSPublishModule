using System;
using System.IO;

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
            if (relative.Length == 0 || Path.IsPathRooted(evidence.Output) ||
                !directory.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"BenchmarkEvidence '{evidence.Name}' Output must be a directory inside the site output, such as data/benchmarks.");

            var catalogPath = Path.IsPathRooted(evidence.Catalog) ? evidence.Catalog : Path.Combine(rootPath, evidence.Catalog);
            var view = new PowerForge.BenchmarkComparisonViewBuilder().Build(catalogPath);
            PowerForge.BenchmarkComparisonViewBuilder.WriteAreaFiles(view, directory);
        }
    }
}
