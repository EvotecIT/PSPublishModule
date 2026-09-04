using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal DotNetPublishArtefactResult BuildMacAppPackage(
        DotNetPublishPlan plan,
        IReadOnlyList<DotNetPublishArtefactResult> artefacts,
        DotNetPublishStep step)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (artefacts is null) throw new ArgumentNullException(nameof(artefacts));
        if (step is null) throw new ArgumentNullException(nameof(step));
        string installerId = (step.InstallerId ?? string.Empty).Trim();
        string target = (step.TargetName ?? string.Empty).Trim();
        string framework = (step.Framework ?? string.Empty).Trim();
        string runtime = (step.Runtime ?? string.Empty).Trim();
        DotNetPublishStyle? style = step.Style;
        if (string.IsNullOrWhiteSpace(installerId))
            throw new InvalidOperationException($"Step '{step.Key}' is missing InstallerId.");
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(framework) || string.IsNullOrWhiteSpace(runtime))
            throw new InvalidOperationException($"Step '{step.Key}' is missing target/framework/runtime metadata.");
        if (!style.HasValue)
            throw new InvalidOperationException($"Step '{step.Key}' is missing style metadata.");
        if (string.IsNullOrWhiteSpace(step.InstallerOutputPath))
            throw new InvalidOperationException($"Step '{step.Key}' is missing installer output path.");

        DotNetPublishInstallerPlan installer = ResolveInstallerPlan(plan, installerId)
            ?? throw new InvalidOperationException($"Installer '{installerId}' was not found in the plan.");
        DotNetPublishMacAppOptions options = installer.MacApp
            ?? throw new InvalidOperationException($"MacApp installer '{installerId}' is missing package metadata.");
        ValidateMacAppExecutionBoundary(options, installerId);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new PlatformNotSupportedException("macOS application bundles must be built on macOS with codesign and ditto available.");
        string? sourceBundleId = ResolveInstallerSourceBundleId(plan, installerId, step.BundleId);
        DotNetPublishArtefactResult source = ResolveInstallerSourceArtefact(
                artefacts,
                target,
                framework,
                runtime,
                style.Value,
                sourceBundleId)
            ?? throw new InvalidOperationException($"MacApp package step '{step.Key}' could not find its published payload.");

        string sourceRoot = Path.GetFullPath(source.OutputDir);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Published payload directory was not found: {sourceRoot}");
        string zipPath = Path.GetFullPath(step.InstallerOutputPath!);
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, zipPath, $"Installer '{installerId}' output path");
        EnsureNativeInstallerOutputDoesNotOverlapSource(sourceRoot, zipPath, installerId);
        string outputDirectory = Path.GetDirectoryName(zipPath)!;
        Directory.CreateDirectory(outputDirectory);
        EnsureNoReparsePointsInExistingPath(
            outputDirectory,
            plan.AllowOutputOutsideProjectRoot ? outputDirectory : plan.ProjectRoot,
            $"Installer '{installerId}' output path");

        string appFileName = SanitizeMacBundleName(options.BundleName) + ".app";
        string appPath = Path.Combine(outputDirectory, appFileName);
        string stagingRoot = Path.Combine(outputDirectory, ".powerforge-macapp-" + Guid.NewGuid().ToString("N"));
        string stagedAppPath = Path.Combine(stagingRoot, appFileName);
        string stagedZipPath = Path.Combine(stagingRoot, Path.GetFileName(zipPath));
        string validationRoot = Path.Combine(stagingRoot, "validated");
        EnsurePathWithinRoot(outputDirectory, stagingRoot, $"Installer '{installerId}' staging path");
        try
        {
            string contentsPath = Path.Combine(stagedAppPath, "Contents");
            string macOsPath = Path.Combine(contentsPath, "MacOS");
            string resourcesPath = Path.Combine(contentsPath, "Resources");
            Directory.CreateDirectory(macOsPath);
            Directory.CreateDirectory(resourcesPath);
            DirectoryCopy(sourceRoot, macOsPath, stagingRoot);

            string executablePath = Path.GetFullPath(Path.Combine(macOsPath, options.Executable.Replace('/', Path.DirectorySeparatorChar)));
            EnsurePathWithinRoot(macOsPath, executablePath, $"MacApp installer '{installerId}' executable");
            if (!File.Exists(executablePath))
                throw new FileNotFoundException($"MacApp executable was not found in the published payload: {options.Executable}", executablePath);
            RunRequiredMacTool("/bin/chmod", stagingRoot, new[] { "0755", executablePath });

            string? iconFileName = BuildMacIcon(plan, installerId, options, resourcesPath, stagingRoot);
            string plistPath = Path.Combine(contentsPath, "Info.plist");
            File.WriteAllText(plistPath, BuildMacInfoPlist(options, Path.GetFileName(options.Executable), iconFileName), new UTF8Encoding(false));

            var signArguments = new List<string> { "--force", "--deep", "--sign", options.CodesignIdentity };
            if (ShouldEnableMacHardenedRuntime(options))
            {
                signArguments.Add("--options");
                signArguments.Add("runtime");
            }
            else if (options.HardenedRuntime)
            {
                _logger.Warn(
                    $"MacApp installer '{installerId}' uses ad-hoc signing; hardened runtime is omitted because " +
                    "ad-hoc nested libraries do not share an Apple Team ID. Use PowerForge's Apple release flow for signed distribution builds.");
            }
            if (options.Timestamp && !string.Equals(options.CodesignIdentity, "-", StringComparison.Ordinal))
                signArguments.Add("--timestamp");
            if (!string.IsNullOrWhiteSpace(options.EntitlementsPath))
            {
                string entitlementsPath = ResolvePath(plan.ProjectRoot, options.EntitlementsPath!);
                EnsurePathWithinRoot(plan.ProjectRoot, entitlementsPath, $"MacApp installer '{installerId}' entitlements source");
                if (!File.Exists(entitlementsPath))
                    throw new FileNotFoundException("MacApp entitlements file was not found.", entitlementsPath);
                signArguments.Add("--entitlements");
                signArguments.Add(entitlementsPath);
            }
            signArguments.Add(stagedAppPath);
            RunRequiredMacTool("/usr/bin/codesign", stagingRoot, signArguments);
            RunRequiredMacTool("/usr/bin/codesign", stagingRoot, new[] { "--verify", "--deep", "--strict", "--verbose=2", stagedAppPath });

            RunRequiredMacTool(
                "/usr/bin/ditto",
                stagingRoot,
                new[] { "-c", "-k", "--sequesterRsrc", "--keepParent", stagedAppPath, stagedZipPath });
            if (!File.Exists(stagedZipPath) || new FileInfo(stagedZipPath).Length == 0)
                throw new InvalidOperationException($"ditto did not produce the expected package: {stagedZipPath}");

            Directory.CreateDirectory(validationRoot);
            RunRequiredMacTool("/usr/bin/ditto", stagingRoot, new[] { "-x", "-k", stagedZipPath, validationRoot });
            string validatedAppPath = Path.Combine(validationRoot, appFileName);
            RunRequiredMacTool(
                "/usr/bin/codesign",
                stagingRoot,
                new[] { "--verify", "--deep", "--strict", "--verbose=2", validatedAppPath });

            string stagedArchiveHash = ComputeSha256(stagedZipPath);
            if (Directory.Exists(appPath))
                Directory.Delete(appPath, recursive: true);
            Directory.Move(stagedAppPath, appPath);
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            File.Move(stagedZipPath, zipPath);
            if (!string.Equals(stagedArchiveHash, ComputeSha256(zipPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The macOS archive changed while it was committed to the installer output.");

            int files = Directory.EnumerateFiles(appPath, "*", SearchOption.AllDirectories).Count();
            long totalBytes = Directory.EnumerateFiles(appPath, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
            _logger.Info($"macOS app -> {appPath}");
            _logger.Info($"macOS app zip -> {zipPath}");
            return new DotNetPublishArtefactResult
            {
                Category = DotNetPublishArtefactCategory.Installer,
                InstallerId = installerId,
                Target = target,
                Kind = source.Kind,
                Runtime = runtime,
                Framework = framework,
                Style = style.Value,
                PublishDir = appPath,
                OutputDir = outputDirectory,
                OutputFiles = new[] { zipPath },
                Files = files + 1,
                TotalBytes = totalBytes + new FileInfo(zipPath).Length
            };
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    internal static void ValidateMacAppExecutionBoundary(DotNetPublishMacAppOptions options, string installerId)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(installerId))
            throw new ArgumentException("An installer identifier is required.", nameof(installerId));

        string executable = (options.Executable ?? string.Empty).Trim();
        if (executable.Length == 0 ||
            executable is "." or ".." ||
            executable.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
            !string.Equals(Path.GetFileName(executable), executable, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"MacApp installer '{installerId}' Executable must be a file name in Contents/MacOS, not a nested path.");
        }

        string codesignIdentity = (options.CodesignIdentity ?? string.Empty).Trim();
        if (!string.Equals(codesignIdentity, "-", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"MacApp installer '{installerId}' may use only ad-hoc signing until PowerForge owns notarization, stapling, and Gatekeeper validation.");
        }
    }

    internal static string BuildMacInfoPlist(
        DotNetPublishMacAppOptions options,
        string executableName,
        string? iconFileName)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        builder.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
        builder.Append("<plist version=\"1.0\">\n<dict>\n");
        AppendPlistString(builder, "CFBundleDevelopmentRegion", "en");
        AppendPlistString(builder, "CFBundleDisplayName", options.BundleName);
        AppendPlistString(builder, "CFBundleExecutable", executableName);
        AppendPlistString(builder, "CFBundleIdentifier", options.BundleIdentifier);
        AppendPlistString(builder, "CFBundleInfoDictionaryVersion", "6.0");
        AppendPlistString(builder, "CFBundleName", options.BundleName);
        AppendPlistString(builder, "CFBundlePackageType", "APPL");
        AppendPlistString(builder, "CFBundleShortVersionString", options.Version);
        AppendPlistString(builder, "CFBundleVersion", options.BuildNumber);
        AppendPlistString(builder, "LSMinimumSystemVersion", options.MinimumSystemVersion);
        builder.Append("  <key>NSHighResolutionCapable</key>\n  <true/>\n");
        if (!string.IsNullOrWhiteSpace(iconFileName))
            AppendPlistString(builder, "CFBundleIconFile", iconFileName!);
        if (!string.IsNullOrWhiteSpace(options.Category))
            AppendPlistString(builder, "LSApplicationCategoryType", options.Category!);
        if (!string.IsNullOrWhiteSpace(options.Copyright))
            AppendPlistString(builder, "NSHumanReadableCopyright", options.Copyright!);
        if (options.DocumentExtensions.Length > 0)
        {
            builder.Append("  <key>CFBundleDocumentTypes</key>\n  <array>\n    <dict>\n");
            AppendPlistString(builder, "CFBundleTypeName", options.BundleName + " document", indent: 6);
            AppendPlistString(builder, "CFBundleTypeRole", "Editor", indent: 6);
            AppendPlistString(builder, "LSHandlerRank", "Alternate", indent: 6);
            builder.Append("      <key>CFBundleTypeExtensions</key>\n      <array>\n");
            foreach (string extension in options.DocumentExtensions)
                builder.Append("        <string>").Append(Escape(extension)).Append("</string>\n");
            builder.Append("      </array>\n    </dict>\n  </array>\n");
        }
        builder.Append("</dict>\n</plist>\n");
        return builder.ToString();
    }

    private static void AppendPlistString(StringBuilder builder, string key, string value, int indent = 2)
    {
        string prefix = new(' ', indent);
        builder.Append(prefix).Append("<key>").Append(SecurityElement.Escape(key)).Append("</key>\n");
        builder.Append(prefix).Append("<string>").Append(SecurityElement.Escape(value)).Append("</string>\n");
    }

    private static string? BuildMacIcon(
        DotNetPublishPlan plan,
        string installerId,
        DotNetPublishMacAppOptions options,
        string resourcesPath,
        string stagingRoot)
    {
        if (string.IsNullOrWhiteSpace(options.IconPath))
            return null;

        string sourceIcon = ResolvePath(plan.ProjectRoot, options.IconPath!);
        EnsurePathWithinRoot(plan.ProjectRoot, sourceIcon, $"MacApp installer '{installerId}' icon source");
        if (!File.Exists(sourceIcon))
            throw new FileNotFoundException("MacApp icon was not found.", sourceIcon);
        string destinationIcon = Path.Combine(resourcesPath, "AppIcon.icns");
        if (Path.GetExtension(sourceIcon).Equals(".icns", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceIcon, destinationIcon, overwrite: false);
            return Path.GetFileName(destinationIcon);
        }

        string iconsetPath = Path.Combine(stagingRoot, "AppIcon.iconset");
        Directory.CreateDirectory(iconsetPath);
        foreach ((int pixels, string name) in new[]
        {
            (16, "icon_16x16.png"),
            (32, "icon_16x16@2x.png"),
            (32, "icon_32x32.png"),
            (64, "icon_32x32@2x.png"),
            (128, "icon_128x128.png"),
            (256, "icon_128x128@2x.png"),
            (256, "icon_256x256.png"),
            (512, "icon_256x256@2x.png"),
            (512, "icon_512x512.png"),
            (1024, "icon_512x512@2x.png")
        })
        {
            RunRequiredMacTool(
                "/usr/bin/sips",
                stagingRoot,
                new[] { "-z", pixels.ToString(), pixels.ToString(), sourceIcon, "--out", Path.Combine(iconsetPath, name) });
        }
        RunRequiredMacTool("/usr/bin/iconutil", stagingRoot, new[] { "-c", "icns", iconsetPath, "-o", destinationIcon });
        return Path.GetFileName(destinationIcon);
    }

    private static string SanitizeMacBundleName(string value)
    {
        string result = string.Concat(value.Trim().Select(character => character is '/' or '\\' or ':' ? '-' : character));
        return string.IsNullOrWhiteSpace(result) ? "Application" : result;
    }

    internal static bool ShouldEnableMacHardenedRuntime(DotNetPublishMacAppOptions options)
        => options.HardenedRuntime &&
           !string.Equals(options.CodesignIdentity, "-", StringComparison.Ordinal);

    private static void RunRequiredMacTool(string tool, string workingDirectory, IReadOnlyList<string> arguments)
    {
        try
        {
            (int exitCode, string stdOut, string stdErr) = RunProcess(tool, workingDirectory, arguments);
            if (exitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;
                throw new InvalidOperationException($"{Path.GetFileName(tool)} failed with exit code {exitCode}: {detail.Trim()}");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException($"Required macOS packaging tool '{tool}' was not found.", exception);
        }
    }
}
