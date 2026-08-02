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
            var binaryDirectory = Path.Combine(root, "Lib", "Core");
            Directory.CreateDirectory(binaryDirectory);
            foreach (var file in Directory.EnumerateFiles(outputDirectory))
                File.Copy(file, Path.Combine(binaryDirectory, Path.GetFileName(file)), overwrite: true);
            File.Delete(Path.Combine(binaryDirectory, "BinaryDocFixture.xml"));

            var manifestPath = Path.Combine(root, "MetadataFixture.psd1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'Lib/Core/BinaryDocFixture.dll'
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

            var legacyPrimaryDirectory = Path.Combine(root, "en-US");
            Directory.CreateDirectory(legacyPrimaryDirectory);
            File.WriteAllText(
                Path.Combine(legacyPrimaryDirectory, "MetadataFixture-help.xml"),
                "<legacyHelpItems />");
            var staleAliasDirectory = Path.Combine(root, "Lib", "Removed", "en-US");
            Directory.CreateDirectory(staleAliasDirectory);
            var staleAlias = Path.Combine(staleAliasDirectory, "Removed.dll-Help.xml");
            File.WriteAllText(
                staleAlias,
                DocumentationExternalHelpAliasWriter.GetLegacyGeneratedAliasMarker() + "<legacyHelpItems />");
            var bundledRoot = Path.Combine(root, "Bundled");
            var bundledAliasDirectory = Path.Combine(bundledRoot, "Lib", "en-US");
            Directory.CreateDirectory(bundledAliasDirectory);
            File.WriteAllText(Path.Combine(bundledRoot, "Bundled.psd1"), "@{ RootModule = '' }");
            var bundledAlias = Path.Combine(bundledAliasDirectory, "Bundled.dll-Help.xml");
            File.WriteAllText(
                bundledAlias,
                DocumentationExternalHelpAliasWriter.GetLegacyGeneratedAliasMarker() + "<legacyHelpItems />");

            var result = engine.Build(
                "MetadataFixture",
                root,
                manifestPath,
                new DocumentationConfiguration { Path = "Docs", PathReadme = "Docs/Readme.md" },
                new BuildDocumentationConfiguration
                {
                    Enable = true,
                    GenerateExternalHelp = true,
                    ExternalHelpCulture = "fr-FR",
                    IncludeAboutTopics = false,
                    GenerateFallbackExamples = false
                });

            Assert.True(result.Succeeded, result.ErrorMessage);
            var primary = Path.Combine(root, "fr-FR", "MetadataFixture-help.xml");
            var binaryAlias = Path.Combine(root, "Lib", "Core", "fr-FR", "BinaryDocFixture.dll-Help.xml");
            Assert.True(File.Exists(primary));
            Assert.True(File.Exists(binaryAlias));
            Assert.False(File.Exists(staleAlias));
            Assert.True(File.Exists(bundledAlias));
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

    [Fact]
    public void ExternalHelpAliases_PreserveAuthoredAndOtherModuleFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-binary-alias-ownership-" + Guid.NewGuid().ToString("N"));
        var cultureDirectory = Path.Combine(root, "en-US");
        var coreDirectory = Path.Combine(root, "Lib", "Core");
        var authoredDirectory = Path.Combine(root, "Lib", "Authored", "en-US");
        Directory.CreateDirectory(cultureDirectory);
        Directory.CreateDirectory(coreDirectory);
        Directory.CreateDirectory(authoredDirectory);

        try
        {
            var primary = Path.Combine(cultureDirectory, "Foo.dll-Help.xml");
            File.WriteAllText(primary, "<helpItems />");
            var assemblyPath = Path.Combine(coreDirectory, "Foo.dll");
            File.WriteAllText(assemblyPath, string.Empty);
            var authoredAssemblyPath = Path.Combine(root, "Lib", "Authored", "Authored.dll");
            File.WriteAllText(authoredAssemblyPath, string.Empty);
            var authoredAlias = Path.Combine(authoredDirectory, "Authored.dll-Help.xml");
            File.WriteAllText(authoredAlias, "<authoredHelpItems />");
            var otherAlias = Path.Combine(root, "Lib", "Other", "en-US", "Other.dll-Help.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(otherAlias)!);
            File.WriteAllText(
                otherAlias,
                DocumentationExternalHelpAliasWriter.GetGeneratedAliasMarker("OtherModule") + "<helpItems />");

            var payload = new DocumentationExtractionPayload
            {
                ModuleName = "OwnerModule",
                Commands =
                [
                    new DocumentationCommandHelp { AssemblyPath = assemblyPath },
                    new DocumentationCommandHelp { AssemblyPath = authoredAssemblyPath }
                ]
            };
            var paths = DocumentationExternalHelpAliasWriter.WriteAliases(payload, primary, "OwnerModule");
            var nestedAlias = Path.Combine(coreDirectory, "en-US", "Foo.dll-Help.xml");

            Assert.Contains(nestedAlias, paths, StringComparer.OrdinalIgnoreCase);
            Assert.True(File.Exists(nestedAlias));
            Assert.Equal("<authoredHelpItems />", File.ReadAllText(authoredAlias));
            Assert.DoesNotContain(authoredAlias, paths, StringComparer.OrdinalIgnoreCase);

            DocumentationExternalHelpAliasWriter.PruneGeneratedAliases(root, "OwnerModule");

            Assert.False(File.Exists(nestedAlias));
            Assert.True(File.Exists(authoredAlias));
            Assert.True(File.Exists(otherAlias));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
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
