using System.Text;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> StatementErrorHosts()
    {
        yield return new object[] { "net10.0", "pwsh" };
        if (OperatingSystem.IsWindows()) yield return new object[] { "net472", "powershell.exe" };
        var pinnedHost = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        if (!string.IsNullOrWhiteSpace(pinnedHost)) yield return new object[] { "net10.0", pinnedHost };
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorHost_PreservesContinuationPreferencesAndCallerCatch(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        var project = FindStatementErrorFixtureProject();
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "-f", framework, "--nologo");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var assembly = Path.Combine(Path.GetDirectoryName(project)!, "bin", "Release", framework, "Generic.Compiler.StatementErrors.dll");
        using var fixture = ArtifactFixture.Create("""
            function Get-ParsedRecords {
                [CmdletBinding()] param([string[]]$Values)
                'before'
                foreach ($text in $Values) {
                    [int]$parsed = 99
                    $parsed = [int]::Parse($text)
                    $parsed
                }
                'after'
            }
            """);
        const string probe = """
            $ErrorActionPreference = 'Continue'
            function Describe-Fault($item) {
                $record = if ($item -is [System.Management.Automation.ErrorRecord]) { $item } else { $item.ErrorRecord }
                [pscustomobject]@{
                    objectType = $item.GetType().FullName
                    id = $record.FullyQualifiedErrorId
                    type = $record.Exception.GetType().FullName
                    inner = $record.Exception.InnerException.GetType().FullName
                    category = [string]$record.CategoryInfo.Category
                    message = $record.Exception.Message
                }
            }
            $cases = @(
                @{name='empty';values=[string[]]@()},
                @{name='one';values=[string[]]@('1')},
                @{name='many';values=[string[]]@('1','bad','2')})
            foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                foreach ($case in $cases) {
                    $faults = @()
                    $records = @(Get-ParsedRecords -Values $case.values -ErrorAction $action -ErrorVariable faults 2>$null)
                    [pscustomobject]@{ context='normal'; case=$case.name; action=$action; records=$records; faults=@($faults | ForEach-Object { Describe-Fault $_ }) } | ConvertTo-Json -Depth 6 -Compress
                    $faults = @()
                    $records = [Collections.Generic.List[object]]::new()
                    $caught = $null
                    try { Get-ParsedRecords -Values $case.values -ErrorAction $action -ErrorVariable faults 2>$null | ForEach-Object { [void]$records.Add($_) } }
                    catch { $caught = Describe-Fault $_ }
                    [pscustomobject]@{ context='caller-catch'; case=$case.name; action=$action; records=@($records.ToArray()); caught=$caught; faults=@($faults | ForEach-Object { Describe-Fault $_ }) } | ConvertTo-Json -Depth 6 -Compress
                }
            }
            $mixed = @(Get-ParsedRecords -Values '1','bad','2' -ErrorAction Continue 2>&1)
            $mixed | ForEach-Object { if ($_ -is [System.Management.Automation.ErrorRecord]) { 'redirect:' + $_.FullyQualifiedErrorId } else { 'output:' + [string]$_ } }
            'preference:' + $ErrorActionPreference
            & {
                Set-Variable ErrorActionPreference -Value Stop -Option ReadOnly
                $readOnlyRecords = @(Get-ParsedRecords -Values 'bad','2' -ErrorAction Continue 2>$null)
                'readonly:' + ($readOnlyRecords -join ',') + ':' + $ErrorActionPreference
            }
            $firstFaults = @()
            $secondFaults = @()
            $null = Get-ParsedRecords -Values 'bad' -ErrorAction Continue -ErrorVariable firstFaults 2>$null
            $firstCount = $firstFaults.Count
            $null = Get-ParsedRecords -Values 'bad','bad' -ErrorAction Continue -ErrorVariable secondFaults 2>$null
            'lifetimes:' + $firstCount + ':' + $firstFaults.Count + ':' + $secondFaults.Count
            $stoppedFaults = @()
            $stopped = @(Get-ParsedRecords -Values 'bad','2' -ErrorAction Continue -ErrorVariable stoppedFaults 2>$null | Select-Object -First 2)
            $stoppedCount = $stoppedFaults.Count
            $nextFaults = @()
            $null = Get-ParsedRecords -Values 'bad' -ErrorAction Continue -ErrorVariable nextFaults 2>$null
            'stopped:' + ($stopped -join ',') + ':' + $stoppedCount + ':' + $stoppedFaults.Count + ':' + $nextFaults.Count
            foreach ($inherited in 'Continue','SilentlyContinue','Ignore') {
                & {
                    $ErrorActionPreference = $inherited
                    $inheritedRecords = @(Get-ParsedRecords -Values 'bad','2' 2>$null)
                    'inherited:' + $inherited + ':' + ($inheritedRecords -join ',') + ':' + $ErrorActionPreference
                }
            }
            """;
        var original = RunStatementErrorProbe(host, ". '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe, fixture.RootPath, "original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(assembly) + "'; " + probe, fixture.RootPath, "compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("\"records\":[\"before\",1,99,2,\"after\"]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("FormatException,Get-ParsedRecords", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("redirect:FormatException", original.StandardOutput, StringComparison.Ordinal);
        var expectedLines = original.StandardOutput.Trim().Split('\n');
        var actualLines = compiled.StandardOutput.Trim().Split('\n');
        Assert.Equal(expectedLines.Length, actualLines.Length);
        for (var index = 0; index < expectedLines.Length; index++)
            Assert.True(expectedLines[index] == actualLines[index],
                "Original: " + expectedLines[index] + Environment.NewLine + "Compiled: " + actualLines[index]);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorHost_PreservesHandlersExplicitThrowAndFinally(string framework, string host)
    {
        if (framework == "net472" && !OperatingSystem.IsWindows()) return;
        var project = FindStatementErrorFixtureProject();
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "-f", framework, "--nologo");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var assembly = Path.Combine(Path.GetDirectoryName(project)!, "bin", "Release", framework, "Generic.Compiler.StatementErrors.dll");
        using var fixture = ArtifactFixture.Create("""
            function Get-HandledRecords {
                [CmdletBinding()] param([string]$Mode)
                'before'
                if ($Mode -eq 'finally') {
                    try { [int]::Parse('bad'); 'inside-after' }
                    finally { 'cleanup' }
                } else {
                    try {
                        if ($Mode -eq 'throw') { throw [InvalidOperationException]::new('authored') }
                        [int]::Parse('bad')
                        'inside-after'
                    }
                    catch [FormatException] { 'format'; $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName }
                    catch [InvalidOperationException] { 'invalid'; $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName }
                    catch { 'other'; $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName }
                    finally { 'cleanup' }
                }
                'after'
            }
            """);
        const string probe = """
            $ErrorActionPreference = 'Continue'
            foreach ($mode in 'parse','throw','finally') {
                foreach ($action in 'Continue','SilentlyContinue','Stop') {
                    $records = [Collections.Generic.List[object]]::new()
                    $caught = $null
                    try { Get-HandledRecords -Mode $mode -ErrorAction $action 2>$null | ForEach-Object { [void]$records.Add($_) } }
                    catch { $caught = $_.FullyQualifiedErrorId + ':' + $_.Exception.GetType().FullName }
                    [pscustomobject]@{ mode=$mode; action=$action; records=$records.ToArray(); caught=$caught } | ConvertTo-Json -Compress
                }
            }
            'preference:' + $ErrorActionPreference
            """;
        var original = RunStatementErrorProbe(host, ". '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe, fixture.RootPath, "original-handlers");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(assembly) + "'; " + probe, fixture.RootPath, "compiled-handlers");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("\"records\":[\"before\",\"format\",\"FormatException:System.FormatException\",\"cleanup\",\"after\"]", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"records\":[\"before\",\"invalid\",", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorHost_PreservesAuthoredInvocationExceptions(string framework, string host)
    {
        var project = FindStatementErrorFixtureProject();
        var build = RunProcess("dotnet", "build", project, "-c", "Release", "-f", framework, "--nologo");
        Assert.True(build.ExitCode == 0, build.StandardOutput + build.StandardError);
        var assembly = Path.Combine(Path.GetDirectoryName(project)!, "bin", "Release", framework, "Generic.Compiler.StatementErrors.dll");
        using var fixture = ArtifactFixture.Create("""
            function Get-FailureRecords {
                [CmdletBinding()] param([string]$Kind)
                'before'
                [Generic.Compiler.StatementErrors.InvocationFailureProbe]::Invoke($Kind)
                'after'
            }
            """);
        const string probe = """
            function Describe-InvocationFault($record) {
                $chain = [Collections.Generic.List[string]]::new()
                $exception = $record.Exception
                while ($null -ne $exception) {
                    $chain.Add($exception.GetType().FullName + ':' + $exception.Message)
                    $exception = $exception.InnerException
                }
                [pscustomobject]@{ id=$record.FullyQualifiedErrorId; chain=$chain.ToArray() }
            }
            foreach ($kind in 'format','wrapped','double-wrapped','wrapped-method','wrapped-depth','method','invocation','depth') {
                $records = @(Get-FailureRecords -Kind $kind -ErrorAction Continue 2>&1)
                [pscustomobject]@{ kind=$kind; records=@($records | ForEach-Object {
                    if ($_ -is [System.Management.Automation.ErrorRecord]) { Describe-InvocationFault $_ } else { $_ }
                }) } | ConvertTo-Json -Depth 6 -Compress
                $caught = $null
                $records = [Collections.Generic.List[object]]::new()
                try { Get-FailureRecords -Kind $kind -ErrorAction Continue | ForEach-Object { [void]$records.Add($_) } }
                catch [FormatException] { $caught = 'format' }
                catch [System.Management.Automation.MethodException] { $caught = 'method' }
                catch { $caught = Describe-InvocationFault $_ }
                [pscustomobject]@{ kind=$kind; records=$records.ToArray(); caught=$caught } | ConvertTo-Json -Depth 6 -Compress
            }
            """;
        var import = "Import-Module '" + EscapeStatementErrorPath(assembly) + "'; ";
        var original = RunStatementErrorProbe(host, import + ". '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-exceptions");
        var compiled = RunStatementErrorProbe(host, import + probe, fixture.RootPath, "compiled-exceptions");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("authored method failure", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("wrapped-method", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("wrapped-depth", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    private static string FindStatementErrorFixtureProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "PowerForge.Tests", "Fixtures",
                "PowerShellCompilationStatementErrorFixture", "PowerShellCompilationStatementErrorFixture.csproj");
            if (File.Exists(project)) return project;
        }
        throw new DirectoryNotFoundException("The statement-error host fixture project could not be found.");
    }

    private static string EscapeStatementErrorPath(string path) => path.Replace("'", "''", StringComparison.Ordinal);

    private static (int ExitCode, string StandardOutput, string StandardError) RunStatementErrorProbe(
        string host, string source, string root, string name)
    {
        var script = Path.Combine(root, name + ".ps1");
        File.WriteAllText(script, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return RunProcess(host, "-NoProfile", "-NonInteractive", "-File", script);
    }
}
