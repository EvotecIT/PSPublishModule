using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static string[] ResolveUnpackedFinalizationFiles(
        IReadOnlyList<ArtefactModuleEntry> modules,
        IReadOnlyList<ArtefactCopyEntry> copied)
    {
        var files = new HashSet<string>(FrameworkCompatibility.PathComparer);
        void Add(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                files.Add(fullPath);
        }

        // Module and mapped-directory copies replace their destination directories.
        // The retained artefact root itself is not wholly owned by this build.
        foreach (ArtefactModuleEntry module in modules)
            foreach (string file in Directory.EnumerateFiles(module.Path, "*", SearchOption.AllDirectories))
                Add(file);

        foreach (ArtefactCopyEntry entry in copied)
        {
            if (!entry.IsDirectory)
            {
                Add(entry.Destination);
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(entry.Destination, "*", SearchOption.AllDirectories))
                Add(file);
        }

        return files.OrderBy(path => path, FrameworkCompatibility.PathComparer).ToArray();
    }
}
