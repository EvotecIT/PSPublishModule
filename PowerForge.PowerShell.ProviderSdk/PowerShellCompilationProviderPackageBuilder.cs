using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>One provider assembly selected for deterministic package creation.</summary>
public sealed class PowerShellCompilationProviderAssemblyInput
{
    /// <summary>Creates a provider assembly input.</summary>
    public PowerShellCompilationProviderAssemblyInput(string sourcePath, string packagePath)
    {
        SourcePath = string.IsNullOrWhiteSpace(sourcePath)
            ? throw new ArgumentException("A provider assembly source path is required.", nameof(sourcePath))
            : Path.GetFullPath(sourcePath.Trim().Trim('"'));
        PackagePath = string.IsNullOrWhiteSpace(packagePath)
            ? throw new ArgumentException("A provider assembly package path is required.", nameof(packagePath))
            : packagePath.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>Full source assembly path.</summary>
    public string SourcePath { get; }

    /// <summary>Package-relative assembly path.</summary>
    public string PackagePath { get; }
}

/// <summary>Deterministic provider-package creation request.</summary>
public sealed class PowerShellCompilationProviderPackageBuildRequest
{
    /// <summary>Creates a provider-package build request.</summary>
    public PowerShellCompilationProviderPackageBuildRequest(
        string outputPath,
        PowerShellCompilationProviderPackageManifest manifest)
    {
        OutputPath = string.IsNullOrWhiteSpace(outputPath)
            ? throw new ArgumentException("A provider package output path is required.", nameof(outputPath))
            : Path.GetFullPath(outputPath.Trim().Trim('"'));
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    /// <summary>Destination <c>.nupkg</c> path.</summary>
    public string OutputPath { get; }

    /// <summary>Provider manifest. Assembly evidence is rebuilt from <see cref="Assemblies"/>.</summary>
    public PowerShellCompilationProviderPackageManifest Manifest { get; }

    /// <summary>Managed provider assemblies to include.</summary>
    public PowerShellCompilationProviderAssemblyInput[] Assemblies { get; set; } = Array.Empty<PowerShellCompilationProviderAssemblyInput>();
}

/// <summary>
/// Builds deterministic metadata-only provider packages and immediately runs the compiler-owned conformance reader.
/// </summary>
public sealed class PowerShellCompilationProviderPackageBuilder
{
    private static readonly DateTimeOffset DeterministicTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Builds one deterministic provider package and returns its validated non-executing resolution.</summary>
    public PowerShellCompilationProviderResolution Build(PowerShellCompilationProviderPackageBuildRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.Assemblies is null || request.Assemblies.Length == 0)
            throw new ArgumentException("At least one provider assembly is required.", nameof(request));
        var duplicate = request.Assemblies.GroupBy(static assembly => assembly.PackagePath, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Provider assembly package path '{duplicate.Key}' was selected more than once.");
        foreach (var assembly in request.Assemblies)
        {
            if (!File.Exists(assembly.SourcePath)) throw new FileNotFoundException("Provider assembly was not found.", assembly.SourcePath);
            if (assembly.PackagePath.Contains("../", StringComparison.Ordinal))
                throw new InvalidOperationException($"Provider assembly package path '{assembly.PackagePath}' is unsafe.");
        }

        request.Manifest.Assemblies = request.Assemblies
            .OrderBy(static assembly => assembly.PackagePath, StringComparer.Ordinal)
            .Select(static assembly => PowerShellCompilationProviderPackageReader.InspectAssembly(assembly.SourcePath, assembly.PackagePath))
            .ToArray();
        var directory = Path.GetDirectoryName(request.OutputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = request.OutputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                AddText(archive, request.Manifest.PackageId + ".nuspec", CreateNuspec(request.Manifest));
                AddText(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"json\" ContentType=\"application/json\"/><Default Extension=\"dll\" ContentType=\"application/octet\"/><Default Extension=\"nuspec\" ContentType=\"application/octet\"/></Types>");
                AddText(archive, PowerShellCompilationProviderPackageReader.ManifestPath, SerializeManifest(request.Manifest));
                foreach (var assembly in request.Assemblies.OrderBy(static assembly => assembly.PackagePath, StringComparer.Ordinal))
                    AddFile(archive, assembly.PackagePath, assembly.SourcePath);
            }
            if (File.Exists(request.OutputPath))
                File.Replace(temporary, request.OutputPath, destinationBackupFileName: null);
            else
                File.Move(temporary, request.OutputPath);
            return new PowerShellCompilationProviderPackageReader().Resolve(
                new[] { new PowerShellCompilationProviderPackageReference(request.OutputPath) });
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string SerializeManifest(PowerShellCompilationProviderPackageManifest manifest)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(manifest, options);
    }

    private static string CreateNuspec(PowerShellCompilationProviderPackageManifest manifest)
    {
        var dependencies = string.Join(string.Empty, manifest.Dependencies
            .OrderBy(static dependency => dependency.PackageId, StringComparer.Ordinal)
            .Select(static dependency => $"<dependency id=\"{Xml(dependency.PackageId)}\" version=\"[{Xml(dependency.Version)}]\" />"));
        return $"<?xml version=\"1.0\" encoding=\"utf-8\"?><package><metadata><id>{Xml(manifest.PackageId)}</id><version>{Xml(manifest.PackageVersion)}</version><authors>{Xml(manifest.Publisher)}</authors><description>PowerForge PowerShell compilation provider metadata.</description><license type=\"expression\">{Xml(manifest.LicenseExpression)}</license><dependencies><group targetFramework=\"net8.0\">{dependencies}</group></dependencies></metadata></package>";
    }

    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static void AddText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicTimestamp;
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void AddFile(ZipArchive archive, string path, string sourcePath)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicTimestamp;
        using var target = entry.Open();
        using var source = File.OpenRead(sourcePath);
        source.CopyTo(target);
    }
}
