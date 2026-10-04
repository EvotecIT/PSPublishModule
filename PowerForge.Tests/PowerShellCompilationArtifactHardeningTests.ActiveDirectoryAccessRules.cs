using System.Runtime.InteropServices;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Theory]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_HybridModulePreservesBorrowedActiveDirectoryAccessRuleBinding(string framework, string host)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        using var fixture = ArtifactFixture.Create(
            """
            function Get-AceRights {
                [CmdletBinding()]
                param([Parameter(ValueFromPipeline)][System.DirectoryServices.ActiveDirectoryAccessRule[]]$Ace)
                process { foreach ($item in $Ace) { "$($item.AccessControlType):$($item.ActiveDirectoryRights)" } }
            }
            Export-ModuleMember -Function Get-AceRights
            """, ".psm1");
        var parsed = PowerShellSourceParser.Parse(File.ReadAllText(fixture.ScriptPath), fixture.ScriptPath);
        var strict = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { parsed }, framework, PowerShellCompilationCapabilities.TypedLibrary);
        Assert.Empty(strict.Emitted.Methods);

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.BorrowedAce" + framework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = framework
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);

        const string probe = """
            $sid=[System.Security.Principal.SecurityIdentifier]::new('S-1-1-0');
            $allow=[System.DirectoryServices.ActiveDirectoryAccessRule]::new($sid,[System.DirectoryServices.ActiveDirectoryRights]::GenericAll,[System.Security.AccessControl.AccessControlType]::Allow);
            $deny=[System.DirectoryServices.ActiveDirectoryAccessRule]::new($sid,[System.DirectoryServices.ActiveDirectoryRights]::GenericAll,[System.Security.AccessControl.AccessControlType]::Deny);
            @($allow,$deny) | Get-AceRights;
            Get-AceRights -Ace @($allow,$deny)
            """;
        var original = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {probe}");
        var generated = Run(host, "-NoProfile", "-NonInteractive", "-Command",
            $"Import-Module -Name '{result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal)}' -Force; {probe}");
        var expected = new[] { "Allow:GenericAll", "Deny:GenericAll", "Allow:GenericAll", "Deny:GenericAll" };
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(0, generated.ExitCode);
        Assert.Equal(expected, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(original.StandardOutput, generated.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(generated.StandardError), generated.StandardError);
    }
}
