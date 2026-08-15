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
    public async Task ReleaseArtifactVerify_PowerShellModuleRequiresPublisherIdentityAtCliBoundary()
    {
        var repoRoot = FindRepositoryRoot();
        const string revision = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var (exitCode, stdout, stderr) = await RunCliAsync(
            repoRoot,
            $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- dotnet release-artifact verify --kind powershell-module --artifact-id Sample --project-root . --artifact sample.nupkg --checksums SHA256SUMS.txt --source-revision {revision} --signing-evidence sample.signing.json --output json");

        Assert.True(exitCode == 2, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Contains("--sign-thumbprint or --sign-subject-name", document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleaseArtifactVerify_InstallerRequiresPublisherIdentityAtCliBoundary()
    {
        var repoRoot = FindRepositoryRoot();
        const string revision = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var (exitCode, stdout, stderr) = await RunCliAsync(
            repoRoot,
            $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- dotnet release-artifact verify --project-root . --manifest manifest.json --checksums SHA256SUMS.txt --config config.json --installer Sample.MSI --source-revision {revision} --output json");

        Assert.True(exitCode == 2, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Contains("--sign-thumbprint or --sign-subject-name", document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleaseArtifactVerify_RequiresFullSourceRevisionAtCliBoundary()
    {
        var repoRoot = FindRepositoryRoot();
        var (exitCode, stdout, stderr) = await RunCliAsync(
            repoRoot,
            $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- dotnet release-artifact verify --kind portable-cli --artifact-id Sample --project-root . --artifact sample.zip --checksums SHA256SUMS.txt --source-revision abcdef1 --manifest manifest.json --config config.json --output json");

        Assert.True(exitCode == 2, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        using JsonDocument document = JsonDocument.Parse(stdout);
        Assert.Contains("full 40- or 64-character", document.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReleaseArtifactVerify_PortableCliRequiresPublisherIdentityAtCliBoundary()
    {
        string repoRoot = FindRepositoryRoot();
        string tempRoot = CreateTempDirectory();
        try
        {
            string manifestPath = Path.Combine(tempRoot, "manifest.json");
            string checksumsPath = Path.Combine(tempRoot, "SHA256SUMS.txt");
            string configurationPath = Path.Combine(tempRoot, "powerforge.dotnetpublish.json");
            File.WriteAllText(manifestPath, "[]");
            File.WriteAllText(checksumsPath, string.Empty);
            File.WriteAllText(configurationPath, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Targets = new[]
                {
                    new
                    {
                        Name = "Sample.CLI",
                        Kind = "Cli",
                        Publish = new { Sign = new { Enabled = true } }
                    }
                }
            }));

            var (exitCode, stdout, stderr) = await RunCliAsync(
                repoRoot,
                $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- dotnet release-artifact verify --kind portable-cli --artifact-id Sample.CLI --project-root \"{tempRoot}\" --artifact missing.zip --checksums \"{checksumsPath}\" --source-revision {new string('b', 40)} --manifest \"{manifestPath}\" --config \"{configurationPath}\" --output json");

            Assert.True(exitCode == 2, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            using JsonDocument document = JsonDocument.Parse(stdout);
            Assert.Contains(
                "--sign-thumbprint or --sign-subject-name",
                document.RootElement.GetProperty("error").GetString(),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ReleaseArtifactVerify_RealSignedPortableCliWithoutPublisherInventoryFailsClosed()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();

        try
        {
            string outputDirectory = Path.Combine(tempRoot, "Artifacts", "Sample.CLI", "win-x64", "net10.0", "PortableCompat");
            Directory.CreateDirectory(outputDirectory);
            var signedWindowsExecutable = FindSignedWindowsExecutable();
            FileVersionInfo signedIdentity = FileVersionInfo.GetVersionInfo(signedWindowsExecutable);
            string artifactId = new[] { signedIdentity.ProductName, signedIdentity.InternalName, signedIdentity.OriginalFilename }
                .First(value => !string.IsNullOrWhiteSpace(value))!;
            if (artifactId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                artifactId.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                artifactId = Path.GetFileNameWithoutExtension(artifactId);
            string executablePath = Path.Combine(outputDirectory, artifactId + ".exe");
            string sourceRevision = ReadPortableSourceRevision(signedWindowsExecutable);
            File.Copy(signedWindowsExecutable, executablePath);
            DotNetPublishReleaseArtifactVerifier.AuthenticodeResult realSignature =
                DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode(executablePath);
            Assert.True(realSignature.IsValid);
            string manifestPath = Path.Combine(tempRoot, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new[]
            {
                new
                {
                    Category = "Publish",
                    Target = artifactId,
                    Kind = "Cli",
                    Runtime = "win-x64",
                    Framework = "net10.0",
                    Style = "PortableCompat",
                    OutputDir = outputDirectory,
                    ZipPath = string.Empty,
                    ExePath = executablePath,
                    SignedFiles = 1,
                    SignedFilePaths = new[] { executablePath },
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
                        Name = artifactId,
                        Kind = "Cli",
                        Publish = new
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            Style = "PortableCompat",
                            ExecutableIdentity = artifactId,
                            Sign = new { Enabled = true, SubjectName = realSignature.Subject }
                        }
                    }
                }
            }));
            string checksumsPath = Path.Combine(tempRoot, "SHA256SUMS.txt");
            File.WriteAllLines(checksumsPath, new[] { manifestPath, configurationPath, executablePath }.Select(path =>
                $"{ComputeSha256(path)} *{Path.GetRelativePath(tempRoot, path).Replace('\\', '/')}"));

            var (exitCode, stdout, stderr) = await RunCliAsync(
                repoRoot,
                $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- dotnet release-artifact verify --kind portable-cli --artifact-id \"{artifactId}\" --project-root \"{tempRoot}\" --artifact \"{executablePath}\" --checksums \"{checksumsPath}\" --source-revision {sourceRevision} --manifest \"{manifestPath}\" --config \"{configurationPath}\" --rid win-x64 --framework net10.0 --style PortableCompat --sign-thumbprint {realSignature.Thumbprint} --output json");

            Assert.True(exitCode == 1, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            using JsonDocument document = JsonDocument.Parse(stdout);
            JsonElement root = document.RootElement;
            Assert.False(root.GetProperty("success").GetBoolean());
            Assert.Equal("dotnet.release-artifact.verify", root.GetProperty("command").GetString());
            Assert.Contains(
                PowerForgePortablePayloadInventory.DirectInventorySuffix,
                root.GetProperty("error").GetString(),
                StringComparison.OrdinalIgnoreCase);
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
            if ((Version.TryParse(productVersion, out _) || Version.TryParse(version.FileVersion, out _)) &&
                TryReadPortableSourceRevision(version.ProductVersion, out _))
                return candidate;
        }

        throw new InvalidOperationException("No embedded-signed Windows executable with a numeric version was available for real signature proof.");
    }

    private static string ReadPortableSourceRevision(string path)
    {
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
        return TryReadPortableSourceRevision(version.ProductVersion, out string sourceRevision)
            ? sourceRevision
            : throw new InvalidOperationException("Signed Windows executable does not carry a full source object ID.");
    }

    private static bool TryReadPortableSourceRevision(string? productVersion, out string sourceRevision)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            productVersion ?? string.Empty,
            @"(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{64}|[0-9A-Fa-f]{40})(?![0-9A-Fa-f])");
        sourceRevision = match.Success ? match.Value : string.Empty;
        return match.Success;
    }

}
