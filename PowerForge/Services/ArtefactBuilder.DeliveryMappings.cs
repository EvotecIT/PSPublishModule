using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    // Mappings operate on the finalized package inventory, never on arbitrary repository files.
    // This is also the plan consumed by preflight, promotion and packed signing metadata.
    private static IReadOnlyList<ModulePackageCopyEntry> ResolveMappedDeliveryCopyPlan(
        IReadOnlyList<ArtefactCopyMapping> mappings,
        string internalsRoot,
        string deliveryRoot,
        IReadOnlyList<string> packagedFiles)
    {
        var plan = new List<ModulePackageCopyEntry>();
        var sourceComparison = GetArtefactPathComparison(internalsRoot);
        var destinationComparison = GetArtefactPathComparison(deliveryRoot);
        var comparer = destinationComparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var destinations = new HashSet<string>(comparer);
        var directories = new HashSet<string>(comparer);
        foreach (var mapping in mappings)
        {
            if (mapping is null)
                throw new InvalidOperationException("DeliveryMappings must not contain null entries.");
            string source = ResolveDeliveryMappingPath(internalsRoot, mapping.Source, "Source");
            string destination = ResolveDeliveryMappingPath(deliveryRoot, mapping.Destination, "Destination");
            bool isFile = File.Exists(source);
            bool isDirectory = Directory.Exists(source);
            if (!isFile && !isDirectory)
                throw new InvalidOperationException($"Delivery mapping source does not exist: '{mapping.Source}'.");

            var selected = packagedFiles.Where(file => isFile
                ? string.Equals(file, source, sourceComparison)
                : IsSameOrBelowPath(file, source)).ToArray();
            if (selected.Length == 0)
                throw new InvalidOperationException($"Delivery mapping source contains no packaged delivery files: '{mapping.Source}'.");
            if (isFile && (PathValueResolver.Clean(mapping.Destination).EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                           string.Equals(destination, Path.GetFullPath(deliveryRoot), destinationComparison)))
                throw new InvalidOperationException("A delivery file mapping destination must include a file name.");

            foreach (string file in selected)
            {
                string target = isFile ? destination : Path.GetFullPath(Path.Combine(destination, ComputeRelativePath(source, file)));
                if (!destinations.Add(target) || directories.Contains(target))
                    throw new InvalidOperationException($"Delivery mappings conflict at destination '{target}'.");
                for (string? parent = Path.GetDirectoryName(target); parent is not null && IsSameOrBelowPath(parent, deliveryRoot); parent = Path.GetDirectoryName(parent))
                {
                    if (destinations.Contains(parent))
                        throw new InvalidOperationException($"Delivery mappings conflict at destination '{target}'.");
                    directories.Add(parent);
                }
                plan.Add(new ModulePackageCopyEntry(file, target));
            }
        }
        return plan;
    }

    private static string ResolveDeliveryMappingPath(string root, string? configuredPath, string field)
    {
        string path = PathValueResolver.Clean(configuredPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"Delivery mapping {field} is required; use dot for the root.");
        if (path != "." && (Path.IsPathRooted(path) || path.Split(Path.DirectorySeparatorChar)
                .Where(static segment => segment.Length > 0).Any(segment => !ArtefactLayoutPathResolver.IsPortableFileName(segment))))
            throw new InvalidOperationException($"Delivery mapping {field} must be a literal relative path without parent traversal or wildcards.");
        string resolved = Path.GetFullPath(Path.Combine(root, path));
        if (!IsSameOrBelowPath(resolved, root))
            throw new InvalidOperationException($"Delivery mapping {field} must stay inside its delivery root.");
        return resolved;
    }
}
