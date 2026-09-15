using NuGet.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    /// <summary>Pins the effective endpoint while retaining the existing named-source authentication context.</summary>
    internal static Action? CapturePublicationDestination(DotNetRepositoryReleaseSpec spec)
    {
        var source = spec.PublishSource ?? ProjectBuildPackageFeedResolver.GetDefaultPublishSource();
        var resolved = ResolvePublishSource(source, spec.RootPath);
        if (Path.IsPathRooted(resolved) || Uri.TryCreate(resolved, UriKind.Absolute, out _))
        {
            spec.PublishSource = resolved;
            return null;
        }
        var name = source.Trim();
        var root = spec.RootPath;
        var configured = LoadNamedPublishSource(name, root)
            ?? throw new InvalidOperationException($"NuGet publication source '{name}' is not configured.");
        if (!Uri.TryCreate(configured.Source, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException($"NuGet publication source '{name}' has no supported endpoint.");
        spec.PublishSource = configured.Source;
        var fingerprint = PublicationSourceFingerprint(configured);
        // NuGet still reads authentication from the repository's named source. Do not
        // copy secrets to a new file or let validation silently replace that context.
        return () => {
            var current = LoadNamedPublishSource(name, root);
            if (current is null || !string.Equals(fingerprint, PublicationSourceFingerprint(current), StringComparison.Ordinal))
                throw new InvalidOperationException($"NuGet publication source '{name}' or its authentication changed after checkpoint capture.");
        };
    }

    private static string PublicationSourceFingerprint(PackageSource source)
    {
        var credential = source.Credentials;
        var value = string.Join("\0", source.Name, source.Source, source.IsEnabled.ToString(),
            credential?.Username, credential?.PasswordText, credential?.IsPasswordClearText.ToString(),
            credential?.ValidAuthenticationTypesText,
            Environment.GetEnvironmentVariable("NuGetPackageSourceCredentials_" + source.Name));
        using var hash = SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}
