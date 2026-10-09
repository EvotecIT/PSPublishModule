using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    /// <summary>Reads the module manifest's <c>Description</c> value without running it, or returns null.</summary>
    /// <param name="manifestPath">Path to the PowerShell module manifest (.psd1).</param>
    /// <returns>The description, or null when the manifest has none or cannot be read.</returns>
    internal static string? TryReadModuleManifestDescription(string? manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            return null;

        try
        {
            return ModuleManifestValueReader.ReadTopLevelString(manifestPath, "Description");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
        {
            Trace.TraceWarning($"Module manifest description read failed for {manifestPath}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads the assembly's <see cref="AssemblyDescriptionAttribute"/> without loading it, or returns null.</summary>
    /// <param name="assemblyPath">Path to the assembly file.</param>
    /// <returns>The trimmed description, or null when the file has none or cannot be read.</returns>
    internal static string? TryReadAssemblyDescription(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            return null;

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new System.Reflection.PortableExecutable.PEReader(stream);
            if (!peReader.HasMetadata)
                return null;

            var reader = peReader.GetMetadataReader();
            foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != System.Reflection.Metadata.HandleKind.MemberReference)
                    continue;

                var constructor = reader.GetMemberReference((System.Reflection.Metadata.MemberReferenceHandle)attribute.Constructor);
                if (constructor.Parent.Kind != System.Reflection.Metadata.HandleKind.TypeReference)
                    continue;

                var type = reader.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)constructor.Parent);
                if (reader.GetString(type.Name) != "AssemblyDescriptionAttribute" ||
                    reader.GetString(type.Namespace) != "System.Reflection")
                    continue;

                // Blob layout: prolog (0x0001), then one serialized string argument.
                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1)
                    return null;
                var description = blob.ReadSerializedString();
                return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException)
        {
            Trace.TraceWarning($"Assembly description read failed for {assemblyPath}: {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

}
