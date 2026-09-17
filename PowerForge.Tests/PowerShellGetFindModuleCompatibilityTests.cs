using System.Text;
using PowerForge;

namespace PowerForge.Tests;

[Collection("PowerShellGetFindModuleCompatibility")]
public sealed class PowerShellGetFindModuleCompatibilityTests
{
    [Fact]
    public void FindModuleScript_QueriesMultipleNamesIndividuallyAcrossPowerShellHosts()
    {
        using var root = new TemporaryDirectory();
        var wrapperPath = Path.Combine(root.Path, "Invoke-FindModuleTest.ps1");
        File.WriteAllText(wrapperPath, """
param(
  [string]$TargetScript,
  [string]$NamesB64,
  [string]$ReposB64,
  [string]$PrereleaseFlag,
  [string]$CredentialUser,
  [string]$CredentialSecret
)

function Import-Module {
  [CmdletBinding()]
  param([Parameter(Position = 0)][string]$Name)
}

function Find-Module {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][string[]]$Name,
    [string]$Repository,
    [switch]$AllVersions,
    [switch]$AllowPrerelease,
    [pscredential]$Credential
  )

  if ($Name.Count -ne 1) { throw 'Find-Module received more than one name.' }
  if (-not $AllVersions) { throw 'Find-Module did not receive AllVersions.' }
  if ($Repository -ne 'PSGallery') { throw 'Find-Module did not receive the repository.' }
  if ($Name[0] -eq 'Missing.Module') { throw "No match was found for the specified search criteria and module name '$($Name[0])'." }

  $versions = if ($Name[0] -eq 'First.Module') { @('2.0.0', '1.0.0') } else { @('3.0.0') }
  foreach ($version in $versions) {
    [pscustomobject]@{
      Name = $Name[0]
      Version = [version]$version
      Repository = $Repository
      Guid = [guid]'4ace752a-e0ca-4eca-85ef-14c11c750ce1'
    }
  }
}

& $TargetScript $NamesB64 $ReposB64 $PrereleaseFlag $CredentialUser $CredentialSecret
""",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var scriptPath = Path.Combine(root.Path, "Find-Module.ps1");
        File.WriteAllText(
            scriptPath,
            PowerForgeScripts.Load("Scripts/PowerShellGet/Find-Module.ps1"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var names = EncodeLines("First.Module", "Missing.Module", "Second.Module");
        var repositories = EncodeLines("PSGallery");
        var hosts = OperatingSystem.IsWindows()
            ? new[] { "pwsh.exe", "powershell.exe" }
            : new[] { "pwsh" };

        foreach (var host in hosts)
        {
            var result = new PowerShellRunner().Run(new PowerShellRunRequest(
                wrapperPath,
                new[] { scriptPath, names, repositories, "0", string.Empty, string.Empty },
                TimeSpan.FromMinutes(1),
                workingDirectory: root.Path,
                executableOverride: host));

            Assert.True(result.ExitCode == 0, $"{host}: {result.StdOut}{Environment.NewLine}{result.StdErr}");
            var items = result.StdOut
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith("PFPWSGET::ITEM::", StringComparison.Ordinal))
                .Select(DecodeItem)
                .ToArray();

            Assert.Equal(
                new[]
                {
                    ("First.Module", "2.0.0", "PSGallery"),
                    ("First.Module", "1.0.0", "PSGallery"),
                    ("Second.Module", "3.0.0", "PSGallery")
                },
                items);
        }
    }

    private static string EncodeLines(params string[] values) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", values)));

    private static (string Name, string Version, string Repository) DecodeItem(string line)
    {
        var fields = line.Substring("PFPWSGET::ITEM::".Length).Split(new[] { "::" }, StringSplitOptions.None);
        return (Decode(fields[0]), Decode(fields[1]), Decode(fields[2]));
    }

    private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
}
