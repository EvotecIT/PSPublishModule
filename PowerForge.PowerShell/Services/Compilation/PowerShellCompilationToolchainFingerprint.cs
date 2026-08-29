using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PowerForge;

internal static class PowerShellCompilationToolchainFingerprint
{
    private static readonly ConcurrentDictionary<string, string> SdkHashes = new(StringComparer.OrdinalIgnoreCase);

    internal static string ComputeSdkSha256(string sdkVersion)
    {
        var selection = ResolveSelectedSdk();
        if (!selection.Version.Equals(sdkVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Selected dotnet SDK '{selection.Version}' does not match requested provenance version '{sdkVersion}'.");
        return SdkHashes.GetOrAdd(selection.RootPath, ComputeToolchainSha256);
    }

    internal static PowerShellCompilationSdkSelection ResolveSelectedSdk()
    {
        var versionRun = new ProcessRunner().RunAsync(new ProcessRunRequest(
            "dotnet",
            Directory.GetCurrentDirectory(),
            new[] { "--version" },
            TimeSpan.FromSeconds(30))).GetAwaiter().GetResult();
        if (!versionRun.Succeeded || string.IsNullOrWhiteSpace(versionRun.StdOut))
            throw new InvalidOperationException("Unable to resolve the selected dotnet SDK version for PowerShell compilation.");
        var sdkVersion = versionRun.StdOut.Trim();
        var run = new ProcessRunner().RunAsync(new ProcessRunRequest(
            "dotnet",
            Directory.GetCurrentDirectory(),
            new[] { "--list-sdks" },
            TimeSpan.FromSeconds(30))).GetAwaiter().GetResult();
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
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value).Append('\n');

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(stream));
    }

    private static string Hex(IEnumerable<byte> bytes)
        => string.Concat(bytes.Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
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
