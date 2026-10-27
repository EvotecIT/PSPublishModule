namespace PowerForge;

internal sealed partial class PowerShellModulePipelineHostedOperations
{
    private static IReadOnlyDictionary<string, string?> CreateSigningEnvironment(
        SigningOptionsConfiguration signing, string? pfxBase64, string? pfxPassword)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["POWERFORGE_SIGNING_PFX_PATH"] = signing.CertificatePFXPath,
            ["POWERFORGE_SIGNING_PFX_BASE64"] = pfxBase64,
            ["POWERFORGE_SIGNING_PFX_PASSWORD"] = pfxPassword
        };
        // Do not leave the original secret variables in the child's inherited environment.
        foreach (var name in new[] { signing.CertificatePFXBase64EnvironmentVariable, signing.CertificatePFXPasswordEnvironmentVariable })
        {
            if (!string.IsNullOrWhiteSpace(name) && !environment.ContainsKey(name!)) environment[name!] = null;
        }
        return environment;
    }
}
