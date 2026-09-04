using System.Runtime.InteropServices;
using System.Text;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal DotNetPublishArtefactResult BuildDebianPackage(
        DotNetPublishPlan plan,
        IReadOnlyList<DotNetPublishArtefactResult> artefacts,
        DotNetPublishStep step)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (artefacts is null) throw new ArgumentNullException(nameof(artefacts));
        if (step is null) throw new ArgumentNullException(nameof(step));
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            throw new PlatformNotSupportedException("Debian packages must be built on Linux with dpkg-deb available.");

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
        DotNetPublishDebianOptions debian = installer.Debian
            ?? throw new InvalidOperationException($"Debian installer '{installerId}' is missing package metadata.");
        string? sourceBundleId = ResolveInstallerSourceBundleId(plan, installerId, step.BundleId);
        DotNetPublishArtefactResult source = ResolveInstallerSourceArtefact(
                artefacts,
                target,
                framework,
                runtime,
                style.Value,
                sourceBundleId)
            ?? throw new InvalidOperationException(
                $"Debian package step '{step.Key}' could not find its published payload.");

        string sourceRoot = Path.GetFullPath(source.OutputDir);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Published payload directory was not found: {sourceRoot}");
        string outputPath = Path.GetFullPath(step.InstallerOutputPath!);
        if (!plan.AllowOutputOutsideProjectRoot)
            EnsurePathWithinRoot(plan.ProjectRoot, outputPath, $"Installer '{installerId}' output path");
        string outputDirectory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDirectory);
        EnsureNoReparsePointsInExistingPath(outputDirectory, plan.AllowOutputOutsideProjectRoot ? outputDirectory : plan.ProjectRoot, $"Installer '{installerId}' output path");

        string stagingRoot = Path.Combine(outputDirectory, ".powerforge-debian-" + Guid.NewGuid().ToString("N"));
        EnsurePathWithinRoot(outputDirectory, stagingRoot, $"Installer '{installerId}' staging path");
        try
        {
            string packageRoot = Path.Combine(stagingRoot, "opt", debian.InstallDirectoryName);
            string controlDirectory = Path.Combine(stagingRoot, "DEBIAN");
            string commandDirectory = Path.Combine(stagingRoot, "usr", "bin");
            Directory.CreateDirectory(packageRoot);
            Directory.CreateDirectory(controlDirectory);
            Directory.CreateDirectory(commandDirectory);
            DirectoryCopy(sourceRoot, packageRoot, stagingRoot);

            string executablePath = Path.GetFullPath(Path.Combine(packageRoot, debian.Executable.Replace('/', Path.DirectorySeparatorChar)));
            EnsurePathWithinRoot(packageRoot, executablePath, $"Debian installer '{installerId}' executable");
            if (!File.Exists(executablePath))
                throw new FileNotFoundException($"Debian installer executable was not found in the published payload: {debian.Executable}", executablePath);

            string architecture = ResolveDebianArchitecture(runtime, debian.Architecture);
            string controlPath = Path.Combine(controlDirectory, "control");
            File.WriteAllText(controlPath, BuildDebianControl(debian, architecture), new UTF8Encoding(false));

            string? desktopPath = null;
            string? iconPath = null;
            if (!string.IsNullOrWhiteSpace(debian.DesktopName))
            {
                string desktopDirectory = Path.Combine(stagingRoot, "usr", "share", "applications");
                Directory.CreateDirectory(desktopDirectory);
                desktopPath = Path.Combine(desktopDirectory, debian.PackageName + ".desktop");
                File.WriteAllText(desktopPath, BuildDesktopEntry(debian), new UTF8Encoding(false));

                if (!string.IsNullOrWhiteSpace(debian.IconPath))
                {
                    string sourceIcon = ResolvePath(plan.ProjectRoot, debian.IconPath!);
                    EnsurePathWithinRoot(plan.ProjectRoot, sourceIcon, $"Debian installer '{installerId}' icon source");
                    if (!File.Exists(sourceIcon))
                        throw new FileNotFoundException("Debian desktop icon was not found.", sourceIcon);
                    string iconDirectory = Path.Combine(
                        stagingRoot,
                        "usr",
                        "share",
                        "icons",
                        "hicolor",
                        $"{debian.IconSize}x{debian.IconSize}",
                        "apps");
                    Directory.CreateDirectory(iconDirectory);
                    iconPath = Path.Combine(iconDirectory, debian.PackageName + ".png");
                    File.Copy(sourceIcon, iconPath, overwrite: false);
                }
            }

            RunRequiredLinuxTool("chmod", stagingRoot, new[] { "-R", "u=rwX,go=rX", stagingRoot });
            RunRequiredLinuxTool("chmod", stagingRoot, new[] { "0755", executablePath });
            RunRequiredLinuxTool(
                "ln",
                stagingRoot,
                new[]
                {
                    "-s",
                    "/opt/" + debian.InstallDirectoryName + "/" + debian.Executable.Replace('\\', '/'),
                    Path.Combine(commandDirectory, debian.CommandName)
                });
            RunRequiredLinuxTool("chmod", stagingRoot, new[] { "0755", controlDirectory });
            RunRequiredLinuxTool("chmod", stagingRoot, new[] { "0644", controlPath });
            if (desktopPath is not null)
                RunRequiredLinuxTool("chmod", stagingRoot, new[] { "0644", desktopPath });
            if (iconPath is not null)
                RunRequiredLinuxTool("chmod", stagingRoot, new[] { "0644", iconPath });

            if (File.Exists(outputPath))
                File.Delete(outputPath);
            RunRequiredLinuxTool("dpkg-deb", outputDirectory, new[] { "--build", "--root-owner-group", stagingRoot, outputPath });
            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                throw new InvalidOperationException($"dpkg-deb did not produce the expected package: {outputPath}");

            long bytes = new FileInfo(outputPath).Length;
            _logger.Info($"Debian package -> {outputPath}");
            return new DotNetPublishArtefactResult
            {
                Category = DotNetPublishArtefactCategory.Installer,
                InstallerId = installerId,
                Target = target,
                Kind = source.Kind,
                Runtime = runtime,
                Framework = framework,
                Style = style.Value,
                PublishDir = outputDirectory,
                OutputDir = outputDirectory,
                OutputFiles = new[] { outputPath },
                Files = 1,
                TotalBytes = bytes
            };
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
                Directory.Delete(stagingRoot, recursive: true);
        }
    }

    internal static string BuildDebianControl(DotNetPublishDebianOptions options, string architecture)
    {
        var builder = new StringBuilder();
        builder.Append("Package: ").Append(options.PackageName).Append('\n');
        builder.Append("Version: ").Append(options.Version).Append('\n');
        builder.Append("Section: ").Append(options.Section).Append('\n');
        builder.Append("Priority: ").Append(options.Priority).Append('\n');
        builder.Append("Architecture: ").Append(architecture).Append('\n');
        builder.Append("Maintainer: ").Append(options.Maintainer).Append('\n');
        if (!string.IsNullOrWhiteSpace(options.Depends))
            builder.Append("Depends: ").Append(options.Depends).Append('\n');
        builder.Append("Description: ").Append(options.Description).Append('\n');
        return builder.ToString();
    }

    internal static string BuildDesktopEntry(DotNetPublishDebianOptions options)
    {
        var builder = new StringBuilder();
        builder.Append("[Desktop Entry]\n");
        builder.Append("Type=Application\n");
        builder.Append("Name=").Append(options.DesktopName).Append('\n');
        if (!string.IsNullOrWhiteSpace(options.DesktopComment))
            builder.Append("Comment=").Append(options.DesktopComment).Append('\n');
        builder.Append("Exec=/usr/bin/").Append(options.CommandName).Append(" %F\n");
        if (!string.IsNullOrWhiteSpace(options.IconPath))
            builder.Append("Icon=").Append(options.PackageName).Append('\n');
        builder.Append("Terminal=false\n");
        if (!string.IsNullOrWhiteSpace(options.DesktopCategories))
            builder.Append("Categories=").Append(EnsureDesktopListTerminator(options.DesktopCategories!)).Append('\n');
        if (!string.IsNullOrWhiteSpace(options.MimeTypes))
            builder.Append("MimeType=").Append(EnsureDesktopListTerminator(options.MimeTypes!)).Append('\n');
        if (!string.IsNullOrWhiteSpace(options.StartupWmClass))
            builder.Append("StartupWMClass=").Append(options.StartupWmClass).Append('\n');
        return builder.ToString();
    }

    private static string EnsureDesktopListTerminator(string value)
        => value.EndsWith(";", StringComparison.Ordinal) ? value : value + ";";

    private static void RunRequiredLinuxTool(string tool, string workingDirectory, IReadOnlyList<string> arguments)
    {
        try
        {
            (int exitCode, string stdOut, string stdErr) = RunProcess(tool, workingDirectory, arguments);
            if (exitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;
                throw new InvalidOperationException($"{tool} failed with exit code {exitCode}: {detail.Trim()}");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException($"Required Linux packaging tool '{tool}' was not found.", exception);
        }
    }
}
