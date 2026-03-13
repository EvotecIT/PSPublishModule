using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private string ResolveVersion(
        DotNetRepositoryProjectResult project,
        string? expectedVersion,
        DotNetRepositoryReleaseSpec spec,
        out string? warning)
    {
        warning = null;

        if (!spec.UpdateVersions)
        {
            if (CsprojVersionEditor.TryGetVersion(project.CsprojPath, out var v))
                return v;
            warning = "UpdateVersions is disabled and no version tags were found in the project file.";
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            if (CsprojVersionEditor.TryGetVersion(project.CsprojPath, out var v))
                return v;
            warning = "No expected version provided and no version tags were found in the project file.";
            return string.Empty;
        }

        if (Version.TryParse(expectedVersion, out var exact))
            return exact.ToString();

        var current = _resolver.ResolveLatest(
            packageId: string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId,
            sources: spec.VersionSources,
            credential: spec.VersionSourceCredential,
            includePrerelease: spec.IncludePrerelease);

        if (current is null)
            warning = $"No current package version found; using 0 baseline for '{expectedVersion}'.";

        return VersionPatternStepper.Step(expectedVersion!, current);
    }

    private static DotNetPackResult PackProject(DotNetRepositoryProjectResult project, DotNetRepositoryReleaseSpec spec)
    {
        var result = new DotNetPackResult();

        var csprojDir = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();

        string? outputPath = null;
        if (!string.IsNullOrWhiteSpace(spec.OutputPath))
        {
            outputPath = Path.IsPathRooted(spec.OutputPath)
                ? spec.OutputPath
                : Path.GetFullPath(Path.Combine(spec.RootPath, spec.OutputPath));
            Directory.CreateDirectory(outputPath);
        }

        var exitCode = RunDotnetPack(project.CsprojPath, csprojDir, configuration, outputPath, out var stdErr, out var stdOut);
        if (exitCode != 0)
        {
            result.ErrorMessage = $"dotnet pack failed for {project.ProjectName} (exit {exitCode}). {stdErr}".Trim();
            return result;
        }

        var packageRoot = outputPath ?? Path.Combine(csprojDir, "bin", configuration);
        if (Directory.Exists(packageRoot))
        {
            var pkgs = Directory.EnumerateFiles(packageRoot, "*.nupkg", SearchOption.AllDirectories)
                .Where(p => !p.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            result.Packages.AddRange(pkgs);
        }

        result.Success = true;
        return result;
    }

    private static string? ResolvePackagePath(DotNetRepositoryReleaseSpec spec, DotNetRepositoryProjectResult project, string version)
    {
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var outputPath = string.IsNullOrWhiteSpace(spec.OutputPath)
            ? Path.Combine(Path.GetDirectoryName(project.CsprojPath) ?? string.Empty, "bin", configuration)
            : (Path.IsPathRooted(spec.OutputPath)
                ? spec.OutputPath
                : Path.Combine(spec.RootPath, spec.OutputPath));

        if (string.IsNullOrWhiteSpace(outputPath)) return null;
        var packageId = string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId;
        return Path.Combine(outputPath, $"{packageId}.{version}.nupkg");
    }

    private static int RunDotnetPack(string csproj, string workingDirectory, string configuration, string? outputPath, out string stdErr, out string stdOut)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        var args = new List<string> { "pack", csproj, "--configuration", configuration };
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            args.Add("-o");
            args.Add(outputPath!);
        }
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("pack");
        psi.ArgumentList.Add(csproj);
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(configuration);
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outputPath!);
        }
#endif

        using var p = Process.Start(psi);
        if (p is null) return 1;
        stdOut = p.StandardOutput.ReadToEnd();
        stdErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    internal static PackagePushResult ClassifyNuGetPushOutcome(int exitCode, bool skipDuplicate, string stdErr, string stdOut)
    {
        var combined = string.Join(Environment.NewLine, stdErr, stdOut).Trim();

        if (exitCode != 0)
        {
            return new PackagePushResult
            {
                Outcome = PackagePushOutcome.Failed,
                Message = combined
            };
        }

        if (skipDuplicate && LooksLikeSkippedDuplicate(combined))
        {
            return new PackagePushResult
            {
                Outcome = PackagePushOutcome.SkippedDuplicate,
                Message = combined
            };
        }

        return new PackagePushResult
        {
            Outcome = PackagePushOutcome.Published,
            Message = combined
        };
    }

    private static bool PushPackage(string packagePath, string apiKey, string source, bool skipDuplicate, out PackagePushResult result)
    {
        result = new PackagePushResult();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

#if NET472
        var args = new List<string>
        {
            "nuget", "push", packagePath,
            "--api-key", apiKey,
            "--source", source
        };
        if (skipDuplicate) args.Add("--skip-duplicate");
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        psi.ArgumentList.Add("nuget");
        psi.ArgumentList.Add("push");
        psi.ArgumentList.Add(packagePath);
        psi.ArgumentList.Add("--api-key");
        psi.ArgumentList.Add(apiKey);
        psi.ArgumentList.Add("--source");
        psi.ArgumentList.Add(source);
        if (skipDuplicate) psi.ArgumentList.Add("--skip-duplicate");
#endif

        using var p = Process.Start(psi);
        if (p is null)
        {
            result = new PackagePushResult
            {
                Outcome = PackagePushOutcome.Failed,
                Message = "Failed to start dotnet."
            };
            return false;
        }
        var stdOut = p.StandardOutput.ReadToEnd();
        var stdErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        result = ClassifyNuGetPushOutcome(p.ExitCode, skipDuplicate, stdErr, stdOut);
        return result.Outcome != PackagePushOutcome.Failed;
    }

    private static bool LooksLikeSkippedDuplicate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("409 (Conflict)", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("cannot be modified", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("skip duplicate", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal enum PackagePushOutcome
    {
        Published,
        SkippedDuplicate,
        Failed
    }

    internal sealed class PackagePushResult
    {
        public PackagePushOutcome Outcome { get; set; }
        public string? Message { get; set; }
    }

}
