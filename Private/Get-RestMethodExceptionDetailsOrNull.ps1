function Get-RestMethodExceptionDetailsOrNull([Exception] $restMethodException) {
    try {
        $responseDetails = @{
            ResponseUri       = $exception.Response.ResponseUri
            StatusCode        = $exception.Response.StatusCode
            StatusDescription = $exception.Response.StatusDescription
            ErrorMessage      = $exception.Message
        }
        [string] $responseDetailsAsNicelyFormattedString = Convert-HashTableToNicelyFormattedString $responseDetails

        [string] $errorInfo = "Request Details:" + $NewLine + $requestDetailsAsNicelyFormattedString
        $errorInfo += $NewLine
        $errorInfo += "Response Details:" + $NewLine + $responseDetailsAsNicelyFormattedString
        return $errorInfo
    } catch {
        return $null
    }
}
