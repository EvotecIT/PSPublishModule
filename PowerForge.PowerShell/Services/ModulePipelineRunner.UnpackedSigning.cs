using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private IReadOnlyList<string> FinalizeSignedUnpackedArtefact(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        PackedArtefactFinalizationContext context)
    {
        string[] ownedFiles = context.FinalizationFilePaths ?? throw new InvalidOperationException(
            "Unpacked finalization requires the copied file inventory.");
        var moduleContext = new PackedArtefactFinalizationContext(ArtefactType.Unpacked,
            context.MainModulePath, context.MainModulePath, context.ManifestPath,
            context.EntryPointPath, context.OutputPath, context.ModuleName, context.Version)
        {
            FinalizationFilePaths = ownedFiles.Where(path => IsSameOrChildPath(context.MainModulePath, path)).ToArray(),
            PromotedDeliveryFilePaths = context.PromotedDeliveryFilePaths
                .Where(path => IsSameOrChildPath(context.MainModulePath, path)).ToArray()
        };
        IReadOnlyList<string> evidence = FinalizeSignedModuleLayout(plan, state, moduleContext);
        SigningOptionsConfiguration signing = plan.Signing ?? throw new InvalidOperationException(
            "Signing is enabled but no signing options were provided.");
        IEnumerable<string> supplementalFiles = ownedFiles
            .Where(path => !IsSameOrChildPath(context.MainModulePath, path));
        if (signing.IncludeInternals != true && plan.Delivery?.Sign != true)
            supplementalFiles = supplementalFiles.Except(context.PromotedDeliveryFilePaths, FrameworkCompatibility.PathComparer);

        // Explicit external module destinations can be on a different volume. Keep each
        // signing call inside its declared root rather than widening to a common drive root.
        foreach (var group in supplementalFiles.GroupBy(path => ResolveUnpackedSigningRoot(context, path), FrameworkCompatibility.PathComparer))
        {
            ModuleSigningResult result = _hostedOperations.SignModuleOutput(
                context.ModuleName, group.Key, group.ToArray(), BuildSigningIncludePatterns(signing),
                BuildSigningExcludeSubstrings(signing, plan.Delivery, excludeBundledRequiredModules: false), signing);
            if (result.Failed > 0)
                throw new InvalidOperationException("Copied unpacked runtime files could not be signed.");
            state.SigningResult = AggregateSigningResults(state.SigningResult, result);
        }

        return evidence;
    }

    private static string ResolveUnpackedSigningRoot(PackedArtefactFinalizationContext context, string path)
    {
        if (IsSameOrChildPath(context.RootPath, path))
            return context.RootPath;
        string? declaredRoot = context.FinalizationRoots
            .Where(root => IsSameOrChildPath(root, path))
            .OrderByDescending(root => root.Length)
            .FirstOrDefault();
        return declaredRoot ?? throw new InvalidOperationException(
            "A copied unpacked file is outside its declared output roots.");
    }
}
