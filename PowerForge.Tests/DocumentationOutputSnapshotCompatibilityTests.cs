using System.Text;

namespace PowerForge.Tests;

public sealed partial class DocumentationPowerShellCollectorTests
{
    [Fact]
    public void OutputSnapshot_PreservesSzAndNonSzRankOneArrayIdentities()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-output-shape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var scriptPath = Path.Combine(root, "VerifyOutputShapes.ps1");
            File.WriteAllText(
                scriptPath,
                PowerForgeScripts.Load("Scripts/Documentation/Export-HelpJson.OutputSnapshot.ps1") + """

$szIdentity = GetRuntimeTypeInstanceIdentity ([int[]])
$nonSzIdentity = GetRuntimeTypeInstanceIdentity ([int].MakeArrayType(1))
if (-not $szIdentity.EndsWith('[]', [System.StringComparison]::Ordinal)) {
    throw ('Unexpected SZ identity: ' + $szIdentity)
}
if (-not $nonSzIdentity.EndsWith('[*]', [System.StringComparison]::Ordinal)) {
    throw ('Unexpected non-SZ identity: ' + $nonSzIdentity)
}
if ($szIdentity -ceq $nonSzIdentity) {
    throw 'SZ and non-SZ rank-one arrays collapsed to one runtime identity.'
}
""",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var execution = new ExecutablePowerShellRunner(host, root).Run(new PowerShellRunRequest(
                    scriptPath,
                    Array.Empty<string>(),
                    TimeSpan.FromMinutes(1)));
                Assert.True(execution.ExitCode == 0, execution.StdErr);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup; do not mask assertion failures.
            }
        }
    }
}
