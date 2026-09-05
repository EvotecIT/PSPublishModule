try {
    return [int]::Parse('expected')
} catch [System.InvalidOperationException] {
    return -1
} catch [System.FormatException] {
    return 42
} catch {
    return -2
}
