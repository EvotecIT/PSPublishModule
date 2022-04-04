function Invoke-RestMethodAndThrowDescriptiveErrorOnFailure($requestParametersHashTable) {
    $requestDetailsAsNicelyFormattedString = Convert-HashTableToNicelyFormattedString $requestParametersHashTable
    Write-Verbose "Making web request with the following parameters:$NewLine$requestDetailsAsNicelyFormattedString"

    try {
        $webRequestResult = Invoke-RestMethod @requestParametersHashTable
    } catch {
        [Exception] $exception = $_.Exception

        [string] $errorMessage = Get-RestMethodExceptionDetailsOrNull -restMethodException $exception
        if ([string]::IsNullOrWhiteSpace($errorMessage)) {
            $errorMessage = $exception.ToString()
        }

        throw "An unexpected error occurred while making web request:$NewLine$errorMessage"
    }

    Write-Verbose "Web request returned the following result:$NewLine$webRequestResult"
    return $webRequestResult
}
