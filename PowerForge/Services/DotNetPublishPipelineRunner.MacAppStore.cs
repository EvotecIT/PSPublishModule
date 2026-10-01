using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal static void PrepareMacStorePayload(
        DotNetPublishMacAppOptions options, string codeRoot, string resourcesRoot, string projectRoot)
    {
        string entitlements = ResolvePath(projectRoot, options.EntitlementsPath!);
        EnsurePathWithinRoot(projectRoot, entitlements, "Mac App Store entitlements");
        var values = XDocument.Load(entitlements).Root?.Element("dict")?.Elements().ToArray()
            ?? throw new InvalidOperationException("Mac App Store entitlements must be a plist dictionary.");
        int sandbox = Array.FindIndex(values, element => element.Name == "key" && element.Value == "com.apple.security.app-sandbox");
        if (sandbox < 0 || sandbox + 1 >= values.Length || values[sandbox + 1].Name != "true")
            throw new InvalidOperationException("Mac App Store packages require com.apple.security.app-sandbox=true.");
        if (!string.IsNullOrWhiteSpace(options.ThirdPartyNoticesManifestPath))
        {
            if (string.IsNullOrWhiteSpace(options.DependenciesPath))
                throw new InvalidOperationException("DependenciesPath is required when generating third-party notices.");
            string manifest = ResolvePath(projectRoot, options.ThirdPartyNoticesManifestPath!);
            EnsurePathWithinRoot(projectRoot, manifest, "Third-party notice manifest");
            DotNetPublishThirdPartyNotices.Generate(projectRoot, ResolvePath(projectRoot, options.DependenciesPath!),
                manifest, Path.Combine(resourcesRoot, "Licenses"));
        }
        foreach (string file in Directory.EnumerateFiles(codeRoot, "*", SearchOption.AllDirectories).ToArray())
        {
            if (IsMacNativeCode(file)) continue;
            if (Path.GetExtension(file).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Mac App Store packaging requires a single-file publish with native libraries left outside the bundle; managed DLLs cannot be placed in Contents/MacOS.");
            string destination = Path.Combine(resourcesRoot, FrameworkCompatibility.GetRelativePath(codeRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(file, destination);
        }
        foreach (string directory in Directory.EnumerateDirectories(codeRoot, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        if (!string.IsNullOrWhiteSpace(options.ProvisioningProfilePath))
        {
            string profile = ResolvePath(projectRoot, options.ProvisioningProfilePath!);
            EnsurePathWithinRoot(projectRoot, profile, "Mac App Store provisioning profile");
            var (exitCode, decoded, error) = RunProcess("/usr/bin/security", projectRoot, new[] { "cms", "-D", "-i", profile });
            if (exitCode != 0) throw new InvalidOperationException("Cannot decode the provisioning profile: " + error);
            ValidateMacStoreProvisioningProfile(options, XDocument.Parse(decoded));
            File.Copy(profile, Path.Combine(Path.GetDirectoryName(codeRoot)!, "embedded.provisionprofile"));
        }
    }

    internal static void ValidateMacStoreProvisioningProfile(DotNetPublishMacAppOptions options, XDocument profile)
    {
        static XElement? Value(XElement? dictionary, string key)
            => dictionary?.Elements("key").SingleOrDefault(element => element.Value == key)?.ElementsAfterSelf().FirstOrDefault();
        var dict = profile.Root?.Element("dict");
        var teams = Value(dict, "TeamIdentifier")?.Elements("string").Select(element => element.Value).ToArray();
        var expiration = Value(dict, "ExpirationDate");
        var entitlements = Value(dict, "Entitlements");
        string? identifier = Value(entitlements, "com.apple.application-identifier")?.Value
            ?? Value(entitlements, "application-identifier")?.Value;
        string expected = options.TeamId + "." + options.BundleIdentifier;
        bool identifierMatches = identifier == expected || identifier == options.TeamId + ".*";
        if (teams?.Contains(options.TeamId, StringComparer.Ordinal) != true || !identifierMatches ||
            expiration?.Name != "date" || !DateTimeOffset.TryParse(expiration.Value, out var expires) || expires <= DateTimeOffset.UtcNow ||
            Value(entitlements, "get-task-allow")?.Name == "true" || Value(dict, "ProvisionedDevices") is not null ||
            Value(dict, "ProvisionsAllDevices")?.Name == "true")
            throw new InvalidOperationException("The provisioning profile must be an unexpired App Store profile for the configured team and bundle.");
    }

    internal static bool IsMacNativeCode(string path)
    {
        using var stream = File.OpenRead(path);
        var magic = new byte[4];
        if (stream.Read(magic, 0, magic.Length) != 4) return false;
        uint value = ((uint)magic[0] << 24) | ((uint)magic[1] << 16) | ((uint)magic[2] << 8) | magic[3];
        return value is 0xfeedface or 0xcefaedfe or 0xfeedfacf or 0xcffaedfe or 0xcafebabe or 0xbebafeca or 0xcafebabf or 0xbfbafeca;
    }

    private static void SignMacStoreNestedCode(DotNetPublishMacAppOptions options, string codeRoot, string workingDirectory)
    {
        foreach (string path in Directory.EnumerateFiles(codeRoot, "*", SearchOption.AllDirectories)
                     .Where(IsMacNativeCode).Where(path => Path.GetFileName(path) != options.Executable)
                     .OrderByDescending(path => path.Length))
        {
            RunRequiredMacTool("/usr/bin/codesign", workingDirectory,
                new[] { "--force", "--sign", options.CodesignIdentity, "--options", "runtime", path });
        }
    }

    private static void BuildAndValidateMacStoreInstaller(
        DotNetPublishMacAppOptions options, string appPath, string packagePath, string workingDirectory)
    {
        if (!Path.GetExtension(packagePath).Equals(".pkg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Mac App Store installer OutputName must end in .pkg.");
        var (_, output, error) = RunProcess("/usr/bin/codesign", workingDirectory,
            new[] { "--display", "--verbose=4", appPath });
        string details = output + "\n" + error;
        if (!details.Split('\n').Any(line => line.Trim() == "TeamIdentifier=" + options.TeamId) ||
            !(details.Contains("Authority=Apple Distribution:") || details.Contains("Authority=3rd Party Mac Developer Application:")))
            throw new InvalidOperationException("The app must be signed with an App Store distribution certificate for the configured TeamId.");
        RunRequiredMacTool("/usr/bin/productbuild", workingDirectory,
            new[] { "--sign", options.InstallerSigningIdentity!, "--component", appPath, "/Applications", packagePath });
        var (exitCode, packageOutput, packageError) = RunProcess("/usr/sbin/pkgutil", workingDirectory,
            new[] { "--check-signature", packagePath });
        string signature = packageOutput + "\n" + packageError;
        if (exitCode != 0 || !signature.Contains("(" + options.TeamId + ")") ||
            !(signature.Contains("3rd Party Mac Developer Installer:") || signature.Contains("Mac Installer Distribution:")))
            throw new InvalidOperationException("The installer must have a valid Mac App Store installer signature for the configured TeamId.");
        string validation = Path.Combine(workingDirectory, "store-validation");
        RunRequiredMacTool("/usr/sbin/pkgutil", workingDirectory, new[] { "--expand-full", packagePath, validation });
        var apps = Directory.EnumerateDirectories(validation, "*.app", SearchOption.AllDirectories).ToArray();
        if (apps.Length != 1)
            throw new InvalidOperationException("The Mac App Store installer must contain exactly one application.");
        RunRequiredMacTool("/usr/bin/codesign", workingDirectory, new[] { "--verify", "--deep", "--strict", apps[0] });
    }
}
