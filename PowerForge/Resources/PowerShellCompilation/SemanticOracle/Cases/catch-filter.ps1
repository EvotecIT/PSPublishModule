try {
    throw [System.ArgumentException] 'expected'
} catch [System.InvalidOperationException] {
    -1
} catch [System.ArgumentException] {
    42
} catch {
    -2
}
