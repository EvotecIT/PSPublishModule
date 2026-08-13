using System.Text;

namespace PowerForge;

/// <summary>Writes source provenance into a data file that is included in module Authenticode signing.</summary>
public static class PowerForgeModuleSourceAttestationWriter
{
    /// <summary>Canonical file name placed beside the primary module manifest.</summary>
    public const string FileName = "PowerForge.ReleaseProvenance.psd1";

    /// <summary>Writes a data-only source attestation beside the selected module manifest before signing.</summary>
    /// <param name="manifestPath">Primary module manifest that will be signed and packed.</param>
    /// <param name="moduleName">Primary module name.</param>
    /// <param name="version">Full module version, including any prerelease label.</param>
    /// <param name="sourceRevision">Full SHA-1 or SHA-256 Git object ID.</param>
    /// <param name="sourceDirty">Whether tracked or untracked source inputs were dirty.</param>
    /// <returns>Full path to the generated attestation.</returns>
    public static string Write(
        string manifestPath,
        string moduleName,
        string version,
        string sourceRevision,
        bool sourceDirty)
    {
        string manifest = Path.GetFullPath(RequireText(manifestPath, nameof(manifestPath)));
        if (!File.Exists(manifest))
            throw new FileNotFoundException("Primary module manifest was not found.", manifest);
        if (sourceDirty)
            throw new InvalidOperationException("A signed module source attestation cannot claim a dirty checkout.");

        string name = RequireSafeValue(moduleName, nameof(moduleName));
        string normalizedVersion = RequireSafeValue(version, nameof(version));
        string revision = DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(
            sourceRevision,
            nameof(sourceRevision)).ToLowerInvariant();
        string destination = Path.Combine(Path.GetDirectoryName(manifest)!, FileName);
        string content = string.Join(Environment.NewLine, new[]
        {
            "@{",
            "    SchemaVersion = '1'",
            $"    ModuleName = '{name}'",
            $"    Version = '{normalizedVersion}'",
            $"    SourceRevision = '{revision}'",
            "    SourceDirty = 'false'",
            "}",
            string.Empty
        });
        File.WriteAllText(destination, content, new UTF8Encoding(false));
        return destination;
    }

    internal static PowerForgeModuleSourceAttestation Read(byte[] bytes)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes ?? Array.Empty<byte>());
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Signed module source attestation is not valid UTF-8.", exception);
        }

        string schema = ReadRequired(text, "SchemaVersion");
        if (!string.Equals(schema, "1", StringComparison.Ordinal))
            throw new InvalidDataException("Signed module source attestation schema version is not supported.");
        string dirty = ReadRequired(text, "SourceDirty");
        if (!string.Equals(dirty, "false", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Signed module source attestation must bind a clean source checkout.");
        return new PowerForgeModuleSourceAttestation(
            ReadRequired(text, "ModuleName"),
            ReadRequired(text, "Version"),
            DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(
                ReadRequired(text, "SourceRevision"),
                "signed module source revision"));
    }

    private static string ReadRequired(string text, string name)
    {
        if (!ModuleManifestTextParser.TryGetTopLevelQuotedStringValue(text, name, out string? value) ||
            string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Signed module source attestation is missing '{name}'.");
        return value!.Trim();
    }

    private static string RequireSafeValue(string? value, string name)
    {
        string normalized = RequireText(value, name);
        if (normalized.Any(character =>
                !(char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_')))
            throw new ArgumentException($"{name} contains characters that cannot be represented safely.", name);
        return normalized;
    }

    private static string RequireText(string? value, string name)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length > 0
            ? normalized
            : throw new ArgumentException($"{name} is required.", name);
    }
}

internal sealed class PowerForgeModuleSourceAttestation
{
    internal PowerForgeModuleSourceAttestation(string moduleName, string version, string sourceRevision)
    {
        ModuleName = moduleName;
        Version = version;
        SourceRevision = sourceRevision;
    }

    internal string ModuleName { get; }
    internal string Version { get; }
    internal string SourceRevision { get; }
}
