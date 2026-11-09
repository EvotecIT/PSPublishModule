using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private sealed partial class SourceLinkContext
    {
        private static MetadataReaderProvider OpenSymbolReader(string assemblyPath)
        {
            // Prefer the symbols embedded in this exact assembly over a possibly stale sidecar.
            using (var pe = new PEReader(File.OpenRead(assemblyPath)))
            {
                foreach (var entry in pe.ReadDebugDirectory())
                {
                    if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
                        return pe.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                }
            }

            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (!File.Exists(pdbPath))
                throw new FileNotFoundException($"No embedded portable PDB or sidecar PDB found at {pdbPath}.");

            var stream = File.OpenRead(pdbPath);
            try
            {
                // The provider owns the stream for its full metadata-reader lifetime.
                return MetadataReaderProvider.FromPortablePdbStream(stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
    }
}
