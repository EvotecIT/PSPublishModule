using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Creates the versioned profile, ABI, and generated-source identity for a Strict runtime-free artifact.</summary>
internal sealed class PowerShellRuntimeFreeArtifactContract
{
    private PowerShellRuntimeFreeArtifactContract(
        PowerShellCompilationSemanticProfile semanticProfile,
        PowerShellCompilationAbiManifest publicAbi,
        string generatedSourceSha256)
    {
        SemanticProfile = semanticProfile;
        PublicAbi = publicAbi;
        GeneratedSourceSha256 = generatedSourceSha256;
    }

    internal PowerShellCompilationSemanticProfile SemanticProfile { get; }
    internal PowerShellCompilationAbiManifest PublicAbi { get; }
    internal string GeneratedSourceSha256 { get; }

    internal static PowerShellRuntimeFreeArtifactContract Create(
        string workspace,
        string namespaceName,
        string typeName,
        IEnumerable<PowerShellCompiledMethod> methods)
    {
        var profile = new PowerShellCompilationSemanticProfile();
        var abi = PowerShellCompilationAbiBuilder.Create(namespaceName, typeName, methods);
        File.WriteAllText(
            Path.Combine(workspace, "PowerForgeRuntimeFreeContract.g.cs"),
            PowerShellRuntimeFreeContractSource.Generate(profile, abi),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new PowerShellRuntimeFreeArtifactContract(profile, abi, ComputeGeneratedSourceSha256(workspace));
    }

    private static string ComputeGeneratedSourceSha256(string workspace)
    {
        var normalized = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(workspace, "*.cs", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            normalized.Append(Path.GetFileName(path)).Append(':').Append(ComputeFileSha256(path)).Append('\n');
        }
        return PowerShellCompilationAbiBuilder.ComputeSha256(normalized.ToString());
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return string.Concat(algorithm.ComputeHash(stream)
            .Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
