using System.Text;

namespace PowerForge.Tests;

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
    FunctionsToExport = @('Get-DefaultStateFixture')
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(Path.Combine(root, "DefaultStateFixture.psm1"), """
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
        foreach ($entry in @(
            @('SharedStringReferences', [object[]]@($sharedString, $sharedString)),
            @('SharedBoxReferences', [object[]]@($sharedBox, $sharedBox)),
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
        $parameters
    }
}
""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var payload = new DocumentationEngine(new PowerShellRunner(), new NullLogger())
                .ExtractHelpPayload(root, manifestPath, TimeSpan.FromMinutes(1));
            var command = Assert.Single(payload.Commands);
            foreach (var name in new[] { "SharedStringReferences", "SharedBoxReferences", "SessionBoundScript" })
            {
                var parameter = Assert.Single(command.Parameters, item => item.Name == name);
                Assert.True(string.IsNullOrEmpty(parameter.DefaultValue), name);
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
