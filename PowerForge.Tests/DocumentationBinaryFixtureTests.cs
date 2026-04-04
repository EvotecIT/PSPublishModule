using System.Diagnostics;
using System.Text;

namespace PowerForge.Tests;

public sealed class DocumentationBinaryFixtureTests
{
    [Fact]
    public void DocumentationEngine_GeneratesExpectedOutputs_ForBinaryFixture()
    {
        var fixtureRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PowerForge.Tests", "Fixtures", "BinaryDocFixture"));
        var outputDirectory = BuildFixtureProject(fixtureRoot);
        var tempRoot = Path.Combine(Path.GetTempPath(), "pf-binary-doc-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var moduleName = "BinaryDocFixture";
            var assemblyPath = Path.Combine(outputDirectory, moduleName + ".dll");
            var xmlPath = Path.Combine(outputDirectory, moduleName + ".xml");
            var manifestPath = Path.Combine(tempRoot, moduleName + ".psd1");

            Assert.True(File.Exists(assemblyPath), $"Expected built fixture assembly at '{assemblyPath}'.");
            Assert.True(File.Exists(xmlPath), $"Expected built fixture XML docs at '{xmlPath}'.");

            File.Copy(assemblyPath, Path.Combine(tempRoot, Path.GetFileName(assemblyPath)), overwrite: true);
            File.Copy(xmlPath, Path.Combine(tempRoot, Path.GetFileName(xmlPath)), overwrite: true);

            File.WriteAllText(manifestPath, """
@{
    RootModule = 'BinaryDocFixture.dll'
    ModuleVersion = '1.0.0'
    GUID = '66666666-6666-6666-6666-666666666666'
    Author = 'PowerForge.Tests'
    Description = 'Binary fixture module for documentation generation tests.'
    FunctionsToExport = @()
    CmdletsToExport = @('Get-BinaryDocSample')
    AliasesToExport = @()
    VariablesToExport = @()
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var engine = new DocumentationEngine(new PowerShellRunner(), new NullLogger());
            var result = engine.Build(
                moduleName: moduleName,
                stagingPath: tempRoot,
                moduleManifestPath: manifestPath,
                documentation: new DocumentationConfiguration
                {
                    Path = "Docs",
                    PathReadme = Path.Combine("Docs", "Readme.md")
                },
                buildDocumentation: new BuildDocumentationConfiguration
                {
                    Enable = true,
                    StartClean = true,
                    GenerateExternalHelp = true,
                    IncludeAboutTopics = false,
                    GenerateFallbackExamples = true
                });

            Assert.True(result.Succeeded, result.ErrorMessage);

            var markdownPath = Path.Combine(tempRoot, "Docs", "Get-BinaryDocSample.md");
            var externalHelpPath = Path.Combine(tempRoot, "en-US", "BinaryDocFixture-help.xml");
            Assert.True(File.Exists(markdownPath), $"Expected generated markdown help at '{markdownPath}'.");
            Assert.True(File.Exists(externalHelpPath), $"Expected generated MAML help at '{externalHelpPath}'.");

            var expectedRoot = Path.Combine(fixtureRoot, "Expected");
            Assert.Equal(
                NormalizeText(File.ReadAllText(Path.Combine(expectedRoot, "Get-BinaryDocSample.md"))),
                NormalizeText(File.ReadAllText(markdownPath)));
            Assert.Equal(
                NormalizeText(File.ReadAllText(Path.Combine(expectedRoot, "BinaryDocFixture-help.xml"))),
                NormalizeText(File.ReadAllText(externalHelpPath)));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }

            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }

    private static string BuildFixtureProject(string fixtureRoot)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "pf-binary-doc-fixture-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = fixtureRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add("BinaryDocFixture.csproj");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Release");
        psi.ArgumentList.Add("--nologo");
        psi.ArgumentList.Add("--verbosity");
        psi.ArgumentList.Add("minimal");
        psi.ArgumentList.Add("-p:OutputPath=" + outputDirectory + Path.DirectorySeparatorChar);

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var stdOut = process!.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Fixture build failed.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{stdErr}");

        return outputDirectory;
    }

    private static string NormalizeText(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Trim();
}
