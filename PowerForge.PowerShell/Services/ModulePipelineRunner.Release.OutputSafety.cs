using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static void AddArtefactOutputPathConflicts(
        ModulePipelinePlan plan,
        ICollection<string> conflicts)
    {
        var priorOutputs = new List<ArtefactOutputOperations>();
        foreach (ConfigurationArtefactSegment artefact in plan.Artefacts
                     .Where(static item => item is not null && item.Configuration?.Enabled == true))
        {
            var current = new ArtefactOutputOperations(
                artefact,
                CollectPersistentArtefactDestructivePaths(plan, artefact, artefact.Configuration!));

            foreach (ArtefactOutputOperations prior in priorOutputs)
            {
                (ArtefactDestructivePath Prior, ArtefactDestructivePath Current)? overlap =
                    FindDestructiveOutputOverlap(prior.Paths, current.Paths);
                if (!overlap.HasValue)
                    continue;

                bool sameZipOutput = overlap.Value.Prior.Kind == ArtefactDestructivePathKind.ExactFile &&
                                     overlap.Value.Current.Kind == ArtefactDestructivePathKind.ExactFile &&
                                     IsZipFilePath(overlap.Value.Prior.Path) &&
                                     string.Equals(
                                         overlap.Value.Prior.Path,
                                         overlap.Value.Current.Path,
                                         GetPathComparison(overlap.Value.Prior.Path, overlap.Value.Current.Path));
                conflicts.Add(sameZipOutput
                    ? $"Artefacts '{prior.Artefact.ArtefactType}' and '{current.Artefact.ArtefactType}' resolve to the same zip output file '{overlap.Value.Prior.Path}'. Configure a unique Path or ArtefactName for each enabled packed artefact."
                    : $"Artefacts '{prior.Artefact.ArtefactType}' and '{current.Artefact.ArtefactType}' have overlapping destructive outputs: " +
                      $"{overlap.Value.Prior.Label} '{overlap.Value.Prior.Path}' conflicts with {overlap.Value.Current.Label} '{overlap.Value.Current.Path}'. " +
                      "Configure a unique Path, ArtefactName, module layout, or copy destination for each enabled artefact.");
                break;
            }

            priorOutputs.Add(current);
        }
    }

    private static IReadOnlyList<ArtefactDestructivePath> CollectPersistentArtefactDestructivePaths(
        ModulePipelinePlan plan,
        ConfigurationArtefactSegment artefact,
        ArtefactConfiguration configuration)
    {
        if (artefact.ArtefactType is ArtefactType.Unpacked or ArtefactType.Script)
            return CollectArtefactDestructivePaths(plan, artefact, configuration);

        string outputRoot = ArtefactLayoutPathResolver.ResolveOutputRoot(
            configuration.Path,
            plan.ProjectRoot,
            plan.ModuleName,
            plan.ResolvedVersion,
            plan.PreRelease,
            artefact.ArtefactType);
        var paths = new List<ArtefactDestructivePath>();
        if (configuration.DoNotClear != true)
        {
            paths.Add(new ArtefactDestructivePath(
                outputRoot,
                "direct output files",
                ArtefactDestructivePathKind.DirectChildNonZipFiles));
        }

        string outputPath = Path.Combine(
            outputRoot,
            ArtefactLayoutPathResolver.ResolveArtefactFileName(
                configuration,
                plan.ModuleName,
                plan.ResolvedVersion,
                plan.PreRelease));
        paths.Add(new ArtefactDestructivePath(
            outputPath,
            "zip output file",
            ArtefactDestructivePathKind.ExactFile));
        if (artefact.ArtefactType == ArtefactType.Packed &&
            plan.SignModule &&
            plan.GenerateReleaseProvenance)
        {
            paths.Add(new ArtefactDestructivePath(
                outputPath + ".signing.json",
                "packed signing evidence file",
                ArtefactDestructivePathKind.ExactFile));
        }

        return paths;
    }

    private static (ArtefactDestructivePath Prior, ArtefactDestructivePath Current)? FindDestructiveOutputOverlap(
        IReadOnlyList<ArtefactDestructivePath> priorPaths,
        IReadOnlyList<ArtefactDestructivePath> currentPaths)
    {
        foreach (ArtefactDestructivePath prior in priorPaths)
        {
            foreach (ArtefactDestructivePath current in currentPaths)
            {
                if (DoDestructiveOutputsOverlap(prior, current))
                    return (prior, current);
            }
        }

        return null;
    }

    private static bool DoDestructiveOutputsOverlap(
        ArtefactDestructivePath left,
        ArtefactDestructivePath right)
    {
        if (left.Kind == ArtefactDestructivePathKind.DirectoryTree &&
            right.Kind == ArtefactDestructivePathKind.DirectoryTree)
        {
            return DoPathsOverlap(left.Path, right.Path);
        }

        if (left.Kind == ArtefactDestructivePathKind.DirectoryTree &&
            right.Kind == ArtefactDestructivePathKind.ExactFile)
        {
            return IsSameOrChildPath(left.Path, right.Path);
        }

        if (left.Kind == ArtefactDestructivePathKind.ExactFile &&
            right.Kind == ArtefactDestructivePathKind.DirectoryTree)
        {
            return IsSameOrChildPath(right.Path, left.Path);
        }

        if (left.Kind == ArtefactDestructivePathKind.ExactFile &&
            right.Kind == ArtefactDestructivePathKind.ExactFile)
        {
            return string.Equals(left.Path, right.Path, GetPathComparison(left.Path, right.Path));
        }

        if (left.Kind == ArtefactDestructivePathKind.DirectChildNonZipFiles &&
            right.Kind == ArtefactDestructivePathKind.ExactFile)
        {
            return !IsZipFilePath(right.Path) && IsDirectChildPath(left.Path, right.Path);
        }

        if (left.Kind == ArtefactDestructivePathKind.ExactFile &&
            right.Kind == ArtefactDestructivePathKind.DirectChildNonZipFiles)
        {
            return !IsZipFilePath(left.Path) && IsDirectChildPath(right.Path, left.Path);
        }

        if (left.Kind == ArtefactDestructivePathKind.DirectChildNonZipFiles &&
            right.Kind == ArtefactDestructivePathKind.DirectoryTree)
        {
            return string.Equals(left.Path, right.Path, GetPathComparison(left.Path, right.Path));
        }

        if (left.Kind == ArtefactDestructivePathKind.DirectoryTree &&
            right.Kind == ArtefactDestructivePathKind.DirectChildNonZipFiles)
        {
            return string.Equals(left.Path, right.Path, GetPathComparison(left.Path, right.Path));
        }

        return false;
    }

    private readonly struct ArtefactOutputOperations
    {
        internal ArtefactOutputOperations(
            ConfigurationArtefactSegment artefact,
            IReadOnlyList<ArtefactDestructivePath> paths)
        {
            Artefact = artefact;
            Paths = paths;
        }

        internal ConfigurationArtefactSegment Artefact { get; }

        internal IReadOnlyList<ArtefactDestructivePath> Paths { get; }
    }
}
