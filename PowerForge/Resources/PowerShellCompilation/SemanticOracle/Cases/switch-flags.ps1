switch -Regex ('forty-two') {
    '^forty' { return 42 }
    default { return -1 }
}
