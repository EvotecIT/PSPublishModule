using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PowerForge;

internal static class PowerShellCompilationToolchainFingerprint
{
    private static readonly ConcurrentDictionary<string, string> SdkHashes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SdkHashGate = new();

    internal static string ComputeSdkSha256(string sdkVersion, bool forceRefresh = false)
    {
        var selection = ResolveSelectedSdk();
        if (!selection.Version.Equals(sdkVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Selected dotnet SDK '{selection.Version}' does not match requested provenance version '{sdkVersion}'.");
        if (forceRefresh)
            return ComputeToolchainSha256(selection.RootPath);
        if (SdkHashes.TryGetValue(selection.RootPath, out var cached))
            return cached;
        // GetOrAdd may run the expensive factory for every concurrent first caller.
        // Serialize initial inventories while keeping cached reads lock-free and failures retryable.
        lock (SdkHashGate)
            return SdkHashes.GetOrAdd(selection.RootPath, ComputeToolchainSha256);
    }

    internal static PowerShellCompilationSdkSelection ResolveSelectedSdk()
    {
        using var environment = PowerShellCompilationWorkspace.Create(keep: false);
        var versionRun = new ProcessRunner().RunAsync(PowerShellCompilationSdkProcessEnvironment.CreateDiscoveryRequest(
            Directory.GetCurrentDirectory(), environment.Path, new[] { "--version" })).GetAwaiter().GetResult();
        if (!versionRun.Succeeded || string.IsNullOrWhiteSpace(versionRun.StdOut))
            throw new InvalidOperationException("Unable to resolve the selected dotnet SDK version for PowerShell compilation.");
        var sdkVersion = versionRun.StdOut.Trim();
        var run = new ProcessRunner().RunAsync(PowerShellCompilationSdkProcessEnvironment.CreateDiscoveryRequest(
            Directory.GetCurrentDirectory(), environment.Path, new[] { "--list-sdks" })).GetAwaiter().GetResult();
        if (!run.Succeeded)
            throw new InvalidOperationException("Unable to enumerate installed dotnet SDK paths for build-cache provenance.");
        var sdkRoot = run.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(line => line.StartsWith(sdkVersion + " ", StringComparison.Ordinal))
            .Select(static line =>
            {
                var open = line.LastIndexOf('[', line.Length - 1);
                var close = line.LastIndexOf(']');
                return open >= 0 && close > open ? line.Substring(open + 1, close - open - 1) : string.Empty;
            })
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.Combine(path, sdkVersion)))
            .SingleOrDefault();
        if (sdkRoot is null || !Directory.Exists(sdkRoot))
            throw new InvalidOperationException($"Unable to resolve the selected dotnet SDK directory for version '{sdkVersion}'.");
        return new PowerShellCompilationSdkSelection(sdkVersion, sdkRoot);
    }

    internal static string ResolveRuntimePackVersion(string targetFramework)
    {
        var selection = ResolveSelectedSdk();
        var bundledVersionsPath = Path.Combine(selection.RootPath, "Microsoft.NETCoreSdk.BundledVersions.props");
        if (!File.Exists(bundledVersionsPath))
            throw new InvalidOperationException($"Selected dotnet SDK '{selection.Version}' does not expose bundled runtime-pack metadata.");
        var document = XDocument.Load(bundledVersionsPath, LoadOptions.None);
        var framework = document.Descendants()
            .Where(static element => element.Name.LocalName.Equals("KnownFrameworkReference", StringComparison.Ordinal))
            .SingleOrDefault(element =>
                string.Equals((string?)element.Attribute("Include"), "Microsoft.NETCore.App", StringComparison.Ordinal) &&
                string.Equals((string?)element.Attribute("TargetFramework"), targetFramework, StringComparison.OrdinalIgnoreCase));
        var version = (string?)framework?.Attribute("LatestRuntimeFrameworkVersion");
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException($"Selected dotnet SDK '{selection.Version}' has no Microsoft.NETCore.App runtime-pack identity for '{targetFramework}'.");
        return version!;
    }

    internal static string ResolveILLinkPackVersion(string targetFramework)
    {
        var selection = ResolveSelectedSdk();
        var document = XDocument.Load(Path.Combine(selection.RootPath, "Microsoft.NETCoreSdk.BundledVersions.props"), LoadOptions.None);
        var linker = document.Descendants().SingleOrDefault(element => element.Name.LocalName == "KnownILLinkPack" &&
            (string?)element.Attribute("Include") == "Microsoft.NET.ILLink.Tasks" &&
            string.Equals((string?)element.Attribute("TargetFramework"), targetFramework, StringComparison.OrdinalIgnoreCase));
        return (string?)linker?.Attribute("ILLinkPackVersion") ??
            throw new InvalidOperationException($"Selected dotnet SDK '{selection.Version}' has no ILLink package identity for '{targetFramework}'.");
    }

    private static string ComputeToolchainSha256(string sdkRoot)
    {
        var builder = new StringBuilder();
        var dotnetRoot = Directory.GetParent(Directory.GetParent(sdkRoot)!.FullName)!.FullName;
        var packsRoot = Path.Combine(dotnetRoot, "packs");
        var roots = new[] { sdkRoot }
            .Concat(Directory.Exists(packsRoot)
                ? Directory.EnumerateDirectories(packsRoot, "Microsoft.NETCore.App.Ref", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateDirectories(packsRoot, "NETStandard.Library.Ref", SearchOption.TopDirectoryOnly))
                    .Concat(Directory.EnumerateDirectories(packsRoot, "Microsoft.NETCore.App.Host.*", SearchOption.TopDirectoryOnly))
                : Array.Empty<string>())
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var root in roots)
        {
            Append(builder, Path.GetFileName(root));
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .OrderBy(path => FrameworkCompatibility.GetRelativePath(root, path), StringComparer.Ordinal))
            {
                var relative = FrameworkCompatibility.GetRelativePath(root, path).Replace('\\', '/');
                Append(builder, relative);
                Append(builder, ComputeFileSha256(path));
            }
        }
        using var sha = CreateSha256();
        return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value).Append('\n');

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = CreateSha256();
        return Hex(sha.ComputeHash(stream));
    }

    private static string Hex(IEnumerable<byte> bytes)
        => string.Concat(bytes.Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));

    private static SHA256 CreateSha256()
    {
#if NETFRAMEWORK
        // Windows PowerShell's default managed implementation makes a complete SDK
        // fingerprint take minutes. CNG hashes the same bytes with the Windows provider.
        return new SHA256Cng();
#else
        return SHA256.Create();
#endif
    }
}

internal sealed class PowerShellCompilationSdkSelection
{
    internal PowerShellCompilationSdkSelection(string version, string rootPath)
    {
        Version = version;
        RootPath = rootPath;
    }

    internal string Version { get; }
    internal string RootPath { get; }
}
