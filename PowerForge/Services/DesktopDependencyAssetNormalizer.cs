using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NuGet.Frameworks;

namespace PowerForge;

// A declared package asset choice belongs to packaging, not to a consumer's import script.
internal static class DesktopDependencyAssetNormalizer
{
    internal const string BindingPolicyFileName = ".powerforge-desktop-bindings";
    private const string FacadeToken = "cc7b13ffcd2ddd51";

    internal static void Normalize(
        string payloadDirectory,
        string? assetsFile,
        string framework,
        IReadOnlyDictionary<string, string> assetFrameworks)
    {
        if (assetFrameworks.Count == 0 || ModuleBinaryPayloadLayout.IsCoreFramework(framework)) return;
        NuGetFramework targetFramework = NuGetFramework.ParseFolder(framework);
        if (targetFramework.Framework != ".NETFramework" || targetFramework.Version < new Version(4, 7, 2))
            throw new InvalidOperationException("Desktop dependency asset selection requires .NET Framework 4.7.2 or later.");
        if (string.IsNullOrWhiteSpace(assetsFile) || !File.Exists(assetsFile))
            throw new InvalidOperationException("Desktop dependency asset selection requires the entry project's resolved NuGet assets file.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsFile));
        JsonElement root = document.RootElement;
        JsonProperty[] matchingTargets = root.GetProperty("targets").EnumerateObject()
            .Where(item => item.Name.IndexOf('/') < 0 && NuGetFramework.Parse(item.Name).Equals(targetFramework)).ToArray();
        if (matchingTargets.Length != 1)
            throw new InvalidOperationException($"Expected one resolved package target for '{framework}'.");
        JsonElement target = matchingTargets[0].Value;
        JsonElement libraries = root.GetProperty("libraries");
        string[] packageRoots = root.GetProperty("packageFolders").EnumerateObject()
            .Select(item => Path.GetFullPath(item.Name)).ToArray();
        List<AssetChoice> choices = new();

        foreach (KeyValuePair<string, string> setting in assetFrameworks.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(setting.Value, "netstandard2.0", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Desktop dependency '{setting.Key}' must select the supported netstandard2.0 asset framework.");
            JsonProperty[] packages = target.EnumerateObject().Where(item => string.Equals(
                item.Name.Split('/')[0], setting.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (packages.Length != 1 || !libraries.TryGetProperty(packages[0].Name, out JsonElement library) ||
                library.GetProperty("type").GetString() != "package")
                throw new InvalidOperationException($"Desktop dependency '{setting.Key}' is not a uniquely resolved NuGet package for '{framework}'.");
            string packagePath = library.GetProperty("path").GetString()!;
            if (!string.Equals(packagePath.Replace('\\', '/'), packages[0].Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Package asset path does not match the resolved package and version '{packages[0].Name}'.");
            string[] files = library.GetProperty("files").EnumerateArray().Select(item => item.GetString()!).ToArray();
            if (!packages[0].Value.TryGetProperty("runtime", out JsonElement runtime))
                throw new InvalidOperationException($"Desktop dependency '{setting.Key}' has no resolved managed runtime assets.");
            string[] originalAssets = runtime.EnumerateObject().Select(item => item.Name)
                .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (originalAssets.Length == 0)
                throw new InvalidOperationException($"Desktop dependency '{setting.Key}' has no managed runtime DLL to replace.");
            foreach (string originalAsset in originalAssets)
            {
                string fileName = Path.GetFileName(originalAsset.Replace('/', Path.DirectorySeparatorChar));
                string selectedAsset = "lib/netstandard2.0/" + fileName;
                if (!files.Contains(originalAsset, StringComparer.Ordinal) || !files.Contains(selectedAsset, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Package '{packages[0].Name}' does not declare both '{originalAsset}' and '{selectedAsset}'.");
                string original = LocatePackageAsset(packageRoots, packagePath, originalAsset);
                string selected = LocatePackageAsset(packageRoots, packagePath, selectedAsset);
                string destination = ContainedPath(payloadDirectory, fileName);
                if (!File.Exists(destination) || !string.Equals(Hash(destination), Hash(original), StringComparison.Ordinal))
                    throw new InvalidOperationException($"Published dependency '{fileName}' does not match the resolved '{packages[0].Name}' runtime asset.");
                AssemblyName before = AssemblyName.GetAssemblyName(original);
                AssemblyName after = AssemblyName.GetAssemblyName(selected);
                if (!SameIdentityExceptVersion(before, after))
                    throw new InvalidOperationException($"Package asset '{selectedAsset}' changes the assembly name, culture or public key token.");
                bool facadeBinding = before.Version != after.Version;
                if (facadeBinding && !IsQualifiedFacadePair(before, after))
                    throw new InvalidOperationException($"Package asset '{selectedAsset}' changes an assembly version outside the qualified Desktop facade policy.");
                choices.Add(new AssetChoice(selected, destination, before, after, Hash(selected), facadeBinding));
            }
        }

        // Validate every choice before mutating the staged payload. Core and package caches remain untouched.
        List<string> bindings = new();
        foreach (AssetChoice choice in choices)
        {
            File.Copy(choice.Source, choice.Destination, overwrite: true);
            if (choice.FacadeBinding)
                bindings.Add(string.Join("|", choice.Before.Name, choice.Before.Version, choice.After.Version, FacadeToken, choice.Hash));
        }
        File.WriteAllLines(Path.Combine(payloadDirectory, BindingPolicyFileName), bindings, new UTF8Encoding(false));
    }

    private static string LocatePackageAsset(IEnumerable<string> roots, string packagePath, string asset)
    {
        foreach (string root in roots)
        {
            string package = ContainedPath(root, packagePath);
            string candidate = ContainedPath(package, asset);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Resolved package asset '{packagePath}/{asset}' is unavailable in the recorded package folders.");
    }

    private static string ContainedPath(string root, string relative)
    {
        string[] parts = relative.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(relative) || parts.Any(part => part is "" or "." or ".." || part.IndexOf(':') >= 0))
            throw new InvalidOperationException("Package asset paths must stay within their recorded package directory.");
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Package asset path escaped its recorded package directory.");
        return path;
    }

    private static bool SameIdentityExceptVersion(AssemblyName before, AssemblyName after)
        => string.Equals(before.Name, after.Name, StringComparison.Ordinal) &&
           string.Equals(before.CultureName ?? "", after.CultureName ?? "", StringComparison.Ordinal) &&
           (before.GetPublicKeyToken() ?? Array.Empty<byte>()).SequenceEqual(after.GetPublicKeyToken() ?? Array.Empty<byte>());

    internal static bool IsQualifiedFacadePair(AssemblyName before, AssemblyName after)
    {
        if (!SameIdentityExceptVersion(before, after) || !string.IsNullOrEmpty(before.CultureName) ||
            Token(before) != FacadeToken) return false;
        return before.Name switch
        {
            "System.Memory" or "System.Buffers" => before.Version == new Version(4, 0, 5, 0) && after.Version == new Version(4, 0, 2, 0),
            "System.Threading.Tasks.Extensions" => before.Version == new Version(4, 2, 4, 0) && after.Version == new Version(4, 2, 1, 0),
            _ => false
        };
    }

    private static string Token(AssemblyName identity)
        => BitConverter.ToString(identity.GetPublicKeyToken() ?? Array.Empty<byte>()).Replace("-", "").ToLowerInvariant();

    private static string Hash(string path)
    {
        using SHA256 algorithm = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
    }

    private sealed class AssetChoice
    {
        internal AssetChoice(string source, string destination, AssemblyName before, AssemblyName after, string hash, bool facadeBinding)
        {
            Source = source;
            Destination = destination;
            Before = before;
            After = after;
            Hash = hash;
            FacadeBinding = facadeBinding;
        }
        internal string Source { get; }
        internal string Destination { get; }
        internal AssemblyName Before { get; }
        internal AssemblyName After { get; }
        internal string Hash { get; }
        internal bool FacadeBinding { get; }
    }
}
