function Out-More {
    [cmdletbinding()]
    [alias('om')]
    param(
        [Parameter(Mandatory, ValueFromPipeline)]
        [object[]]$InputObject,

        [parameter(Position = 0, HelpMessage = 'Specify the approximate number of items to page.')]
        [ValidateRange(1, 1000)]
        [Alias('i', 'page')]
        [int]$Count = 50,

        [Alias('cls')]
        [Switch]$ClearScreen
    )

    begin {
        #tags are used for categorizing the command
        #cmdTags = console
        if ($ClearScreen) {
            Clear-Host
        }

        Write-Verbose "Starting: $($MyInvocation.MyCommand)"
        Write-Verbose "Using a count of $count"

        #initialize an array to hold objects
        Write-Verbose 'Initializing data array'
        $data = @()

        #initialize some variables to control flow
        $ShowAll = $False
        $ShowNext = $False
        $Ready = $False
        $Quit = $False

        #22 July 2026 ANSI code to clear the prompt when displaying the next page
        function _clearPrompt {
            $e = [char]27
            "$($e)[2A"
            "$($e)[K"
            "$($e)[3A"
            "$($e)[1G"
        }
    } #begin

    process {
        if ($Quit) {
            Write-Verbose 'Quitting'
            break
        }
        elseif ($ShowAll) {
            $InputObject
        }
        elseif ($ShowNext) {
            Write-Verbose 'Show Next'
            $ShowNext = $False
            $Ready = $True
            $data = , $InputObject
        }
        elseif ($data.count -lt $count) {
            Write-Verbose 'Adding data'
            $data += $InputObject
        }
        else {
            #write the data to the pipeline
            $data
            #reset data
            $data = , $InputObject
            $Ready = $True
        }

        if ($Ready) {
            #pause
            do {
                Write-Host '[M]ore [A]ll [N]ext [Q]uit ' -ForegroundColor Green -NoNewline
                $r = Read-Host
                if ($r.Length -eq 0 -or $r -match '^m') {
                    #don't really do anything
                    $Asked = $True
                    _clearPrompt
                }
                else {
                    switch -Regex ($r) {
                        '^n' {
                            _clearPrompt
                            $ShowNext = $True
                            $InputObject
                            $Asked = $True
                        }
                        '^a' {
                            _clearPrompt
                            $InputObject
                            $Asked = $True
                            $ShowAll = $True
                        }
                        '^q' {
                            #bail out
                            $Asked = $True
                            $Quit = $True
                            $ShowAll = $True
                        }
                        default {
                            $Asked = $False
                        }
                    } #Switch

                } #else
            } until ($Asked)

            $Ready = $False
            $Asked = $False
        } #else

    } #process

    end {
        #test if data is from a Get-Help command in
        #which case it will be a single string that needs
        #to be broken apart

        if ([regex]::Matches($data, "`n").count -gt 1) {
            [void]$PSBoundParameters.remove('InputObject')
            Write-Verbose 'Splitting input and re-running through Out-More'
            $data.split("`n") | Out-More @PSBoundParameters
        }
        elseif ($data[0].PSObject.TypeNames -contains 'MamlCommandHelpInfo') {
            Write-Verbose 'Help output detected'
            [void]$PSBoundParameters.remove('InputObject')
            ($data | Out-String).split("`n") | Out-More @PSBoundParameters
        }
        #display whatever is left in $data
        if ($data -and -not $ShowAll) {
            Write-Verbose 'Displaying remaining data'
            $data
        }
        Write-Verbose "Ending: $($MyInvocation.MyCommand)"
    } #end

} #end Out-More