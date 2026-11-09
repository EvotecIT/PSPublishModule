using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private sealed partial class SourceLinkContext
    {
        private static MetadataReaderProvider OpenSymbolReader(string assemblyPath)
        {
            using var pe = new PEReader(File.OpenRead(assemblyPath));
            // The platform validates sidecar identity against CodeView and falls back to embedded
            // symbols when the sidecar is missing, stale or unreadable. The provider owns its data.
            if (pe.TryOpenAssociatedPortablePdb(assemblyPath,
                path => File.Exists(path) ? File.OpenRead(path) : null, out var provider, out _))
                return provider!;

            throw new FileNotFoundException($"No matching portable PDB found for {assemblyPath}.");
        }
    }
}
