using System;
using System.IO;
using System.Text;
using PowerForge;

public class DocumentationEngineCommentHelpTests
{
    [Fact]
    public void DocumentationEngine_ExtractsCommentBasedHelp_WhenCommentPrecedesFunction()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            const string moduleName = "TestCommentHelpModule";
            var manifestPath = Path.Combine(tempRoot, $"{moduleName}.psd1");
            var modulePath = Path.Combine(tempRoot, $"{moduleName}.psm1");

            File.WriteAllText(
                modulePath,
                """
                <#
                .SYNOPSIS
                    Determine whether a value should be treated as populated.
                .DESCRIPTION
                    Returns true when the value is non-null and, for strings or enumerables, non-empty.
                .PARAMETER Value
                    The value to evaluate.
                #>
                function Test-DSAHasValue {
                    [CmdletBinding()]
                    param(
                        [Parameter()]
                        $Value
                    )

                    return $null -ne $Value
                }

                Export-ModuleMember -Function Test-DSAHasValue
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            File.WriteAllText(
                manifestPath,
                """
                @{
                    RootModule = 'TestCommentHelpModule.psm1'
                    ModuleVersion = '1.0.0'
                    GUID = 'd38a4b58-4f78-4da7-9cb9-166c6a8e9b7f'
                    Author = 'PowerForge.Tests'
                    Description = 'Test module for comment-based help extraction.'
                    FunctionsToExport = @('Test-DSAHasValue')
                    CmdletsToExport = @()
                    VariablesToExport = @()
                    AliasesToExport = @()
                }
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var engine = new DocumentationEngine(new PowerShellRunner(), new NullLogger());
            var res = engine.Build(
                moduleName: moduleName,
                stagingPath: tempRoot,
                moduleManifestPath: manifestPath,
                documentation: new DocumentationConfiguration { Path = "Docs", PathReadme = string.Empty },
                buildDocumentation: new BuildDocumentationConfiguration
                {
                    Enable = true,
                    StartClean = true,
                    GenerateExternalHelp = false,
                    IncludeAboutTopics = false,
                    GenerateFallbackExamples = false
                });

            Assert.True(res.Succeeded, res.ErrorMessage);

            var mdPath = Path.Combine(tempRoot, "Docs", "Test-DSAHasValue.md");
            Assert.True(File.Exists(mdPath), $"Expected help file at '{mdPath}'.");

            var markdown = File.ReadAllText(mdPath, Encoding.UTF8);
            Assert.Contains("Determine whether a value should be treated as populated.", markdown);
            Assert.Contains("Returns true when the value is non-null", markdown);
            Assert.Contains("The value to evaluate.", markdown);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}

