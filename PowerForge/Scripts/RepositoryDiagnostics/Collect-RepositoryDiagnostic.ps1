# Native host adapter. Only allowlisted metadata crosses the process boundary.
function ConvertTo-PFDiagnosticUri {
    param([string]$Value)
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -notin @('http', 'https')) { return '' }
    $builder = [UriBuilder]::new($uri)
    $builder.UserName = ''; $builder.Password = ''; $builder.Query = ''; $builder.Fragment = ''
    $builder.Uri.AbsoluteUri
}

function Get-PFDiagnosticFileStatus {
    param([string]$Path)
    try { $null = [IO.File]::GetAttributes($Path); return 'Present' }
    catch [IO.FileNotFoundException] { return 'Missing' }
    catch [IO.DirectoryNotFoundException] { return 'Missing' }
    catch { return 'Unreadable' }
}

function Import-PFDiagnosticAssembly {
    param([Reflection.Assembly]$Owner, [Reflection.AssemblyName]$Reference, [string]$ModuleBase)
    if ($PSVersionTable.PSEdition -eq 'Core') {
        return [Runtime.Loader.AssemblyLoadContext]::GetLoadContext($Owner).LoadFromAssemblyName($Reference)
    }
    try { return [Reflection.Assembly]::Load($Reference) }
    catch {
        # Desktop does not use the module's Core ALC resolver. Load the exact
        # referenced identity from the publisher's own dependency directory.
        $path = [IO.Path]::Combine($ModuleBase, 'dependencies', $Reference.Name + '.dll')
        if ([Reflection.AssemblyName]::GetAssemblyName($path).FullName -ne $Reference.FullName) { throw }
        return [Reflection.Assembly]::LoadFrom($path)
    }
}

function Write-PFRepositoryDiagnostic {
    param([string]$Repository, [string]$Tool, [string]$ExpectedUri)
    try {
        if ([string]::IsNullOrWhiteSpace($Repository)) { $Repository = 'PSGallery' }
        $moduleName = if ($Tool -eq 'PSResourceGet') { 'Microsoft.PowerShell.PSResourceGet' } else { 'PowerShellGet' }
        $module = Get-Module $moduleName | Select-Object -First 1
        $runtime = [ordered]@{
            Executable = [Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
            PowerShellVersion = $PSVersionTable.PSVersion.ToString()
            DotNetVersion = [Environment]::Version.ToString()
            WorkingDirectory = [Environment]::CurrentDirectory
            ModuleAvailable = $null -ne $module
            ModuleVersion = if ($module) { $module.Version.ToString() } else { $null }
            ModulePath = if ($module) { $module.Path } else { $null }
            NuGetVersion = $null
            SettingsInspection = 'Unavailable'
            CredentialProviderConfigured = $false
            CredentialReferenceConfigured = $false
        }
        $report = [ordered]@{ CollectedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); Runtime = $runtime; RepositoryFound = $null; RegisteredUri = ''; PublishSourceUri = ''; ConfigurationFiles = @(); AuthenticationInputs = @() }
        $registeredUri = ''
        $registrationPath = [IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'PSResourceGet', 'PSResourceRepository.xml')
        # Legacy Get-PSRepository initializes and migrates stores, including endpoint requests.
        # Leave its registration unknown rather than invoking a getter with side effects.
        $usesPSResourceGet = $Tool -eq 'PSResourceGet' -or ($module -and $module.Version.Major -ge 3)
        $canReadRegistration = $usesPSResourceGet -and [IO.File]::Exists($registrationPath)
        if ($module -and $canReadRegistration) {
            try {
                $registration = Get-PSResourceRepository -Name $Repository -ErrorAction Stop
                $report.RepositoryFound = $null -ne $registration
                if ($registration) {
                    $registeredUri = [string]$registration.Uri
                    $report.RegisteredUri = ConvertTo-PFDiagnosticUri $registeredUri
                    $runtime.CredentialProviderConfigured = $null -ne $registration.PSObject.Properties['CredentialProvider'] -and $null -ne $registration.CredentialProvider
                    $runtime.CredentialReferenceConfigured = $null -ne $registration.PSObject.Properties['CredentialInfo'] -and $null -ne $registration.CredentialInfo
                }
            } catch { $report.RepositoryFound = $null }
        } elseif ($module -and $usesPSResourceGet) {
            $report.RepositoryFound = $false
        }
        $target = if ($registeredUri) { $registeredUri } else { $ExpectedUri }
        # PSResourceGet passes this source to NuGet after its v2 publish rewrite.
        if ($usesPSResourceGet -and $target.EndsWith('/v2', [StringComparison]::OrdinalIgnoreCase)) { $target += '/package' }
        $report.PublishSourceUri = ConvertTo-PFDiagnosticUri $target
        $settings = $null
        $configPaths = [Collections.Generic.List[string]]::new()
        if ($module -and $usesPSResourceGet) {
            try {
                # Resolve public NuGet APIs through the publisher's own assembly context, including its ALC.
                $publisherCommand = Get-Command Publish-PSResource -ErrorAction Stop
                $publisher = $publisherCommand.ImplementingType.Assembly
                $publisherModuleBase = $publisherCommand.Module.ModuleBase
                $reference = $publisher.GetReferencedAssemblies() | Where-Object { $_.Name -eq 'NuGet.Configuration' } | Select-Object -First 1
                $nuget = Import-PFDiagnosticAssembly $publisher $reference $publisherModuleBase
                $runtime.NuGetVersion = $nuget.GetName().Version.ToString()
                $settingsType = $nuget.GetType('NuGet.Configuration.Settings')
                $commonReference = $nuget.GetReferencedAssemblies() | Where-Object { $_.Name -eq 'NuGet.Common' } | Select-Object -First 1
                $common = Import-PFDiagnosticAssembly $nuget $commonReference $publisherModuleBase
                $folderType = $common.GetType('NuGet.Common.NuGetFolderPath')
                $folder = [Enum]::Parse($folderType, 'UserSettingsDirectory')
                $userSettings = $common.GetType('NuGet.Common.NuGetEnvironment').GetMethod('GetFolderPath').Invoke($null, [object[]]@($folder))
                $defaultConfig = [IO.Path]::Combine($userSettings, 'NuGet.Config')
                $defaultStatus = Get-PFDiagnosticFileStatus $defaultConfig
                if ($defaultStatus -eq 'Missing') { $report.ConfigurationFiles += [ordered]@{ Path = $defaultConfig; Status = 'Missing' } }
                else { $configPaths.Add($defaultConfig) }
                $additional = [IO.Path]::Combine($userSettings, 'config')
                if ([IO.Directory]::Exists($additional)) {
                    $additionalFiles = @([IO.Directory]::EnumerateFiles($additional) | Where-Object { [IO.Path]::GetExtension($_) -ieq '.config' -and [IO.Path]::GetFileName($_) -ine 'NuGet.Config' } | Sort-Object)
                    foreach ($configPath in $additionalFiles) { $configPaths.Add($configPath) }
                }
                # Publish-PSResource uses a null root and null machine-wide settings: only user settings.
                # Loading defaults can create NuGet.Config. Immutable loading preserves inspection's read-only contract.
                $context = [Activator]::CreateInstance($nuget.GetType('NuGet.Configuration.SettingsLoadingContext'))
                $loader = $settingsType.GetMethod('LoadImmutableSettingsGivenConfigPaths')
                $settings = $loader.Invoke($null, [object[]]@($configPaths, $context))
                $runtime.SettingsInspection = 'Complete'
            } catch { $runtime.SettingsInspection = 'Unavailable' }
        }
        $sources = @()
        $effectiveApiKeys = @()
        if ($settings) {
            $apiSection = $settings.GetSection('apikeys')
            if ($apiSection) { $effectiveApiKeys = @($apiSection.Items | Where-Object { $_.PSObject.Properties['Key'] }) }
            $section = $settings.GetSection('packageSources')
            if ($section) {
                $sources = @($section.Items | Where-Object { $_.PSObject.Properties['Value'] -and [string]$_.Value -ceq $target } | ForEach-Object { [string]$_.Key })
            }
        }
        foreach ($configPath in $configPaths) {
                $file = [ordered]@{ Path = $configPath; Status = 'Unreadable' }
                try {
                    if ((Get-PFDiagnosticFileStatus $configPath) -eq 'Missing') { $file.Status = 'Missing' }
                    else {
                        $readerOptions = [Xml.XmlReaderSettings]::new()
                        $readerOptions.DtdProcessing = [Xml.DtdProcessing]::Prohibit
                        $readerOptions.XmlResolver = $null
                        $readerOptions.MaxCharactersInDocument = 4194304
                        $reader = [Xml.XmlReader]::Create($configPath, $readerOptions)
                        try { $document = [Xml.XmlDocument]::new(); $document.XmlResolver = $null; $document.Load($reader) } finally { $reader.Dispose() }
                        $file.Status = 'Readable'
                        foreach ($entry in $document.SelectNodes('/configuration/apikeys/add')) {
                            $key = $entry.GetAttribute('key')
                            $match = if ($key -ceq $target) { 'ExactEndpoint' } else { 'SameHost' }
                            $keyUri = $null; $targetUri = $null
                            if ([Uri]::TryCreate($key, [UriKind]::Absolute, [ref]$keyUri) -and [Uri]::TryCreate($target, [UriKind]::Absolute, [ref]$targetUri) -and $keyUri.Host -eq $targetUri.Host) {
                                $effective = if ($settings) { @($effectiveApiKeys | Where-Object { [string]$_.Key -ceq $key -and [string]$_.Value -ceq $entry.GetAttribute('value') }).Count -gt 0 } else { $null }
                                $report.AuthenticationInputs += [ordered]@{ Kind = 'NuGetApiKey'; Name = (ConvertTo-PFDiagnosticUri $key); Location = $configPath; Present = -not [string]::IsNullOrEmpty($entry.GetAttribute('value')); Effective = $effective; Match = $match; Storage = 'Encrypted' }
                            }
                        }
                        foreach ($entry in $document.SelectNodes('/configuration/packageSourceCredentials/*')) {
                            $name = [Xml.XmlConvert]::DecodeName($entry.LocalName)
                            if ($sources -ccontains $name) {
                                $password = $entry.SelectSingleNode('add[@key="Password"]')
                                $clearText = $entry.SelectSingleNode('add[@key="ClearTextPassword"]')
                                $storage = if ($password) { 'Encrypted' } elseif ($clearText -and $clearText.GetAttribute('value') -match '%[^%]+%') { 'EnvironmentReference' } else { 'ClearText' }
                                $report.AuthenticationInputs += [ordered]@{ Kind = 'SourceCredential'; Name = $name; Location = $configPath; Present = $null -ne $password -or $null -ne $clearText; Match = 'SourceName'; Storage = $storage }
                            }
                        }
                    }
                } catch { $runtime.SettingsInspection = 'Partial' }
                $report.ConfigurationFiles += $file
        }
        foreach ($source in $sources) {
            $name = 'NuGetPackageSourceCredentials_' + $source
            $report.AuthenticationInputs += [ordered]@{ Kind = 'EnvironmentCredential'; Name = $name; Location = 'Process environment'; Present = $null -ne [Environment]::GetEnvironmentVariable($name); Match = 'SourceName'; Storage = 'ProcessEnvironment' }
        }
        $nugetVersion = $null
        if ([Version]::TryParse([string]$runtime.NuGetVersion, [ref]$nugetVersion) -and $nugetVersion -ge [Version]'7.6') {
            $report.AuthenticationInputs += [ordered]@{ Kind = 'ApiKeyEnvironment'; Name = 'NUGET_API_KEY'; Location = 'Process environment'; Present = $null -ne [Environment]::GetEnvironmentVariable('NUGET_API_KEY'); Match = 'ExactEndpoint'; Storage = 'ProcessEnvironment' }
        }
        $json = ConvertTo-Json -InputObject $report -Depth 8 -Compress
        Write-Output ('PFREPO::DIAGNOSTIC::' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)))
    } catch {
        # Metadata collection must never prevent an otherwise valid publish or expose raw exceptions.
        Write-Output 'PFREPO::DIAGNOSTIC::e30='
    }
}
