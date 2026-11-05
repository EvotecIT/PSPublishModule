using System;
using System.Collections.Generic;
using System.IO;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    // Mapping kinds depend on the actual selected dependency payload. Acquire it
    // before output cleanup and pass the same bytes to the eventual copy step.
    private PreparedArtefactDependencies? PrepareMappingDependencies(
        ArtefactConfiguration cfg, ArtefactType type, string outputRoot,
        IReadOnlyList<RequiredModuleReference> requiredModules)
    {
        if (type is not (ArtefactType.Packed or ArtefactType.Unpacked) ||
            cfg.RequiredModules.Enabled != true ||
            (cfg.DirectoryOutput is not { Length: > 0 } && cfg.FilesOutput is not { Length: > 0 }))
            return null;

        string root = Path.Combine(Path.GetTempPath(), "PowerForge", "artefacts", "prepared", Guid.NewGuid().ToString("N"));
        if (cfg.DoNotClear != true && WouldOutputClearRemoveSource(root, true, outputRoot, type, copyContents: true))
            throw new InvalidOperationException(
                $"{type} artefact output root '{outputRoot}' would remove temporary prepared dependency sources. Choose an output root separate from the temporary build directory.");
        var prepared = new PreparedArtefactDependencies(root, _logger);
        try
        {
            foreach (var module in FilterRequiredModulesForArtefact(requiredModules, cfg.RequiredModules.ExcludeModuleName))
                prepared.Modules.Add(SaveRequiredModuleToFolder(
                    module, root, cfg.RequiredModules.Repository, cfg.RequiredModules.Credential,
                    cfg.RequiredModules.Tool ?? ModuleSaveTool.Auto,
                    cfg.RequiredModules.Source ?? RequiredModulesSource.Installed));
            return prepared;
        }
        catch
        {
            prepared.Dispose();
            throw;
        }
    }

    private sealed class PreparedArtefactDependencies : IDisposable
    {
        private readonly string _root;
        private readonly ILogger _logger;
        internal List<ArtefactModuleEntry> Modules { get; } = new();

        internal PreparedArtefactDependencies(string root, ILogger logger)
        {
            _root = root;
            _logger = logger;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _logger.Warn($"Could not remove prepared artefact dependencies at '{_root}': {error.Message}");
            }
        }
    }
}
