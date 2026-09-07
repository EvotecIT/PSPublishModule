using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorEmission_PreservesAssignmentAndFailedReturnContinuation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-ParsedValues {
                [CmdletBinding()] param([string[]]$Values)
                'before'
                foreach ($text in $Values) {
                    [int]$parsed = 99
                    $parsed = [int]::Parse($text)
                    $parsed
                }
                'after'
            }
            function Get-ReturnedValue {
                [CmdletBinding()] param([string]$Text)
                return [int]::Parse($Text)
                'after-return-failure'
            }
            """, ".psm1");
        var assembly = BuildStatementErrorEmission(fixture, framework, 2);
        const string probe = """
            foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                $values = @(Get-ParsedValues -Values '1','bad','2' -ErrorAction $action 2>&1)
                'assignment:' + $action + ':' + (($values | ForEach-Object {
                    if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName }
                    else { $_.GetType().Name + ':' + $_ }
                }) -join '|')
                foreach ($text in '1','bad') {
                    $values = @(Get-ReturnedValue -Text $text -ErrorAction $action 2>&1)
                    'return:' + $action + ':' + $text + ':' + (($values | ForEach-Object {
                        if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName }
                        else { $_.GetType().Name + ':' + $_ }
                    }) -join '|')
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-emission");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(assembly) + "'; " + probe,
            fixture.RootPath, "compiled-emission");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("String:after-return-failure", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorEmission_PreservesTypedCatchesThrowAndFinally(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-HandledValues {
                [CmdletBinding()] param([string]$Text)
                try { return [int]::Parse($Text) }
                catch [FormatException] { 'format' }
                catch [System.Management.Automation.RuntimeException] { 'runtime' }
                finally { 'cleanup' }
                'after'
            }
            function Get-ThrownValues {
                [CmdletBinding()] param([string]$Text)
                try { throw [InvalidOperationException]::new($Text) }
                catch [InvalidOperationException] { 'invalid' }
                finally { 'cleanup' }
                'after'
            }
            function Get-FinallyValues {
                [CmdletBinding()] param([string]$Text)
                try { [void][int]::Parse($Text); 'inside-after' }
                finally { 'cleanup' }
                'after'
            }
            function Get-RethrownValues {
                [CmdletBinding()] param([string]$Text)
                try { [void][int]::Parse($Text) }
                catch { throw }
                finally { 'cleanup' }
                'after'
            }
            """, ".psm1");
        var assembly = BuildStatementErrorEmission(fixture, framework, 4);
        const string probe = """
            foreach ($command in 'Get-HandledValues','Get-ThrownValues','Get-FinallyValues','Get-RethrownValues') {
                foreach ($action in 'Continue','SilentlyContinue','Stop') {
                    foreach ($text in '1','bad') {
                        $records = [Collections.Generic.List[object]]::new()
                        $caught = $null
                        $faults = @()
                        try { & $command -Text $text -ErrorAction $action -ErrorVariable faults 2>$null | ForEach-Object { [void]$records.Add($_) } }
                        catch { $caught = $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName }
                        [pscustomobject]@{ command=$command; action=$action; text=$text; records=$records.ToArray(); caught=$caught;
                            faults=@($faults | ForEach-Object { $_.GetType().FullName }) } | ConvertTo-Json -Depth 5 -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-handled-emission");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(assembly) + "'; " + probe,
            fixture.RootPath, "compiled-handled-emission");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("\"records\":[\"format\",\"cleanup\",\"after\"]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorEmission_PreservesReceiverArgumentOrderAndConstructorErrors(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-ReceiverValues {
                [CmdletBinding()] param([string]$Text, [Text.StringBuilder]$Builder)
                [void]$Builder.Append('R').Append([int]::Parse($Text))
                'after'
            }
            function Get-ConstructorValues {
                [CmdletBinding()] param([string]$Text)
                [void][Text.StringBuilder]::new([int]::Parse($Text))
                'after'
            }
            function Get-NullValues {
                [CmdletBinding()] param([string]$Text, [Text.StringBuilder]$Builder)
                [void]$Builder.Append([int]::Parse($Text))
                'after'
            }
            """, ".psm1");
        var assembly = BuildStatementErrorEmission(fixture, framework, 3);
        const string probe = """
            foreach ($command in 'Get-ReceiverValues','Get-ConstructorValues','Get-NullValues') {
                foreach ($action in 'Continue','SilentlyContinue','Stop') {
                    foreach ($text in '1','bad','-1') {
                        $builder = [Text.StringBuilder]::new()
                        $parameters = @{ Text=$text; ErrorAction=$action }
                        if ($command -eq 'Get-ReceiverValues') { $parameters.Builder=$builder }
                        if ($command -eq 'Get-NullValues') { $parameters.Builder=$null }
                        $records = [Collections.Generic.List[string]]::new()
                        try {
                            & $command @parameters 2>&1 | ForEach-Object {
                                if ($_ -is [System.Management.Automation.ErrorRecord]) {
                                    [void]$records.Add($_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName + ':' + $_.Exception.Message)
                                } else { [void]$records.Add([string]$_) }
                            }
                        } catch { [void]$records.Add('caught:' + $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName + ':' + $_.Exception.Message) }
                        [pscustomobject]@{ command=$command; action=$action; text=$text; builder=$builder.ToString(); records=$records.ToArray() } | ConvertTo-Json -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-receiver-emission");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(assembly) + "'; " + probe,
            fixture.RootPath, "compiled-receiver-emission");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("InvokeMethodOnNull", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"builder\":\"R\"", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Exception calling", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorEmission_PreservesMutableValueTypeParameterStorage(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-SpinValues {
                [CmdletBinding()] param([Threading.SpinWait]$Spinner)
                $Spinner.SpinOnce()
                [int]$first = $Spinner.Count
                $first
                $Spinner.SpinOnce()
                [int]$second = $Spinner.Count
                $second
                'after'
            }
            """, ".psm1");
        var assembly = BuildStatementErrorEmission(fixture, framework, 1);
        const string probe = """
            $spinner = New-Object Threading.SpinWait
            @(Get-SpinValues -Spinner $spinner) -join '|'
            'caller:' + $spinner.Count
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-struct-emission");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(assembly) + "'; " + probe,
            fixture.RootPath, "compiled-struct-emission");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("1|2|after", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorEmission_PreservesNestedFunctionErrorIdentityAndContinuation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-Inner {
                [CmdletBinding()] param([string]$Text)
                'inner-before'
                [void][int]::Parse($Text)
                'inner-after'
            }
            function Read-Outer {
                [CmdletBinding()] param([string]$Text)
                'outer-before'
                Read-Middle -Text $Text
                'outer-after'
            }
            function Read-Middle {
                [CmdletBinding()] param([string]$Text)
                'middle-before'
                Read-Inner -Text $Text
                'middle-after'
            }
            function Read-HandledOuter {
                [CmdletBinding()] param([string]$Text)
                'outer-before'
                try { Read-Inner -Text $Text; 'inside-after' }
                catch { 'handled' }
                'outer-after'
            }
            """, ".psm1");
        var assembly = BuildStatementErrorEmission(fixture, framework, 4);
        const string probe = """
            foreach ($command in 'Read-Outer','Read-HandledOuter') {
                foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                    foreach ($text in '1','bad') {
                        $faults=@(); $Error.Clear()
                        $records=[Collections.Generic.List[string]]::new()
                        foreach ($callerCatch in $false,$true) {
                            if (!$callerCatch -and $action -eq 'Stop') { continue }
                            $faults=@(); $Error.Clear(); $records.Clear()
                            if ($callerCatch) {
                                try {
                                    & $command -Text $text -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object {
                                        if ($_ -is [Management.Automation.ErrorRecord]) { [void]$records.Add($_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName) }
                                        else { [void]$records.Add([string]$_) }
                                    }
                                } catch { [void]$records.Add('caller-catch:' + $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName) }
                            } else {
                                & $command -Text $text -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object {
                                    if ($_ -is [Management.Automation.ErrorRecord]) { [void]$records.Add($_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName) }
                                    else { [void]$records.Add([string]$_) }
                                }
                            }
                            [pscustomobject]@{ command=$command; action=$action; text=$text; callerCatch=$callerCatch; records=$records.ToArray();
                                faults=@($faults | ForEach-Object { $_.GetType().Name }); errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId }) } | ConvertTo-Json -Compress
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-nested-emission");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(assembly) + "'; " + probe,
            fixture.RootPath, "compiled-nested-emission");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("FormatException,Read-Inner", original.StandardOutput, StringComparison.Ordinal);
        Assert.True(original.StandardOutput == compiled.StandardOutput, "Original:\n" + original.StandardOutput + "Compiled:\n" + compiled.StandardOutput);
    }

    private static string BuildStatementErrorEmission(ArtifactFixture fixture, string framework, int methodCount)
    {
        var capabilities = PowerShellCompilationCapabilities.BinaryModule;
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(new[] { fixture.ScriptPath },
            "PowerForge.Compiled", "StatementErrorMethods", framework, capabilities);
        Assert.True(typed.Diagnostics.Length == 0, string.Join(Environment.NewLine, typed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(methodCount, typed.Methods.Length);
        Assert.All(typed.Methods, method => Assert.True(method.RequiresPowerShellStatementErrors));
        Directory.CreateDirectory(fixture.OutputPath);
        File.WriteAllText(Path.Combine(fixture.OutputPath, "Compiled.cs"), typed.SourceCode);
        File.WriteAllText(Path.Combine(fixture.OutputPath, "Cmdlets.cs"), PowerShellBinaryCmdletSourceGenerator.Generate(typed, null, framework));
        foreach (var source in PowerShellCommandHostRuntimeSource.Render(typed))
            File.WriteAllText(Path.Combine(fixture.OutputPath, source.Key), source.Value);
        var references = framework == "net472"
            ? "<PackageReference Include=\"Microsoft.NETFramework.ReferenceAssemblies\" Version=\"1.0.3\" /><PackageReference Include=\"Microsoft.PowerShell.5.ReferenceAssemblies\" Version=\"1.1.0\" />"
            : "<PackageReference Include=\"Microsoft.PowerShell.SDK\" Version=\"" + (framework == "net8.0" ? "7.4.18" : "7.6.5") + "\" ExcludeAssets=\"runtime\" />";
        var project = Path.Combine(fixture.OutputPath, "Generated.StatementErrors.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>" + framework +
            "</TargetFramework><LangVersion>latest</LangVersion><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>" + references + "</ItemGroup></Project>");
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "--nologo");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        return Path.Combine(fixture.OutputPath, "bin", "Release", framework, "Generated.StatementErrors.dll");
    }
}
