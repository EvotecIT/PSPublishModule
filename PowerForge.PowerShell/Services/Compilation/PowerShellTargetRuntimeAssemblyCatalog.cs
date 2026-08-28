using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace PowerForge;

/// <summary>Reads exact assembly identities from the selected target framework reference pack.</summary>
internal static class PowerShellTargetRuntimeAssemblyCatalog
{
    internal static IReadOnlyCollection<string> ReadStableKeys(string targetFramework)
    {
        PowerShellGeneratedReferenceAssemblyResolver.EnsureAvailable(targetFramework);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in PowerShellGeneratedTypePolicy.GetTargetRuntimeAssemblyPaths(targetFramework))
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) continue;
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) continue;
            var definition = reader.GetAssemblyDefinition();
            var publicKey = reader.GetBlobBytes(definition.PublicKey);
            var token = publicKey.Length == 0 ? string.Empty : ComputePublicKeyToken(publicKey);
            identities.Add(CreateStableKey(
                reader.GetString(definition.Name),
                definition.Version,
                token,
                definition.Culture.IsNil ? string.Empty : reader.GetString(definition.Culture),
                IsRetargetable(definition.Flags),
                GetContentType(definition.Flags)));
        }
        if (identities.Count == 0)
            throw new InvalidOperationException($"Target framework '{targetFramework}' did not expose any certifiable reference-assembly identities.");
        return identities;
    }

    internal static string CreateStableKey(
        string name,
        Version version,
        string publicKeyToken,
        string culture = "",
        bool retargetable = false,
        string? contentType = null)
        => $"{name}|{version}|{publicKeyToken}|{NormalizeCulture(culture)}|{retargetable}|{NormalizeContentType(contentType)}";

    internal static string NormalizeCulture(string? culture)
        => string.IsNullOrWhiteSpace(culture) ? "neutral" : culture!.Trim();

    internal static string NormalizeContentType(string? contentType)
        => string.IsNullOrWhiteSpace(contentType) ? "Default" : contentType!.Trim();

    internal static bool IsRetargetable(AssemblyFlags flags)
        => (flags & AssemblyFlags.Retargetable) != 0;

    internal static string GetContentType(AssemblyFlags flags)
        => (flags & AssemblyFlags.ContentTypeMask) == AssemblyFlags.WindowsRuntime ? "WindowsRuntime" : "Default";

    internal static string ComputePublicKeyToken(byte[] publicKey)
    {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(publicKey);
        var token = new byte[8];
        for (var index = 0; index < token.Length; index++)
            token[index] = hash[hash.Length - 1 - index];
        return string.Concat(token.Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
