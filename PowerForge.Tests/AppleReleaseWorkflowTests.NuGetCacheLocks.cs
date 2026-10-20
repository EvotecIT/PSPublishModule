namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Fact]
    public void PinnedAppleChildProcessesKeepSharedNuGetLocksAcrossPrivateTempRoots()
    {
        var root = FindRepoRoot();
        var parent = Path.Combine(root, ".test-temp", $"pinned-nuget-locks-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(parent);
            var harness = Path.Combine(parent, "nuget-locks.ps1");
            File.WriteAllText(harness, PinnedNuGetLocksHarness);
            var sharedScratch = Path.Combine(parent, "shared-scratch");
            Directory.CreateDirectory(sharedScratch);
            foreach (var job in new[] { "first", "second" })
            {
                var temporary = Path.Combine(parent, job);
                var packages = Path.Combine(temporary, "private-packages");
                Directory.CreateDirectory(packages);
                var result = Run("pwsh", parent,
                    "-NoLogo", "-NoProfile", "-File", harness,
                    "-Owner", root, "-Temporary", temporary,
                    "-SharedScratch", sharedScratch, "-PrivatePackages", packages);
                result.EnsureSuccess();
                Assert.Contains("PASS", result.StandardOutput, StringComparison.Ordinal);
            }
        }
        finally
        {
            DeletePinnedEvidenceDirectory(parent);
        }
    }

    private const string PinnedNuGetLocksHarness = """
        param([string] $Owner, [string] $Temporary, [string] $SharedScratch, [string] $PrivatePackages)
        $ErrorActionPreference = 'Stop'
        $env:NUGET_SCRATCH = $SharedScratch
        $env:TMPDIR = $Temporary
        $env:POWERFORGE_NUGET_TEST_SECRET = 'must-not-reach-child'
        $script:validatedCredentialEnvironment = @{}
        $script:validatedCredentialKeyPath = ''
        $tokens = $null; $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            (Join-Path $Owner 'scripts/Invoke-PinnedPowerForge.ps1'), [ref]$tokens, [ref]$errors)
        if ($errors.Count) { throw 'Pinned helper contains parse errors.' }
        foreach ($name in @('Get-RedactedToolText', 'Invoke-RedactedProcess')) {
            $function = $ast.Find({param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
            }, $true)
            if ($null -eq $function) { throw "Missing pinned helper function: $name" }
            Invoke-Expression $function.Extent.Text
        }
        $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        foreach ($item in @(
            @{ Name = 'temp'; Expected = $SharedScratch },
            @{ Name = 'global-packages'; Expected = $PrivatePackages })) {
            $output = Join-Path $Temporary ($item.Name + '.txt')
            $code = Invoke-RedactedProcess -FilePath $dotnet -WorkingDirectory $Temporary `
                -Arguments @('nuget', 'locals', $item.Name, '--list') `
                -NuGetPackagesPath $PrivatePackages -CaptureStandardOutputPath $output
            if ($code -ne 0) { throw "NuGet folder query failed: $($item.Name)" }
            $text = Get-Content -LiteralPath $output -Raw
            $match = [regex]::Match($text, '(?m)^(?:info : )?' + [regex]::Escape($item.Name) + ': (.+)\r?$')
            if (-not $match.Success) { throw "Missing NuGet folder result: $text" }
            $actual = [IO.Path]::GetFullPath($match.Groups[1].Value.Trim()).TrimEnd([IO.Path]::DirectorySeparatorChar)
            $expected = [IO.Path]::GetFullPath($item.Expected).TrimEnd([IO.Path]::DirectorySeparatorChar)
            if ($actual -ne $expected) { throw "NuGet $($item.Name) changed: expected '$expected', actual '$actual'." }
        }
        $pwsh = Join-Path $PSHOME $(if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' })
        $output = Join-Path $Temporary 'unrelated-environment.txt'
        $code = Invoke-RedactedProcess -FilePath $pwsh -WorkingDirectory $Temporary `
            -Arguments @('-NoLogo', '-NoProfile', '-Command', '[Console]::Out.Write([Environment]::GetEnvironmentVariable("POWERFORGE_NUGET_TEST_SECRET"))') `
            -NuGetPackagesPath $PrivatePackages -CaptureStandardOutputPath $output
        if ($code -ne 0 -or (Get-Item -LiteralPath $output).Length -ne 0) {
            throw 'An unrelated environment value reached the isolated child.'
        }
        'PASS'
        """;
}
