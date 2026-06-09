using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;

namespace PowerForge;

/// <summary>
/// Builds a .NET project (dotnet build/pack) and prepares release artefacts (zip + optional signing).
/// </summary>
public sealed class DotNetReleaseBuildService
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new release build service.
    /// </summary>
    /// <param name="logger">Logger used for progress output.</param>
    public DotNetReleaseBuildService(ILogger logger) => _logger = logger;

    /// <summary>
    /// Executes the release build described by <paramref name="spec"/>.
    /// </summary>
    /// <param name="spec">Release build specification.</param>
    /// <param name="signAssemblies">
    /// Optional callback used to sign assemblies in the release output directory (PowerShell-only in PSPublishModule).
    /// When null, assembly signing is skipped.
    /// </param>
    public DotNetReleaseBuildResult Execute(DotNetReleaseBuildSpec spec, Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies = null)
    {
        var result = new DotNetReleaseBuildResult();

        try
        {
            if (spec is null) throw new ArgumentNullException(nameof(spec));
            if (string.IsNullOrWhiteSpace(spec.ProjectPath))
            {
                result.ErrorMessage = "ProjectPath is required.";
                return result;
            }

            var fullProjectPath = Path.GetFullPath(spec.ProjectPath.Trim().Trim('"'));
            if (!Directory.Exists(fullProjectPath) && !File.Exists(fullProjectPath))
            {
                result.ErrorMessage = $"Project path '{fullProjectPath}' not found.";
                return result;
            }

            var csprojPath = ResolveCsproj(fullProjectPath);
            if (csprojPath is null)
            {
                result.ErrorMessage = $"No csproj found in {fullProjectPath}";
                return result;
            }

            var csprojDir = Path.GetDirectoryName(csprojPath) ?? fullProjectPath;
            var projectName = Path.GetFileNameWithoutExtension(csprojPath) ?? "Project";
            result.ProjectName = projectName;
            result.CsprojPath = csprojPath;

            var doc = XDocument.Load(csprojPath);
            var version = GetFirstElementValue(doc, "VersionPrefix");
            if (string.IsNullOrWhiteSpace(version))
            {
                result.ErrorMessage = $"VersionPrefix not found in '{csprojPath}'";
                return result;
            }

            result.Version = version;

            var dependencyProjects = spec.PackDependencies
                ? GetProjectReferences(doc, csprojPath).ToArray()
                : Array.Empty<string>();
            result.DependencyProjects = dependencyProjects;

            var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
            var timeStampServer = string.IsNullOrWhiteSpace(spec.TimeStampServer) ? "http://timestamp.digicert.com" : spec.TimeStampServer;
            var releasePath = Path.Combine(csprojDir, "bin", configuration);
            result.ReleasePath = releasePath;

            var zipPath = Path.Combine(releasePath, $"{projectName}.{version}.zip");
            result.ZipPath = zipPath;

            if (spec.WhatIf)
            {
                result.Success = true;
                result.Packages = new[] { Path.Combine(releasePath, $"{projectName}.{version}.nupkg") };
                return result;
            }

            if (!IsDotnetAvailable())
            {
                result.ErrorMessage = "dotnet CLI is not available.";
                return result;
            }

            CleanReleaseDirectory(releasePath);

            var buildExit = RunProcess(
                fileName: "dotnet",
                arguments: $"build {Quote(csprojPath)} --configuration {Quote(configuration)}",
                workingDirectory: csprojDir,
                out var buildStdErr,
                out var buildStdOut);
            if (buildExit != 0)
            {
                result.ErrorMessage = $"dotnet build failed. ExitCode={buildExit}\n{buildStdErr}\n{buildStdOut}".Trim();
                return result;
            }

            if (!string.IsNullOrWhiteSpace(spec.CertificateThumbprint) && signAssemblies is not null)
            {
                signAssemblies(new DotNetReleaseBuildAssemblySigningRequest
                {
                    ReleasePath = releasePath,
                    LocalStore = spec.LocalStore,
                    CertificateThumbprint = spec.CertificateThumbprint!.Trim(),
                    TimeStampServer = timeStampServer,
                    IncludePatterns = spec.AssemblyIncludePatterns ?? Array.Empty<string>()
                });
            }

            CreateZipFromDirectoryContents(releasePath, zipPath);

            var packExit = RunProcess(
                fileName: "dotnet",
                arguments: $"pack {Quote(csprojPath)} --configuration {Quote(configuration)} --no-restore --no-build",
                workingDirectory: csprojDir,
                out var packStdErr,
                out var packStdOut);
            if (packExit != 0)
            {
                result.ErrorMessage = $"dotnet pack failed. ExitCode={packExit}\n{packStdErr}\n{packStdOut}".Trim();
                return result;
            }

            if (spec.PackDependencies && dependencyProjects.Length > 0)
            {
                _logger.Verbose($"DotNetReleaseBuild - Packing {dependencyProjects.Length} dependency projects");
                foreach (var depProj in dependencyProjects)
                {
                    var depName = Path.GetFileName(depProj) ?? depProj;
                    _logger.Verbose($"DotNetReleaseBuild - Packing dependency: {depName}");

                    var depExit = RunProcess(
                        fileName: "dotnet",
                        arguments: $"pack {Quote(depProj)} --configuration {Quote(configuration)} --no-restore --no-build",
                        workingDirectory: Path.GetDirectoryName(depProj) ?? csprojDir,
                        out var depStdErr,
                        out var depStdOut);
                    if (depExit != 0)
                    {
                        _logger.Warn($"DotNetReleaseBuild - Failed to pack dependency: {depName}");
                        if (!string.IsNullOrWhiteSpace(depStdErr)) _logger.Verbose(depStdErr.Trim());
                        if (!string.IsNullOrWhiteSpace(depStdOut)) _logger.Verbose(depStdOut.Trim());
                    }
                }
            }

            var allPackages = new List<string>();
            allPackages.AddRange(Directory.EnumerateFiles(releasePath, "*.nupkg", SearchOption.AllDirectories));

            if (spec.PackDependencies)
            {
                foreach (var depProj in dependencyProjects)
                {
                    var depDir = Path.GetDirectoryName(depProj);
                    if (string.IsNullOrWhiteSpace(depDir)) continue;
                    var depRelease = Path.Combine(depDir, "bin", configuration);
                    if (!Directory.Exists(depRelease)) continue;
                    allPackages.AddRange(Directory.EnumerateFiles(depRelease, "*.nupkg", SearchOption.AllDirectories));
                }
            }

            if (!string.IsNullOrWhiteSpace(spec.CertificateThumbprint) && allPackages.Count > 0)
            {
                _logger.Verbose($"DotNetReleaseBuild - Signing {allPackages.Count} packages");

                var sha256 = GetCertificateSha256(spec.CertificateThumbprint!.Trim(), spec.LocalStore);
                if (sha256 is null)
                {
                    _logger.Warn($"DotNetReleaseBuild - Certificate with thumbprint '{spec.CertificateThumbprint}' not found in {spec.LocalStore}\\My store");
                    result.ErrorMessage = "Certificate not found for signing";
                    return result;
                }

                _logger.Verbose($"DotNetReleaseBuild - Using certificate SHA256: {sha256}");

                foreach (var pkgPath in allPackages)
                {
                    var pkgName = Path.GetFileName(pkgPath) ?? pkgPath;
                    _logger.Verbose($"DotNetReleaseBuild - Signing package: {pkgName}");

                    var signExit = RunProcess(
                        fileName: "dotnet",
                        arguments: $"nuget sign {Quote(pkgPath)} --certificate-fingerprint {Quote(sha256)} --certificate-store-location {spec.LocalStore} --certificate-store-name My --timestamper {Quote(timeStampServer)} --overwrite",
                        workingDirectory: csprojDir,
                        out _,
                        out _);
                    if (signExit != 0)
                        _logger.Warn($"DotNetReleaseBuild - Failed to sign {pkgPath}");
                    else
                        _logger.Verbose($"DotNetReleaseBuild - Successfully signed {pkgPath}");
                }
            }

            result.Success = true;
            result.Packages = allPackages.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    private static string? ResolveCsproj(string path)
    {
        if (File.Exists(path) && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(path);

        if (!Directory.Exists(path))
            return null;

        return Directory.EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static string? GetFirstElementValue(XDocument doc, string localName)
        => doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value;

    private static IEnumerable<string> GetProjectReferences(XDocument doc, string csprojPath)
    {
        var csprojDir = Path.GetDirectoryName(csprojPath) ?? string.Empty;
        foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase)))
        {
            var include = pr.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;

            var depPath = Path.GetFullPath(Path.Combine(csprojDir, include));
            if (File.Exists(depPath))
                yield return depPath;
        }
    }

    private static void CleanReleaseDirectory(string releasePath)
    {
        if (Directory.Exists(releasePath))
        {
            try
            {
                Directory.Delete(releasePath, recursive: true);
            }
            catch
            {
                foreach (var file in Directory.EnumerateFiles(releasePath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch { }
                }

                foreach (var dir in Directory.EnumerateDirectories(releasePath, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    try { Directory.Delete(dir, recursive: true); } catch { }
                }
            }
        }

        Directory.CreateDirectory(releasePath);
    }

    private static void CreateZipFromDirectoryContents(string directoryPath, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);

        var zipFullPath = Path.GetFullPath(zipPath);

        using var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            // Avoid trying to zip the zip file that we are currently writing (common when zipPath is under directoryPath).
            try
            {
                if (string.Equals(Path.GetFullPath(file), zipFullPath, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            catch
            {
                // best effort
            }

            var rel = ComputeRelativePath(directoryPath, file);
            archive.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
        }
    }

    private static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(baseDir)));
            var pathUri = new Uri(Path.GetFullPath(fullPath));
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return Path.GetFileName(fullPath) ?? fullPath;
        }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;

    private static bool IsDotnetAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            ProcessStartInfoEncoding.TryApplyUtf8(psi);
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static int RunProcess(string fileName, string arguments, string workingDirectory, out string stdErr, out string stdOut)
    {
        stdErr = string.Empty;
        stdOut = string.Empty;

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

        using var p = Process.Start(psi);
        if (p is null) return 1;
        stdOut = p.StandardOutput.ReadToEnd();
        stdErr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    private static string? GetCertificateSha256(string thumbprint, CertificateStoreLocation storeLocation)
    {
        try
        {
            var loc = storeLocation == CertificateStoreLocation.LocalMachine ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;
            using var store = new X509Store(StoreName.My, loc);
            store.Open(OpenFlags.ReadOnly);
            var cert = store.Certificates.Cast<X509Certificate2>()
                .FirstOrDefault(c => NormalizeThumbprint(c.Thumbprint) == NormalizeThumbprint(thumbprint));
            if (cert is null) return null;
#if NET472
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(cert.RawData);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
#else
            return cert.GetCertHashString(HashAlgorithmName.SHA256);
#endif
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeThumbprint(string? thumbprint)
        => (thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (!value.Contains(" ") && !value.Contains("\"")) return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
