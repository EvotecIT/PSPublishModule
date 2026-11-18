using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void BorrowedSyntaxTreesAndCompletionEnumsPreserveNativeIdentityAndBindingOnBothHosts()
    {
        const string source = """
            function Read-SyntaxTree {
                [CmdletBinding()]
                param([Parameter(ValueFromPipeline)][System.Management.Automation.Language.Ast] $Value)
                process {
                    [pscustomobject]@{
                        kind = $Value.GetType().FullName
                        text = $Value.Extent.Text
                        isFunction = $Value -is [System.Management.Automation.Language.FunctionDefinitionAst]
                    }
                }
            }
            function Return-SyntaxTree {
                param([System.Management.Automation.Language.Ast] $Value)
                $Value
            }
            function Return-FunctionTree {
                param([System.Management.Automation.Language.FunctionDefinitionAst] $Value)
                $Value
            }
            function Read-CompletionKind {
                param([System.Management.Automation.CompletionResultType] $Kind = [System.Management.Automation.CompletionResultType]::Text)
                $Kind
            }
            Export-ModuleMember -Function Read-SyntaxTree, Return-SyntaxTree, Return-FunctionTree, Read-CompletionKind
            """;
        const string probe = """
            $tokens = $null; $parseErrors = $null
            $tree = [System.Management.Automation.Language.Parser]::ParseInput('function sample { 42 }', [ref]$tokens, [ref]$parseErrors)
            if ($parseErrors.Count) { throw 'The syntax fixture must parse.' }
            $function = $tree.Find({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst]}, $true)
            $returned = Return-SyntaxTree $function
            $derived = Return-FunctionTree $function
            $invalidTree = try { Return-FunctionTree $tree -ErrorAction Stop; 'unexpected' } catch { $_.FullyQualifiedErrorId }
            $invalidEnum = try { Read-CompletionKind -Kind 'NotACompletionKind' -ErrorAction Stop; 'unexpected' } catch { $_.FullyQualifiedErrorId }
            $default = Read-CompletionKind
            $enum = Read-CompletionKind -Kind 'Command'
            [ordered]@{
                rows = @($tree, $function | Read-SyntaxTree)
                same = [object]::ReferenceEquals($function, $returned)
                derivedSame = [object]::ReferenceEquals($function, $derived)
                parentSame = [object]::ReferenceEquals($function.Parent, $returned.Parent)
                default = $default.ToString()
                enum = $enum.ToString()
                enumType = $enum.GetType().FullName
                invalidTree = $invalidTree
                invalidEnum = $invalidEnum
                recovered = (Read-CompletionKind -Kind 0).ToString()
                parameterType = (Get-Command Return-SyntaxTree).Parameters.Value.ParameterType.FullName
            } | ConvertTo-Json -Depth 6 -Compress
            """;
        var modernHost = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(modernHost), "The exact PowerShell 7.6.6 host is required.");
        foreach (var (framework, profile, host) in new[]
                 {
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modernHost),
                     ("net472", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, (string?)null)
                 })
        {
            using var fixture = OracleFixture.Create(string.Empty);
            var module = Path.Combine(fixture.RootPath, "SyntaxTypes.psm1");
            File.WriteAllText(module, source);
            var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                module, Path.Combine(fixture.RootPath, "built"), "SyntaxTypes",
                PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
                allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = framework,
                SemanticProfileId = profile
            });
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            foreach (var name in new[] { "Read-SyntaxTree", "Return-SyntaxTree", "Return-FunctionTree", "Read-CompletionKind" })
            {
                var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, entry => entry.Name == name);
                Assert.True(unit.EmittedClrMethod, name + " must have a complete CLR body.");
                Assert.False(unit.RetainedHostedSource);
            }
            PowerShellCompilationSemanticOracleEnvelope Observe(string path)
            {
                File.WriteAllText(fixture.ScriptPath,
                    "$global:VerbosePreference='SilentlyContinue'\nImport-Module '" +
                    path.Replace("'", "''", StringComparison.Ordinal) + "' -Force -ErrorAction Stop\n" +
                    "$global:VerbosePreference='Continue'\n" + probe);
                return new PowerShellCompilationSemanticOracleRunner().Observe(
                    new PowerShellCompilationSemanticOracleRequest(profile, fixture.ScriptPath)
                    {
                        HostExecutablePath = host,
                        ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(
                            PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile))
                    });
            }
            var original = Observe(module);
            var generated = Observe(built.ArtifactPath!);
            Assert.Empty(original.ErrorRecords);
            Assert.Empty(generated.ErrorRecords);
            var value = Assert.Single(original.Success).Value;
            Assert.Contains("\"same\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"derivedSame\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"parentSame\":true", value, StringComparison.Ordinal);
            Assert.Contains("\"default\":\"Text\"", value, StringComparison.Ordinal);
            Assert.Contains("ParameterArgumentTransformationError", value, StringComparison.Ordinal);
            Assert.Contains("\"recovered\":\"Text\"", value, StringComparison.Ordinal);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
            Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences.Select(static difference =>
                difference.Path + ": original=" + difference.Expected + "; generated=" + difference.Actual)));
        }
    }
}
