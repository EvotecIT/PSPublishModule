using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management.Automation;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;

namespace PSPublishModule;

/// <summary>
/// Builds a .NET project in Release configuration and prepares release artefacts.
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "DotNetReleaseBuild", SupportsShouldProcess = true)]
public sealed class InvokeDotNetReleaseBuildCommand : PSCmdlet
{
    /// <summary>Path to the folder containing the project (*.csproj) file (or the csproj file itself).</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Optional certificate thumbprint used to sign assemblies and packages. When omitted, no signing is performed.</summary>
    [Parameter]
    public string? CertificateThumbprint { get; set; }

    /// <summary>Certificate store location used when searching for the signing certificate. Default: CurrentUser.</summary>
    [Parameter]
    public CertificateStoreLocation LocalStore { get; set; } = CertificateStoreLocation.CurrentUser;

    /// <summary>Timestamp server URL used while signing. Default: http://timestamp.digicert.com.</summary>
    [Parameter]
    public string TimeStampServer { get; set; } = "http://timestamp.digicert.com";

    /// <summary>When enabled, also packs all project dependencies that have their own .csproj files.</summary>
    [Parameter]
    public SwitchParameter PackDependencies { get; set; }

    /// <summary>Executes build/pack/sign operations and returns a result object.</summary>
    protected override void ProcessRecord()
    {
        var result = new DotNetReleaseBuildResult();

        try
        {
            var fullProjectPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectPath);
            if (!Directory.Exists(fullProjectPath) && !File.Exists(fullProjectPath))
            {
                result.ErrorMessage = $"Project path '{ProjectPath}' not found.";
                WriteObject(result);
                return;
            }

            var csprojPath = ResolveCsproj(fullProjectPath);
            if (csprojPath is null)
            {
                result.ErrorMessage = $"No csproj found in {ProjectPath}";
                WriteObject(result);
                return;
            }

            var csprojDir = Path.GetDirectoryName(csprojPath) ?? fullProjectPath;
            var projectName = Path.GetFileNameWithoutExtension(csprojPath) ?? "Project";

            var doc = XDocument.Load(csprojPath);
            var version = GetFirstElementValue(doc, "VersionPrefix");
            if (string.IsNullOrWhiteSpace(version))
            {
                result.ErrorMessage = $"VersionPrefix not found in '{csprojPath}'";
                WriteObject(result);
                return;
            }

            result.Version = version;

            var dependencyProjects = PackDependencies.IsPresent
                ? GetProjectReferences(doc, csprojPath).ToArray()
                : Array.Empty<string>();
            result.DependencyProjects = dependencyProjects;

            var releasePath = Path.Combine(csprojDir, "bin", "Release");
            result.ReleasePath = releasePath;

            var zipPath = Path.Combine(releasePath, $"{projectName}.{version}.zip");
            result.ZipPath = zipPath;

            if (!ShouldProcess($"{projectName} v{version}", "Build and pack .NET project"))
            {
                result.Success = true;
                result.Packages = new[] { Path.Combine(releasePath, $"{projectName}.{version}.nupkg") };

                if (PackDependencies.IsPresent && dependencyProjects.Length > 0)
                {
                    WriteVerbose($"Would also pack {dependencyProjects.Length} dependency projects: {string.Join(", ", dependencyProjects.Select(Path.GetFileName))}");
                }

                WriteObject(result);
                return;
            }

            if (!IsDotnetAvailable())
            {
                result.ErrorMessage = "dotnet CLI is not available.";
                WriteObject(result);
                return;
            }

            CleanReleaseDirectory(releasePath);

            var buildExit = RunProcess(
                fileName: "dotnet",
                arguments: $"build {Quote(csprojPath)} --configuration Release",
                workingDirectory: csprojDir,
                out var buildStdErr,
                out var buildStdOut);
            if (buildExit != 0)
            {
                result.ErrorMessage = $"dotnet build failed. ExitCode={buildExit}\n{buildStdErr}\n{buildStdOut}".Trim();
                WriteObject(result);
                return;
            }

            if (!string.IsNullOrWhiteSpace(CertificateThumbprint))
            {
                InvokeRegisterCertificate(releasePath, LocalStore, CertificateThumbprint!, TimeStampServer, new[] { "*.dll" });
            }

            CreateZipFromDirectoryContents(releasePath, zipPath);

            var packExit = RunProcess(
                fileName: "dotnet",
                arguments: $"pack {Quote(csprojPath)} --configuration Release --no-restore --no-build",
                workingDirectory: csprojDir,
                out var packStdErr,
                out var packStdOut);
            if (packExit != 0)
            {
                result.ErrorMessage = $"dotnet pack failed. ExitCode={packExit}\n{packStdErr}\n{packStdOut}".Trim();
                WriteObject(result);
                return;
            }

            if (PackDependencies.IsPresent && dependencyProjects.Length > 0)
            {
                WriteVerbose($"Invoke-DotNetReleaseBuild - Packing {dependencyProjects.Length} dependency projects");
                foreach (var depProj in dependencyProjects)
                {
                    var depName = Path.GetFileName(depProj) ?? depProj;
                    WriteVerbose($"Invoke-DotNetReleaseBuild - Packing dependency: {depName}");

                    var depExit = RunProcess(
                        fileName: "dotnet",
                        arguments: $"pack {Quote(depProj)} --configuration Release --no-restore --no-build",
                        workingDirectory: Path.GetDirectoryName(depProj) ?? csprojDir,
                        out var depStdErr,
                        out var depStdOut);
                    if (depExit != 0)
                    {
                        WriteWarning($"Invoke-DotNetReleaseBuild - Failed to pack dependency: {depName}");
                        if (!string.IsNullOrWhiteSpace(depStdErr)) WriteVerbose(depStdErr.Trim());
                        if (!string.IsNullOrWhiteSpace(depStdOut)) WriteVerbose(depStdOut.Trim());
                    }
                }
            }

            var allPackages = new List<string>();
            allPackages.AddRange(Directory.EnumerateFiles(releasePath, "*.nupkg", SearchOption.AllDirectories));

            if (PackDependencies.IsPresent)
            {
                foreach (var depProj in dependencyProjects)
                {
                    var depDir = Path.GetDirectoryName(depProj);
                    if (string.IsNullOrWhiteSpace(depDir)) continue;
                    var depRelease = Path.Combine(depDir, "bin", "Release");
                    if (!Directory.Exists(depRelease)) continue;
                    allPackages.AddRange(Directory.EnumerateFiles(depRelease, "*.nupkg", SearchOption.AllDirectories));
                }
            }

            if (!string.IsNullOrWhiteSpace(CertificateThumbprint) && allPackages.Count > 0)
            {
                WriteVerbose($"Invoke-DotNetReleaseBuild - Signing {allPackages.Count} packages");

                var sha256 = GetCertificateSha256(CertificateThumbprint!, LocalStore);
                if (sha256 is null)
                {
                    WriteWarning($"Invoke-DotNetReleaseBuild - Certificate with thumbprint '{CertificateThumbprint}' not found in {LocalStore}\\My store");
                    result.ErrorMessage = "Certificate not found for signing";
                    WriteObject(result);
                    return;
                }

                WriteVerbose($"Invoke-DotNetReleaseBuild - Using certificate SHA256: {sha256}");

                foreach (var pkgPath in allPackages)
                {
                    var pkgName = Path.GetFileName(pkgPath) ?? pkgPath;
                    WriteVerbose($"Invoke-DotNetReleaseBuild - Signing package: {pkgName}");

                    var signExit = RunProcess(
                        fileName: "dotnet",
                        arguments:
                            $"nuget sign {Quote(pkgPath)} --certificate-fingerprint {Quote(sha256)} --certificate-store-location {LocalStore} --certificate-store-name My --timestamper {Quote(TimeStampServer)} --overwrite",
                        workingDirectory: csprojDir,
                        out _,
                        out _);
                    if (signExit != 0)
                    {
                        WriteWarning($"Invoke-DotNetReleaseBuild - Failed to sign {pkgPath}");
                    }
                    else
                    {
                        WriteVerbose($"Invoke-DotNetReleaseBuild - Successfully signed {pkgPath}");
                    }
                }
            }

            result.Success = true;
            result.Packages = allPackages.ToArray();
            WriteObject(result);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            WriteObject(result);
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

    private void InvokeRegisterCertificate(string releasePath, CertificateStoreLocation store, string thumbprint, string timeStampServer, string[] includePatterns)
    {
        var module = MyInvocation.MyCommand?.Module;
        if (module is null) return;

        var sb = ScriptBlock.Create(@"
param($path,$store,$thumb,$ts,$include)
Register-Certificate -Path $path -LocalStore $store -Thumbprint $thumb -TimeStampServer $ts -Include $include
");
        var bound = module.NewBoundScriptBlock(sb);
        bound.Invoke(releasePath, store.ToString(), thumbprint, timeStampServer, includePatterns);
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

        using var fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
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

    /// <summary>
    /// Result returned by <c>Invoke-DotNetReleaseBuild</c>.
    /// </summary>
    public sealed class DotNetReleaseBuildResult
    {
        /// <summary>Whether the command completed successfully.</summary>
        public bool Success { get; set; }

        /// <summary>Resolved project version (from VersionPrefix).</summary>
        public string? Version { get; set; }

        /// <summary>Path to the bin/Release folder.</summary>
        public string? ReleasePath { get; set; }

        /// <summary>Path to the created zip archive.</summary>
        public string? ZipPath { get; set; }

        /// <summary>NuGet packages produced (or simulated in WhatIf).</summary>
        public string[] Packages { get; set; } = Array.Empty<string>();

        /// <summary>Dependency project paths discovered when <c>-PackDependencies</c> is used.</summary>
        public string[] DependencyProjects { get; set; } = Array.Empty<string>();

        /// <summary>Optional error message when <see cref="Success"/> is false.</summary>
        public string? ErrorMessage { get; set; }
    }
}
