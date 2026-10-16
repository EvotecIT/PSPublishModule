using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsCommandCountTests
{
    [Theory]
    [InlineData("Cmdlet", "Cmdlet", "cmdlets")]
    [InlineData("Function", "Function", "functions")]
    [InlineData("Cmdlet", "Function", "commands")]
    public void Generate_LabelsCommandCountsForSingleAndMixedKinds(string firstKind, string secondKind, string expectedUnit)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-command-count-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var help = Path.Combine(root, "Demo-help.xml");
            File.WriteAllText(help, $$"""
                <helpItems xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10">
                  <command:command><command:details><command:name>Get-First</command:name><command:commandType>{{firstKind}}</command:commandType>
                  <maml:description><maml:para>First command.</maml:para></maml:description></command:details></command:command>
                  <command:command><command:details><command:name>Get-Second</command:name><command:commandType>{{secondKind}}</command:commandType>
                  <maml:description><maml:para>Second command.</maml:para></maml:description></command:details></command:command>
                </helpItems>
                """);
            var output = Path.Combine(root, "api");
            WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.PowerShell, HelpPath = help, OutputPath = output,
                Format = "html", Template = "docs", PowerShellModuleName = "Demo", GeneratePowerShellFallbackExamples = false
            });
            var html = File.ReadAllText(Path.Combine(output, "index.html"));
            Assert.Contains($"data-unit=\"{expectedUnit}\">Showing 2 of 2 {expectedUnit}", html);
            Assert.Contains($"<p class=\"section-desc\">Browse all 2 {expectedUnit}", html);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
