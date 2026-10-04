using PowerForge;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationManagementHostDataTests
{
    private const string Source = """
        function Read-Session { param([Microsoft.Management.Infrastructure.CimSession]$Session) $Session }
        function Read-Sessions {
            [CmdletBinding()]param([Parameter(ValueFromPipeline)][Microsoft.Management.Infrastructure.CimSession[]]$Sessions)
            process { foreach($session in $Sessions){$session} }
        }
        function Read-SessionMap { param([Collections.Generic.Dictionary[string,Microsoft.Management.Infrastructure.CimSession]]$Sessions) $Sessions['one'] }
        function Read-Class { param([Microsoft.Management.Infrastructure.CimClass]$Class) $Class.CimClassName }
        function Read-CommonParameters { [System.Management.Automation.Cmdlet]::CommonParameters }
        function New-OfflineSession { [Microsoft.Management.Infrastructure.CimSession]::Create("factory.compiler-offline.invalid") }
        function Read-ClassType { [Microsoft.Management.Infrastructure.CimClass] }
        """;

    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void ManagementHostData_PreservesNativeIdentityAndRequiresBothCapabilities(string framework)
    {
        var source = PowerShellSourceParser.Parse(Source, Path.Combine(Path.GetTempPath(), "management-host-data.psm1"));
        foreach (var capabilities in new[]
        {
            PowerShellCompilationCapabilities.TypedLibrary,
            PowerShellCompilationCapabilities.BinaryModule,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.NativeFunctionBinding,
            PowerShellCompilationCapabilities.HybridModule & ~PowerShellCompilationCapability.PowerShellHostTypes
        })
            Assert.Empty(new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework, capabilities).Emitted.Methods);

        var qualified = new PowerShellSemanticCompilationPipeline().Compile(new[] { source }, framework,
            PowerShellCompilationCapabilities.HybridModule);
        Assert.Equal(7, qualified.Emitted.Methods.Length);
        Assert.All(qualified.Analyzed.Functions, function => Assert.NotNull(function.NativeFunctionBinding));
    }
}
