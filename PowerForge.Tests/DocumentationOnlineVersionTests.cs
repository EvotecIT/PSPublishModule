namespace PowerForge.Tests;

public sealed class DocumentationOnlineVersionTests
{
    [Fact]
    public void GeneratedMarkdown_PrefersCommandHelpUriAndPreservesModuleFallbacks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-docs-online-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string moduleName = "OnlineVersionModule";
            var manifestPath = Path.Combine(root, moduleName + ".psd1");
            File.WriteAllText(Path.Combine(root, moduleName + ".psm1"), """
                function Get-FirstCommand {
                    <#
                    .SYNOPSIS
                    First command.
                    .LINK
                    https://example.test/related
                    #>
                    [CmdletBinding(HelpUri = 'https://example.test/commands/first')]
                    param()
                }
                function Get-SecondCommand {
                    [CmdletBinding(HelpUri = 'https://example.test/commands/second')]
                    param()
                }
                function Get-FallbackCommand {
                    <#
                    .SYNOPSIS
                    Command without a declared online help URI.
                    .LINK
                    https://example.test/related-fallback
                    #>
                    [CmdletBinding()]
                    param()
                }
                """);
            File.WriteAllText(manifestPath, """
                @{
                    RootModule = 'OnlineVersionModule.psm1'
                    ModuleVersion = '1.0.0'
                    GUID = 'abed6fa0-f408-4fc2-a49a-c741f9b43504'
                    FunctionsToExport = @('Get-FirstCommand', 'Get-SecondCommand', 'Get-FallbackCommand')
                    CmdletsToExport = @()
                    AliasesToExport = @()
                    HelpInfoURI = 'https://example.test/module-help'
                    PrivateData = @{ PSData = @{ ProjectUri = 'https://example.test/project' } }
                }
                """);

            var engine = new DocumentationEngine(new PowerShellRunner(), new NullLogger());
            var payload = engine.ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
            Assert.Equal(3, payload.Commands.Count);
            var docsPath = Path.Combine(root, "Docs");

            foreach (var fallback in new[] { "https://example.test/module-help", "https://example.test/project", "" })
            {
                new MarkdownHelpWriter().WriteCommandHelpFiles(payload, moduleName, docsPath);
                Assert.Contains("online version: https://example.test/commands/first\n", ReadMarkdown("Get-FirstCommand"));
                Assert.Contains("online version: https://example.test/commands/second\n", ReadMarkdown("Get-SecondCommand"));
                Assert.Contains("https://example.test/related", ReadMarkdown("Get-FirstCommand"));
                Assert.Contains("https://example.test/related-fallback", ReadMarkdown("Get-FallbackCommand"));
                Assert.Contains("online version:" + (fallback.Length == 0 ? "" : " " + fallback) + "\n",
                    ReadMarkdown("Get-FallbackCommand"));

                if (!string.IsNullOrWhiteSpace(payload.HelpInfoUri))
                    payload.HelpInfoUri = null;
                else
                    payload.ProjectUri = null;
            }

            string ReadMarkdown(string commandName)
                => File.ReadAllText(Path.Combine(docsPath, commandName + ".md")).Replace("\r\n", "\n");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
