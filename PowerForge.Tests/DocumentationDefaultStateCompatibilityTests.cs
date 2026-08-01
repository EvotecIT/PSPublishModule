using System.Text;

namespace PowerForge.Tests;

[CollectionDefinition("DocumentationPowerShellHost", DisableParallelization = true)]
public sealed class DocumentationPowerShellHostCollection
{
}

[Collection("DocumentationPowerShellHost")]
public sealed class DocumentationDefaultStateCompatibilityTests
{
    [Fact]
    public void DocumentationEngine_RejectsDefaultsWhoseObjectIdentityOrSessionStateCannotBeRecreated()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-doc-default-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "DefaultStateFixture.psd1");
            File.WriteAllText(manifestPath, """
@{
    RootModule = 'DefaultStateFixture.psm1'
    ModuleVersion = '1.0.0'
    GUID = '55555555-5555-5555-5555-555555555555'
    FunctionsToExport = @('Get-DefaultStateFixture', 'ConvertToPowerShellDefaultValue')
    AliasesToExport = @('GetCanonicalTypeNameFromType')
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(root, "DefaultStateFixture.psm1"), """
Add-Type -TypeDefinition @'
using System;
using System.Reflection;
using System.Management.Automation;

public static class PowerForgeAutomationNullFixture {
    private static object Value() {
        var type = typeof(PSObject).Assembly.GetType(
            "System.Management.Automation.Internal.AutomationNull", true);
        return type.GetProperty("Value", BindingFlags.Public | BindingFlags.Static)
            .GetValue(null, null);
    }

    public static PSDefaultValueAttribute Create() {
        var attribute = new PSDefaultValueAttribute();
        attribute.Value = Value();
        return attribute;
    }

    public static PSDefaultValueAttribute CreateArray() {
        var attribute = new PSDefaultValueAttribute();
        attribute.Value = new object[] { Value() };
        return attribute;
    }

    public static PSDefaultValueAttribute CreateDictionary(bool key) {
        var attribute = new PSDefaultValueAttribute();
        var dictionary = new System.Collections.Specialized.OrderedDictionary();
        dictionary.Add(key ? Value() : "key", key ? "value" : Value());
        attribute.Value = dictionary;
        return attribute;
    }
}
'@

$sessionPowerShell = [powershell]::Create()
try {
    $script:sessionBoundDefault = $sessionPowerShell.AddScript(
        '$other = ''other''; function PowerForgeDocumentationSessionDefault { $other }; (Get-Command PowerForgeDocumentationSessionDefault).ScriptBlock').Invoke()[0]
} finally {
    $sessionPowerShell.Dispose()
}
$sessionStateProperty = [scriptblock].GetProperty(
    'SessionStateInternal',
    [System.Reflection.BindingFlags]'Instance,Public,NonPublic')
if ($script:sessionBoundDefault.Module -or $script:sessionBoundDefault.File -or
    $null -eq $sessionStateProperty.GetValue($script:sessionBoundDefault, $null)) {
    throw 'The session-bound ScriptBlock fixture must retain only hidden session state.'
}

function Get-DefaultStateFixture {
    [CmdletBinding()]
    param()

    dynamicparam {
        $parameters = [System.Management.Automation.RuntimeDefinedParameterDictionary]::new()

        $sharedString = -join @('shared', '-', 'string')
        $sharedBox = [object]42
        $sharedBacking = [System.Collections.Generic.List[int]]::new()
        $sharedBacking.Add(1)
        $sharedCollections = [object[]]@(
            [System.Collections.ObjectModel.Collection[int]]::new($sharedBacking),
            [System.Collections.ObjectModel.Collection[int]]::new($sharedBacking))
        $sharedComparer = [System.StringComparer]::Create(
            [System.Globalization.CultureInfo]::GetCultureInfo('tr-TR'), $true)
        $internedString = [string]::Intern((-join @('interned', '-', 'value')))
        $nonInternedEmptyString = [string]::Copy('')
        $uriText = -join @('relative', '/', 'path')
        $uri = [uri]::new($uriText, [System.UriKind]::Relative)
        if (-not [object]::ReferenceEquals($uriText, $uri.OriginalString)) {
            throw 'The URI fixture must retain its original backing string.'
        }
        $firstDictionary = [System.Collections.Generic.Dictionary[string,int]]::new($sharedComparer)
        $secondDictionary = [System.Collections.Generic.Dictionary[string,int]]::new($sharedComparer)
        $firstDictionary.Add('one', 1)
        $secondDictionary.Add('two', 2)
        $createdInvariantComparer = [System.StringComparer]::Create(
            [System.Globalization.CultureInfo]::InvariantCulture, $false)
        $createdInvariantDictionary = [System.Collections.Generic.Dictionary[string,int]]::new(
            $createdInvariantComparer)
        $createdInvariantDictionary.Add('invariant', 1)
        foreach ($entry in @(
            @('SharedStringReferences', [object[]]@($sharedString, $sharedString)),
            @('SharedBoxReferences', [object[]]@($sharedBox, $sharedBox)),
            @('SharedCollectionBacking', $sharedCollections),
            @('SharedCultureComparer', [object[]]@($firstDictionary, $secondDictionary)),
            @('InternedString', $internedString),
            @('NonInternedEmptyString', $nonInternedEmptyString),
            @('CreatedInvariantComparer', $createdInvariantDictionary),
            @('SharedUriBacking', [object[]]@($uri, $uri.OriginalString)),
            @('SessionBoundScript', $script:sessionBoundDefault)
        )) {
            if ($entry[0] -eq 'SharedBoxReferences' -and
                -not [object]::ReferenceEquals($entry[1][0], $entry[1][1])) {
                throw 'The shared boxed-value fixture must retain object identity.'
            }
            $attributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
            $default = [System.Management.Automation.PSDefaultValueAttribute]::new()
            $default.Value = $entry[1]
            $attributes.Add($default)
            $parameters.Add($entry[0], [System.Management.Automation.RuntimeDefinedParameter]::new(
                $entry[0], [object], $attributes))
        }

        $automationNullAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $automationNullAttributes.Add([PowerForgeAutomationNullFixture]::Create())
        $parameters.Add('AutomationNull', [System.Management.Automation.RuntimeDefinedParameter]::new(
            'AutomationNull', [object], $automationNullAttributes))
        foreach ($entry in @(
            @('AutomationNullArray', [PowerForgeAutomationNullFixture]::CreateArray()),
            @('AutomationNullDictionaryKey', [PowerForgeAutomationNullFixture]::CreateDictionary($true)),
            @('AutomationNullDictionaryValue', [PowerForgeAutomationNullFixture]::CreateDictionary($false))
        )) {
            $attributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
            $attributes.Add($entry[1])
            $parameters.Add($entry[0], [System.Management.Automation.RuntimeDefinedParameter]::new(
                $entry[0], [object], $attributes))
        }

        $collidingValidateSetAttributes = [System.Collections.ObjectModel.Collection[System.Attribute]]::new()
        $collidingValidateSetAttributes.Add([System.Management.Automation.ValidateSetAttribute]::new(
            [string[]]@("A$([char]1)", 'A([char]1)', "(-join @('A', ([char]1)))")))
        $parameters.Add('CollidingValidateSet', [System.Management.Automation.RuntimeDefinedParameter]::new(
            'CollidingValidateSet', [string], $collidingValidateSetAttributes))
        $parameters
    }
}

function ConvertToPowerShellDefaultValue {
    throw 'The documented module clobbered a collector helper.'
}

Set-Alias -Name GetCanonicalTypeNameFromType -Value Write-Error

Export-ModuleMember -Function Get-DefaultStateFixture, ConvertToPowerShellDefaultValue -Alias GetCanonicalTypeNameFromType
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var hosts = OperatingSystem.IsWindows()
                ? new[] { "pwsh.exe", "powershell.exe" }
                : new[] { "pwsh" };
            foreach (var host in hosts)
            {
                var payload = new DocumentationEngine(
                        new ExecutablePowerShellRunner(host, root),
                        new NullLogger())
                    .ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(2));
                var command = Assert.Single(payload.Commands, item => item.Name == "Get-DefaultStateFixture");
                Assert.Contains(payload.Commands, item => item.Name == "ConvertToPowerShellDefaultValue");
                foreach (var name in new[]
                         {
                             "SharedStringReferences", "SharedBoxReferences", "SharedCollectionBacking",
                             "SharedCultureComparer", "SharedUriBacking", "SessionBoundScript", "AutomationNull",
                             "AutomationNullArray", "AutomationNullDictionaryKey", "AutomationNullDictionaryValue"
                         })
                {
                    var parameter = Assert.Single(command.Parameters, item => item.Name == name);
                    Assert.True(string.IsNullOrEmpty(parameter.DefaultValue), name);
                }

                var interned = Assert.Single(command.Parameters, item => item.Name == "InternedString");
                Assert.Equal("[string]::Intern('interned-value')", interned.DefaultValue);
                var verificationPath = Path.Combine(root, "VerifyInterned.ps1");
                File.WriteAllText(verificationPath, """
param([string]$Expression)
$value = & ([scriptblock]::Create($Expression))
if (-not [object]::ReferenceEquals($value, [string]::IsInterned($value))) {
    throw 'The reconstructed value is not the intern-pool singleton.'
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                var verification = new ExecutablePowerShellRunner(host, root).Run(
                    new PowerShellRunRequest(
                        verificationPath,
                        new[] { interned.DefaultValue },
                        TimeSpan.FromMinutes(1)));
                Assert.Equal(0, verification.ExitCode);

                var nonInternedEmpty = Assert.Single(
                    command.Parameters,
                    item => item.Name == "NonInternedEmptyString");
                Assert.Equal("[string]::Copy('')", nonInternedEmpty.DefaultValue);
                var nonInternedVerificationPath = Path.Combine(root, "VerifyNonInternedEmpty.ps1");
                File.WriteAllText(nonInternedVerificationPath, """
param([string]$Expression)
$value = & ([scriptblock]::Create($Expression))
if ([object]::ReferenceEquals($value, [string]::Empty) -or
    [object]::ReferenceEquals($value, [string]::IsInterned($value))) {
    throw 'The reconstructed empty string unexpectedly uses the intern-pool singleton.'
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                var nonInternedVerification = new ExecutablePowerShellRunner(host, root).Run(
                    new PowerShellRunRequest(
                        nonInternedVerificationPath,
                        new[] { nonInternedEmpty.DefaultValue },
                        TimeSpan.FromMinutes(1)));
                Assert.Equal(0, nonInternedVerification.ExitCode);

                var invariantComparer = Assert.Single(
                    command.Parameters,
                    item => item.Name == "CreatedInvariantComparer");
                Assert.Contains(
                    "[System.StringComparer]::Create([System.Globalization.CultureInfo]::InvariantCulture, $false)",
                    invariantComparer.DefaultValue,
                    StringComparison.Ordinal);
                var comparerVerificationPath = Path.Combine(root, "VerifyInvariantComparer.ps1");
                File.WriteAllText(comparerVerificationPath, """
param([string]$Expression)
$dictionary = & ([scriptblock]::Create($Expression))
if ([object]::ReferenceEquals($dictionary.Comparer, [System.StringComparer]::InvariantCulture)) {
    throw 'The reconstructed comparer unexpectedly aliases the invariant singleton.'
}
if ($dictionary.Comparer.Compare('a', 'A') -eq 0) {
    throw 'The reconstructed comparer unexpectedly ignores case.'
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                var comparerVerification = new ExecutablePowerShellRunner(host, root).Run(
                    new PowerShellRunRequest(
                        comparerVerificationPath,
                        new[] { invariantComparer.DefaultValue },
                        TimeSpan.FromMinutes(1)));
                Assert.Equal(0, comparerVerification.ExitCode);

                var colliding = Assert.Single(command.Parameters, item => item.Name == "CollidingValidateSet");
                Assert.Equal(
                    new[]
                    {
                        "(-join @('A', ([char]1))) [encoded 1]",
                        "'A([char]1)'",
                        "(-join @('A', ([char]1)))"
                    },
                    colliding.PossibleValues);

                var hostOutput = Path.Combine(root, host.Replace('.', '-'));
                var mamlPath = new MamlHelpWriter().WriteExternalHelpFile(
                    payload,
                    "DefaultStateFixture",
                    hostOutput);
                var maml = File.ReadAllText(mamlPath);
                Assert.Contains("(-join @('A', ([char]1))) [encoded 1]", maml, StringComparison.Ordinal);
                Assert.Contains("'A([char]1)'", maml, StringComparison.Ordinal);
                Assert.DoesNotContain('\u0001', maml);
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

    private sealed class ExecutablePowerShellRunner : IPowerShellRunner
    {
        private readonly string _executable;
        private readonly string _workingDirectory;
        private readonly PowerShellRunner _inner = new();

        public ExecutablePowerShellRunner(string executable, string workingDirectory)
        {
            _executable = executable;
            _workingDirectory = workingDirectory;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _inner.Run(new PowerShellRunRequest(
                request.ScriptPath!,
                request.Arguments,
                request.Timeout,
                request.PreferPwsh,
                request.WorkingDirectory ?? _workingDirectory,
                request.EnvironmentVariables,
                _executable,
                request.CaptureOutput,
                request.CaptureError,
                request.OutputLineReceived,
                request.ErrorLineReceived));
    }
}
