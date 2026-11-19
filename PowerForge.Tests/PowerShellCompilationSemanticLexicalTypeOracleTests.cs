using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void RuntimeLoadedTypePreservesScopeAndDelegateAssignmentOnBothHosts()
    {
        const string source = """
            function Read-ScopedFormatter {
                [CmdletBinding()]
                param([bool] $Load)
                if (!$Load) { return 'skipped' }
                Add-Type @'
            using System;
            public sealed class LexicalProofFormatter
            {
                public static Func<string, string> Format { get; set; }
                public static Func<string, string> WithPrefix(string prefix)
                {
                    return text => prefix + text;
                }
                public static string Render(string name) { return Format(name); }
            }
            '@
                [LexicalProofFormatter]::Format = [LexicalProofFormatter]::WithPrefix('item:')
                [LexicalProofFormatter]::Render('pear')
            }
            Export-ModuleMember -Function Read-ScopedFormatter
            """;
        const string probe = """
            $skipped = Read-ScopedFormatter $false
            $before = $null -ne ('LexicalProofFormatter' -as [type])
            $rendered = Read-ScopedFormatter $true
            [ordered]@{skipped=$skipped; before=$before; rendered=$rendered} | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-ScopedFormatter" }, value =>
        {
            Assert.Contains("\"skipped\":\"skipped\"", value, StringComparison.Ordinal);
            Assert.Contains("\"before\":false", value, StringComparison.Ordinal);
            Assert.Contains("\"rendered\":\"item:pear\"", value, StringComparison.Ordinal);
        });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void LiteralClrReceiversPreserveNativeBindingAndMissingMemberErrorsOnBothHosts()
    {
        const string source = """
            function Read-UriPath {
                ([uri][Environment]::GetEnvironmentVariable('COMPILER_ORACLE_TYPED_URI')).GetComponents(
                    [UriComponents]::Path, [UriFormat]::Unescaped)
            }
            function Set-OptionalStaticMember([bool] $UseMissingMember) {
                if ($UseMissingMember) { [System.IO.Path]::CompilerOracleMissing = 'unused' }
                'continued'
            }
            Export-ModuleMember -Function Read-UriPath, Set-OptionalStaticMember
            """;
        const string probe = """
            $env:COMPILER_ORACLE_TYPED_URI = 'https://example.invalid/one%20two'
            $path = Read-UriPath
            $skipped = Set-OptionalStaticMember $false
            $oldPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Stop'
            try {
                $invalid = try { Set-OptionalStaticMember $true } catch { $_.FullyQualifiedErrorId + ': ' + $_.Exception.Message }
            } finally { $ErrorActionPreference = $oldPreference }
            [ordered]@{path=$path; skipped=$skipped; invalid=$invalid} | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-UriPath", "Set-OptionalStaticMember" }, value =>
        {
            Assert.Contains("\"path\":\"one two\"", value, StringComparison.Ordinal);
            Assert.Contains("\"skipped\":\"continued\"", value, StringComparison.Ordinal);
            Assert.Contains("CompilerOracleMissing", value, StringComparison.OrdinalIgnoreCase);
        });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FunctionLocalDataClassPreservesIdentityAndValidationOnBothHosts()
    {
        const string source = """
            function New-LexicalRecord {
                [CmdletBinding()]
                param([string] $Name, [string] $Category)
                class LexicalProofRecord {
                    static [string] $Schema = 'v1'
                    [string] $Name
                    [ValidateSet('Fruit', 'Vegetable')][string] $Category
                    [scriptblock] $Project
                    [uri[]] $Links
                }
                [LexicalProofRecord[]] $rows = @(
                    [LexicalProofRecord]@{
                        Name = $Name
                        Category = $Category
                        Project = { param([string] $Value) $Value + '!' }
                        Links = @('https://example.invalid/item')
                    }
                )
                [ordered]@{rows=$rows; schema=[LexicalProofRecord]::Schema; typed=$rows -is [LexicalProofRecord[]]}
            }
            function Read-LexicalCastOverload {
                [CmdletBinding()]
                param()
                class LexicalProofOverloadArgument {
                    [string] $Name
                }
                Add-Type @'
            public static class LexicalProofOverloads
            {
                public static string Pick(object[] values) { return "objects"; }
                public static string Pick(string[] values) { return "strings"; }
            }
            '@
                [LexicalProofOverloads]::Pick([LexicalProofOverloadArgument[]] $null)
            }
            Export-ModuleMember -Function New-LexicalRecord, Read-LexicalCastOverload
            """;
        const string probe = """
            $first = New-LexicalRecord apple Fruit
            $second = New-LexicalRecord carrot Vegetable
            $invalid = try { New-LexicalRecord stone Mineral -ErrorAction Stop } catch { $_.FullyQualifiedErrorId }
            $project = $first.rows[0].Project
            [ordered]@{
                schema = $first.schema
                typed = $first.typed
                name = $first.rows[0].Name
                projected = & $project $first.rows[0].Name
                link = $first.rows[0].Links[0].Host
                same = [object]::ReferenceEquals($first.rows[0].GetType(), $second.rows[0].GetType())
                outside = $null -ne ('LexicalProofRecord' -as [type])
                overload = Read-LexicalCastOverload
                invalid = $invalid
            } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "New-LexicalRecord" }, value =>
        {
            Assert.Contains("\"schema\":\"v1\"", value, StringComparison.Ordinal);
            Assert.Contains("\"typed\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"projected\":\"apple!\"", value, StringComparison.Ordinal);
            Assert.Contains("\"link\":\"example.invalid\"", value, StringComparison.Ordinal);
            Assert.Contains("\"same\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"outside\":false", value, StringComparison.Ordinal);
            Assert.Contains("\"overload\":\"objects\"", value, StringComparison.Ordinal);
            Assert.Contains("\"invalid\":\"", value, StringComparison.Ordinal);
        }, assertArtifact: built =>
        {
            var overload = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,
                entry => entry.Name == "Read-LexicalCastOverload");
            Assert.True(overload.RetainedHostedSource);
            Assert.Contains(overload.DiagnosticChain, diagnostic =>
                diagnostic.Message.Contains("Runtime-owned cast constraints", StringComparison.Ordinal));
        });
    }

    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void DeclaredWindowsModuleSuppliesBorrowedTypeMetadataOnBothHosts()
    {
        const string source = """
            function Read-BorrowedPrincipal {
                [CmdletBinding()]
                param([Microsoft.PowerShell.Commands.LocalPrincipal[]] $Records)
                [ordered]@{
                    records = $Records
                    type = [Microsoft.PowerShell.Commands.LocalPrincipal]
                }
            }
            Export-ModuleMember -Function Read-BorrowedPrincipal
            """;
        const string probe = """
            $record = [Microsoft.PowerShell.Commands.LocalPrincipal]::new()
            $result = Read-BorrowedPrincipal $record
            $invalid = try { Read-BorrowedPrincipal ([object]::new()) -ErrorAction Stop } catch { $_.FullyQualifiedErrorId }
            [ordered]@{
                record = [object]::ReferenceEquals($record,$result.records[0])
                array = $result.records -is [Microsoft.PowerShell.Commands.LocalPrincipal[]]
                type = [object]::ReferenceEquals($record.GetType(), $result.type)
                invalid = $invalid
            } | ConvertTo-Json -Compress
            """;
        CompareScopedArtifacts(source, probe, new[] { "Read-BorrowedPrincipal" }, value =>
        {
            Assert.Contains("\"record\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"array\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"type\":true", value, StringComparison.Ordinal);
            Assert.Contains("ParameterArgumentTransformationError", value, StringComparison.Ordinal);
        }, requiredModule: "Microsoft.PowerShell.LocalAccounts", assertArtifact: built =>
        {
            var nodes = built.Manifest!.DependencyGraph!.Nodes;
            var metadata = Assert.Single(nodes, node =>
                node.Identity.Provenance == "WindowsHostModuleAssembly" &&
                Path.GetFileName(node.Identity.Source).Equals("Microsoft.PowerShell.LocalAccounts.dll", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(PowerShellCompilationDependencyGraphDisposition.External, metadata.Disposition);
            Assert.StartsWith("windows-module/", metadata.Identity.Source, StringComparison.Ordinal);
            Assert.False(Path.IsPathRooted(metadata.Identity.Source));
            Assert.Matches("^[a-f0-9]{64}$", metadata.Identity.Sha256);
            Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(built.ArtifactPath!)!, "*.dll",
                SearchOption.AllDirectories), path =>
                Path.GetFileName(path).Equals("Microsoft.PowerShell.LocalAccounts.dll", StringComparison.OrdinalIgnoreCase));
        });
    }
}
