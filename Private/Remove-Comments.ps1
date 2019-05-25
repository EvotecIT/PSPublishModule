function Remove-Comments {
    # We are not restricting scriptblock type as Tokenize() can take several types
    Param (
        [string] $FilePath,
        [parameter( ValueFromPipeline = $True )] $Scriptblock,
        [string] $ScriptContent
    )

    Begin {
        # Intialize collection
        $Items = @()

        if ($PSBoundParameters['FilePath']) {
            $ScriptBlockString = [IO.File]::ReadAllText((Resolve-Path $FilePath))
            $ScriptBlock = [ScriptBlock]::Create($ScriptBlockString)
        } elseif ($PSBoundParameters['ScriptContent']) {
            $ScriptBlock = [ScriptBlock]::Create($ScriptContent)
        } else {
            # Convert the scriptblock to a string so that it can be referenced with array notation
            #$ScriptBlockString = $ScriptBlock.ToString()
        }
    }

    Process {
        # Collect all of the inputs together
        $Items += $Scriptblock
    }

    End {
        ## Process the script as a single unit

        # Convert input to a single string if needed
        $OldScript = $Items -join [environment]::NewLine

        # If no work to do
        # We're done
        If ( -not $OldScript.Trim( " `n`r`t" ) ) { return }

        # Use the PowerShell tokenizer to break the script into identified tokens
        $Tokens = [System.Management.Automation.PSParser]::Tokenize( $OldScript, [ref]$Null )

        # Define useful, allowed comments
        $AllowedComments = @(
            'requires'
            '.SYNOPSIS'
            '.DESCRIPTION'
            '.PARAMETER'
            '.EXAMPLE'
            '.INPUTS'
            '.OUTPUTS'
            '.NOTES'
            '.LINK'
            '.COMPONENT'
            '.ROLE'
            '.FUNCTIONALITY'
            '.FORWARDHELPCATEGORY'
            '.REMOTEHELPRUNSPACE'
            '.EXTERNALHELP' )

        # Strip out the Comments, but not useful comments
        # (Bug: This will break comment-based help that uses leading # instead of multiline <#,
        # because only the headings will be left behind.)

        $Tokens = $Tokens.ForEach{
            If ( $_.Type -ne 'Comment' ) {
                $_
            } Else {
                $CommentText = $_.Content.Substring( $_.Content.IndexOf( '#' ) + 1 )
                $FirstInnerToken = [System.Management.Automation.PSParser]::Tokenize( $CommentText, [ref]$Null ) |
                    Where-Object { $_.Type -ne 'NewLine' } |
                    Select-Object -First 1
                If ( $FirstInnerToken.Content -in $AllowedComments ) {
                    $_
                }
            } }

        # Initialize script string
        $NewScriptText = ''
        $SkipNext = $False

        # If there are at least 2 tokens to process...
        If ( $Tokens.Count -gt 1 ) {
            # For each token (except the last one)...
            ForEach ( $i in ( 0..($Tokens.Count - 2) ) ) {
                # If token is not a line continuation and not a repeated new line or semicolon...
                If (    -not $SkipNext -and
                    $Tokens[$i  ].Type -ne 'LineContinuation' -and (
                        $Tokens[$i  ].Type -notin ( 'NewLine', 'StatementSeparator' ) -or
                        $Tokens[$i + 1].Type -notin ( 'NewLine', 'StatementSeparator', 'GroupEnd' ) ) ) {
                    # Add Token to new script
                    # For string and variable, reference old script to include $ and quotes
                    If ( $Tokens[$i].Type -in ( 'String', 'Variable' ) ) {
                        $NewScriptText += $OldScript.Substring( $Tokens[$i].Start, $Tokens[$i].Length )
                    } Else {
                        $NewScriptText += $Tokens[$i].Content
                    }

                    # If the token does not never require a trailing space
                    # And the next token does not never require a leading space
                    # And this token and the next are on the same line
                    # And this token and the next had white space between them in the original...
                    If (    $Tokens[$i  ].Type -notin ( 'NewLine', 'GroupStart', 'StatementSeparator' ) -and
                        $Tokens[$i + 1].Type -notin ( 'NewLine', 'GroupEnd', 'StatementSeparator' ) -and
                        $Tokens[$i].EndLine -eq $Tokens[$i + 1].StartLine -and
                        $Tokens[$i + 1].StartColumn - $Tokens[$i].EndColumn -gt 0 ) {
                        # Add a space to new script
                        $NewScriptText += ' '
                    }

                    # If the next token is a new line or semicolon following
                    # an open parenthesis or curly brace, skip it
                    $SkipNext = $Tokens[$i].Type -eq 'GroupStart' -and $Tokens[$i + 1].Type -in ( 'NewLine', 'StatementSeparator' )
                }

                # Else (Token is a line continuation or a repeated new line or semicolon)...
                Else {
                    # [Do not include it in the new script]

                    # If the next token is a new line or semicolon following
                    # an open parenthesis or curly brace, skip it
                    $SkipNext = $SkipNext -and $Tokens[$i + 1].Type -in ( 'NewLine', 'StatementSeparator' )
                }
            }
        }

        # If there is a last token to process...
        If ( $Tokens ) {
            # Add last token to new script
            # For string and variable, reference old script to include $ and quotes
            If ( $Tokens[$i].Type -in ( 'String', 'Variable' ) ) {
                $NewScriptText += $OldScript.Substring( $Tokens[-1].Start, $Tokens[-1].Length )
            } Else {
                $NewScriptText += $Tokens[-1].Content
            }
        }

        # Trim any leading new lines from the new script
        $NewScriptText = $NewScriptText.TrimStart( "`n`r;" )
        #return [scriptblock]::Create( $NewScriptText )


        # Return the new script as the same type as the input
        If ( $Items.Count -eq 1 ) {
            If ( $Items[0] -is [scriptblock] ) {
                # Return single scriptblock
                return [scriptblock]::Create( $NewScriptText )
            } Else {
                # Return single string
                return $NewScriptText
            }
        } Else {
            # Return array of strings
            return $NewScriptText.Split( "`n`r", [System.StringSplitOptions]::RemoveEmptyEntries )
        }

    }
}