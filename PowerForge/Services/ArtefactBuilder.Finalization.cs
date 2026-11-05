using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static string[] ResolveUnpackedFinalizationFiles(
        IReadOnlyList<ArtefactModuleEntry> modules,
        IReadOnlyList<ArtefactCopyEntry> copied,
        IEnumerable<string>? mainModuleFiles = null)
    {
        var files = new HashSet<string>(FrameworkCompatibility.PathComparer);
        void Add(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                files.Add(fullPath);
        }

        // Whole-module and mapped-directory copies replace their destination directories.
        // A retained Script main root instead supplies its exact selected files.
        // The retained artefact root itself is not wholly owned by this build.
        foreach (ArtefactModuleEntry module in modules)
            foreach (string file in module.IsMainModule && mainModuleFiles is not null
                         ? mainModuleFiles
                         : Directory.EnumerateFiles(module.Path, "*", SearchOption.AllDirectories))
                Add(file);

        foreach (ArtefactCopyEntry entry in copied)
        {
            if (!entry.IsDirectory)
            {
                Add(entry.Destination);
                continue;
            }

            if (!entry.IncludesDirectoryContents)
                continue;

            foreach (string file in Directory.EnumerateFiles(entry.Destination, "*", SearchOption.AllDirectories))
                Add(file);
        }

        return files.OrderBy(path => path, FrameworkCompatibility.PathComparer).ToArray();
    }
}
