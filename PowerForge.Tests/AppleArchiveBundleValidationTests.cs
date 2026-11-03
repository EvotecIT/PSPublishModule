using System.Xml.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed class AppleArchiveBundleValidationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadArchiveAsync_verifies_final_app_and_embedded_extension_before_export(bool macLayout)
    {
        using var fixture = new Fixture(macLayout);
        var runner = new InspectionRunner { Universal = macLayout };
        var result = await new AppleAppArchiveService(runner).UploadArchiveAsync(fixture.Request());

        Assert.True(result.Succeeded);
        Assert.Equal(1, runner.Exports);
        Assert.Contains("Example.app", runner.VerifiedBundles);
        Assert.Contains("Widget.appex", runner.VerifiedBundles);
    }

    [Theory]
    [InlineData("signature", "signature verification failed")]
    [InlineData("team", "signed team")]
    [InlineData("team-other-architecture", "signed team")]
    [InlineData("bundle", "missing required embedded bundle")]
    [InlineData("entitlement", "missing required signed entitlement")]
    [InlineData("entitlement-other-architecture", "missing required signed entitlement")]
    [InlineData("malformed", "signed entitlements are malformed")]
    public Task UploadArchiveAsync_blocks_invalid_signed_bundle_evidence_before_export(string defect, string expected)
        => AssertRejectedBeforeExportAsync(defect, expected);

    [UnixSourcePathFact]
    public Task UploadArchiveAsync_rejects_resource_links_outside_the_signed_app()
        => AssertRejectedBeforeExportAsync("external-link", "must remain inside");

    private static async Task AssertRejectedBeforeExportAsync(string defect, string expected)
    {
        using var fixture = new Fixture(false);
        var runner = new InspectionRunner { Defect = defect };
        var request = fixture.Request();
        if (defect == "bundle")
            request.RequiredEmbeddedBundleIds = ["com.example.missing"];
        if (defect == "external-link")
            fixture.AddExternalResourceLink();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AppleAppArchiveService(runner).UploadArchiveAsync(request));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runner.Exports);
        Assert.False(Directory.Exists(request.ExportPath));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        private readonly string _archive;

        internal Fixture(bool macLayout)
        {
            _archive = Path.Combine(_root, "Example.xcarchive");
            var app = Path.Combine(_archive, "Products", "Applications", "Example.app");
            var content = macLayout ? Path.Combine(app, "Contents") : app;
            var extension = Path.Combine(content, "PlugIns", "Widget.appex");
            Directory.CreateDirectory(extension);
            WritePlist(Path.Combine(content, "Info.plist"), "com.example.app");
            WritePlist(Path.Combine(extension, "Info.plist"), "com.example.widget");
        }

        internal AppleAppArchiveUploadRequest Request() => new()
        {
            ArchivePath = _archive,
            ExportPath = Path.Combine(_root, "export"),
            BundleId = "com.example.app",
            MarketingVersion = "1.2",
            BuildNumber = "3",
            TeamId = "TEAM123456",
            RequireArchiveAttestation = true,
            RequiredEmbeddedBundleIds = ["com.example.widget"],
            RequiredEntitlementKeys = ["com.apple.security.application-groups"],
            RequireTrustedSystemTools = true
        };

        internal void AddExternalResourceLink()
        {
            var outside = Path.Combine(_root, "outside.txt");
            File.WriteAllText(outside, "outside the signed app");
            File.CreateSymbolicLink(Path.Combine(_archive, "Products", "Applications", "Example.app", "external.txt"), "../../../../outside.txt");
        }

        private static void WritePlist(string path, string bundleId)
            => new XDocument(new XElement("plist", new XElement("dict",
                new XElement("key", "CFBundleIdentifier"), new XElement("string", bundleId),
                new XElement("key", "CFBundleShortVersionString"), new XElement("string", "1.2"),
                new XElement("key", "CFBundleVersion"), new XElement("string", "3"))))
                .Save(path);

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    // Only external tool execution is substituted. Selection, plist traversal,
    // signature/entitlement admission and export ordering use the public service.
    private sealed class InspectionRunner : IProcessRunner
    {
        internal string? Defect { get; init; }
        internal bool Universal { get; init; } = true;
        internal int Exports { get; private set; }
        internal List<string> VerifiedBundles { get; } = new();

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tool = Path.GetFileName(request.FileName);
            var code = 0;
            var output = "";
            if (tool == "plutil")
            {
                var dictionary = XDocument.Load(request.Arguments[^1]).Root!.Element("dict")!;
                var key = dictionary.Elements("key").SingleOrDefault(value => value.Value == request.Arguments[1]);
                output = (key?.NextNode as XElement)?.Value ?? "";
                if (key is null) code = 1;
            }
            else if (tool == "codesign")
            {
                var bundle = Path.GetFileName(request.Arguments[^1]);
                var otherArchitecture = request.Arguments.Contains("--architecture") && request.Arguments.Contains("x86_64");
                if (request.Arguments.Contains("--verify"))
                {
                    VerifiedBundles.Add(bundle);
                    if (Defect == "signature" && bundle == "Widget.appex") code = 1;
                }
                else if (request.Arguments.Contains("--entitlements"))
                {
                    output = (Defect == "entitlement" || Defect == "entitlement-other-architecture" && otherArchitecture) ? "<plist><dict/></plist>"
                        : Defect == "malformed" ? "<plist><dict><key>broken</dict></plist>"
                        : "<plist><dict><key>com.apple.security.application-groups</key><array><string>group.com.example</string></array></dict></plist>";
                }
                else
                {
                    var wrongTeam = bundle == "Widget.appex" && (Defect == "team" || Defect == "team-other-architecture" && otherArchitecture);
                    output = (Universal ? "Format=app bundle with Mach-O universal (arm64 x86_64)" : "Format=app bundle with Mach-O thin (arm64)") + "\nTeamIdentifier=" + (wrongTeam ? "OTHERTEAM1" : "TEAM123456");
                }
            }
            else if (tool == "xcodebuild")
            {
                request.InvokePreStartBoundary();
                request.InvokeStartBoundary();
                Exports++;
            }
            else
                throw new InvalidOperationException("Unexpected tool: " + tool);
            return Task.FromResult(new ProcessRunResult(code, output, code == 0 ? "" : "invalid signature or plist", tool,
                TimeSpan.Zero, false));
        }
    }
}
