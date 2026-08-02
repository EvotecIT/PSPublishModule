using System.Diagnostics;
using System.Text;

namespace PowerForge.Tests;

[Collection("BinaryDocFixture")]
public sealed class DocumentationBinaryMetadataContractTests
{
    [Fact]
    public void DocumentationEngine_NormalizesBinaryMetadataAndWritesDirectImportHelpAlias()
    {
        var fixtureRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "PowerForge.Tests", "Fixtures", "BinaryDocFixture"));
        var outputDirectory = BuildFixture(fixtureRoot);
        var root = Path.Combine(Path.GetTempPath(), "pf-binary-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            foreach (var file in Directory.EnumerateFiles(outputDirectory))
                File.Copy(file, Path.Combine(root, Path.GetFileName(file)), overwrite: true);

            var manifestPath = Path.Combine(root, "MetadataFixture.psd1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'BinaryDocFixture.dll'
    ModuleVersion = '1.0.0'
    GUID = '12121212-1212-1212-1212-121212121212'
    Author = 'PowerForge.Tests'
    Description = 'Binary documentation metadata contract.'
    FunctionsToExport = @()
    CmdletsToExport = @('Get-BinaryDocMetadataContract')
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(false));

            var engine = new DocumentationEngine(new PowerShellRunner(), new NullLogger());
            var payload = engine.ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
            new XmlDocCommentEnricher(new NullLogger()).Enrich(payload);
            var command = Assert.Single(payload.Commands);
            var nullable = Assert.Single(command.Parameters, parameter => parameter.Name == "NullableMode");
            var nullableMatrix = Assert.Single(command.Parameters, parameter => parameter.Name == "NullableModeMatrix");
            var inherited = Assert.Single(command.Parameters, parameter => parameter.Name == "InheritedLabel");

            Assert.Equal("BinaryDocMode", nullable.Type);
            Assert.Equal("BinaryDocMode[,]", nullableMatrix.Type);
            Assert.Equal(new[] { "Advanced", "Basic" }, nullable.PossibleValues.OrderBy(value => value));
            Assert.Equal("Inherited label documented in a separate declaring assembly.", inherited.Description);
            Assert.DoesNotContain(command.Parameters, parameter => parameter.Name == "HiddenTransport");
            Assert.All(command.Syntax, syntax => Assert.DoesNotContain("HiddenTransport", syntax.Text, StringComparison.Ordinal));

            var result = engine.Build(
                "MetadataFixture",
                root,
                manifestPath,
                new DocumentationConfiguration { Path = "Docs", PathReadme = "Docs/Readme.md" },
                new BuildDocumentationConfiguration
                {
                    Enable = true,
                    GenerateExternalHelp = true,
                    IncludeAboutTopics = false,
                    GenerateFallbackExamples = false
                });

            Assert.True(result.Succeeded, result.ErrorMessage);
            var primary = Path.Combine(root, "en-US", "MetadataFixture-help.xml");
            var binaryAlias = Path.Combine(root, "en-US", "BinaryDocFixture.dll-Help.xml");
            Assert.True(File.Exists(primary));
            Assert.True(File.Exists(binaryAlias));
            var primaryDocument = System.Xml.Linq.XDocument.Load(primary);
            var aliasDocument = System.Xml.Linq.XDocument.Load(binaryAlias);
            Assert.Equal(primaryDocument.Root!.ToString(), aliasDocument.Root!.ToString());
            Assert.Contains("PowerForgeGeneratedExternalHelpAlias", File.ReadAllText(binaryAlias), StringComparison.Ordinal);
            Assert.Contains(binaryAlias, result.ExternalHelpFilePaths, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(outputDirectory, true); } catch { }
        }
    }

    private static string BuildFixture(string fixtureRoot)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "pf-binary-metadata-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = fixtureRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("build");
        start.ArgumentList.Add("BinaryDocFixture.csproj");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add(outputDirectory);
        start.ArgumentList.Add("--nologo");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start fixture build.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stdout + Environment.NewLine + stderr);
        return outputDirectory;
    }
}
