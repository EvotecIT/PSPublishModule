function Test-AllFilePathsAndThrowErrorIfOneIsNotValid([string[]] $filePaths) {
    foreach ($filePath in $filePaths) {
        [bool] $fileWasNotFoundAtPath = [string]::IsNullOrEmpty($filePath) -or !(Test-Path -Path $filePath -PathType Leaf)
        if ($fileWasNotFoundAtPath) {
            throw "There is no file at the specified path, '$filePath'."
        }
    }
}