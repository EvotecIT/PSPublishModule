using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliDotNetPublishTests
{
    [Fact]
    public async Task Version_CliAcceptsOutputSelectionBeforeVersionFlag()
    {
        var repoRoot = FindRepositoryRoot();
        var cliPath = Path.Combine(
            repoRoot,
            "PowerForge.Cli",
            "bin",
            "Release",
            "net10.0",
            "PowerForge.Cli.dll");
        var (exitCode, stdout, stderr) = await RunCliAsync(
            repoRoot,
            $"\"{cliPath}\" --output json --version");

        Assert.True(exitCode == 0, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        using var document = JsonDocument.Parse(stdout);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("version", document.RootElement.GetProperty("command").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("result").GetProperty("version").GetString()));
    }

    [Fact]
    public async Task ReleaseArtifactVerify_MissingEvidenceReturnsStructuredFailure()
    {
        var repoRoot = FindRepositoryRoot();
        var (exitCode, stdout, stderr) = await RunCliAsync(
            repoRoot,
            $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- dotnet release-artifact verify --output json");

        Assert.True(exitCode == 2, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        using var document = JsonDocument.Parse(stdout);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("dotnet.release-artifact.verify", document.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public async Task ReleaseArtifactVerify_PortableCliKindUsesGeneralEvidenceContract()
    {
        var repoRoot = FindRepositoryRoot();
        var (exitCode, stdout, stderr) = await RunCliAsync(
            repoRoot,
            $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- dotnet release-artifact verify --kind portable-cli --output json");

        Assert.True(exitCode == 2, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        using var document = JsonDocument.Parse(stdout);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("dotnet.release-artifact.verify", document.RootElement.GetProperty("command").GetString());
        Assert.Contains("artifact ID", document.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReleaseArtifactVerify_RealSignedPortableCliReturnsStableJsonEvidenceShape()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();
        const string sourceRevision = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        try
        {
            string outputDirectory = Path.Combine(tempRoot, "Artifacts", "Sample.CLI", "win-x64", "net10.0", "PortableCompat");
            Directory.CreateDirectory(outputDirectory);
            string executablePath = Path.Combine(outputDirectory, "Sample.CLI.exe");
            var signedWindowsExecutable = FindSignedWindowsExecutable();
            File.Copy(signedWindowsExecutable, executablePath);
            DotNetPublishReleaseArtifactVerifier.AuthenticodeResult realSignature =
                DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode(executablePath);
            Assert.True(realSignature.IsValid);
            string realVersion = ReadNormalizedPortableVersion(executablePath);

            string archivePath = Path.Combine(Path.GetDirectoryName(outputDirectory)!, "Sample.CLI.zip");
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
                archive.CreateEntryFromFile(executablePath, "Sample.CLI.exe", CompressionLevel.NoCompression);

            string manifestPath = Path.Combine(tempRoot, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new[]
            {
                new
                {
                    Category = "Publish",
                    Target = "Sample.CLI",
                    Kind = "Cli",
                    Runtime = "win-x64",
                    Framework = "net10.0",
                    Style = "PortableCompat",
                    OutputDir = outputDirectory,
                    ZipPath = archivePath,
                    ExePath = executablePath,
                    SignedFiles = 1,
                    SourceRevision = sourceRevision,
                    SourceDirty = false
                }
            }));
            string configurationPath = Path.Combine(tempRoot, "powerforge.dotnetpublish.json");
            File.WriteAllText(configurationPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                DotNet = new { AllowOutputOutsideProjectRoot = false },
                Targets = new[]
                {
                    new
                    {
                        Name = "Sample.CLI",
                        Kind = "Cli",
                        Publish = new
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Style = "PortableCompat",
                            Sign = new { Enabled = true, SubjectName = realSignature.Subject }
                        }
                    }
                }
            }));
            string sbomPath = Path.Combine(tempRoot, "sample.cdx.json");
            File.WriteAllText(sbomPath, JsonSerializer.Serialize(new
            {
                bomFormat = "CycloneDX",
                specVersion = "1.6",
                version = 1,
                metadata = new
                {
                    component = new
                    {
                        name = "Sample.CLI",
                        version = realVersion,
                        hashes = new[] { new { alg = "SHA-256", content = ComputeSha256(archivePath) } }
                    }
                }
            }));
            string checksumsPath = Path.Combine(tempRoot, "SHA256SUMS.txt");
            File.WriteAllLines(checksumsPath, new[] { manifestPath, configurationPath, executablePath, archivePath, sbomPath }.Select(path =>
                $"{ComputeSha256(path)} *{Path.GetRelativePath(tempRoot, path).Replace('\\', '/')}"));

            var (exitCode, stdout, stderr) = await RunCliAsync(
                repoRoot,
                $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- dotnet release-artifact verify --kind portable-cli --artifact-id Sample.CLI --project-root \"{tempRoot}\" --artifact \"{archivePath}\" --checksums \"{checksumsPath}\" --source-revision {sourceRevision} --manifest \"{manifestPath}\" --config \"{configurationPath}\" --rid win-x64 --framework net10.0 --style PortableCompat --sbom \"{sbomPath}\" --output json");

            Assert.True(exitCode == 0, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            using JsonDocument document = JsonDocument.Parse(stdout);
            JsonElement root = document.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal("dotnet.release-artifact.verify", root.GetProperty("command").GetString());
            JsonElement result = root.GetProperty("result");
            Assert.Equal("PortableCli", result.GetProperty("artifactKind").GetString());
            Assert.Equal("Sample.CLI", result.GetProperty("artifactId").GetString());
            Assert.Equal("valid", result.GetProperty("signatureStatus").GetString());
            Assert.Equal(realSignature.Subject, result.GetProperty("signerSubject").GetString());
            Assert.Contains(result.GetProperty("evidenceFiles").EnumerateArray(), evidence =>
                evidence.GetProperty("role").GetString() == "sbom" &&
                evidence.GetProperty("sha256").GetString() == ComputeSha256(sbomPath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DotNetPublish_CliProjectRootOverrideWinsOverExplicitConfigRoot()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();

        try
        {
            var configPath = Path.Combine(tempRoot, "powerforge.dotnetpublish.json");
            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = tempRoot,
                    SolutionPath = "PSPublishModule.sln",
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "PowerForge.Cli",
                        ProjectPath = "PowerForge.Cli/PowerForge.Cli.csproj",
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            UseStaging = false,
                            Zip = false
                        }
                    }
                }
            };
            File.WriteAllText(configPath, JsonSerializer.Serialize(spec, new JsonSerializerOptions { WriteIndented = true }));

            var (exitCode, stdout, stderr) = await RunCliAsync(
                repoRoot,
                $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- dotnet publish --config \"{configPath}\" --project-root \"{repoRoot}\" --validate --output json");

            Assert.True(exitCode == 0, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(repoRoot, root.GetProperty("plan").GetProperty("projectRoot").GetString(), ignoreCase: true);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(string workingDirectory, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();

        if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(120))) != exitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException("PowerForge CLI dotnet publish validation test timed out.");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge.Cli", "PowerForge.Cli.csproj")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for PowerForge CLI tests.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeCliDotNetPublish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string FindSignedWindowsExecutable()
    {
        string[] pathCandidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory.Trim().Trim('"'), "pwsh.exe"))
            .Concat(new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (string candidate in pathCandidates)
        {
            if (!File.Exists(candidate) || !DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode(candidate).IsValid)
                continue;
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(candidate);
            string productVersion = (version.ProductVersion ?? string.Empty).Split('+')[0].Trim();
            if (Version.TryParse(productVersion, out _) || Version.TryParse(version.FileVersion, out _))
                return candidate;
        }

        throw new InvalidOperationException("No embedded-signed Windows executable with a numeric version was available for real signature proof.");
    }

    private static string ReadNormalizedPortableVersion(string path)
    {
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
        string value = (version.ProductVersion ?? string.Empty).Split('+')[0].Trim();
        if (!Version.TryParse(value, out Version? parsed))
        {
            value = version.FileVersion ?? string.Empty;
            parsed = Version.Parse(value);
        }
        return parsed.Revision == 0
            ? new Version(parsed.Major, parsed.Minor, parsed.Build).ToString()
            : parsed.ToString();
    }
}
