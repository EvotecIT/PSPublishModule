using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    /// <summary>
    /// Resolves paths/defaults from <paramref name="spec"/> and produces an ordered execution plan.
    /// </summary>
    /// <param name="spec">Publish spec.</param>
    /// <param name="configPath">
    /// Optional path to the JSON config file. When provided, relative paths are resolved against its directory,
    /// unless <see cref="DotNetPublishDotNetOptions.ProjectRoot"/> is set.
    /// </param>
    public DotNetPublishPlan Plan(DotNetPublishSpec spec, string? configPath)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (spec.Targets is null || spec.Targets.Length == 0)
            throw new ArgumentException("Targets must not be empty.", nameof(spec));

        var configDir = string.IsNullOrWhiteSpace(configPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();

        var projectRoot = string.IsNullOrWhiteSpace(spec.DotNet.ProjectRoot)
            ? configDir
            : ResolvePath(configDir, spec.DotNet.ProjectRoot!);

        if (!Directory.Exists(projectRoot))
            throw new DirectoryNotFoundException($"ProjectRoot not found: {projectRoot}");

        var solutionPath = string.IsNullOrWhiteSpace(spec.DotNet.SolutionPath)
            ? null
            : ResolvePath(projectRoot, spec.DotNet.SolutionPath!);

        if (!string.IsNullOrWhiteSpace(solutionPath) && !File.Exists(solutionPath))
            throw new FileNotFoundException($"SolutionPath not found: {solutionPath}");

        var cfg = string.IsNullOrWhiteSpace(spec.DotNet.Configuration) ? "Release" : spec.DotNet.Configuration.Trim();

        var defaultsRids = (spec.DotNet.Runtimes ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var msbuildProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (spec.DotNet.MsBuildProperties is not null)
        {
            foreach (var kv in spec.DotNet.MsBuildProperties)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                msbuildProps[kv.Key.Trim()] = kv.Value ?? string.Empty;
            }
        }

        var targets = new List<DotNetPublishTargetPlan>();
        foreach (var t in spec.Targets)
        {
            if (t is null) continue;
            if (string.IsNullOrWhiteSpace(t.Name))
                throw new ArgumentException("Target.Name is required.", nameof(spec));
            if (string.IsNullOrWhiteSpace(t.ProjectPath))
                throw new ArgumentException($"Target.ProjectPath is required for '{t.Name}'.", nameof(spec));
            if (t.Publish is null)
                throw new ArgumentException($"Target.Publish is required for '{t.Name}'.", nameof(spec));

            var frameworks = (t.Publish.Frameworks ?? Array.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (frameworks.Length == 0)
            {
                if (string.IsNullOrWhiteSpace(t.Publish.Framework))
                    throw new ArgumentException($"Target.Publish.Framework is required for '{t.Name}' (or set Target.Publish.Frameworks).", nameof(spec));
                frameworks = new[] { t.Publish.Framework.Trim() };
            }

            var projectPath = ResolvePath(projectRoot, t.ProjectPath);
            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"ProjectPath not found for '{t.Name}': {projectPath}");

            var rids = (t.Publish.Runtimes ?? Array.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (rids.Length == 0) rids = defaultsRids;
            if (rids.Length == 0)
                throw new ArgumentException($"No runtimes provided for target '{t.Name}'. Set Target.Publish.Runtimes or DotNet.Runtimes.", nameof(spec));

            // Clone publish settings for the plan and force resolved runtimes.
            var publish = new DotNetPublishPublishOptions
            {
                Style = t.Publish.Style,
                Framework = frameworks[0],
                Frameworks = frameworks,
                Runtimes = rids,
                OutputPath = t.Publish.OutputPath,
                UseStaging = t.Publish.UseStaging,
                ClearOutput = t.Publish.ClearOutput,
                Slim = t.Publish.Slim,
                KeepSymbols = t.Publish.KeepSymbols,
                KeepDocs = t.Publish.KeepDocs,
                PruneReferences = t.Publish.PruneReferences,
                Zip = t.Publish.Zip,
                ZipPath = t.Publish.ZipPath,
                ZipNameTemplate = t.Publish.ZipNameTemplate,
                RenameTo = t.Publish.RenameTo,
                ReadyToRun = t.Publish.ReadyToRun,
                Sign = t.Publish.Sign
            };

            targets.Add(new DotNetPublishTargetPlan
            {
                Name = t.Name.Trim(),
                Kind = t.Kind,
                ProjectPath = projectPath,
                Publish = publish
            });
        }

        var outputs = new DotNetPublishOutputs
        {
            ManifestJsonPath = string.IsNullOrWhiteSpace(spec.Outputs.ManifestJsonPath)
                ? ResolvePath(projectRoot, Path.Combine("Artifacts", "DotNetPublish", "manifest.json"))
                : ResolvePath(projectRoot, spec.Outputs.ManifestJsonPath!),
            ManifestTextPath = string.IsNullOrWhiteSpace(spec.Outputs.ManifestTextPath)
                ? ResolvePath(projectRoot, Path.Combine("Artifacts", "DotNetPublish", "manifest.txt"))
                : ResolvePath(projectRoot, spec.Outputs.ManifestTextPath!)
        };

        var steps = new List<DotNetPublishStep>();

        var distinctRuntimes = targets
            .SelectMany(t => t.Publish.Runtimes ?? Array.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (spec.DotNet.Clean)
            steps.Add(new DotNetPublishStep { Key = "clean", Kind = DotNetPublishStepKind.Clean, Title = "Clean" });

        if (spec.DotNet.Restore)
        {
            if (spec.DotNet.NoRestoreInPublish && distinctRuntimes.Length > 0)
            {
                foreach (var rid in distinctRuntimes)
                {
                    steps.Add(new DotNetPublishStep
                    {
                        Key = $"restore:{rid}",
                        Kind = DotNetPublishStepKind.Restore,
                        Title = "Restore",
                        Runtime = rid
                    });
                }
            }
            else
            {
                steps.Add(new DotNetPublishStep { Key = "restore", Kind = DotNetPublishStepKind.Restore, Title = "Restore" });
            }
        }

        if (spec.DotNet.Build)
        {
            if (spec.DotNet.NoBuildInPublish && distinctRuntimes.Length > 0)
            {
                foreach (var rid in distinctRuntimes)
                {
                    steps.Add(new DotNetPublishStep
                    {
                        Key = $"build:{rid}",
                        Kind = DotNetPublishStepKind.Build,
                        Title = "Build",
                        Runtime = rid
                    });
                }
            }
            else
            {
                steps.Add(new DotNetPublishStep { Key = "build", Kind = DotNetPublishStepKind.Build, Title = "Build" });
            }
        }

        foreach (var t in targets)
        {
            var frameworks = (t.Publish.Frameworks ?? Array.Empty<string>())
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (frameworks.Length == 0 && !string.IsNullOrWhiteSpace(t.Publish.Framework))
                frameworks = new[] { t.Publish.Framework.Trim() };

            foreach (var framework in frameworks)
            {
                foreach (var rid in t.Publish.Runtimes)
                {
                    var key = $"publish:{t.Name}:{framework}:{rid}";
                    steps.Add(new DotNetPublishStep
                    {
                        Key = key,
                        Kind = DotNetPublishStepKind.Publish,
                        Title = "Publish",
                        TargetName = t.Name,
                        Framework = framework,
                        Runtime = rid
                    });
                }
            }
        }

        steps.Add(new DotNetPublishStep { Key = "manifest", Kind = DotNetPublishStepKind.Manifest, Title = "Write manifest" });

        return new DotNetPublishPlan
        {
            ProjectRoot = projectRoot,
            Configuration = cfg,
            SolutionPath = solutionPath,
            Restore = spec.DotNet.Restore,
            Clean = spec.DotNet.Clean,
            Build = spec.DotNet.Build,
            NoRestoreInPublish = spec.DotNet.NoRestoreInPublish,
            NoBuildInPublish = spec.DotNet.NoBuildInPublish,
            MsBuildProperties = msbuildProps,
            Targets = targets.ToArray(),
            Outputs = outputs,
            Steps = steps.ToArray()
        };
    }

}
