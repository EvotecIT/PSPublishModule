using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static void ValidateDeliveryLayout(
        ArtefactType type,
        ArtefactConfiguration cfg,
        string outputRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders,
        IReadOnlyList<string>? finalizedPayloadFiles)
    {
        if (cfg.Layout is null || cfg.Layout == ArtefactLayout.Module)
            return;
        if (cfg.Layout != ArtefactLayout.Delivery)
            throw new InvalidOperationException($"Unsupported artefact layout '{cfg.Layout}'.");
        if (type != ArtefactType.Packed && type != ArtefactType.Unpacked)
            throw new InvalidOperationException("Delivery layout is supported by Packed and Unpacked artefacts.");
        if (delivery?.Enable != true)
            throw new InvalidOperationException("Delivery layout requires enabled delivery configuration.");
        if (IsSameOrBelowPath(outputRoot, stagingPath) || IsSameOrBelowPath(stagingPath, outputRoot))
            throw new InvalidOperationException("Delivery artefact output must not overlap module staging.");

        // Both archive and folder bundles use relative module locations within their own root.
        ValidateDeliveryModulePath(cfg.RequiredModules.Path);
        ValidateDeliveryModulePath(cfg.RequiredModules.ModulesPath);
        string requiredRoot = ArtefactLayoutPathResolver.ResolveRequiredModulesRootForUnpacked(
            cfg, outputRoot, moduleName, moduleVersion, preRelease);
        string modulesRoot = ArtefactLayoutPathResolver.ResolveModulesRootForUnpacked(
            cfg, outputRoot, requiredRoot, moduleName, moduleVersion, preRelease);
        if (!IsSameOrBelowPath(requiredRoot, outputRoot) || !IsSameOrBelowPath(modulesRoot, outputRoot) ||
            string.Equals(Path.GetFullPath(modulesRoot), Path.GetFullPath(outputRoot), GetArtefactPathComparison(outputRoot)) ||
            string.Equals(Path.GetFullPath(requiredRoot), Path.GetFullPath(outputRoot), GetArtefactPathComparison(outputRoot)))
            throw new InvalidOperationException("Delivery module paths must name subdirectories inside the artefact root.");

        var copyPlan = ResolveDeliveryCopyPlan(cfg, stagingPath, outputRoot, information, delivery,
            includeScriptFolders, finalizedPayloadFiles);
        foreach (var entry in copyPlan)
        {
            // Delivery files must not overwrite the module or dependency namespace.
            if (IsSameOrBelowPath(entry.DestinationPath, modulesRoot) ||
                IsSameOrBelowPath(entry.DestinationPath, requiredRoot) ||
                IsSameOrBelowPath(modulesRoot, entry.DestinationPath) ||
                IsSameOrBelowPath(requiredRoot, entry.DestinationPath))
                throw new InvalidOperationException("Delivery files conflict with the configured modules directory.");
            ValidateScriptDestinationDoesNotTraverseReparsePoint(entry.DestinationPath, outputRoot, "Delivery artefact files");
        }
        ValidateScriptDestinationDoesNotTraverseReparsePoint(outputRoot, outputRoot, "Delivery artefact output");
        ValidateScriptDestinationDoesNotTraverseReparsePoint(modulesRoot, outputRoot, "Delivery module directory");
        ValidateScriptDestinationDoesNotTraverseReparsePoint(requiredRoot, outputRoot, "Delivery dependency directory");
    }

    private static void ValidateDeliveryModulePath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(PathValueResolver.NormalizeSeparators(path!)))
            throw new InvalidOperationException("Delivery module paths must be relative to the artefact root.");
    }

    private static IReadOnlyList<ModulePackageCopyEntry> ResolveDeliveryCopyPlan(
        ArtefactConfiguration cfg,
        string stagingPath,
        string destinationRoot,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders,
        IReadOnlyList<string>? finalizedPayloadFiles)
    {
        if (cfg.Layout != ArtefactLayout.Delivery)
            return Array.Empty<ModulePackageCopyEntry>();

        string internalsPath = string.IsNullOrWhiteSpace(delivery?.InternalsPath) ? "Internals" : delivery!.InternalsPath!;
        string internalsRoot = Path.GetFullPath(Path.Combine(stagingPath, PathValueResolver.NormalizeSeparators(internalsPath)));
        if (!IsSameOrBelowPath(internalsRoot, stagingPath) ||
            string.Equals(internalsRoot, Path.GetFullPath(stagingPath), GetArtefactPathComparison(stagingPath)))
            throw new InvalidOperationException("Delivery internals must name a subdirectory inside module staging.");

        string[] files = ResolveModulePackageSourceFiles(stagingPath, information, delivery, includeScriptFolders, finalizedPayloadFiles)
            .Where(path => IsSameOrBelowPath(path, internalsRoot))
            .ToArray();
        if (files.Length == 0)
            throw new InvalidOperationException("Delivery layout has no packaged delivery files to copy to the artefact root.");
        return CreateModulePackageCopyPlan(internalsRoot, destinationRoot, files);
    }

    private static void CopyDeliveryFiles(
        ArtefactConfiguration cfg,
        string stagingPath,
        string destinationRoot,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders,
        IReadOnlyList<string>? finalizedPayloadFiles,
        List<ArtefactCopyEntry> copied)
    {
        foreach (var entry in ResolveDeliveryCopyPlan(cfg, stagingPath, destinationRoot, information, delivery,
                     includeScriptFolders, finalizedPayloadFiles))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(entry.DestinationPath)!);
            File.Copy(entry.SourcePath, entry.DestinationPath, overwrite: true);
            copied.Add(new ArtefactCopyEntry(entry.SourcePath, entry.DestinationPath, isDirectory: false));
        }
    }
}
