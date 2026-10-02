using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerMacAppPackageTests
{
    [Fact]
    public void MacDesktopExample_ValidatesAndDeserializes()
    {
        string root = FindSourceRoot();
        string schemaPath = Path.Combine(root, "Schemas", "powerforge.dotnetpublish.schema.json");
        string examplePath = Path.Combine(root, "Module", "Examples", "DotNetPublish", "Example.MacDesktopApp.json");
        JsonSchema schema = JsonSchema.FromText(File.ReadAllText(schemaPath));
        JsonNode document = JsonNode.Parse(File.ReadAllText(examplePath))!;
        EvaluationResults evaluation = schema.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        serializerOptions.Converters.Add(new JsonStringEnumConverter());
        DotNetPublishSpec? spec = JsonSerializer.Deserialize<DotNetPublishSpec>(File.ReadAllText(examplePath), serializerOptions);

        Assert.True(evaluation.IsValid, evaluation.ToString());
        Assert.NotNull(spec);
        DotNetPublishInstaller installer = Assert.Single(spec.Installers);
        Assert.Equal(DotNetPublishInstallerKind.MacApp, installer.Kind);
        Assert.Equal("com.example.sample-studio", installer.MacApp!.BundleIdentifier);
    }

    [Fact]
    public void Plan_AddsMacAppPackageStepAfterPublish()
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "osx-arm64");

            DotNetPublishPlan plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            DotNetPublishStep step = Assert.Single(plan.Steps, candidate => candidate.Kind == DotNetPublishStepKind.MacAppPackage);
            DotNetPublishStepKind[] kinds = plan.Steps.Select(candidate => candidate.Kind).ToArray();

            Assert.True(Array.IndexOf(kinds, DotNetPublishStepKind.MacAppPackage) > Array.IndexOf(kinds, DotNetPublishStepKind.Publish));
            Assert.True(Array.IndexOf(kinds, DotNetPublishStepKind.Manifest) > Array.IndexOf(kinds, DotNetPublishStepKind.MacAppPackage));
            Assert.DoesNotContain(plan.Steps, candidate => candidate.Kind == DotNetPublishStepKind.MsiPrepare);
            Assert.Equal("studio.macapp", step.InstallerId);
            Assert.EndsWith("OfficeIMO Studio-0.1.0-osx-arm64.zip", step.InstallerOutputPath, StringComparison.Ordinal);
            Assert.Equal(DotNetPublishInstallerKind.MacApp, Assert.Single(plan.Installers).Kind);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_StoreInstallerUsesPkgAndRequiresDistributionSigning()
    {
        string root = CreateTempRoot();
        try
        {
            var spec = CreateSpec(root, "osx-arm64");
            var installer = Assert.Single(spec.Installers);
            var mac = installer.MacApp!;
            mac.AppStore = true;
            Assert.Throws<ArgumentException>(() => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));
            mac.CodesignIdentity = "Apple Distribution: Example (ABCDE12345)";
            mac.TeamId = "ABCDE12345";
            mac.InstallerSigningIdentity = "3rd Party Mac Developer Installer: Example (ABCDE12345)";
            mac.EntitlementsPath = "AppStore.entitlements";
            var plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            Assert.EndsWith(".pkg", Assert.Single(plan.Steps, step => step.Kind == DotNetPublishStepKind.MacAppPackage).InstallerOutputPath);
            installer.OutputName = "explicit.pkg";
            plan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null);
            Assert.EndsWith("explicit.pkg", Assert.Single(plan.Steps, step => step.Kind == DotNetPublishStepKind.MacAppPackage).InstallerOutputPath);
            installer.OutputName = "wrong.zip";
            Assert.Throws<ArgumentException>(() => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void StorePayloadRequiresSandboxAndSingleFileAndPreservesResourceNotices()
    {
        string root = CreateTempRoot();
        try
        {
            string code = Directory.CreateDirectory(Path.Combine(root, "Contents", "MacOS")).FullName;
            string resources = Directory.CreateDirectory(Path.Combine(root, "Contents", "Resources")).FullName;
            File.WriteAllBytes(Path.Combine(code, "OfficeIMO.Studio"), new byte[] { 0xcf, 0xfa, 0xed, 0xfe });
            Directory.CreateDirectory(Path.Combine(code, "Licenses"));
            File.WriteAllText(Path.Combine(code, "Licenses", "NOTICE.txt"), "Copyright notice");
            var mac = CreateMacOptions();
            mac.EntitlementsPath = "AppStore.entitlements";
            File.WriteAllText(Path.Combine(root, mac.EntitlementsPath), "<plist><dict><key>com.apple.security.app-sandbox</key><false/></dict></plist>");
            Assert.Throws<InvalidOperationException>(() => DotNetPublishPipelineRunner.PrepareMacStorePayload(mac, code, resources, root));
            File.WriteAllText(Path.Combine(root, mac.EntitlementsPath), "<plist><dict><key>com.apple.security.app-sandbox</key><true/></dict></plist>");
            File.WriteAllText(Path.Combine(code, "managed.dll"), "managed assembly");
            Assert.Throws<InvalidOperationException>(() => DotNetPublishPipelineRunner.PrepareMacStorePayload(mac, code, resources, root));
            File.Delete(Path.Combine(code, "managed.dll"));
            DotNetPublishPipelineRunner.PrepareMacStorePayload(mac, code, resources, root);
            Assert.True(File.Exists(Path.Combine(code, "OfficeIMO.Studio")));
            Assert.Equal("Copyright notice", File.ReadAllText(Path.Combine(resources, "Licenses", "NOTICE.txt")));
        }
        finally { TryDelete(root); }
    }

    [Theory]
    [InlineData("ABCDE12345", "2099-01-01T00:00:00Z", false, true)]
    [InlineData("OTHER12345", "2099-01-01T00:00:00Z", false, false)]
    [InlineData("ABCDE12345", "2000-01-01T00:00:00Z", false, false)]
    [InlineData("ABCDE12345", "2099-01-01T00:00:00Z", true, false)]
    public void StoreProfileRejectsWrongTeamExpiredAndDevelopmentProfiles(string team, string expiry, bool development, bool valid)
    {
        var mac = CreateMacOptions();
        mac.TeamId = "ABCDE12345";
        var profile = System.Xml.Linq.XDocument.Parse($"<plist><dict><key>TeamIdentifier</key><array><string>{team}</string></array><key>ExpirationDate</key><date>{expiry}</date><key>Entitlements</key><dict><key>com.apple.application-identifier</key><string>{team}.{mac.BundleIdentifier}</string><key>get-task-allow</key><{(development ? "true" : "false")}/></dict></dict></plist>");
        if (valid) DotNetPublishPipelineRunner.ValidateMacStoreProvisioningProfile(mac, profile);
        else Assert.Throws<InvalidOperationException>(() => DotNetPublishPipelineRunner.ValidateMacStoreProvisioningProfile(mac, profile));
    }

    [Fact]
    public void StoreProfileRejectsAnotherCertificateWithTheSameSubjectAndTeam()
    {
        using var key = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=Apple Distribution: Example, OU=ABCDE12345", key,
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var authorized = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var replacement = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        Assert.Equal(authorized.Subject, replacement.Subject);
        var mac = CreateMacOptions();
        mac.TeamId = "ABCDE12345";
        var profile = System.Xml.Linq.XDocument.Parse($"<plist><dict><key>TeamIdentifier</key><array><string>{mac.TeamId}</string></array><key>ExpirationDate</key><date>2099-01-01T00:00:00Z</date><key>Entitlements</key><dict><key>com.apple.application-identifier</key><string>{mac.TeamId}.{mac.BundleIdentifier}</string></dict><key>DeveloperCertificates</key><array><data>{Convert.ToBase64String(authorized.RawData)}</data></array></dict></plist>");
        DotNetPublishPipelineRunner.ValidateMacStoreProvisioningProfile(mac, profile);
        DotNetPublishPipelineRunner.ValidateMacStoreProvisioningCertificate(profile, authorized.RawData);
        Assert.Throws<InvalidOperationException>(() => DotNetPublishPipelineRunner.ValidateMacStoreProvisioningCertificate(profile, replacement.RawData));
        profile.Root!.Element("dict")!.Elements("array").Last().RemoveNodes();
        Assert.Throws<InvalidOperationException>(() => DotNetPublishPipelineRunner.ValidateMacStoreProvisioningCertificate(profile, authorized.RawData));
    }

    [Fact]
    public void Plan_RejectsMacAppForNonMacRuntime()
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "linux-x64");

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));

            Assert.Contains("osx-*", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("invalid", "BundleIdentifier")]
    [InlineData("com.evotec.officeimo.studio", "../OfficeIMO.Studio")]
    public void Plan_RejectsUnsafeMacMetadata(string bundleIdentifier, string executable)
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "osx-arm64");
            DotNetPublishMacAppOptions options = Assert.Single(spec.Installers).MacApp!;
            options.BundleIdentifier = bundleIdentifier;
            options.Executable = executable;

            Assert.Throws<ArgumentException>(() => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_RejectsNestedMacExecutablePath()
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "osx-arm64");
            Assert.Single(spec.Installers).MacApp!.Executable = "bin/OfficeIMO.Studio";

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));

            Assert.Contains("file name", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_RejectsDeveloperIdentityUntilNotarizationIsOwned()
    {
        string root = CreateTempRoot();
        try
        {
            DotNetPublishSpec spec = CreateSpec(root, "osx-arm64");
            Assert.Single(spec.Installers).MacApp!.CodesignIdentity = "Developer ID Application: Example Corp (ABCDE12345)";

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new DotNetPublishPipelineRunner(new NullLogger()).Plan(spec, null));

            Assert.Contains("notarization", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Plan_PreservesDevelopmentSigningAndRejectsAdHocOrStoreMixing()
    {
        string root = CreateTempRoot();
        try
        {
            var spec = CreateSpec(root, "osx-arm64");
            var mac = Assert.Single(spec.Installers).MacApp!;
            mac.DevelopmentOnly = true;
            mac.TeamId = "ABCDE12345";
            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            Assert.Throws<ArgumentException>(() => runner.Plan(spec, null));
            mac.CodesignIdentity = "Apple Development: Example (ABCDEFGHIJ)";
            var planned = Assert.Single(runner.Plan(spec, null).Installers).MacApp!;
            Assert.True(planned.DevelopmentOnly);
            Assert.Equal(mac.CodesignIdentity, planned.CodesignIdentity);
            Assert.Equal(mac.TeamId, planned.TeamId);
            mac.TeamId = null;
            Assert.Throws<ArgumentException>(() => runner.Plan(spec, null));
            mac.TeamId = "ABCDE12345";
            mac.AppStore = true;
            Assert.Throws<ArgumentException>(() => runner.Plan(spec, null));
        }
        finally { TryDelete(root); }
    }

    [Fact]
    public void InfoPlist_ContainsStableIdentityAndDocumentContracts()
    {
        DotNetPublishMacAppOptions options = CreateMacOptions();

        string plist = DotNetPublishPipelineRunner.BuildMacInfoPlist(options, "OfficeIMO.Studio", "AppIcon.icns");

        Assert.Contains("<string>com.evotec.officeimo.studio</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>CFBundleShortVersionString</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>0.1.0</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>NSHighResolutionCapable</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>pdf</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>docx</string>", plist, StringComparison.Ordinal);
        Assert.Contains("<string>AppIcon.icns</string>", plist, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-", false)]
    [InlineData("Developer ID Application: Example Corp (ABCDE12345)", true)]
    public void HardenedRuntime_IsLimitedToAppleIdentitySigning(string identity, bool expected)
    {
        DotNetPublishMacAppOptions options = CreateMacOptions();
        options.CodesignIdentity = identity;

        Assert.Equal(expected, DotNetPublishPipelineRunner.ShouldEnableMacHardenedRuntime(options));
    }

    [Fact]
    public void BuildMacAppPackage_OnMac_CreatesSignedInspectableBundleAndZip()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        string root = CreateTempRoot();
        try
        {
            string publish = Directory.CreateDirectory(Path.Combine(root, "publish")).FullName;
            string executable = Path.Combine(publish, "OfficeIMO.Studio");
            File.WriteAllText(executable, "#!/bin/sh\nexit 0\n");
            Run("/bin/chmod", root, "0755", executable);
            string package = Path.Combine(root, "artifacts", "OfficeIMO-Studio.zip");
            var plan = new DotNetPublishPlan
            {
                UseControlledSourceProvenance = true,
                ProjectRoot = root,
                Installers = new[]
                {
                    new DotNetPublishInstallerPlan
                    {
                        Id = "studio.macapp",
                        Kind = DotNetPublishInstallerKind.MacApp,
                        PrepareFromTarget = "studio",
                        MacApp = CreateMacOptions()
                    }
                }
            };
            var source = new DotNetPublishArtefactResult
            {
                Target = "studio",
                Runtime = "osx-arm64",
                Framework = "net10.0",
                Style = DotNetPublishStyle.PortableCompat,
                OutputDir = publish
            };
            var step = new DotNetPublishStep
            {
                Key = "macapp.package:studio.macapp",
                Kind = DotNetPublishStepKind.MacAppPackage,
                InstallerId = "studio.macapp",
                TargetName = "studio",
                Runtime = "osx-arm64",
                Framework = "net10.0",
                Style = DotNetPublishStyle.PortableCompat,
                InstallerOutputPath = package
            };

            DotNetPublishArtefactResult result = new DotNetPublishPipelineRunner(new NullLogger())
                .BuildMacAppPackage(plan, new[] { source }, step);

            string app = Path.Combine(root, "artifacts", "OfficeIMO Studio.app");
            Assert.True(File.Exists(package));
            Assert.True(File.Exists(Path.Combine(app, "Contents", "Info.plist")));
            Assert.Equal(DotNetPublishArtefactCategory.Installer, result.Category);
            Run("/usr/bin/codesign", root, "--verify", "--deep", "--strict", app);
            string identifier = Run("/usr/libexec/PlistBuddy", root, "-c", "Print :CFBundleIdentifier", Path.Combine(app, "Contents", "Info.plist"));
            Assert.Equal("com.evotec.officeimo.studio", identifier.Trim());
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static DotNetPublishSpec CreateSpec(string root, string runtime)
    {
        string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "Studio")).FullName;
        string projectPath = Path.Combine(projectDirectory, "Studio.csproj");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        return new DotNetPublishSpec
        {
            DotNet = new DotNetPublishDotNetOptions { ProjectRoot = root },
            Targets = new[]
            {
                new DotNetPublishTarget
                {
                    Name = "studio",
                    ProjectPath = projectPath,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0",
                        Runtimes = new[] { runtime },
                        Style = DotNetPublishStyle.PortableCompat
                    }
                }
            },
            Installers = new[]
            {
                new DotNetPublishInstaller
                {
                    Id = "studio.macapp",
                    Kind = DotNetPublishInstallerKind.MacApp,
                    PrepareFromTarget = "studio",
                    Runtimes = new[] { runtime },
                    OutputPath = "artifacts",
                    MacApp = CreateMacOptions()
                }
            }
        };
    }

    private static DotNetPublishMacAppOptions CreateMacOptions() => new()
    {
        BundleIdentifier = "com.evotec.officeimo.studio",
        BundleName = "OfficeIMO Studio",
        Version = "0.1.0",
        BuildNumber = "1",
        Executable = "OfficeIMO.Studio",
        MinimumSystemVersion = "13.0",
        Category = "public.app-category.productivity",
        Copyright = "Copyright © Evotec",
        DocumentExtensions = new[] { "pdf", "docx" },
        CodesignIdentity = "-",
        HardenedRuntime = true,
        Timestamp = false
    };

    private static string Run(string fileName, string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    private static string CreateTempRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "PowerForgeMacAppTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindSourceRoot([CallerFilePath] string sourcePath = "")
        => Directory.GetParent(Path.GetDirectoryName(sourcePath)!)!.FullName;

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }
}
