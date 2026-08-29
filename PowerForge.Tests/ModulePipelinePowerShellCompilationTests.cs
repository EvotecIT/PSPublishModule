using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class ModulePipelinePowerShellCompilationTests
{
    [WindowsCodeSigningFact]
    public void Public_dsl_produces_signed_zip_with_post_sign_evidence_and_runnable_extraction()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var moduleName = "Random.Signed." + Guid.NewGuid().ToString("N")[..8];
        _ = CreateDslModule(
            testRoot,
            moduleName,
            "function Get-SignedValue { param([int] $Number); [int] $Result = $Number; $Result += 1; return $Result }; Export-ModuleMember -Function Get-SignedValue");
        var staging = Path.Combine(testRoot, "staging");
        var artefactRoot = Path.Combine(testRoot, "artefacts");
        var extractRoot = Path.Combine(testRoot, "extracted");
        var scriptPath = Path.Combine(testRoot, "Invoke-SignedDsl.ps1");
        Directory.CreateDirectory(testRoot);
        var thumbprint = Environment.GetEnvironmentVariable(WindowsCodeSigningFactAttribute.ThumbprintEnvironmentVariable)!;
        File.WriteAllText(scriptPath,
            $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module -Name '{{EscapePowerShellLiteral(typeof(PSPublishModule.InvokeModuleBuildCommand).Assembly.Location)}}' -Force
            Build-Module -Path '{{EscapePowerShellLiteral(testRoot)}}' -ModuleName '{{moduleName}}' `
                -StagingPath '{{EscapePowerShellLiteral(staging)}}' -KeepStaging -SkipInstall -NoInteractive -Quiet -PassThru -Settings {
                    New-ConfigurationBuild -Enable -CompilePowerShell -PowerShellCompilationMode Strict `
                        -PowerShellCompilationTargetFramework net8.0 -PowerShellCompilationAllowUnreviewedDependencies `
                        -SignModule -CertificateThumbprint '{{EscapePowerShellLiteral(thumbprint)}}'
                    New-ConfigurationArtefact -Type Packed -Enable -Path '{{EscapePowerShellLiteral(artefactRoot)}}' `
                        -ArtefactName '{{moduleName}}.zip'
                } | Out-Null
            """);

        try
        {
            var run = RunPowerShellFile(scriptPath);
            Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
            var manifestPath = Path.Combine(staging, moduleName + ".powerforge-compilation.json");
            var manifestJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            manifestJsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            var manifest = JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(
                File.ReadAllText(manifestPath),
                manifestJsonOptions);
            Assert.NotNull(manifest);
            Assert.True(manifest.AuthenticodeSigned);
            Assert.True(manifest.AuthenticodeSignedFiles >= 2);
            Assert.Equal(thumbprint, manifest.SigningCertificateThumbprint, ignoreCase: true);
            var stagingEvidenceRoot = Path.GetDirectoryName(manifestPath)!;
            AssertPortableEvidence(manifest, stagingEvidenceRoot);
            Assert.Equal(64, Assert.IsType<PowerShellCompilationReproductionEvidence>(manifest.Reproduction).EvidenceSha256.Length);

            var zipPath = Path.Combine(artefactRoot, moduleName + ".zip");
            ZipFile.ExtractToDirectory(zipPath, extractRoot);
            var extractedManifest = Directory.EnumerateFiles(extractRoot, moduleName + ".psd1", SearchOption.AllDirectories).Single();
            var extractedAssembly = Directory.EnumerateFiles(extractRoot, moduleName + ".dll", SearchOption.AllDirectories).Single();
            var extractedCompilationManifest = Directory.EnumerateFiles(
                extractRoot,
                moduleName + ".powerforge-compilation.json",
                SearchOption.AllDirectories).Single();
            var extractedCompilationSignature = Directory.EnumerateFiles(
                extractRoot,
                moduleName + ".powerforge-compilation.p7s",
                SearchOption.AllDirectories).Single();
            var extractedEvidence = JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(
                File.ReadAllText(extractedCompilationManifest),
                manifestJsonOptions);
            Assert.NotNull(extractedEvidence);
            var extractedModuleRoot = Path.GetDirectoryName(extractedCompilationManifest)!;
            Assert.All(extractedEvidence.Files, file =>
            {
                Assert.False(string.IsNullOrWhiteSpace(file.RelativePath));
                var extractedPath = Path.Combine(extractedModuleRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(extractedPath), extractedPath);
                Assert.Equal(file.SizeBytes, new FileInfo(extractedPath).Length);
                Assert.Equal(file.Sha256, ComputeFileSha256(extractedPath), ignoreCase: true);
            });
            var extractedPrimary = Path.Combine(
                extractedModuleRoot,
                extractedEvidence.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(extractedEvidence.ArtifactSha256, ComputeFileSha256(extractedPrimary), ignoreCase: true);
            var evidenceSigner = PowerForgePortablePayloadInventoryCms.Verify(
                File.ReadAllBytes(extractedCompilationManifest),
                File.ReadAllBytes(extractedCompilationSignature));
            Assert.True(evidenceSigner.CertificateTrusted);
            Assert.Equal(thumbprint, evidenceSigner.Thumbprint, ignoreCase: true);
            Assert.DoesNotContain(
                Directory.EnumerateFiles(extractRoot, "*", SearchOption.AllDirectories),
                path => path.Contains("powerforge-module-compilation", StringComparison.OrdinalIgnoreCase));
            var invocation = RunPowerShellCommand(
                "$files = @('" + EscapePowerShellLiteral(extractedManifest) + "','" + EscapePowerShellLiteral(extractedAssembly) + "'); " +
                "$invalid = @($files | ForEach-Object { Get-AuthenticodeSignature -FilePath $_ } | Where-Object Status -ne Valid); " +
                "if ($invalid.Count -ne 0) { throw ($invalid | ForEach-Object { $_.StatusMessage } | Out-String) }; " +
                "Import-Module -Name '" + EscapePowerShellLiteral(extractedManifest) + "' -Force; Get-SignedValue 41");
            Assert.Equal(0, invocation.ExitCode);
            Assert.Equal(new[] { "42" }, SplitOutput(invocation.StandardOutput));
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void Public_dsl_switches_arbitrary_script_modules_to_hybrid_or_strict_binary_output()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var hybridName = "Random.Hybrid." + Guid.NewGuid().ToString("N")[..8];
        var strictName = "Random.Strict." + Guid.NewGuid().ToString("N")[..8];
        var rejectedName = "Random.Rejected." + Guid.NewGuid().ToString("N")[..8];
        var hybridRoot = CreateDslModule(
            testRoot,
            hybridName,
            "function Get-ConstantValue { return 42 }; function Get-CurrentYear { return (Get-Date).Year }; Export-ModuleMember -Function Get-ConstantValue, Get-CurrentYear");
        var strictRoot = CreateDslModule(
            testRoot,
            strictName,
            "function Get-StrictValue { param([int] $Number); [int] $Result = $Number; $Result += 1; return $Result }; Export-ModuleMember -Function Get-StrictValue");
        _ = CreateDslModule(
            testRoot,
            rejectedName,
            "function Get-RejectedValue { return (Get-Date).Year }; Export-ModuleMember -Function Get-RejectedValue");
        var hybridStaging = Path.Combine(testRoot, "staging-hybrid");
        var strictStaging = Path.Combine(testRoot, "staging-strict");
        var rejectedStaging = Path.Combine(testRoot, "staging-rejected");
        var artefactRoot = Path.Combine(testRoot, "artefacts");
        var localRepository = Path.Combine(testRoot, "local-repository");
        var rejectionMarker = Path.Combine(testRoot, "strict-rejected.txt");
        var scriptPath = Path.Combine(testRoot, "Invoke-PublicDsl.ps1");
        Directory.CreateDirectory(testRoot);
        File.WriteAllText(scriptPath,
            $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module -Name '{{EscapePowerShellLiteral(typeof(PSPublishModule.InvokeModuleBuildCommand).Assembly.Location)}}' -Force
            Build-Module -Path '{{EscapePowerShellLiteral(testRoot)}}' -ModuleName '{{hybridName}}' `
                -StagingPath '{{EscapePowerShellLiteral(hybridStaging)}}' -KeepStaging -SkipInstall -NoInteractive -Quiet -PassThru -Settings {
                    New-ConfigurationBuild -Enable -CompilePowerShell -PowerShellCompilationMode Hybrid `
                        -PowerShellCompilationTargetFramework net8.0 -PowerShellCompilationIncludeResource README.md `
                        -PowerShellCompilationAllowUnreviewedDependencies
                    New-ConfigurationArtefact -Type Packed -Enable -Path '{{EscapePowerShellLiteral(artefactRoot)}}' `
                        -ArtefactName '{{hybridName}}.zip'
                    New-ConfigurationPublish -Type PowerShellGallery -RepositoryName 'LocalAcceptance' `
                        -Tool ManagedModule -RepositoryUri '{{EscapePowerShellLiteral(localRepository)}}' -Enabled
                } | Out-Null
            Build-Module -Path '{{EscapePowerShellLiteral(testRoot)}}' -ModuleName '{{strictName}}' `
                -StagingPath '{{EscapePowerShellLiteral(strictStaging)}}' -KeepStaging -SkipInstall -NoInteractive -Quiet -PassThru -Settings {
                    New-ConfigurationBuild -Enable -CompilePowerShell -PowerShellCompilationMode Strict `
                        -PowerShellCompilationTargetFramework net8.0 -PowerShellCompilationAllowUnreviewedDependencies `
                        -PowerShellCompilationEmitIrSnapshots
                } | Out-Null
            try {
                Build-Module -Path '{{EscapePowerShellLiteral(testRoot)}}' -ModuleName '{{rejectedName}}' `
                    -StagingPath '{{EscapePowerShellLiteral(rejectedStaging)}}' -KeepStaging -SkipInstall -NoInteractive -Quiet -PassThru -Settings {
                        New-ConfigurationBuild -Enable -CompilePowerShell -PowerShellCompilationMode Strict `
                            -PowerShellCompilationTargetFramework net8.0 -PowerShellCompilationAllowUnreviewedDependencies
                    } | Out-Null
                throw 'Strict compilation unexpectedly accepted unsupported runtime behavior.'
            } catch {
                if ($_.Exception.Message -like '*unexpectedly accepted*') { throw }
                Set-Content -LiteralPath '{{EscapePowerShellLiteral(rejectionMarker)}}' -Value $_.Exception.Message
            }
            """);

        try
        {
            var run = RunPowerShellFile(scriptPath);
            Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
            Assert.True(File.Exists(Path.Combine(hybridStaging, hybridName + ".dll")));
            Assert.True(File.Exists(Path.Combine(hybridStaging, "README.md")));
            Assert.True(File.Exists(Path.Combine(strictStaging, strictName + ".dll")));
            var strictEvidence = ReadCompilationEvidence(Path.Combine(strictStaging, strictName + ".powerforge-compilation.json"));
            Assert.True(strictEvidence.IrSnapshots?.Emitted, JsonSerializer.Serialize(strictEvidence.IrSnapshots));
            Assert.True(
                File.Exists(Path.Combine(strictStaging, strictName + ".powerforge-ir.json")),
                "Staged files: " + string.Join(", ", Directory.EnumerateFiles(strictStaging, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(strictStaging, path))));
            Assert.True(File.Exists(rejectionMarker));
            Assert.Contains("compilation failed", File.ReadAllText(rejectionMarker), StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(rejectedStaging, rejectedName + ".dll")));

            var zipPath = Path.Combine(artefactRoot, hybridName + ".zip");
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(hybridName + ".dll", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("README.md", StringComparison.OrdinalIgnoreCase));
                var evidenceEntry = Assert.Single(archive.Entries, entry => entry.FullName.EndsWith(hybridName + ".powerforge-compilation.json", StringComparison.OrdinalIgnoreCase));
                using var evidenceReader = new StreamReader(evidenceEntry.Open());
                var evidenceJson = evidenceReader.ReadToEnd();
                AssertPortableJson(evidenceJson, testRoot);
                Assert.NotNull(JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(evidenceJson, CreateEvidenceJsonOptions())?.UnitDispositionLedger);
            }
            var repositoryPackage = Path.Combine(localRepository, hybridName + ".1.0.0.nupkg");
            Assert.True(File.Exists(repositoryPackage), repositoryPackage);
            using (var package = ZipFile.OpenRead(repositoryPackage))
            {
                Assert.Contains(package.Entries, entry => entry.FullName.Equals(hybridName + ".dll", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(package.Entries, entry => entry.FullName.Equals(hybridName + ".psm1", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(package.Entries, entry => entry.FullName.Equals("README.md", StringComparison.OrdinalIgnoreCase));
                var evidenceEntry = Assert.Single(package.Entries, entry => entry.FullName.Equals(hybridName + ".powerforge-compilation.json", StringComparison.OrdinalIgnoreCase));
                using var evidenceReader = new StreamReader(evidenceEntry.Open());
                var evidenceJson = evidenceReader.ReadToEnd();
                AssertPortableJson(evidenceJson, testRoot);
                var repositoryEvidence = JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(evidenceJson, CreateEvidenceJsonOptions());
                Assert.NotNull(repositoryEvidence?.UnitDispositionLedger);
                Assert.All(repositoryEvidence!.Files, static file => Assert.False(Path.IsPathRooted(file.Path), file.Path));
                Assert.DoesNotContain(package.Entries, entry => entry.FullName.Contains("powerforge-module-compilation", StringComparison.OrdinalIgnoreCase));
            }

            var hybridInvocation = RunPowerShellCommand(
                $"Import-Module -Name '{EscapePowerShellLiteral(Path.Combine(hybridStaging, hybridName + ".psd1"))}' -Force; " +
                "Get-ConstantValue; [bool](Get-CurrentYear -gt 2000)");
            Assert.Equal(0, hybridInvocation.ExitCode);
            Assert.Equal(new[] { "42", "True" }, SplitOutput(hybridInvocation.StandardOutput));
            var strictInvocation = RunPowerShellCommand(
                $"Import-Module -Name '{EscapePowerShellLiteral(Path.Combine(strictStaging, strictName + ".psd1"))}' -Force; Get-StrictValue 41");
            Assert.Equal(0, strictInvocation.ExitCode);
            Assert.Equal(new[] { "42" }, SplitOutput(strictInvocation.StandardOutput));
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_compiles_an_arbitrary_script_module_and_preserves_source_authoring_shape(bool documentationGate)
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(testRoot, "source");
        var stagingRoot = Path.Combine(testRoot, "staging");
        var artefactRoot = Path.Combine(testRoot, "artefacts");
        var installRoot = Path.Combine(testRoot, "installed");
        var checkpointPfxPath = Path.Combine(testRoot, "checkpoint-authority.pfx");
        const string checkpointPfxPassword = "test-only-compilation-checkpoint";
        var driftMarker = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N") + ".drift");
        const string moduleName = "Generic.Random.Module";
        Directory.CreateDirectory(sourceRoot);
        var sourceManifest = Path.Combine(sourceRoot, moduleName + ".psd1");
        var sourceModule = Path.Combine(sourceRoot, moduleName + ".psm1");
        File.WriteAllText(sourceModule,
            """
            function Get-AdjustedLimit {
                [CmdletBinding()]
                param([double] $Baseline, [double] $Ratio, [double] $Offset)
                $relative = $Baseline * (1.0 + $Ratio)
                $absolute = $Baseline + $Offset
                if ($relative -gt $absolute) { return $relative }
                return $absolute
            }
            Export-ModuleMember -Function Get-AdjustedLimit
            """);
        File.WriteAllText(sourceManifest,
            """
            @{
                RootModule = 'Generic.Random.Module.psm1'
                ModuleVersion = '1.0.0'
                GUID = '15d46115-ed16-4266-bc16-d56255761a40'
                FunctionsToExport = @('Get-AdjustedLimit')
                CmdletsToExport = @()
                AliasesToExport = @()
            }
            """);
        File.WriteAllText(Path.Combine(sourceRoot, "README.md"), "generic module payload");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "Assets"));
        File.WriteAllText(Path.Combine(sourceRoot, "Assets", "metadata.txt"), "selected non-conventional payload");
        File.WriteAllText(Path.Combine(sourceRoot, "other.txt"), "optional payload");
        File.WriteAllText(Path.Combine(sourceRoot, "excluded.txt"), "must not ship");
        File.WriteAllText(Path.Combine(sourceRoot, "post-copy.txt"), "unpacked finalizer payload");

        try
        {
            var requiredModuleSegment = new ConfigurationModuleSegment
            {
                Kind = ModuleDependencyKind.RequiredModule,
                Configuration = new ModuleDependencyConfiguration
                {
                    ModuleName = "Pester",
                    RequiredVersion = "5.7.1"
                }
            };
            var segments = new List<IConfigurationSegment>
            {
                new ConfigurationBuildSegment
                {
                    BuildModule = new BuildModuleConfiguration
                    {
                        PowerShellCompilation = new PowerShellModuleCompilationConfiguration
                        {
                            Enabled = true,
                            Mode = PowerShellCompilationMode.Hybrid,
                            TargetFramework = "net8.0",
                            IncludeResource = new[] { "README.md", "Assets/metadata.txt" },
                            ExcludeResource = new[] { "excluded.txt" },
                            AllowUnreviewedDependencies = true,
                            UseBuildCache = true,
                            EmitIrSnapshots = true
                        }
                    }
                },
                requiredModuleSegment,
                new ConfigurationOptionsSegment
                {
                    Options = new ConfigurationOptions
                    {
                        Signing = new SigningOptionsConfiguration
                        {
                            CertificatePFXPath = checkpointPfxPath,
                            CertificatePFXPassword = checkpointPfxPassword
                        }
                    }
                },
                new ConfigurationActionSegment
                {
                    Configuration = new ModulePipelineActionConfiguration
                    {
                        Name = "Detect finalized payload drift",
                        At = ModulePipelineActionStage.BeforeArtefacts,
                        InlineScript =
                            $"if (Test-Path -LiteralPath '{EscapePowerShellLiteral(driftMarker)}') {{ " +
                            $"Add-Content -LiteralPath (Join-Path $env:POWERFORGE_STAGING_PATH '{moduleName}.dll') -Value 'drift' }}"
                    }
                }
            };
            CreateCheckpointAuthorityPfx(checkpointPfxPath, checkpointPfxPassword);
            if (documentationGate)
            {
                segments.Add(new ConfigurationGateSegment
                {
                    Configuration = new GateConfiguration
                    {
                        Mode = ConfigurationGateMode.Documentation
                    }
                });
                segments.Add(new ConfigurationDocumentationSegment
                {
                    Configuration = new DocumentationConfiguration
                    {
                        Path = "Docs",
                        PathReadme = "README.md"
                    }
                });
                segments.Add(new ConfigurationBuildDocumentationSegment
                {
                    Configuration = new BuildDocumentationConfiguration
                    {
                        Enable = true,
                        GenerateExternalHelp = false,
                        SyncExternalHelpToProjectRoot = false
                    }
                });
            }
            else
            {
                segments.Add(new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Packed,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = artefactRoot,
                        ArtefactName = moduleName + ".zip"
                    }
                });
                segments.Add(new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Unpacked,
                    Configuration = new ArtefactConfiguration
                    {
                        Enabled = true,
                        Path = Path.Combine(artefactRoot, "unpacked"),
                        DestinationFilesRelative = true,
                        FilesOutput = new[]
                        {
                            new ArtefactCopyMapping
                            {
                                Source = "post-copy.txt",
                                Destination = Path.Combine(moduleName, "post-copy.txt")
                            }
                        }
                    }
                });
            }

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = sourceRoot,
                    StagingPath = stagingRoot,
                    Version = "1.0.0",
                    KeepStaging = true
                },
                Install = new ModulePipelineInstallOptions
                {
                    Enabled = !documentationGate,
                    Roots = new[] { installRoot }
                },
                Segments = segments.ToArray()
            };

            var result = new ModulePipelineRunner(new NullLogger()).Run(spec);

            var compilation = Assert.IsType<PowerShellModuleCompilationResult>(result.PowerShellCompilationResult);
            Assert.True(
                compilation.CompiledUnits > 0,
                $"Expected emitted units. analyzed={compilation.AnalyzedUnits}, emitted={compilation.EmittedUnits}, " +
                $"runtimeRouted={compilation.RuntimeRoutedUnits}, fallback={compilation.FallbackUnits}, " +
                $"shapedFallback={compilation.ShapedFallbackUnits}, coverage={compilation.CoveragePercentage}.");
            Assert.True(compilation.CoveragePercentage > 0);
            Assert.True(File.Exists(Path.Combine(stagingRoot, moduleName + ".dll")));
            Assert.True(File.Exists(Path.Combine(stagingRoot, moduleName + ".psd1")));
            Assert.True(File.Exists(Path.Combine(stagingRoot, "README.md")));
            Assert.True(File.Exists(Path.Combine(stagingRoot, "Assets", "metadata.txt")));
            Assert.False(File.Exists(Path.Combine(stagingRoot, "excluded.txt")));
            Assert.True(File.Exists(Path.Combine(stagingRoot, moduleName + ".powerforge-module-compilation.json")));
            Assert.True(File.Exists(Path.Combine(stagingRoot, moduleName + ".powerforge-compilation.json")));
            Assert.True(File.Exists(Path.Combine(stagingRoot, moduleName + ".powerforge-ir.json")));
            Assert.True(File.Exists(Path.Combine(stagingRoot, moduleName + ".powerforge-compilation.p7s")));
            AssertPortableEvidence(
                ReadCompilationEvidence(Path.Combine(stagingRoot, moduleName + ".powerforge-compilation.json")),
                stagingRoot);
            var checkpointSignaturePath = Path.Combine(stagingRoot, moduleName + ".powerforge-module-compilation.p7s");
            Assert.True(File.Exists(checkpointSignaturePath));
            using (var checkpointJson = JsonDocument.Parse(File.ReadAllText(
                       Path.Combine(stagingRoot, moduleName + ".powerforge-module-compilation.json"))))
            {
                Assert.Equal(64, checkpointJson.RootElement.GetProperty("stagingInputSha256").GetString()!.Length);
            }
            Assert.Contains("Generic.Random.Module.psm1", File.ReadAllText(sourceManifest), StringComparison.Ordinal);

            if (!documentationGate)
            {
                var zipPath = Path.Combine(artefactRoot, moduleName + ".zip");
                Assert.True(File.Exists(zipPath));
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(moduleName + ".dll", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("README.md", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("Assets/metadata.txt", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(moduleName + ".powerforge-compilation.json", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(moduleName + ".powerforge-compilation.p7s", StringComparison.OrdinalIgnoreCase));
                    Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("powerforge-module-compilation", StringComparison.OrdinalIgnoreCase));
                }
                var unsignedExtractRoot = Path.Combine(testRoot, "unsigned-extracted");
                ZipFile.ExtractToDirectory(zipPath, unsignedExtractRoot);
                var unsignedCompilationManifest = Directory.EnumerateFiles(
                    unsignedExtractRoot,
                    moduleName + ".powerforge-compilation.json",
                    SearchOption.AllDirectories).Single();
                var unsignedEvidenceOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                unsignedEvidenceOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                var unsignedEvidence = JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(
                    File.ReadAllText(unsignedCompilationManifest),
                    unsignedEvidenceOptions)!;
                var unsignedModuleRoot = Path.GetDirectoryName(unsignedCompilationManifest)!;
                Assert.All(unsignedEvidence.Files, file =>
                {
                    var path = Path.Combine(unsignedModuleRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    Assert.Equal(file.Sha256, ComputeFileSha256(path), ignoreCase: true);
                });
                var unsignedEvidenceSignature = Path.Combine(unsignedModuleRoot, moduleName + ".powerforge-compilation.p7s");
                _ = PowerForgePortablePayloadInventoryCms.Verify(
                    File.ReadAllBytes(unsignedCompilationManifest),
                    File.ReadAllBytes(unsignedEvidenceSignature));
                var installedPath = Assert.Single(Assert.IsType<ModuleInstallerResult>(result.InstallResult).InstalledPaths);
                Assert.True(File.Exists(Path.Combine(installedPath, moduleName + ".dll")));
                Assert.True(File.Exists(Path.Combine(installedPath, "README.md")));
                Assert.True(File.Exists(Path.Combine(installedPath, "Assets", "metadata.txt")));
                AssertPortableEvidence(
                    ReadCompilationEvidence(Path.Combine(installedPath, moduleName + ".powerforge-compilation.json")),
                    installedPath);
                var unpackedModule = Assert.Single(
                    result.ArtefactResults
                        .Where(static artefact => artefact.Type == ArtefactType.Unpacked)
                        .SelectMany(static artefact => artefact.Modules),
                    static module => module.IsMainModule);
                var unpackedEvidence = ReadCompilationEvidence(
                    Path.Combine(unpackedModule.Path, moduleName + ".powerforge-compilation.json"));
                AssertPortableEvidence(unpackedEvidence, unpackedModule.Path);
                Assert.Contains(unpackedEvidence.Files, static file =>
                    file.RelativePath == "post-copy.txt" && file.Role == "FinalizedPayload");

                var originalAssembly = File.ReadAllBytes(Path.Combine(stagingRoot, moduleName + ".dll"));
                spec.Build.ReuseStaging = true;
                spec.Build.SkipDotNetBuild = true;
                var reused = new ModulePipelineRunner(new NullLogger()).Run(spec);
                var reusedCompilation = Assert.IsType<PowerShellModuleCompilationResult>(reused.PowerShellCompilationResult);
                Assert.Equal(compilation.CompiledUnits, reusedCompilation.CompiledUnits);
                Assert.Equal(originalAssembly, File.ReadAllBytes(Path.Combine(stagingRoot, moduleName + ".dll")));
                Assert.Contains(moduleName + ".dll", File.ReadAllText(Path.Combine(stagingRoot, moduleName + ".psd1")), StringComparison.Ordinal);

                var checkpointSigningSegment = Assert.IsType<ConfigurationOptionsSegment>(segments[2]);
                var checkpointSigning = checkpointSigningSegment.Options.Signing;
                checkpointSigningSegment.Options.Signing = null;
                var unsignedReuseError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("requires configured signing", unsignedReuseError.Message, StringComparison.OrdinalIgnoreCase);
                checkpointSigningSegment.Options.Signing = checkpointSigning;

                var compilationConfiguration = Assert.IsType<PowerShellModuleCompilationConfiguration>(
                    Assert.IsType<ConfigurationBuildSegment>(segments[0]).BuildModule.PowerShellCompilation);
                compilationConfiguration.IncludeResource = new[] { "README.md", "Assets/metadata.txt", "other.txt" };
                var contractError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("compilation contract", contractError.Message, StringComparison.Ordinal);
                compilationConfiguration.IncludeResource = new[] { "README.md", "Assets/metadata.txt" };

                compilationConfiguration.EmitIrSnapshots = false;
                var irContractError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("compilation contract", irContractError.Message, StringComparison.Ordinal);
                compilationConfiguration.EmitIrSnapshots = true;

                spec.Build.Version = "2.0.0";
                var releasePlanError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("compilation contract", releasePlanError.Message, StringComparison.Ordinal);
                spec.Build.Version = "1.0.0";

                requiredModuleSegment.Configuration.RequiredVersion = "6.0.1";
                var requiredModuleError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("compilation contract", requiredModuleError.Message, StringComparison.Ordinal);
                requiredModuleSegment.Configuration.RequiredVersion = "5.7.1";

                var sourceText = File.ReadAllText(sourceModule);
                File.WriteAllText(sourceModule, sourceText + Environment.NewLine + "# changed compiler input");
                var sourceInputError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("compilation contract", sourceInputError.Message, StringComparison.Ordinal);
                File.WriteAllText(sourceModule, sourceText);

                var resourcePath = Path.Combine(sourceRoot, "Assets", "metadata.txt");
                var resourceText = File.ReadAllText(resourcePath);
                File.WriteAllText(resourcePath, resourceText + " changed");
                var resourceInputError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("compilation contract", resourceInputError.Message, StringComparison.Ordinal);
                File.WriteAllText(resourcePath, resourceText);

                var checkpointPath = Path.Combine(stagingRoot, moduleName + ".powerforge-module-compilation.json");
                var checkpointText = File.ReadAllText(checkpointPath);
                var checkpointSignature = File.ReadAllBytes(checkpointSignaturePath);
                File.WriteAllText(checkpointPath, checkpointText.Replace("\"allowUnreviewedDependencies\": true", "\"allowUnreviewedDependencies\": false", StringComparison.OrdinalIgnoreCase));
                File.WriteAllText(checkpointSignaturePath, ComputeFileSha256(checkpointPath));
                var checkpointError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("signature", checkpointError.Message, StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(checkpointPath, checkpointText);
                File.WriteAllBytes(checkpointSignaturePath, checkpointSignature);

                var unexpectedPayload = Path.Combine(stagingRoot, "unexpected.txt");
                File.WriteAllText(unexpectedPayload, "not checkpointed");
                var payloadError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("payload", payloadError.Message, StringComparison.Ordinal);
                File.Delete(unexpectedPayload);

                var unselectedSourcePath = Path.Combine(sourceRoot, "other.txt");
                var unselectedSourceText = File.ReadAllText(unselectedSourcePath);
                File.WriteAllText(unselectedSourcePath, unselectedSourceText + " changed");
                var sourceTreeError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("compilation contract", sourceTreeError.Message, StringComparison.OrdinalIgnoreCase);
                File.WriteAllText(unselectedSourcePath, unselectedSourceText);

                Directory.CreateDirectory(Path.GetDirectoryName(driftMarker)!);
                File.WriteAllText(driftMarker, "mutate finalized payload");
                var driftError = Assert.Throws<InvalidOperationException>(() => new ModulePipelineRunner(new NullLogger()).Run(spec));
                Assert.Contains("changed after checkpoint finalization", driftError.Message, StringComparison.OrdinalIgnoreCase);
            }

            var escapedManifest = result.BuildResult.ManifestPath.Replace("'", "''", StringComparison.Ordinal);
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add($"Import-Module -Name '{escapedManifest}' -Force; Get-AdjustedLimit 100 0.2 30");
            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(60_000), "Generated module invocation did not finish.");
            Assert.True(process.ExitCode == 0, error + Environment.NewLine + output);
            Assert.Contains("130", output, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(driftMarker)) File.Delete(driftMarker);
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void Public_pipeline_rejects_case_colliding_payload_on_case_sensitive_file_systems()
    {
        if (OperatingSystem.IsWindows()) return;
        var testRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        var moduleName = "Random.CaseCollision." + Guid.NewGuid().ToString("N")[..8];
        var sourceRoot = CreateDslModule(
            testRoot,
            moduleName,
            "function Get-Value { return 42 }; Export-ModuleMember -Function Get-Value");
        var assetsRoot = Path.Combine(sourceRoot, "Assets");
        Directory.CreateDirectory(assetsRoot);
        File.WriteAllText(Path.Combine(assetsRoot, "Icon.json"), "upper-case resource");
        File.WriteAllText(Path.Combine(assetsRoot, "icon.json"), "lower-case resource");

        try
        {
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = sourceRoot,
                    StagingPath = Path.Combine(testRoot, "staging"),
                    Version = "1.0.0",
                    KeepStaging = true
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationBuildSegment
                    {
                        BuildModule = new BuildModuleConfiguration
                        {
                            PowerShellCompilation = new PowerShellModuleCompilationConfiguration
                            {
                                Enabled = true,
                                Mode = PowerShellCompilationMode.Hybrid,
                                TargetFramework = "net8.0",
                                IncludeResource = new[] { "Assets/Icon.json", "Assets/icon.json" },
                                AllowUnreviewedDependencies = true
                            }
                        }
                    }
                }
            };

            var exception = Assert.Throws<InvalidOperationException>(
                () => new ModulePipelineRunner(new NullLogger()).Run(spec));
            Assert.Contains("case-colliding", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
        }
    }

    private static string CreateDslModule(string root, string moduleName, string source)
    {
        var moduleRoot = Path.Combine(root, moduleName);
        Directory.CreateDirectory(moduleRoot);
        File.WriteAllText(Path.Combine(moduleRoot, moduleName + ".psm1"), source);
        File.WriteAllText(Path.Combine(moduleRoot, moduleName + ".psd1"),
            $$"""
            @{
                RootModule = '{{moduleName}}.psm1'
                ModuleVersion = '1.0.0'
                GUID = '{{Guid.NewGuid()}}'
                Author = 'PowerForge acceptance'
                Description = 'Random compiler acceptance fixture.'
                FunctionsToExport = @()
                CmdletsToExport = @()
                AliasesToExport = @()
            }
            """);
        File.WriteAllText(Path.Combine(moduleRoot, "README.md"), "public DSL payload");
        return moduleRoot;
    }

    private static void CreateCheckpointAuthorityPfx(string path, string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=PowerForge compilation checkpoint test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.3") },
            critical: true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunPowerShellFile(string path)
        => RunProcess("-File", path);

    private static (int ExitCode, string StandardOutput, string StandardError) RunPowerShellCommand(string command)
        => RunProcess("-Command", command);

    private static (int ExitCode, string StandardOutput, string StandardError) RunProcess(string argument, string value)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add(value);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(180_000), "PowerShell acceptance process did not finish.");
        return (process.ExitCode, output, error);
    }

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string[] SplitOutput(string value)
        => value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string ComputeFileSha256(string path)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static PowerShellCompilationArtifactManifest ReadCompilationEvidence(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return JsonSerializer.Deserialize<PowerShellCompilationArtifactManifest>(File.ReadAllText(path), options)
               ?? throw new InvalidOperationException("Compilation evidence could not be read.");
    }

    private static JsonSerializerOptions CreateEvidenceJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        return options;
    }

    private static void AssertPortableJson(string json, string producerRoot)
    {
        using var document = JsonDocument.Parse(json);
        var leaks = new List<string>();
        CollectProducerPathLeaks(document.RootElement, "$", producerRoot, leaks);
        Assert.True(
            leaks.Count == 0,
            "Portable compiler evidence retained producer paths:" + Environment.NewLine +
            string.Join(Environment.NewLine, leaks));
    }

    private static void CollectProducerPathLeaks(
        JsonElement element,
        string jsonPath,
        string producerRoot,
        ICollection<string> leaks)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectProducerPathLeaks(property.Value, jsonPath + "." + property.Name, producerRoot, leaks);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    CollectProducerPathLeaks(item, jsonPath + "[" + index++ + "]", producerRoot, leaks);
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (value?.Contains(producerRoot, StringComparison.OrdinalIgnoreCase) == true)
                    leaks.Add(jsonPath + " = " + value);
                break;
        }
    }

    private static void AssertPortableEvidence(PowerShellCompilationArtifactManifest manifest, string moduleRoot)
    {
        Assert.False(Path.IsPathRooted(manifest.ArtifactPath), manifest.ArtifactPath);
        Assert.False(Path.IsPathRooted(manifest.SourcePath), manifest.SourcePath);
        Assert.All(manifest.SourceFiles, static path => Assert.False(Path.IsPathRooted(path), path));
        Assert.NotNull(manifest.UnitDispositionLedger);
        Assert.False(string.IsNullOrWhiteSpace(manifest.Reproduction?.UnitDispositionLedgerSha256));
        var primaryPath = Path.Combine(moduleRoot, manifest.ArtifactRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(manifest.ArtifactSha256, ComputeFileSha256(primaryPath), ignoreCase: true);
        Assert.All(manifest.Files, file =>
        {
            Assert.False(Path.IsPathRooted(file.Path), file.Path);
            Assert.False(string.IsNullOrWhiteSpace(file.RelativePath));
            var deliveredPath = Path.Combine(moduleRoot, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(deliveredPath), deliveredPath);
            Assert.Equal(file.Sha256, ComputeFileSha256(deliveredPath), ignoreCase: true);
        });
    }

}
